using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Services;
using Wazap.Application.Configuration;
using Wazap.Application.Services;
using Wazap.Domain.Configuration;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Contexte de test sur un fournisseur **relationnel** (SQLite en mémoire).
/// <para>
/// Les autres tests utilisent le fournisseur InMemory, qui <b>ne sait pas</b> exécuter
/// <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> ni appliquer une contrainte d'unicité. Les
/// garde-fous anti-double-débit (réclamation atomique d'une offre, débit conditionnel de
/// crédits, complétion idempotente d'un paiement) reposent précisément sur ces mécanismes :
/// ils n'étaient donc exercés par AUCUN test — seule la branche de repli, non atomique,
/// l'était. Ici le code réellement exécuté en production est testé.
/// </para>
/// </summary>
internal sealed class SqliteHarness : IDisposable
{
    private readonly SqliteConnection _connection;

    public ApplicationDbContext Context { get; }

    public SqliteHarness()
    {
        // Une base SQLite « :memory: » vit tant que sa connexion reste ouverte.
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        Context = BuildContext();
        Context.Database.EnsureCreated();

        // SQLite n'applique pas les clés étrangères sans cette activation explicite.
        Context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
    }

    private ApplicationDbContext BuildContext()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);

    /// <summary>Second contexte sur la même base : simule deux sessions applicatives distinctes.</summary>
    public ApplicationDbContext NewContext() => BuildContext();

    /// <summary>Recharge une entité depuis la base (hors suivi) : état réellement persisté.</summary>
    public T? Reload<T>(Guid id) where T : class
    {
        Context.ChangeTracker.Clear();
        return Context.Find<T>(id);
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}

/// <summary>
/// Garde-fous d'argent exécutés sur un fournisseur relationnel — c'est-à-dire exactement les
/// chemins de production (UPDATE conditionnel + transaction), qui ne sont pas ceux du
/// fournisseur InMemory utilisé par les autres tests.
/// </summary>
public class RelationalMoneyPathsTests
{
    private static WhatsAppOrchestrationService Orchestrator(RecordingWhatsAppSender sender)
        => new(sender, new WhatsAppOptions(), NullLogger<WhatsAppOrchestrationService>.Instance);

    // ------------------------------------------------ Acceptation d'une offre (livreur)

    private sealed class OfferFixture
    {
        public SqliteHarness Harness { get; } = new();
        public ApplicationDbContext Context => Harness.Context;
        public User Vendor { get; }
        public User Rider { get; }
        public Order Order { get; }
        public DeliveryOfferService Offers { get; }

        public OfferFixture()
        {
            Vendor = new User("vendeur", "hash", UserRole.Vendor, "+2250700000200");
            Vendor.SetZone("Cocody");
            Vendor.AddCredits(5);

            Rider = new User("livreur", "hash", UserRole.Rider, "+2250700000201");
            Rider.SetAvailability(true);

            Context.Users.AddRange(Vendor, Rider);

            Order = new Order("Client", "+2250700000202", Vendor.PhoneNumber!, "1 colis", 3000m);
            Order.LinkVendor(Vendor.Id);
            Order.ConfirmByVendor();
            Order.AwaitRiderAcceptance();
            Context.Orders.Add(Order);
            Context.SaveChanges();

            var whatsAppOptions = new WhatsAppOptions();
            Offers = new DeliveryOfferService(Context, new RecordingWhatsAppSender(), whatsAppOptions,
                new GeoOptions(), new GroupingOptions(), new ClientOptions(), Orchestrator(new RecordingWhatsAppSender()),
                new RiderSecurityOptions(), new RiderReputationOptions(), new ClientPaymentOptions(),
                new RiderPriorityOptions(), NullLogger<DeliveryOfferService>.Instance);
        }

        public DeliveryOffer NewPendingOffer()
        {
            var offer = new DeliveryOffer(Order.Id, Rider.Id, 1);
            Context.DeliveryOffers.Add(offer);
            Context.SaveChanges();
            return offer;
        }

        public int VendorCredits => Harness.Reload<User>(Vendor.Id)!.Credits;

        public DeliveryOffer ReloadOffer(Guid id) => Harness.Reload<DeliveryOffer>(id)!;
    }

