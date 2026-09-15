using Microsoft.EntityFrameworkCore;
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
/// Tests des WORKERS de fond — diffusion des courses, rétention RGPD, purge GPS.
/// <para>
/// Ces services n'avaient <b>aucun test</b> : seule leur inscription dans le conteneur était
/// vérifiée. Leur logique porte pourtant des effets métier lourds — annulation des commandes
/// sans livreur, suppression des données de rétention, effacement des positions GPS.
/// </para>
/// <para>
/// Les cycles sont exécutés sur un fournisseur RELATIONNEL (SQLite) car la rétention et la
/// purge utilisent <c>ExecuteDelete</c>/<c>ExecuteUpdate</c>, que le fournisseur InMemory
/// refuse : c'est donc bien le code de production qui est exercé.
/// </para>
/// </summary>
public class WorkerCycleTests
{
    // ----------------------------------------------- Worker de diffusion des offres

    private sealed class OfferWorkerFixture
    {
        public SqliteHarness Harness { get; } = new();
        public ApplicationDbContext Context => Harness.Context;
        public User Vendor { get; }
        public User Rider { get; }
        public DeliveryOfferWorker Worker { get; }

        public OfferWorkerFixture(GeoOptions? geo = null, GroupingOptions? grouping = null)
        {
            Vendor = new User("vendeur", "hash", UserRole.Vendor, "+2250700000700");
            Vendor.SetZone("Cocody");
            Vendor.AddCredits(5);
            Vendor.UpdateLocation(5.3599, -4.0083);

            Rider = new User("livreur", "hash", UserRole.Rider, "+2250700000701");
            Rider.SetAvailability(true);
            Rider.UpdateLocation(5.3600, -4.0084);

            Context.Users.AddRange(Vendor, Rider);
            Context.SaveChanges();

            Worker = new DeliveryOfferWorker(
                new ServiceScopeFactoryStub(Context),
                NullLogger<DeliveryOfferWorker>.Instance,
                geo ?? new GeoOptions { MaxDistanceKm = 15 },
                grouping ?? new GroupingOptions { WindowMinutes = 2, MaxOrdersPerBatch = 5, BuyerDispatchDelaySeconds = 30 });
        }

        public Order NewConfirmedOrder(TimeSpan? age = null)
        {
            var order = new Order("Client", "+2250700000702", Vendor.PhoneNumber!, "1 colis", 3000m);
            order.LinkVendor(Vendor.Id);
            order.ConfirmByVendor();
            order.AwaitRiderAcceptance();
            Context.Orders.Add(order);
            Context.SaveChanges();

            if (age is { } offset)
            {
                // Vieillit la commande pour déclencher le délai global (le domaine pose CreatedAt).
                Context.Database.ExecuteSqlRaw(
                    "UPDATE \"Orders\" SET \"CreatedAt\" = {0} WHERE \"Id\" = {1}",
                    DateTime.UtcNow - offset, order.Id);
                Context.ChangeTracker.Clear();
            }

            return order;
        }
    }

    [Fact]
    public async Task DeliveryOfferWorker_AnnuleUneCommande_SansLivreur_AuDelaDuDelai()
    {
        var fixture = new OfferWorkerFixture(new GeoOptions { MaxDistanceKm = 15, GlobalTimeoutMinutes = 5 });
        using var _ = fixture.Harness;

        // Aucun livreur disponible → aucune offre ne sera créée : c'est précisément le cas où
        // l'ancien déclenchement (basé sur l'âge des OFFRES) n'atteignait JAMAIS le délai, et
        // où la commande restait bloquée indéfiniment.
        fixture.Rider.SetAvailability(false);
        fixture.Context.SaveChanges();

        var order = fixture.NewConfirmedOrder(age: TimeSpan.FromMinutes(30));

        await fixture.Worker.ProcessAsync(CancellationToken.None);

        var stored = fixture.Context.Orders.AsNoTracking().First(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Cancelled, stored.Status);
    }

    [Fact]
    public async Task DeliveryOfferWorker_NeTouchePasAUneCommande_Recente()
    {
        var fixture = new OfferWorkerFixture(new GeoOptions { MaxDistanceKm = 15, GlobalTimeoutMinutes = 5 });
        using var _ = fixture.Harness;

        fixture.Rider.SetAvailability(false);
        fixture.Context.SaveChanges();

        var order = fixture.NewConfirmedOrder(); // créée à l'instant

        await fixture.Worker.ProcessAsync(CancellationToken.None);

        var stored = fixture.Context.Orders.AsNoTracking().First(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.AwaitingRiderAcceptance, stored.Status);
    }

