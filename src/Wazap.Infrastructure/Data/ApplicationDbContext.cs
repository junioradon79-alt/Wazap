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
        public DbSet<VendorProduct> VendorProducts { get; set; }          // NOUVELLE
        public DbSet<OrderLine> OrderLines { get; set; }                  // NOUVELLE
        public DbSet<DeliveryOffer> DeliveryOffers { get; set; }
        public DbSet<DeliveryBatch> DeliveryBatches { get; set; }
        public DbSet<CreditTransaction> CreditTransactions { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<WebhookSubscriber> WebhookSubscribers { get; set; }
        public DbSet<Lead> Leads { get; set; }
        public DbSet<RiderIdentity> RiderIdentities { get; set; }
        public DbSet<DeliveryClaim> DeliveryClaims { get; set; }
        public DbSet<RiderRating> RiderRatings { get; set; }
        public DbSet<OrderPayment> OrderPayments { get; set; }
        public DbSet<ClientOrderDraft> ClientOrderDrafts { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Order>()
                .Property(o => o.Amount)
                .HasPrecision(18, 2);

            modelBuilder.Entity<Order>()
                .Property(o => o.DeliveryCode)
                .HasMaxLength(4);

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

            // --- Catalogue produits WAZAP : VendorProduct ---
            modelBuilder.Entity<VendorProduct>(entity =>
            {
                entity.Property(p => p.Name).HasMaxLength(100);
                entity.Property(p => p.Description).HasMaxLength(300);
                entity.Property(p => p.Emoji).HasMaxLength(10);
                entity.Property(p => p.Price).HasPrecision(18, 2);

                entity.HasIndex(p => p.VendorId);
            });

            // --- Lignes de commande : OrderLine ---
            modelBuilder.Entity<OrderLine>(entity =>
            {
                entity.Property(l => l.ProductName).HasMaxLength(100);
                entity.Property(l => l.ProductEmoji).HasMaxLength(10);
                entity.Property(l => l.ProductDescription).HasMaxLength(300);
                entity.Property(l => l.UnitPrice).HasPrecision(18, 2);

                entity.HasIndex(l => l.OrderId);
                entity.HasIndex(l => l.VendorProductId);

                entity.HasOne(l => l.Order)
                    .WithMany(o => o.OrderLines)
                    .HasForeignKey(l => l.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(l => l.VendorProduct)
                    .WithMany(p => p.OrderLines)
                    .HasForeignKey(l => l.VendorProductId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Paiements client (panier Mobile Money) : montants exacts + index de service.
            modelBuilder.Entity<OrderPayment>(entity =>
            {
                entity.Property(p => p.Amount).HasPrecision(18, 2);
                entity.Property(p => p.CommissionAmount).HasPrecision(18, 2);
                entity.Property(p => p.VendorPayoutDue).HasPrecision(18, 2);
                entity.Property(p => p.TransactionReference).HasMaxLength(200);
                entity.Property(p => p.PaymentLink).HasMaxLength(1000);

                entity.HasIndex(p => p.OrderId);
                entity.HasIndex(p => new { p.Status, p.CreatedAt });

                entity.HasOne(p => p.Order)
                    .WithMany()
                    .HasForeignKey(p => p.OrderId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<OutboxMessage>()
                .HasIndex(m => new { m.Status, m.AvailableAt });

            // Brouillons de commande du bot WhatsApp conversationnel (clients) :
            // une conversation par numéro, index client + étape pour la reprise.
            modelBuilder.Entity<ClientOrderDraft>(entity =>
            {
                entity.Property(d => d.ClientWhatsAppNumber).HasMaxLength(30);
                entity.Property(d => d.Description).HasMaxLength(500);
                entity.Property(d => d.Address).HasMaxLength(300);
                entity.Property(d => d.VendorCandidates).HasMaxLength(2000);
                entity.Property(d => d.ProductLineIds).HasMaxLength(2000);
                entity.Property(d => d.SelectedProductIds).HasMaxLength(2000);

                entity.HasIndex(d => d.ClientWhatsAppNumber);
                entity.HasIndex(d => new { d.ClientWhatsAppNumber, d.Stage });
            });

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

            // Certification des livreurs (1:1 User → RiderIdentity).
            modelBuilder.Entity<RiderIdentity>()
                .HasKey(i => i.UserId);

            modelBuilder.Entity<User>()
                .HasOne(u => u.RiderIdentity)
                .WithOne()
                .HasForeignKey<RiderIdentity>(i => i.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RiderIdentity>()
                .Property(i => i.FullName)
                .HasMaxLength(80);

            modelBuilder.Entity<RiderIdentity>()
                .Property(i => i.IdNumber)
                .HasMaxLength(40);

            modelBuilder.Entity<RiderIdentity>()
                .Property(i => i.Motorcycle)
                .HasMaxLength(80);

            modelBuilder.Entity<RiderIdentity>()
                .Property(i => i.IdScanUrl)
                .HasMaxLength(500);

            modelBuilder.Entity<RiderIdentity>()
                .Property(i => i.ScanFileName)
                .HasMaxLength(120);

            modelBuilder.Entity<RiderIdentity>()
                .Property(i => i.BlacklistReason)
                .HasMaxLength(300);

            modelBuilder.Entity<RiderIdentity>()
                .HasIndex(i => i.Status);

            // Dossiers de sinistre « Garantie Colis Sûr » : un seul dossier par commande.
            // Une seule note par commande : le client note la course, pas le livreur en général.
            modelBuilder.Entity<RiderRating>()
                .HasIndex(r => r.OrderId)
                .IsUnique();

            // Moyenne par livreur (profil affiché au vendeur, filtre de réputation).
            modelBuilder.Entity<RiderRating>()
                .HasIndex(r => r.RiderUserId);

            modelBuilder.Entity<RiderRating>()
                .Property(r => r.ClientWhatsAppNumber)
                .HasMaxLength(30);

            modelBuilder.Entity<RiderRating>()
                .Property(r => r.Comment)
                .HasMaxLength(300);

            modelBuilder.Entity<RiderRating>()
                .Property(r => r.Reply)
                .HasMaxLength(500);

            modelBuilder.Entity<DeliveryClaim>()
                .HasIndex(c => c.OrderId)
                .IsUnique();

            modelBuilder.Entity<DeliveryClaim>()
                .HasIndex(c => c.Status);

            modelBuilder.Entity<DeliveryClaim>()
                .Property(c => c.VendorNote)
                .HasMaxLength(500);

            modelBuilder.Entity<DeliveryClaim>()
                .Property(c => c.ReviewNote)
                .HasMaxLength(300);

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

            modelBuilder.Entity<Lead>()
                .Property(l => l.BusinessName)
                .HasMaxLength(120);

            modelBuilder.Entity<Lead>()
                .Property(l => l.ContactName)
                .HasMaxLength(80);

            modelBuilder.Entity<Lead>()
                .Property(l => l.WhatsAppNumber)
                .HasMaxLength(20);

            modelBuilder.Entity<Lead>()
                .Property(l => l.Zone)
                .HasMaxLength(60);

            modelBuilder.Entity<Lead>()
                .Property(l => l.Source)
                .HasMaxLength(40);

            modelBuilder.Entity<Lead>()
                .HasIndex(l => new { l.Status, l.CreatedAt });
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

            // Paiements client (commandes Mobile Money)
            foreach (var entry in ChangeTracker.Entries<OrderPayment>())
            {
                var payment = entry.Entity;
                if (entry.State == EntityState.Modified)
                {
                    var originalStatus = OriginalPaymentStatus(entry);
                    if (originalStatus == TransactionStatus.Pending && payment.Status == TransactionStatus.Completed)
                    {
                        events.Add((WebhookEvents.ClientPaymentCompleted, new
                        {
                            paymentId = payment.Id,
                            orderId = payment.OrderId,
                            amount = payment.Amount,
                            commissionAmount = payment.CommissionAmount,
                            vendorPayoutDue = payment.VendorPayoutDue,
                            transactionReference = payment.TransactionReference,
                            completedAt = payment.CompletedAt
                        }));
                    }
                    else if (originalStatus == TransactionStatus.Pending && payment.Status == TransactionStatus.Failed)
                    {
                        events.Add((WebhookEvents.ClientPaymentFailed, new
                        {
                            paymentId = payment.Id,
                            orderId = payment.OrderId,
                            amount = payment.Amount,
                            errorMessage = payment.ErrorMessage,
                            failedAt = payment.CompletedAt
                        }));
                    }
                }
            }

            // Certification livreurs
            foreach (var entry in ChangeTracker.Entries<RiderIdentity>())
            {
                if (entry.State == EntityState.Modified)
                {
                    var identity = entry.Entity;
                    if (identity.Status == RiderIdentityStatus.Verified)
                    {
                        events.Add((WebhookEvents.RiderCertified, new
                        {
                            riderUserId = identity.UserId,
                            fullName = identity.FullName,
                            verifiedAt = identity.ReviewedAt
                        }));
                    }
                }
            }

            // Sinistres Colis Sûr
            foreach (var entry in ChangeTracker.Entries<DeliveryClaim>())
            {
                var claim = entry.Entity;
                if (entry.State == EntityState.Added)
                {
                    events.Add((WebhookEvents.ClaimFiled, new
                    {
                        claimId = claim.Id,
                        orderId = claim.OrderId,
                        vendorId = claim.VendorUserId,
                        riderId = claim.RiderUserId,
                        status = claim.Status.ToString(),
                        createdAt = claim.CreatedAt
                    }));
                }
                else if (entry.State == EntityState.Modified && claim.Status != DeliveryClaimStatus.Pending)
                {
                    events.Add((WebhookEvents.ClaimResolved, new
                    {
                        claimId = claim.Id,
                        orderId = claim.OrderId,
                        status = claim.Status.ToString(),
                        compensationCredits = claim.CompensationCredits,
                        compensationAmountFcfa = claim.CompensationAmountFcfa,
                        payoutStatus = claim.PayoutStatus.ToString(),
                        reviewedAt = claim.ReviewedAt
                    }));
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

        private static TransactionStatus? OriginalPaymentStatus(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
        {
            var raw = entry.OriginalValues["Status"];
            return raw switch
            {
                TransactionStatus status => status,
                int i => (TransactionStatus)i,
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