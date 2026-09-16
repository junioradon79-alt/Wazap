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
/// Garde-fous d'argent et d'idempotence exécutés sur **PostgreSQL réel** (B-19).
///
/// SQLite (harnais des autres tests relationnels) prouve déjà que la LOGIQUE est atomique ; ce
/// qui restait non vérifié, c'est que la même chose tienne sur le moteur de production :
/// traduction SQL des requêtes, contraintes telles que PostgreSQL les applique, et constructions
/// propres à PostgreSQL. Ces tests s'ignorent d'eux-mêmes (avec la raison) quand
/// <c>WAZAP_TEST_POSTGRES</c> n'est pas défini — la CI fournit le serveur.
/// </summary>
public class PostgresMoneyPathsTests
{
    private sealed class NoCurrentUser : Wazap.Application.Abstractions.ICurrentUser
    {
        public Guid? Id => null;
        public UserRole? Role => null;
    }

    /// <summary>
    /// Vendeur crédité, livreur disponible, une commande : de quoi déclencher l'acceptation
    /// d'une offre (le seul endroit où le vendeur est débité).
    /// </summary>
    private sealed class Fixture : IDisposable
    {
        public PostgresHarness Harness { get; } = new();
        public ApplicationDbContext Context => Harness.Context!;
        public RecordingWhatsAppSender Sender { get; } = new();
        public User Vendor { get; }
        public User Rider { get; }
        public Order Order { get; }
        public DeliveryOfferService Offers { get; }

        public Fixture()
        {
            Vendor = new User("vendeur", "hash", UserRole.Vendor, "+2250700000800");
            Vendor.SetZone("Cocody");
            Vendor.AddCredits(3);
            Vendor.UpdateLocation(5.3599, -4.0083);
            Context.Users.Add(Vendor);

            Rider = new User("livreur", "hash", UserRole.Rider, "+2250700000801");
            Rider.SetAvailability(true);
            Rider.UpdateLocation(5.3600, -4.0084);
            Context.Users.Add(Rider);

            Order = new Order("Client", "+2250700000802", Vendor.PhoneNumber!, "1 colis", 3000m);
            Order.LinkVendor(Vendor.Id);
            Order.ConfirmByVendor();
            // La commande ATTEND un livreur : c'est l'acceptation de l'offre qui l'assigne.
            // L'assigner ici ferait échouer le second AssignRider du domaine.
            Order.AwaitRiderAcceptance();
            Context.Orders.Add(Order);

            Context.SaveChanges();

            var orchestrator = new WhatsAppOrchestrationService(Sender, new WhatsAppOptions(),
                NullLogger<WhatsAppOrchestrationService>.Instance);

            Offers = new DeliveryOfferService(Context, Sender, new WhatsAppOptions(),
                new GeoOptions { MaxDistanceKm = 15 }, new GroupingOptions(), new ClientOptions(),
                orchestrator, new RiderSecurityOptions(), new RiderReputationOptions(),
                new ClientPaymentOptions(), new RiderPriorityOptions(),
                NullLogger<DeliveryOfferService>.Instance);
        }

        public DeliveryOffer NewPendingOffer()
        {
            var offer = new DeliveryOffer(Order.Id, Rider.Id, 1);
            Context.DeliveryOffers.Add(offer);
            Context.SaveChanges();
            return offer;
        }

        public int VendorCredits => Harness.Reload<User>(Vendor.Id)!.Credits;

        public void Dispose() => Harness.Dispose();
    }

    // ------------------------------------------------------------- Migrations

    [PostgresFact]
    public void Migrations_SAppliquentSurPostgresEtSontAPLusJour()
    {
        using var harness = new PostgresHarness();

        // Le harnais a appliqué les migrations : aucune ne doit rester en attente, et le schéma
        // doit exister réellement (une migration écrite pour SQLite échouerait ici).
        var pending = harness.Context!.Database.GetPendingMigrations().ToList();
        Assert.Empty(pending);

        var applied = harness.Context.Database.GetAppliedMigrations().Count();
        Assert.True(applied >= 30, $"seulement {applied} migration(s) appliquée(s) sur PostgreSQL");
    }

    // ------------------------------------------------------------- Argent