    [Fact]
    public async Task AcceptOffer_SurFournisseurRelationnel_ReclameL_OffreEtDebiteUneFois()
    {
        var fixture = new OfferFixture();
        using var _ = fixture.Harness;

        // Le fournisseur est bien relationnel : c'est la branche atomique qui est exercée.
        Assert.True(fixture.Context.SupportsConditionalUpdates);

        var offer = fixture.NewPendingOffer();
        await fixture.Offers.AcceptOfferAsync(offer.Id);

        var stored = fixture.ReloadOffer(offer.Id);
        Assert.Equal(DeliveryOfferStatus.Accepted, stored.Status);
        Assert.NotNull(stored.RespondedAt); // écrit par l'UPDATE conditionnel
        Assert.Equal(4, fixture.VendorCredits);

        // Seconde acceptation de la MÊME offre (autre livreur, ou reprise de webhook).
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Offers.AcceptOfferAsync(offer.Id));

        Assert.Equal(4, fixture.VendorCredits);
        var order = fixture.Harness.Reload<Order>(fixture.Order.Id)!;
        Assert.Equal(OrderStatus.RiderAssigned, order.Status);
    }

    [Fact]
    public async Task AcceptOffer_OffreDejaExpiree_EstRefuseeSansDebit()
    {
        var fixture = new OfferFixture();
        using var _ = fixture.Harness;

        var offer = fixture.NewPendingOffer();

        // Une autre vague a expiré l'offre entre-temps (mise à jour hors de ce contexte).
        offer.Expire();
        fixture.Context.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Offers.AcceptOfferAsync(offer.Id));

        // Le débit conditionnel n'a pas eu lieu : le vendeur n'a rien payé.
        Assert.Equal(5, fixture.VendorCredits);
    }

    // ----------------------------------------------------------- Achat d'un pack vendeur

    private static PackService NewPackService(ApplicationDbContext context, RecordingWhatsAppSender sender)
    {
        var packs = new List<PackConfiguration> { new() { Name = "Mini", Price = 1000, Credits = 6 } };
        return new PackService(context, new FakeClientPaymentGateway(), packs,
            Orchestrator(sender), NullLogger<PackService>.Instance);
    }

    [Fact]
    public async Task CompletePurchase_SurFournisseurRelationnel_NeCrediteQuUneFois()
    {
        using var harness = new SqliteHarness();
        var context = harness.Context;

        var vendor = new User("vendeur-pack", "hash", UserRole.Vendor, "+2250700000300");
        context.Users.Add(vendor);

        var transaction = new CreditTransaction(vendor.Id, 2500m, 15, "PENDING-test");
        context.CreditTransactions.Add(transaction);
        context.SaveChanges();

        var service = NewPackService(context, new RecordingWhatsAppSender());

        await service.CompletePurchaseAsync(transaction.Id, "REF-1");
        await service.CompletePurchaseAsync(transaction.Id, "REF-1"); // webhook rejoué

        Assert.Equal(15, harness.Reload<User>(vendor.Id)!.Credits);
        var stored = harness.Reload<CreditTransaction>(transaction.Id)!;
        Assert.Equal(TransactionStatus.Completed, stored.Status);
        Assert.Equal("REF-1", stored.TransactionReference);
    }

    // ------------------------------------------------------------ Paiement du panier client

    [Fact]
    public async Task CompletePayment_SurFournisseurRelationnel_EcritLaCommissionEtLeNet()
    {
        using var harness = new SqliteHarness();
        var context = harness.Context;

        var vendor = new User("vendeur", "hash", UserRole.Vendor, "+2250700000400");
        vendor.SetZone("Marcory");
        context.Users.Add(vendor);

        var order = new Order("Client", "+2250700000401", vendor.PhoneNumber!, "Panier", 10_000m);
        order.LinkVendor(vendor.Id);
        context.Orders.Add(order);
        context.SaveChanges();

        var payment = new OrderPayment(order.Id, 10_000m);
        context.OrderPayments.Add(payment);
        context.SaveChanges();

        var sender = new RecordingWhatsAppSender();
        var offers = new DeliveryOfferService(context, sender, new WhatsAppOptions(),
            new GeoOptions(), new GroupingOptions(), new ClientOptions(), Orchestrator(sender),
            new RiderSecurityOptions(), new RiderReputationOptions(), new ClientPaymentOptions(),
            new RiderPriorityOptions(), NullLogger<DeliveryOfferService>.Instance);

        var service = new ClientPaymentService(context, new FakeClientPaymentGateway(), sender, offers,
            new ClientPaymentOptions { Enabled = true, CommissionPercent = 2.0m },
            NullLogger<ClientPaymentService>.Instance);

        await service.CompletePaymentAsync(payment.Id, "GENIUS-1");
        await service.CompletePaymentAsync(payment.Id, "GENIUS-1"); // rejeu

        // Les montants sont écrits par l'UPDATE ATOMIQUE : s'ils ne l'étaient pas, le vendeur
        // recevrait une notification avec 0 FCFA de commission et un net erroné.
        var stored = harness.Reload<OrderPayment>(payment.Id)!;
        Assert.Equal(TransactionStatus.Completed, stored.Status);
        Assert.Equal(200m, stored.CommissionAmount);      // 2 % de 10 000
        Assert.Equal(9_800m, stored.VendorPayoutDue);
        Assert.NotNull(stored.CompletedAt);
        Assert.Equal("GENIUS-1", stored.TransactionReference);
    }

    // ---------------------------------------------------------------- Sinistre Colis Sûr

    [Fact]
    public async Task ApproveClaim_SurFournisseurRelationnel_NeRembourseQuUneFois()
    {
        using var harness = new SqliteHarness();
        var context = harness.Context;

        var vendor = new User("vendeur", "hash", UserRole.Vendor, "+2250700000500");
        vendor.AddCredits(3);
        var rider = new User("livreur", "hash", UserRole.Rider, "+2250700000501");
        context.Users.AddRange(vendor, rider);

        var order = new Order("Client", "+2250700000502", vendor.PhoneNumber!, "Colis perdu", 12_000m);
        order.LinkVendor(vendor.Id);
        order.LinkRider(rider.Id);
        context.Orders.Add(order);

        var claim = new DeliveryClaim(order.Id, vendor.Id, rider.Id, "SINISTRE — colis perdu");
        context.DeliveryClaims.Add(claim);
        context.SaveChanges();

        var sender = new RecordingWhatsAppSender();
        var service = new ColisSurService(context, sender, new ConfigStub(), new ColisSurOptions(),
            new ManualPayoutService(NullLogger<ManualPayoutService>.Instance),
            NullLogger<ColisSurService>.Instance);

        var reviewer = Guid.NewGuid();
        await service.ApproveAsync(claim.Id, compensationCredits: 2, note: "vérifié", reviewer, 5_000m);

        var creditsAfterFirst = harness.Reload<User>(vendor.Id)!.Credits;

        // Seconde approbation (double-clic administrateur) : refusée, aucun effet.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApproveAsync(claim.Id, compensationCredits: 2, note: "doublon", reviewer, 5_000m));

        Assert.Equal(creditsAfterFirst, harness.Reload<User>(vendor.Id)!.Credits);

        var stored = harness.Reload<DeliveryClaim>(claim.Id)!;
        Assert.Equal(DeliveryClaimStatus.Approved, stored.Status);
        Assert.Equal(2, stored.CompensationCredits);
        Assert.Equal(5_000m, stored.CompensationAmountFcfa);
    }

    // ------------------------------------------------- Déduplication des webhooks entrants

    [Fact]
    public void ProcessedWebhookMessage_RefuseUnDoublon_AuNiveauDeLaBase()
    {
        using var harness = new SqliteHarness();

        harness.Context.ProcessedWebhookMessages.Add(new ProcessedWebhookMessage("wamid.DOUBLON"));
        harness.Context.SaveChanges();

        // Deux SESSIONS distinctes : c'est le cas réel de deux livraisons du même message.
        // La contrainte d'unicité est la garantie de fond — la lecture préalable du service ne
        // suffirait pas si les deux requêtes arrivaient en même temps.
        using var secondSession = harness.NewContext();
        secondSession.ProcessedWebhookMessages.Add(new ProcessedWebhookMessage("wamid.DOUBLON"));

        Assert.Throws<DbUpdateException>(() => secondSession.SaveChanges());
    }
}
