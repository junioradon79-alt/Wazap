using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Xunit;

namespace Wazap.UnitTests;

public class DeliveryBatchTests
{
    [Fact]
    public void NewBatch_ShouldBeOpen_WithVendorAndTimestamp()
    {
        var vendorId = Guid.NewGuid();
        var batch = new DeliveryBatch(vendorId);

        Assert.Equal(vendorId, batch.VendorUserId);
        Assert.Equal(DeliveryBatchStatus.Open, batch.Status);
        Assert.Null(batch.RiderUserId);
        Assert.Null(batch.RiderWhatsAppNumber);
        Assert.Null(batch.AssignedAt);
        Assert.NotEqual(default, batch.CreatedAt);
    }

    [Fact]
    public void AssignRider_OpenBatch_ShouldSetRiderAndStatus()
    {
        var batch = new DeliveryBatch(Guid.NewGuid());
        var riderId = Guid.NewGuid();

        batch.AssignRider(riderId, "+2250708091011");

        Assert.Equal(riderId, batch.RiderUserId);
        Assert.Equal("+2250708091011", batch.RiderWhatsAppNumber);
        Assert.Equal(DeliveryBatchStatus.Assigned, batch.Status);
        Assert.NotNull(batch.AssignedAt);
    }

    [Fact]
    public void AssignRider_AlreadyAssigned_ShouldThrow()
    {
        var batch = new DeliveryBatch(Guid.NewGuid());
        batch.AssignRider(Guid.NewGuid(), "+2250708091011");

        Assert.Throws<InvalidOperationException>(() => batch.AssignRider(Guid.NewGuid(), "+2250708091012"));
    }

    [Fact]
    public void Cancel_OpenBatch_ShouldSetCancelled()
    {
        var batch = new DeliveryBatch(Guid.NewGuid());

        batch.Cancel();

        Assert.Equal(DeliveryBatchStatus.Cancelled, batch.Status);
    }

    [Fact]
    public void AssignRider_AfterCancel_ShouldThrow()
    {
        var batch = new DeliveryBatch(Guid.NewGuid());
        batch.Cancel();

        Assert.Throws<InvalidOperationException>(() => batch.AssignRider(Guid.NewGuid(), "+2250708091011"));
    }

    [Fact]
    public void Cancel_AssignedBatch_ShouldThrow()
    {
        var batch = new DeliveryBatch(Guid.NewGuid());
        batch.AssignRider(Guid.NewGuid(), "+2250708091011");

        Assert.Throws<InvalidOperationException>(() => batch.Cancel());
    }

    [Fact]
    public void Cancel_AlreadyCancelled_ShouldThrow()
    {
        var batch = new DeliveryBatch(Guid.NewGuid());
        batch.Cancel();

        Assert.Throws<InvalidOperationException>(() => batch.Cancel());
    }
}
