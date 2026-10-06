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
    [InlineData("REMIS", true)]
    [InlineData("REMIS A1B2C3D4", true)]
    [InlineData("PARTI", true)]
    [InlineData("COLIS REMIS", true)]
    [InlineData("SINISTRE", true)]
    [InlineData("SINISTRE A1B2C3D4", true)]
    [InlineData("PRODUITS", true)]
    [InlineData("PRODUIT Poulet | 2500", true)]
    [InlineData("SUPPRIMER PRODUIT 1", true)]
    [InlineData("LIVRAISONNE", false)]
    [InlineData("REMISSIBLE", false)]
    [InlineData("PARTIR", false)]
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

    [Fact]
    public void ParseFreeTextOrder_ExtraitAutomatiquementToutesLesInfosClient()
    {
        var raw = "Nom : Sarah Diop\n"
                + "Tél : 07 08 09 10 11\n"
                + "Article : Robe Wax Dorée\n"
                + "Prix : 25 000 FCFA\n"
                + "Adresse : Cocody Angré 8e tranche vers la pharmacie du 22e";

        var parsed = Wazap.Application.Helpers.VendorCommandParser.ParseFreeTextOrder(raw);

        Assert.Equal("Sarah Diop", parsed.ClientName);
        Assert.Equal("+2250708091011", parsed.ClientPhone);
        Assert.Equal("Robe Wax Dorée", parsed.Description);
        Assert.Equal(25000m, parsed.Amount);
        Assert.Equal("Cocody", parsed.Zone);
        Assert.Contains("Angré", parsed.Address);
    }

    [Fact]
    public async Task Dashboard_RenvoieTableauDeBordCommercantDansWhatsApp()
    {
        using var f = new Fixture();
        f.Vendor.AddCredits(10);
        await f.Context.SaveChangesAsync();

        Assert.True(VendorTextCommands.Matches("DASHBOARD"));
        Assert.True(VendorTextCommands.Matches("SOLDE"));

        await f.Commands.HandleAsync(f.Vendor, "DASHBOARD", f.Reply);

        Assert.NotEmpty(f.LastReply);
        Assert.Contains("TABLEAU DE BORD COMMERÇANT", f.LastReply);
        Assert.Contains("Crédits livraisons disponibles : *10*", f.LastReply);
        Assert.Contains("wa.me/2250544051972?text=LIVRAISON", f.LastReply);
    }

    [Fact]
    public async Task Remis_SansCommandeEnAttente_RepondAucunColisEnAttente()
    {
        using var f = new Fixture();

        await f.Commands.HandleAsync(f.Vendor, "REMIS", f.Reply);

        Assert.Contains("Aucun colis en attente de remise", f.LastReply);
    }

    [Fact]
    public async Task Remis_AvecCommandeAssignee_PasseEnTransitEtNotifie()
    {
        using var f = new Fixture();

        var order = new Order("Client Test", "+2250700000099", f.Vendor.PhoneNumber!, "Colis VIP", 15000m);
        order.LinkVendor(f.Vendor.Id);
        order.ConfirmByVendor();
        order.AwaitRiderAcceptance();
        order.AssignRider("+2250700000088");
        order.LinkRider(Guid.NewGuid());
        f.Context.Orders.Add(order);
        await f.Context.SaveChangesAsync();

        await f.Commands.HandleAsync(f.Vendor, "REMIS", f.Reply);

        Assert.Equal(OrderStatus.InTransit, order.Status);
        Assert.Contains("remis au livreur", f.LastReply);
        Assert.Contains(order.Id.ToString("N")[..8].ToUpperInvariant(), f.LastReply);
    }

    [Fact]
    public async Task Remis_AvecCodeSpecifique_PasseLaBonneCommandeEnTransit()
    {
        using var f = new Fixture();

        var order1 = new Order("Client 1", "+2250700000091", f.Vendor.PhoneNumber!, "Colis 1", 10000m);
        order1.LinkVendor(f.Vendor.Id);
        order1.ConfirmByVendor();
        order1.AwaitRiderAcceptance();
        order1.AssignRider("+2250700000088");
        f.Context.Orders.Add(order1);

        var order2 = new Order("Client 2", "+2250700000092", f.Vendor.PhoneNumber!, "Colis 2", 20000m);
        order2.LinkVendor(f.Vendor.Id);
        order2.ConfirmByVendor();
        order2.AwaitRiderAcceptance();
        order2.AssignRider("+2250700000088");
        f.Context.Orders.Add(order2);

        await f.Context.SaveChangesAsync();

        var code1 = order1.Id.ToString("N")[..8].ToUpperInvariant();
        await f.Commands.HandleAsync(f.Vendor, $"REMIS {code1}", f.Reply);

        Assert.Equal(OrderStatus.InTransit, order1.Status);
        Assert.Equal(OrderStatus.RiderAssigned, order2.Status);
        Assert.Contains($"#{code1} remis au livreur", f.LastReply);
    }
}

