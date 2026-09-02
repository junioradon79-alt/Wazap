using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Domain.Entities;

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

            modelBuilder.Entity<DeliveryOffer>()
                .HasIndex(o => o.OrderId);

            modelBuilder.Entity<DeliveryOffer>()
                .HasIndex(o => o.BatchId);

            modelBuilder.Entity<DeliveryOffer>()
                .HasIndex(o => o.Status);

            modelBuilder.Entity<DeliveryBatch>()
                .HasIndex(b => b.VendorUserId);

            modelBuilder.Entity<DeliveryBatch>()
                .HasIndex(b => b.Status);

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
        }
    }
}