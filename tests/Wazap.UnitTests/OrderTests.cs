using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Xunit;

namespace Wazap.UnitTests;

public class OrderTests
{
    private static Order CreateOrder() =>
        new("Client", "123", "456", "Description", 10m);

    [Fact]
    public void NewOrder_ShouldBePendingVendorConfirmation()
    {
        var order = CreateOrder();
        Assert.Equal(OrderStatus.PendingVendorConfirmation, order.Status);
    }

    [Fact]
    public void ConfirmByVendor_ShouldTransitionToVendorConfirmed()
    {
        var order = CreateOrder();
        order.ConfirmByVendor();
        Assert.Equal(OrderStatus.VendorConfirmed, order.Status);
        Assert.NotNull(order.VendorConfirmedAt);
    }

    [Fact]
    public void ConfirmByVendor_WhenAlreadyConfirmed_ShouldThrow()
    {
        var order = CreateOrder();
        order.ConfirmByVendor();
        Assert.Throws<InvalidOperationException>(() => order.ConfirmByVendor());
    }

    [Fact]
    public void AwaitRiderAcceptance_AfterConfirm_ShouldTransition()
    {
        var order = CreateOrder();
        order.ConfirmByVendor();
        order.AwaitRiderAcceptance();
        Assert.Equal(OrderStatus.AwaitingRiderAcceptance, order.Status);
    }

    [Fact]
    public void AssignRider_ShouldSetRiderAndStatus()
    {
        var order = CreateOrder();
        order.ConfirmByVendor();
        order.AwaitRiderAcceptance();
        order.AssignRider("789");
        Assert.Equal(OrderStatus.RiderAssigned, order.Status);
        Assert.Equal("789", order.RiderWhatsAppNumber);
    }

    [Fact]
    public void AssignRider_WhenNotAwaiting_ShouldThrow()
    {
        var order = CreateOrder();
        Assert.Throws<InvalidOperationException>(() => order.AssignRider("789"));
    }

    [Fact]
    public void Cancel_WhenPending_ShouldCancel()
    {
        var order = CreateOrder();
        order.Cancel();
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.NotNull(order.CancelledAt);
    }

    [Fact]
    public void JoinBatch_ShouldSetBatchId()
    {
        var order = CreateOrder();
        var batchId = Guid.NewGuid();

        order.JoinBatch(batchId);

        Assert.Equal(batchId, order.BatchId);
    }

    [Fact]
    public void Cancel_WhenDelivered_ShouldThrow()
    {
        var order = CreateOrder();
        order.ConfirmByVendor();
        order.AwaitRiderAcceptance();
        order.AssignRider("789");
        order.MarkReadyForPickup();
        order.MarkPickedUp();
        order.MarkInTransit();
        order.MarkDelivered();
        Assert.Throws<InvalidOperationException>(() => order.Cancel());
    }
}