    [Fact]
    public async Task DeliveryOfferWorker_NAccordePasDeNouvelleVague_A_UNE_CommandeAcceptee()
    {
        var fixture = new OfferWorkerFixture();
        using var _ = fixture.Harness;

        var order = fixture.NewConfirmedOrder(age: TimeSpan.FromMinutes(30));

        // La commande est RECHARGÉE : le vieillissement SQL vide le change tracker, donc
        // l'instance retournée n'est plus suivie (une mutation dessus serait sans effet).
        var tracked = fixture.Context.Orders.First(o => o.Id == order.Id);
        tracked.AssignRider(fixture.Rider.PhoneNumber!);
        tracked.LinkRider(fixture.Rider.Id);
        fixture.Context.SaveChanges();

        await fixture.Worker.ProcessAsync(CancellationToken.None);

        // Une course déjà prise n'est jamais annulée ni rediffusée.
        var stored = fixture.Context.Orders.AsNoTracking().First(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.RiderAssigned, stored.Status);
    }

    // ------------------------------------------------------- Worker de rétention RGPD

    [Fact]
    public async Task RetentionWorker_PurgeLesDonneesAnciennes_SansEchouerSurLesPaiements()
    {
        using var harness = new SqliteHarness();
        var context = harness.Context;

        var vendor = new User("vendeur", "hash", UserRole.Vendor, "+2250700000800");
        context.Users.Add(vendor);

        // Commande livrée ANCIENNE, avec un paiement client : la FK en RESTRICT bloquait
        // auparavant TOUTE la passe de rétention (les scans d'identité n'étaient donc jamais
        // purgés — violation RGPD silencieuse).
        var oldOrder = new Order("Client", "+2250700000801", vendor.PhoneNumber!, "vieux colis", 2000m);
        oldOrder.LinkVendor(vendor.Id);
        oldOrder.ConfirmByVendor();
        oldOrder.AwaitRiderAcceptance();
        oldOrder.AssignRider("+2250700000802");
        oldOrder.MarkReadyForPickup();
        oldOrder.MarkPickedUp();
        oldOrder.MarkInTransit();
        oldOrder.MarkDelivered();
        context.Orders.Add(oldOrder);

        var payment = new OrderPayment(oldOrder.Id, 2000m);
        context.OrderPayments.Add(payment);

        // Commande livrée RÉCENTE : doit être conservée.
        var recentOrder = new Order("Client", "+2250700000803", vendor.PhoneNumber!, "colis récent", 3000m);
        recentOrder.LinkVendor(vendor.Id);
        recentOrder.ConfirmByVendor();
        recentOrder.AwaitRiderAcceptance();
        recentOrder.AssignRider("+2250700000804");
        recentOrder.MarkReadyForPickup();
        recentOrder.MarkPickedUp();
        recentOrder.MarkInTransit();
        recentOrder.MarkDelivered();
        context.Orders.Add(recentOrder);

        context.SaveChanges();

        // Vieillit la commande et son paiement au-delà de la fenêtre de rétention.
        context.Database.ExecuteSqlRaw(
            "UPDATE \"Orders\" SET \"DeliveredAt\" = {0} WHERE \"Id\" = {1}",
            DateTime.UtcNow.AddDays(-200), oldOrder.Id);
        context.ChangeTracker.Clear();

        var tempDir = Path.Combine(Path.GetTempPath(), "wazap-retention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var riderService = new RiderService(context, new FakeWebHostEnvironment(tempDir),
            new RecordingWhatsAppSender(), new RiderScansOptions { AllowUnencryptedStorage = true },
            NullLogger<RiderService>.Instance);

        var worker = new RetentionWorker(new ServiceScopeFactoryStub(context),
            new RetentionOptions { Enabled = true, DeliveredOrdersDays = 90, EmptyBatchesDays = 90, SentOutboxDays = 30, RiderScansDays = 90 },
            NullLogger<RetentionWorker>.Instance);

        await worker.PurgeAsync(context, riderService, CancellationToken.None);

        var remaining = context.Orders.AsNoTracking().Select(o => o.Id).ToList();
        Assert.DoesNotContain(oldOrder.Id, remaining);   // purgée
        Assert.Contains(recentOrder.Id, remaining);      // conservée
        Assert.Empty(context.OrderPayments.AsNoTracking()); // dépendance supprimée AVANT la commande

        Directory.Delete(tempDir, recursive: true);
    }

    // ---------------------------------------------------------- Worker de purge GPS

