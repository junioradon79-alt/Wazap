using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Services;
using Wazap.Application.Configuration;
using Wazap.Application.Exceptions;
using Wazap.Application.Services;
using Wazap.Domain.Configuration;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Idempotence des flux ARGENT : complétion d'un achat de pack et acceptation d'une offre
/// de course.
/// <para>
/// Ces deux chemins étaient protégés par une simple relecture de statut (« si déjà
/// Completed, ne rien faire »), c'est-à-dire un <i>check-then-act</i> : un webhook de
/// paiement rejoué, le worker de réconciliation qui tourne en même temps, ou deux livreurs
/// qui répondent « ACCEPTE » au même instant créditaient/débitaient DEUX FOIS.
/// </para>
/// <para>
/// Les tests s'exécutent sur le fournisseur InMemory : ils valident donc la garde
/// fonctionnelle (le second appel ne produit aucun effet). La garantie d'atomicité
/// elle-même repose sur un UPDATE conditionnel propre à PostgreSQL.
/// </para>
/// </summary>
public class PaymentAndAcceptanceIdempotenceTests
{
    // ------------------------------------------------------------------ Packs de crédits

    private static PackService NewPackService(ApplicationDbContext context, RecordingWhatsAppSender sender)
    {
        var packs = new List<PackConfiguration> { new() { Name = "Mini", Price = 1000, Credits = 6 } };
        var orchestrator = new WhatsAppOrchestrationService(
            sender, new WhatsAppOptions(), NullLogger<WhatsAppOrchestrationService>.Instance);

        return new PackService(context, new FakeClientPaymentGateway(), packs, orchestrator,
            NullLogger<PackService>.Instance);
    }

    private static async Task<(ApplicationDbContext Context, User Vendor, CreditTransaction Transaction)> SeedPackPurchaseAsync(
        int credits)
    {
        var context = TestInfra.NewContext("pack-" + Guid.NewGuid().ToString("N"));
        var vendor = new User("vendeur-pack", "hash", UserRole.Vendor, "+2250700000100");
        context.Users.Add(vendor);

        var transaction = new CreditTransaction(vendor.Id, 2500m, credits, "PENDING-test");
        context.CreditTransactions.Add(transaction);
        await context.SaveChangesAsync();

        return (context, vendor, transaction);
    }

    [Fact]
    public async Task CompletePurchase_DeuxFois_NeCrediteQuUneFois()
    {
        var (context, vendor, transaction) = await SeedPackPurchaseAsync(credits: 15);
        var service = NewPackService(context, new RecordingWhatsAppSender());

        await service.CompletePurchaseAsync(transaction.Id, "REF-1");
        await service.CompletePurchaseAsync(transaction.Id, "REF-1"); // webhook rejoué

        var stored = await context.Users.FindAsync(vendor.Id);
        Assert.NotNull(stored);
        Assert.Equal(15, stored!.Credits);

        var storedTransaction = await context.CreditTransactions.FindAsync(transaction.Id);
        Assert.Equal(TransactionStatus.Completed, storedTransaction!.Status);
    }

    [Fact]
    public async Task CompletePurchase_TransactionEnEchec_EstRefusee()
    {
        var (context, _, transaction) = await SeedPackPurchaseAsync(credits: 15);
        var service = NewPackService(context, new RecordingWhatsAppSender());

        await service.FailPurchaseAsync(transaction.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CompletePurchaseAsync(transaction.Id, "REF-1"));
    }

    [Fact]
    public async Task CompletePurchase_VendeurIntrouvable_NeCompletePasLaTransaction()
    {
        // La transaction doit rester Pending : la marquer Completed sans créditer personne
        // ferait perdre définitivement l'achat payé.
        var context = TestInfra.NewContext("pack-orphan-" + Guid.NewGuid().ToString("N"));
        var transaction = new CreditTransaction(Guid.NewGuid(), 2500m, 15, "PENDING-test");
        context.CreditTransactions.Add(transaction);
        await context.SaveChangesAsync();

        var service = NewPackService(context, new RecordingWhatsAppSender());
        await service.CompletePurchaseAsync(transaction.Id, "REF-1");

        var stored = await context.CreditTransactions.FindAsync(transaction.Id);
        Assert.Equal(TransactionStatus.Pending, stored!.Status);
    }

    // ------------------------------------------------------- Acceptation d'une offre

    private sealed class Harness
    {
        public ApplicationDbContext Context { get; }
        public DeliveryOfferService Offers { get; }
        public User Vendor { get; }
        public User Rider { get; }
        public Order Order { get; }

        public Harness()
        {
            Context = TestInfra.NewContext("accept-" + Guid.NewGuid().ToString("N"));

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
            var orchestrator = new WhatsAppOrchestrationService(
                new RecordingWhatsAppSender(), whatsAppOptions, NullLogger<WhatsAppOrchestrationService>.Instance);

            Offers = new DeliveryOfferService(Context, new RecordingWhatsAppSender(), whatsAppOptions,
                new GeoOptions(), new GroupingOptions(), new ClientOptions(), orchestrator,
                new RiderSecurityOptions(), new RiderReputationOptions(), new ClientPaymentOptions(),
                new RiderPriorityOptions(), NullLogger<DeliveryOfferService>.Instance);
        }

        public async Task<DeliveryOffer> NewPendingOfferAsync()
        {
            var offer = new DeliveryOffer(Order.Id, Rider.Id, 1);
            Context.DeliveryOffers.Add(offer);
            await Context.SaveChangesAsync();
            return offer;
        }

        public int VendorCredits
        {
            get
            {
                Context.ChangeTracker.Clear();
                return Context.Users.Find(Vendor.Id)!.Credits;
            }
        }
    }

    [Fact]
    public async Task AcceptOffer_DeuxAcceptationsConcurrentes_NeDebitentQuUnCredit()
    {
        var harness = new Harness();
        var offer = await harness.NewPendingOfferAsync();

        await harness.Offers.AcceptOfferAsync(offer.Id);
        Assert.Equal(4, harness.VendorCredits);

        // Le second livreur (ou un doublon de webhook) retente la MÊME offre.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Offers.AcceptOfferAsync(offer.Id));

        Assert.Equal(4, harness.VendorCredits);
    }

    [Fact]
    public async Task AcceptOffer_CreditsInsuffisants_RefuseSansDebiter()
    {
        var harness = new Harness();
        var offer = await harness.NewPendingOfferAsync();

        // Épuiser les crédits du vendeur : l'acceptation doit échouer proprement.
        for (var i = 0; i < 5; i++)
            harness.Vendor.TryConsumeCredit();
        harness.Context.SaveChanges();

        await Assert.ThrowsAsync<PaymentRequiredException>(
            () => harness.Offers.AcceptOfferAsync(offer.Id));

        Assert.Equal(0, harness.VendorCredits);
    }
}