    [PostgresFact]
    public async Task AcceptOffer_SurPostgres_ReclameLOffreEtDebiteUneFois()
    {
        using var f = new Fixture();

        var offer = f.NewPendingOffer();
        Assert.Equal(3, f.VendorCredits);

        await f.Offers.AcceptOfferAsync(offer.Id);

        // Un crédit débité, une seule fois, et une offre réclamée : les trois écritures
        // reposent sur l'UPDATE conditionnel traduit par Npgsql.
        Assert.Equal(2, f.VendorCredits);
        Assert.Equal(DeliveryOfferStatus.Accepted, f.Harness.Reload<DeliveryOffer>(offer.Id)!.Status);
        Assert.Equal(f.Rider.Id, f.Harness.Reload<Order>(f.Order.Id)!.RiderUserId);
    }

    [PostgresFact]
    public async Task AcceptOffer_SurPostgres_OffreDejaPrise_NeDebitePasDeuxFois()
    {
        using var f = new Fixture();

        var offer = f.NewPendingOffer();
        await f.Offers.AcceptOfferAsync(offer.Id);

        // Deuxième acceptation de la MÊME offre : le perdant ne doit rien débiter ni assigner.
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Offers.AcceptOfferAsync(offer.Id));

        Assert.Equal(2, f.VendorCredits);
    }

    [PostgresFact]
    public async Task DebitVendeur_SurPostgres_SansCreditsSuffisants_RefuseSansDebiter()
    {
        using var f = new Fixture();

        // Solde vidé : le UPDATE conditionnel (`Credits >= n`) ne doit rien modifier et le
        // service doit refuser la course — jamais de solde négatif.
        var vendor = f.Context.Users.First(u => u.Id == f.Vendor.Id);
        for (var i = 0; i < 3; i++)
            vendor.TryConsumeCredit();
        f.Context.SaveChanges();
        Assert.Equal(0, f.VendorCredits);

        var offer = f.NewPendingOffer();
        await Assert.ThrowsAsync<Wazap.Application.Exceptions.PaymentRequiredException>(
            () => f.Offers.AcceptOfferAsync(offer.Id));

        Assert.Equal(0, f.VendorCredits);
        // L'offre reste réclamable : la transaction relationnelle a annulé la réclamation.
        Assert.Equal(DeliveryOfferStatus.Pending, f.Harness.Reload<DeliveryOffer>(offer.Id)!.Status);
    }

    [PostgresFact]
    public async Task CompletePurchase_SurPostgres_NEstIdempotentQuUneFois()
    {
        using var f = new Fixture();

        var transaction = new CreditTransaction(f.Vendor.Id, 10000m, 5, "TX-PG-1");
        f.Context.CreditTransactions.Add(transaction);
        f.Context.SaveChanges();

        var packs = new List<PackConfiguration>
        {
            new() { Name = "Pack test", Credits = 5, Price = 10000m }
        };

        var packService = new PackService(f.Context, new MockPaymentService(null), packs,
            new WhatsAppOrchestrationService(f.Sender, new WhatsAppOptions(),
                NullLogger<WhatsAppOrchestrationService>.Instance),
            NullLogger<PackService>.Instance);

        await packService.CompletePurchaseAsync(transaction.Id, "TX-PG-1");
        var afterFirst = f.VendorCredits;
        await packService.CompletePurchaseAsync(transaction.Id, "TX-PG-1");

        // La seconde complétion ne doit rien créditer de plus : c'est l'UPDATE conditionnel
        // sur le statut de la transaction (traduit par Npgsql) qui l'empêche.
        Assert.Equal(3 + 5, afterFirst);
        Assert.Equal(afterFirst, f.VendorCredits);
    }

    // ------------------------------------------------------------- Idempotence des webhooks

    [PostgresFact]
    public void MessageWebhookDejaTraite_SurPostgres_VioleLaContrainteDUnicite()
    {
        using var harness = new PostgresHarness();

        var context = harness.Context!;
        const string messageId = "wamid.PG-DOUBLON-1";

        context.ProcessedWebhookMessages.Add(new ProcessedWebhookMessage(messageId));
        context.SaveChanges();

        // Deux SESSIONS distinctes : c'est le cas réel de deux livraisons du même message Meta
        // (le suivi en mémoire du premier contexte transformerait le second ajout en UPDATE).
        using var secondSession = harness.NewContext();
        secondSession.ProcessedWebhookMessages.Add(new ProcessedWebhookMessage(messageId));
        Assert.Throws<DbUpdateException>(() => secondSession.SaveChanges());

        context.ChangeTracker.Clear();
        Assert.Single(context.ProcessedWebhookMessages.Where(m => m.Id == messageId));
    }
}
