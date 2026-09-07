using Wazap.Application.Configuration;
using Wazap.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Tests de la « Garantie Colis Sûr » (ColisSurService) : déclaration de sinistre
/// (livreur certifié requis), approbation (remboursement + indemnisation + exclusion),
/// rejet (dossier clos, livreur non exclu).
/// </summary>
public class ColisSurServiceTests
{
    private static ColisSurService CreateService(TestDbContext db, RecordingWhatsAppSender sender)
        => new(db.Context, sender, new ConfigStub(), new ColisSurOptions(),
            new ManualPayoutService(NullLogger<ManualPayoutService>.Instance),
            NullLogger<ColisSurService>.Instance);

    private static User CreateVendor(TestDbContext db, string username = "vendeur", string phone = "+2250700000001")
    {
        var vendor = new User(username, "hash", UserRole.Vendor, phone);
        db.Context.Users.Add(vendor);
        return vendor;
    }

    private static async Task<(User Vendor, User Rider)> SeedCertifiedRiderAsync(TestDbContext db)
    {
        var vendor = CreateVendor(db);
        var rider = new User("rider", "hash", UserRole.Rider, "+2250700000002");
        db.Context.Users.Add(rider);
        var identity = new RiderIdentity(rider.Id, "Livreur Un", "CI123456", "Moto rouge");
        identity.Verify("Livreur Un", "CI123456", "Moto rouge", reviewerId: null);
        db.Context.RiderIdentities.Add(identity);
        await db.Context.SaveChangesAsync();
        return (vendor, rider);
    }

    private static Order NewAssignedOrder(Guid vendorUserId, Guid riderUserId)
    {
        var order = new Order("Client Test", "+2250700000003", "+2250700000001", "Colis précieux", 5000m);
        order.ConfirmByVendor();
        order.AwaitRiderAcceptance();
        order.AssignRider("+2250700000002");
        order.LinkVendor(vendorUserId);
        order.LinkRider(riderUserId);
        return order;
    }

    [Fact]
    public async Task Declare_WithoutCode_ReturnsFormatMessage()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var (vendor, rider) = await SeedCertifiedRiderAsync(db);
        context.Orders.Add(NewAssignedOrder(vendor.Id, rider.Id));
        await context.SaveChangesAsync();

        var sender = new RecordingWhatsAppSender();
        var service = CreateService(db, sender);

        var result = await service.DeclareAsync(vendor.Id, "SINISTRE");

        Assert.False(result.Success);
        Assert.Contains("SINISTRE", result.Message);
        Assert.Empty(context.DeliveryClaims);
    }

    [Fact]
    public async Task Declare_WithUncertifiedRider_ReturnsGarantieNotApplicable()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var vendor = CreateVendor(db);
        var rider = new User("rider", "hash", UserRole.Rider, "+2250700000002");
        context.Users.Add(rider);
        await context.SaveChangesAsync();

        var order = NewAssignedOrder(vendor.Id, rider.Id);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var sender = new RecordingWhatsAppSender();
        var service = CreateService(db, sender);

        var code = order.Id.ToString("N")[..8];
        var result = await service.DeclareAsync(vendor.Id, $"SINISTRE {code}");

        Assert.False(result.Success);
        Assert.Contains("pas certifié", result.Message);
        Assert.Empty(context.DeliveryClaims);
    }

    [Fact]
    public async Task Declare_CertifiedRider_AddsPendingClaim()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var (vendor, rider) = await SeedCertifiedRiderAsync(db);
        var order = NewAssignedOrder(vendor.Id, rider.Id);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var sender = new RecordingWhatsAppSender();
        var service = CreateService(db, sender);

        var code = order.Id.ToString("N")[..8];
        var result = await service.DeclareAsync(vendor.Id, $"SINISTRE {code}");

        Assert.True(result.Success);
        var claim = Assert.Single(context.DeliveryClaims);
        Assert.Equal(order.Id, claim.OrderId);
        Assert.Equal(DeliveryClaimStatus.Pending, claim.Status);
        Assert.Equal(rider.Id, claim.RiderUserId);
    }

    [Fact]
    public async Task Declare_SecondTimeForSameOrder_IsRejected()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var (vendor, rider) = await SeedCertifiedRiderAsync(db);
        var order = NewAssignedOrder(vendor.Id, rider.Id);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var sender = new RecordingWhatsAppSender();
        var service = CreateService(db, sender);

        var code = order.Id.ToString("N")[..8];
        await service.DeclareAsync(vendor.Id, $"SINISTRE {code}");
        var second = await service.DeclareAsync(vendor.Id, $"SINISTRE {code}");

        Assert.False(second.Success);
        Assert.Contains("déjà", second.Message);
        Assert.Single(context.DeliveryClaims);
    }

    [Fact]
    public async Task Approve_RefundsAndCompensates_AndExcludesRider()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var (vendor, rider) = await SeedCertifiedRiderAsync(db);
        var order = NewAssignedOrder(vendor.Id, rider.Id);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var sender = new RecordingWhatsAppSender();
        var service = CreateService(db, sender);

        var code = order.Id.ToString("N")[..8];
        var declared = await service.DeclareAsync(vendor.Id, $"SINISTRE {code}");
        Assert.True(declared.Success);

        var claim = context.DeliveryClaims.Single();
        await service.ApproveAsync(claim.Id, compensationCredits: 5, note: "Colis perdu confirmé",
            reviewerId: Guid.NewGuid());

        // 1 crédit remboursé + 5 d'indemnisation.
        Assert.Equal(6, vendor.Credits);
        Assert.Contains(context.CreditTransactions, t => t.TransactionReference.Contains("REFUND") && t.CreditsPurchased == 1);
        Assert.Contains(context.CreditTransactions, t => t.TransactionReference.Contains("COMP") && t.CreditsPurchased == 5);

        var storedClaim = await context.DeliveryClaims.FindAsync(claim.Id);
        Assert.Equal(DeliveryClaimStatus.Approved, storedClaim!.Status);
        Assert.Equal(5, storedClaim.CompensationCredits);

        var identity = await context.RiderIdentities.FindAsync(rider.Id);
        Assert.Equal(RiderIdentityStatus.Blacklisted, identity!.Status);
        Assert.False(rider.IsAvailable);
        Assert.Contains(sender.TextMessages, m => m.Phone == vendor.PhoneNumber && m.Message.Contains("CONFIRMÉ"));
    }

    [Fact]
    public async Task Reject_ClosesClaim_WithoutExcludingRider()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var (vendor, rider) = await SeedCertifiedRiderAsync(db);
        var order = NewAssignedOrder(vendor.Id, rider.Id);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var sender = new RecordingWhatsAppSender();
        var service = CreateService(db, sender);

        var code = order.Id.ToString("N")[..8];
        await service.DeclareAsync(vendor.Id, $"SINISTRE {code}");
        var claim = context.DeliveryClaims.Single();

        await service.RejectAsync(claim.Id, "Livraison confirmée par le client", reviewerId: Guid.NewGuid());

        var storedClaim = await context.DeliveryClaims.FindAsync(claim.Id);
        Assert.Equal(DeliveryClaimStatus.Rejected, storedClaim!.Status);
        Assert.Null(storedClaim.CompensationCredits);

        var identity = await context.RiderIdentities.FindAsync(rider.Id);
        Assert.Equal(RiderIdentityStatus.Verified, identity!.Status);
        Assert.Equal(0, vendor.Credits);
    }
}
