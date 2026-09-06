using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wazap.Application.Abstractions;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Domain.Services;

namespace Wazap.Infrastructure.Data
{
    public class ApplicationDbContext : DbContext, IApplicationDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<Order> Orders { get; set; }
        public DbSet<OutboxMessage> OutboxMessages { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<DeliveryOffer> DeliveryOffers { get; set; }
        public DbSet<DeliveryBatch> DeliveryBatches { get; set; }
        public DbSet<CreditTransaction> CreditTransactions { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<WebhookSubscriber> WebhookSubscribers { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Order>()
                .Property(o => o.Amount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Order>()
                .HasIndex(o => o.Status);

            modelBuilder.Entity<Order>()
                .HasIndex(o => o.CreatedAt);

            modelBuilder.Entity<Order>()
                .HasIndex(o => o.BatchId);

            // Index composites pour les requêtes métier fréquentes (listes par acteur + statut,
            // statistiques/archivage par statut + date de livraison).
            modelBuilder.Entity<Order>()
                .HasIndex(o => new { o.VendorUserId, o.Status });

            modelBuilder.Entity<Order>()
                .HasIndex(o => new { o.RiderUserId, o.Status });

            modelBuilder.Entity<Order>()
                .HasIndex(o => new { o.Status, o.DeliveredAt });

            modelBuilder.Entity<Order>()
                .HasOne<DeliveryBatch>()
                .WithMany(b => b.Orders)
                .HasForeignKey(o => o.BatchId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<OutboxMessage>()
                .HasIndex(m => new { m.Status, m.AvailableAt });

            modelBuilder.Entity<User>()
                .Property(u => u.Username)
                .HasMaxLength(50);

            modelBuilder.Entity<User>()
                .Property(u => u.PhoneNumber)
                .HasMaxLength(30);

            modelBuilder.Entity<User>()
                .Property(u => u.Zone)
                .HasMaxLength(50);

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.PhoneNumber);

            modelBuilder.Entity<User>()
                .HasIndex(u => u.IsAvailable);

            // Recherche des livreurs/vendeurs par rôle (+ disponibilité pour le matching géo).
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Role);

            modelBuilder.Entity<User>()
                .HasIndex(u => new { u.Role, u.IsAvailable });

            modelBuilder.Entity<DeliveryOffer>()
                .HasIndex(o => o.OrderId);

            modelBuilder.Entity<DeliveryOffer>()
                .HasIndex(o => o.BatchId);

            modelBuilder.Entity<DeliveryOffer>()
                .HasIndex(o => o.Status);

            // Historique des offres par livreur + statut (dashboard, relances, purge).
            modelBuilder.Entity<DeliveryOffer>()
                .HasIndex(o => new { o.RiderUserId, o.Status });

            modelBuilder.Entity<DeliveryBatch>()
                .HasIndex(b => b.VendorUserId);

            modelBuilder.Entity<DeliveryBatch>()
                .HasIndex(b => b.Status);

            // Lots ouverts par ancienneté (timeout global, purge des lots vides anciens).
            modelBuilder.Entity<DeliveryBatch>()
                .HasIndex(b => new { b.Status, b.CreatedAt });

            modelBuilder.Entity<RefreshToken>()
                .HasIndex(r => r.ExpiresAtUtc);

            modelBuilder.Entity<CreditTransaction>()
                .Property(t => t.Amount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<CreditTransaction>()
                .Property(t => t.TransactionReference)
                .HasMaxLength(100);

            modelBuilder.Entity<CreditTransaction>()
                .Property(t => t.PackName)
                .HasMaxLength(100);

            modelBuilder.Entity<CreditTransaction>()
                .HasIndex(t => t.VendorId);

            modelBuilder.Entity<CreditTransaction>()
                .HasIndex(t => t.CreatedAt);

            modelBuilder.Entity<CreditTransaction>()
                .HasOne(t => t.Vendor)
                .WithMany(u => u.Transactions)
                .HasForeignKey(t => t.VendorId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RefreshToken>()
                .Property(r => r.TokenHash)
                .HasMaxLength(64);

            modelBuilder.Entity<RefreshToken>()
                .HasIndex(r => r.UserId);

            modelBuilder.Entity<WebhookSubscriber>()
                .Property(s => s.Name)
                .HasMaxLength(100);

            modelBuilder.Entity<WebhookSubscriber>()
                .Property(s => s.Url)
                .HasMaxLength(500);

            modelBuilder.Entity<WebhookSubscriber>()
                .Property(s => s.Secret)
                .HasMaxLength(200);

            modelBuilder.Entity<WebhookSubscriber>()
                .Property(s => s.Events)
                .HasMaxLength(500);

            modelBuilder.Entity<WebhookSubscriber>()
                .HasIndex(s => s.Url)
                .IsUnique();
        }

        // --- Webhooks sortants : détection des événements commande à la sauvegarde ----------
        // Point d'appel UNIQUE (aucun contrôleur/service à modifier) : quand une commande est
        // créée ou change de statut dans ce contexte, on ajoute dans la MÊME transaction un
        // OutboxMessage « WebhookDelivery » par abonné concerné (livraison fiable, retries).
        private static readonly JsonSerializerOptions WebhookJson = new(JsonSerializerDefaults.Web);

        private void QueueWebhookDeliveries()
        {
            var now = DateTime.UtcNow;
            var events = new List<(string Event, object Data)>();

            foreach (var entry in ChangeTracker.Entries())
            {
                var state = entry.State;
                switch (entry.Entity)
                {
                    case Order order when state == EntityState.Added:
                        events.Add((WebhookEvents.OrderCreated, new
                        {
                            orderId = order.Id, status = order.Status.ToString(),
                            createdAt = order.CreatedAt, amount = order.Amount
                        }));
                        break;

                    case Order order when state == EntityState.Modified
                                          && OriginalStatusOf(entry) is { } before && before != order.Status:
                        events.Add((WebhookEvents.OrderStatusChanged, new
                        {
                            orderId = order.Id, from = before.ToString(),
                            to = order.Status.ToString(), at = now
                        }));
                        break;

                    case User user when state == EntityState.Added && user.Role == UserRole.Vendor:
                        events.Add((WebhookEvents.VendorRegistered, new
                        { userId = user.Id, zone = user.Zone, createdAt = user.CreatedAt }));
                        break;

                    case User user when state == EntityState.Added && user.Role == UserRole.Rider:
                        events.Add((WebhookEvents.RiderRegistered, new
                        { userId = user.Id, zone = user.Zone, createdAt = user.CreatedAt }));
                        break;

                    case CreditTransaction txn when
                        (state == EntityState.Added && txn.Status == TransactionStatus.Completed)
                        || (state == EntityState.Modified
                            && OriginalTransactionStatus(entry) == TransactionStatus.Pending
                            && txn.Status == TransactionStatus.Completed):
                        events.Add((WebhookEvents.CreditPurchased, new
                        {
                            transactionId = txn.Id, vendorId = txn.VendorId, packName = txn.PackName,
                            credits = txn.CreditsPurchased, amount = txn.Amount, createdAt = txn.CreatedAt
                        }));
                        break;
                }
            }

            if (events.Count == 0)
                return;

            List<WebhookSubscriber> subscribers;
            try
            {
                subscribers = WebhookSubscribers.Where(s => s.Enabled).ToList();
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01") // relation inexistante (migration en attente)
            {
                // Migration « AddWebhookSubscribers » pas encore appliquée sur cette base :
                // on n'émet pas de webhook pour l'instant (sans casser les sauvegardes).
                return;
            }

            if (subscribers.Count == 0)
                return;

            foreach (var (eventName, data) in events)
            {
                foreach (var subscriber in subscribers)
                {
                    if (!subscriber.Wants(eventName))
                        continue;

                    var envelope = new WebhookDeliveryEnvelope(subscriber.Url, subscriber.Secret, eventName, now, data);
                    OutboxMessages.Add(new OutboxMessage(
                        WebhookEvents.TypeWebhookDelivery,
                        JsonSerializer.Serialize(envelope, WebhookJson)));
                }
            }
        }

        private static OrderStatus? OriginalStatusOf(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
        {
            var raw = entry.OriginalValues["Status"];
            return raw switch
            {
                OrderStatus status => status,
                int i => (OrderStatus)i,
                _ => null
            };
        }

        private static TransactionStatus? OriginalTransactionStatus(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
        {
            var raw = entry.OriginalValues["Status"];
            return raw switch
            {
                TransactionStatus status => status,
                int i => (TransactionStatus)i,
                _ => null
            };
        }

        public override int SaveChanges()
        {
            QueueWebhookDeliveries();
            return base.SaveChanges();
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            QueueWebhookDeliveries();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            QueueWebhookDeliveries();
            return base.SaveChangesAsync(cancellationToken);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            QueueWebhookDeliveries();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }
}