    [Fact]
    public async Task LocationPurgeWorker_EffaceLesPositionsPerimees_PasLesFraiches()
    {
        using var harness = new SqliteHarness();
        var context = harness.Context;

        var perime = new User("livreur-perime", "hash", UserRole.Rider, "+2250700000901");
        perime.UpdateLocation(5.36, -4.00);
        var frais = new User("livreur-frais", "hash", UserRole.Rider, "+2250700000902");
        frais.UpdateLocation(5.37, -4.01);
        var vendeur = new User("vendeur-geo", "hash", UserRole.Vendor, "+2250700000903");
        vendeur.UpdateLocation(5.38, -4.02); // un VENDEUR n'est jamais purgé (position statique)

        context.Users.AddRange(perime, frais, vendeur);
        context.SaveChanges();

        // Seule la position du livreur « périmé » est vieillie.
        context.Database.ExecuteSqlRaw(
            "UPDATE \"Users\" SET \"LocationUpdatedAt\" = {0} WHERE \"Id\" = {1}",
            DateTime.UtcNow.AddHours(-48), perime.Id);
        context.ChangeTracker.Clear();

        var worker = new LocationPurgeWorker(new ServiceScopeFactoryStub(context),
            NullLogger<LocationPurgeWorker>.Instance,
            new GeoOptions { LocationRetentionHours = 24 });

        var purged = await worker.PurgeAsync(context, CancellationToken.None);

        Assert.Equal(1, purged);

        context.ChangeTracker.Clear();
        Assert.Null(context.Users.AsNoTracking().First(u => u.Id == perime.Id).Latitude);
        Assert.NotNull(context.Users.AsNoTracking().First(u => u.Id == frais.Id).Latitude);
        Assert.NotNull(context.Users.AsNoTracking().First(u => u.Id == vendeur.Id).Latitude);
    }

    // ------------------------------------------------- Worker de réconciliation paiements

    [Fact]
    public async Task PaymentReconciliationWorker_CompleteUneTransaction_PayeeChezLAgregateur()
    {
        using var harness = new SqliteHarness();
        var context = harness.Context;

        var vendor = new User("vendeur", "hash", UserRole.Vendor, "+2250700001000");
        context.Users.Add(vendor);

        // Référence RÉELLE (pas le préfixe provisoire) : seule une transaction réellement
        // initiée chez l'agrégateur est réconciliée.
        var transaction = new CreditTransaction(vendor.Id, 2500m, 15, "GENIUS-REF-1");
        context.CreditTransactions.Add(transaction);
        context.SaveChanges();

        var gateway = new FakeClientPaymentGateway
        {
            NextStatus = new PaymentStatusResult("completed", null)
        };

        var worker = new PaymentReconciliationWorker(
            new ServiceScopeFactoryStub(context),
            new GeniusPayOptions { Enabled = true, ReconciliationMinutes = 5 },
            NullLogger<PaymentReconciliationWorker>.Instance);

        var sender = new RecordingWhatsAppSender();
        var orchestrator = new WhatsAppOrchestrationService(sender, new WhatsAppOptions(),
            NullLogger<WhatsAppOrchestrationService>.Instance);
        var packService = new PackService(context, gateway,
            new List<Wazap.Domain.Configuration.PackConfiguration>
            {
                new() { Name = "Découverte", Price = 2500, Credits = 15 }
            },
            orchestrator, NullLogger<PackService>.Instance);

        var offers = new DeliveryOfferService(context, sender, new WhatsAppOptions(),
            new GeoOptions(), new GroupingOptions(), new ClientOptions(), orchestrator,
            new RiderSecurityOptions(), new RiderReputationOptions(), new ClientPaymentOptions(),
            new RiderPriorityOptions(), NullLogger<DeliveryOfferService>.Instance);
        var clientPayments = new ClientPaymentService(context, gateway, sender, offers,
            new ClientPaymentOptions(), NullLogger<ClientPaymentService>.Instance);
        var priority = new RiderPriorityService(context, gateway,
            new List<Wazap.Domain.Configuration.RiderPriorityPackConfiguration>(),
            new RiderPriorityOptions(), orchestrator, NullLogger<RiderPriorityService>.Instance);

        await worker.ReconcileAsync(context, gateway, packService, clientPayments, priority, CancellationToken.None);

        // Le vendeur a été crédité sans intervention : c'est tout l'objet du rattrapage des
        // webhooks de paiement perdus.
        Assert.Equal(15, context.Users.AsNoTracking().First(u => u.Id == vendor.Id).Credits);
        Assert.Equal(TransactionStatus.Completed,
            context.CreditTransactions.AsNoTracking().First(t => t.Id == transaction.Id).Status);
    }
}
