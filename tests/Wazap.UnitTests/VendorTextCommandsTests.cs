using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory.Infrastructure.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Services;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Tests directs de <see cref="VendorTextCommands"/> — commandes WhatsApp du <b>vendeur</b>
/// (P2 / C-13, second temps). Aucune de ces commandes n'était couverte : le seul test qui les
/// approchait vérifiait le texte du menu d'aide.
///
/// Les cas retenus sont ceux qui coûtent de l'argent ou engagent la garantie commerciale :
/// une demande de course sans zone déclarée, la création puis la suppression d'un produit du
/// catalogue qui alimente le menu des clients, et une déclaration de sinistre sur un code inconnu.
/// </summary>
public class VendorTextCommandsTests
{
    private const string VendorPhone = "+2250700000021";
    private const string ClientPhone = "0700000022";

    /// <summary>Le service de commande exige un porteur courant ; ici aucune requête HTTP.</summary>
    private sealed class NoCurrentUser : ICurrentUser
    {
        public Guid? Id => null;
        public UserRole? Role => null;
    }

    private sealed class Fixture : IDisposable
    {
        public ApplicationDbContext Context { get; }
        public RecordingWhatsAppSender Sender { get; } = new();
        public VendorTextCommands Commands { get; }
        public User Vendor { get; }
        public List<string> Replies { get; } = new();

        public Fixture(bool withZone = true)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase("vendor-commands-" + Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            Context = new ApplicationDbContext(options);

            Vendor = new User("boutique", "hash", UserRole.Vendor, VendorPhone);
            if (withZone)
                Vendor.SetZone("Cocody");
            Context.Users.Add(Vendor);
            Context.SaveChanges();

            var whatsAppOptions = new WhatsAppOptions();
            var orchestrator = new WhatsAppOrchestrationService(Sender, whatsAppOptions,
                NullLogger<WhatsAppOrchestrationService>.Instance);

            var offers = new DeliveryOfferService(Context, Sender, whatsAppOptions,
                new GeoOptions(), new GroupingOptions(), new ClientOptions(), orchestrator,
                new RiderSecurityOptions(), new RiderReputationOptions(), new ClientPaymentOptions(),
                new RiderPriorityOptions(), NullLogger<DeliveryOfferService>.Instance);

            var orders = new OrderService(Context, new NoCurrentUser(), offers,
                new DeliveryProofOptions(), NullLogger<OrderService>.Instance);

            var colisSur = new ColisSurService(Context, Sender, new ConfigStub(), new ColisSurOptions(),
                new ManualPayoutService(NullLogger<ManualPayoutService>.Instance),
                NullLogger<ColisSurService>.Instance);

            Commands = new VendorTextCommands(Context, orders,
                new VendorProductService(Context, NullLogger<VendorProductService>.Instance), colisSur);
        }

        public Task Reply(User user, string message)
        {
            Replies.Add(message);
            return Task.CompletedTask;
        }

        public string LastReply => Replies.Count == 0 ? string.Empty : Replies[^1];

        public void Dispose() => Context.Dispose();
    }

    // ------------------------------------------------------------------ Reconnaissance

    [Theory]
    [InlineData("LIVRAISON", true)]
    [InlineData("LIVRAISON 2 poulets à Marcory", true)]
    [InlineData("SINISTRE", true)]
    [InlineData("SINISTRE A1B2C3D4", true)]
    [InlineData("PRODUITS", true)]
    [InlineData("PRODUIT Poulet | 2500", true)]
    [InlineData("SUPPRIMER PRODUIT 1", true)]
    [InlineData("LIVRAISONNE", false)]
    [InlineData("SINISTREUR", false)]
    [InlineData("PRODUITeur", false)]
    [InlineData("", false)]
    public void Matches_ReconnaitLesCommandesVendeurEtRienDExtra(string upper, bool expected)
    {
        // Une régression ici détournerait un message quelconque vers une demande de course
        // (elle consomme un crédit) ou vers le catalogue.
        Assert.Equal(expected, VendorTextCommands.Matches(upper));
    }

    // ------------------------------------------------- Demande de course (« LIVRAISON »)

    [Fact]
    public async Task Livraison_SansPrecision_ExpliqueLeFormatSansRienCreer()
    {
        using var f = new Fixture();

        await f.Commands.HandleAsync(f.Vendor, "LIVRAISON", f.Reply);

        Assert.Contains("Format : LIVRAISON", f.LastReply);
        Assert.Empty(f.Context.Orders);
    }

    [Fact]
    public async Task Livraison_SansZoneDeclaree_ExpliqueLaCommandeZoneSansRienCreer()
    {
        using var f = new Fixture(withZone: false);

        await f.Commands.HandleAsync(f.Vendor, "LIVRAISON 2 poulets à Marcory", f.Reply);

        // Le matching exige une position GPS ou une zone : le message doit dire quoi faire.
        Assert.Contains("Définissez d'abord votre zone", f.LastReply);
        Assert.Empty(f.Context.Orders);
    }

