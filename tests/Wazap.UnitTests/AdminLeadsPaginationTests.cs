using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Controllers;
using Wazap.API.Services;
using Wazap.Application.Configuration;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// P2 / C-09 — <c>GET /api/admin/leads</c> ne renvoyait que les 200 leads les plus récents, sans
/// dire combien il en existait : au-delà, les leads étaient <b>inatteignables</b> depuis le tableau
/// de bord (l'écran se contentait d'avertir qu'il en manquait). Ces tests fixent le contrat de
/// pagination : <c>offset</c>, total en en-tête <c>X-Total-Count</c>, ordre <b>stable</b> d'une
/// page à l'autre, et filtres appliqués <b>avant</b> la pagination — sinon on paginerait dans le
/// vide, en sautant des lignes.
/// </summary>
public class AdminLeadsPaginationTests
{
    /// <summary>
    /// Prépare `leadCount` leads à dates croissantes (le n° le plus élevé est donc le plus récent)
    /// et un contrôleur branché sur un contexte InMemory, avec un <c>HttpContext</c> réel : sans
    /// lui, l'écriture de l'en-tête <c>X-Total-Count</c> lèverait.
    /// </summary>
    private static (AdminLeadsController Controller, TestDbContext Db) Create(int leadCount)
    {
        var db = new TestDbContext();
        var baseline = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < leadCount; i++)
        {
            var lead = new Lead($"Commerce {i:D4}", $"+2250700000000", "Marcory", "page-vente");
            db.Context.Leads.Add(lead);
            // `CreatedAt` n'a pas de setter public (le domaine pose la date de capture) : EF le
            // renseigne par sa propriété, comme le ferait une insertion réelle.
            db.Context.Entry(lead).Property(l => l.CreatedAt).CurrentValue = baseline.AddMinutes(i);
        }

        db.Context.SaveChanges();

        var controller = new AdminLeadsController(
            db.Context,
            new LeadConversionService(
                db.Context,
                new FakePasswordHasher(),
                new TrialOptions(),
                new RecordingWhatsAppSender(),
                NullLogger<LeadConversionService>.Instance));

        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return (controller, db);
    }

    private static async Task<List<LeadListItem>> ListAsync(
        AdminLeadsController controller,
        int limit = 200,
        int offset = 0,
        string? search = null,
        LeadStatus? status = null)
    {
        var result = Assert.IsType<OkObjectResult>(await controller.List(
            zone: null, source: null, referral: null, search: search, status: status,
            from: null, to: null, limit: limit, offset: offset, ct: CancellationToken.None));

        return Assert.IsType<List<LeadListItem>>(result.Value);
    }

    private static string TotalHeader(AdminLeadsController controller)
        => controller.Response.Headers["X-Total-Count"].ToString();

    [Fact]
    public async Task SansOffset_LaPremierePageDonneLesPlusRecentsEtLeTotal()
    {
        var (controller, db) = Create(250);
        using var _ = db;

        var leads = await ListAsync(controller);

        Assert.Equal(200, leads.Count);
        Assert.Equal("250", TotalHeader(controller));
        // Le plus récent d'abord (dates croissantes : le n° 0249 est le dernier capté).
        Assert.Equal("Commerce 0249", leads[0].BusinessName);
    }

    [Fact]
    public async Task LaSecondePageDonneLeReste_SansRepetitionNiTrou()
    {
        var (controller, db) = Create(250);
        using var _ = db;

        var first = await ListAsync(controller, offset: 0);
        var second = await ListAsync(controller, offset: 200);

        Assert.Equal(50, second.Count);
        Assert.Equal("Commerce 0049", second[0].BusinessName);
        // Aucune ligne vue deux fois, aucune ligne sautée : c'est tout l'enjeu d'un ordre stable
        // (deux leads captés dans la même seconde suffisaient à faire permuter les pages).
        Assert.Empty(first.Select(l => l.Id).Intersect(second.Select(l => l.Id)));
        Assert.Equal(250, first.Concat(second).Select(l => l.Id).Distinct().Count());
    }

    [Fact]
    public async Task UnOffsetNegatif_EstTraiteCommeLaPremierePage()
    {
        var (controller, db) = Create(250);
        using var _ = db;

        var leads = await ListAsync(controller, offset: -5);

        // `Skip(-5)` lève avec certains fournisseurs : la borne est posée côté serveur.
        Assert.Equal("Commerce 0249", leads[0].BusinessName);
    }

    [Fact]
    public async Task UneLimiteAberrante_EstBornee()
    {
        var (controller, db) = Create(250);
        using var _ = db;

        // `limit=0` ne doit pas renvoyer « rien » au hasard du fournisseur : la borne basse est 1.
        Assert.Single(await ListAsync(controller, limit: 0));
        // `limit=5000` est ramené au plafond (1 000) : les 250 leads tiennent dans la page.
        Assert.Equal(250, (await ListAsync(controller, limit: 5000)).Count);
    }

    [Fact]
    public async Task LeTotalPorteSurLeFiltre_PasSurTouteLaTable()
    {
        var (controller, db) = Create(250);
        using var _ = db;

        // Trois leads convertis seulement.
        foreach (var lead in db.Context.Leads.OrderBy(l => l.CreatedAt).Take(3).ToList())
            lead.SetStatus(LeadStatus.Converted);
        db.Context.SaveChanges();

        var leads = await ListAsync(controller, status: LeadStatus.Converted);

        Assert.Equal(3, leads.Count);
        // Un total qui ignorerait le filtre annoncerait « 3 sur 250 » et proposerait une page
        // suivante vide : l'écran doit pouvoir se fier à cet en-tête.
        Assert.Equal("3", TotalHeader(controller));
    }

    [Fact]
    public async Task LeTotalPorteSurLaRecherche_EtLaPaginationResteCoherente()
    {
        var (controller, db) = Create(30);
        using var _ = db;

        // 10 leads « Commerce 001x » (0010 à 0019).
        var page1 = await ListAsync(controller, limit: 4, offset: 0, search: "Commerce 001");
        var page2 = await ListAsync(controller, limit: 4, offset: 4, search: "Commerce 001");
        var page3 = await ListAsync(controller, limit: 4, offset: 8, search: "Commerce 001");

        Assert.Equal("10", TotalHeader(controller));
        Assert.Equal(4, page1.Count);
        Assert.Equal(4, page2.Count);
        Assert.Equal(2, page3.Count);
        Assert.Equal(10, page1.Concat(page2).Concat(page3).Select(l => l.Id).Distinct().Count());
    }
}