    [Fact]
    public async Task Livraison_AvecZone_CreeLaCourseEtRendLeCodeCourt()
    {
        using var f = new Fixture();

        await f.Commands.HandleAsync(f.Vendor, "LIVRAISON 2 poulets à Marcory, rue Princesse", f.Reply);

        var order = await f.Context.Orders.SingleAsync();
        var expectedCode = order.Id.ToString("N")[..8].ToUpperInvariant();
        Assert.Contains($"Course #{expectedCode} enregistrée", f.LastReply);
        Assert.Equal("2 poulets à Marcory, rue Princesse", order.Description);
    }

    [Fact]
    public async Task Livraison_AvecTelephoneClient_RattacheLeNumeroAvecIndicatif()
    {
        using var f = new Fixture();

        await f.Commands.HandleAsync(f.Vendor,
            $"LIVRAISON 1 pagne à Cocody tél {ClientPhone}", f.Reply);

        var order = await f.Context.Orders.SingleAsync();
        // Le numéro est celui du client, pas du vendeur : c'est lui qui recevra les notifications.
        Assert.Equal("+225" + ClientPhone, order.ClientWhatsAppNumber);
    }

    [Fact]
    public async Task Livraison_SansTelephone_LaisseLeClientSansNumero()
    {
        using var f = new Fixture();

        await f.Commands.HandleAsync(f.Vendor, "LIVRAISON 1 pagne à Cocody", f.Reply);

        var order = await f.Context.Orders.SingleAsync();
        Assert.Equal(string.Empty, order.ClientWhatsAppNumber);
    }

    // ------------------------------------------------- Catalogue produits du vendeur

    [Fact]
    public async Task Produit_AjouteAuCataloguePuisListeEtSupprime()
    {
        using var f = new Fixture();

        await f.Commands.HandleAsync(f.Vendor, "PRODUIT Poulet braisé | 2 500 | 🍗", f.Reply);
        Assert.Contains("Produit ajouté", f.LastReply);
        Assert.Contains("Poulet braisé", f.LastReply);

        await f.Commands.HandleAsync(f.Vendor, "PRODUITS", f.Reply);
        Assert.Contains("Votre catalogue", f.LastReply);
        Assert.Contains("1. 🍗 Poulet braisé", f.LastReply);
        Assert.Contains("SUPPRIMER PRODUIT", f.LastReply);

        await f.Commands.HandleAsync(f.Vendor, "SUPPRIMER PRODUIT 1", f.Reply);
        Assert.Contains("Produit retiré : Poulet braisé", f.LastReply);
        Assert.Empty(f.Context.VendorProducts);
    }

    [Fact]
    public async Task Produit_MalgreUnPrixFormate_AvecEspacesEtFCFA()
    {
        using var f = new Fixture();

        await f.Commands.HandleAsync(f.Vendor, "PRODUIT Attiéké | 1 500 FCFA", f.Reply);

        var product = await f.Context.VendorProducts.SingleAsync();
        Assert.Equal(1500m, product.Price);
        Assert.Equal("Attiéké", product.Name);
    }

    [Fact]
    public async Task Produit_PayloadInvalide_ExpliqueLeFormat()
    {
        using var f = new Fixture();

        // Pas de séparateur « | » ni de prix : message d'aide, aucun produit créé.
        await f.Commands.HandleAsync(f.Vendor, "PRODUIT Poulet braisé", f.Reply);

        Assert.Contains("Format : PRODUIT", f.LastReply);
        Assert.Empty(f.Context.VendorProducts);
    }

    [Fact]
    public async Task Produits_CatalogueVide_ExpliqueCommentAjouter()
    {
        using var f = new Fixture();

        await f.Commands.HandleAsync(f.Vendor, "PRODUITS", f.Reply);

        Assert.Contains("catalogue est vide", f.LastReply);
        Assert.Contains("PRODUIT <nom> | <prix>", f.LastReply);
    }

    [Theory]
    [InlineData("SUPPRIMER PRODUIT 0")]
    [InlineData("SUPPRIMER PRODUIT 7")]
    [InlineData("SUPPRIMER PRODUIT")]
    [InlineData("SUPPRIMER PRODUIT abc")]
    public async Task SupprimerProduit_HorsBornes_RefuseSansException(string command)
    {
        using var f = new Fixture();
        await f.Commands.HandleAsync(f.Vendor, "PRODUIT Poulet | 2500", f.Reply);

        await f.Commands.HandleAsync(f.Vendor, command, f.Reply);

        Assert.Contains("Format : SUPPRIMER PRODUIT", f.LastReply);
        // Le produit est toujours là : un index invalide ne doit rien supprimer.
        Assert.Single(f.Context.VendorProducts);
    }

    // ------------------------------------------------- Sinistre (Garantie Colis Sûr)

    [Fact]
    public async Task Sinistre_CodeInconnu_RepondParUnMessageEtNeLevePas()
    {
        using var f = new Fixture();

        await f.Commands.HandleAsync(f.Vendor, "SINISTRE ZZZZZZZZ", f.Reply);

        // Le vendeur reçoit un message lisible : jamais d'exception technique sur ce chemin.
        Assert.NotEmpty(f.LastReply);
        Assert.DoesNotContain("Exception", f.LastReply);
    }
}
