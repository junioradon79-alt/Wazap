using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Xunit;

namespace Wazap.UnitTests;

public class DeliveryOfferTests
{
    [Fact]
    public void NewOffer_ShouldBePending_WithBatchAndTimestamps()
    {
        var orderId = Guid.NewGuid();
        var riderId = Guid.NewGuid();
        var offer = new DeliveryOffer(orderId, riderId, 2);

        Assert.Equal(orderId, offer.OrderId);
        Assert.Equal(riderId, offer.RiderUserId);
        Assert.Equal(2, offer.BatchNumber);
        Assert.Equal(DeliveryOfferStatus.Pending, offer.Status);
        Assert.Null(offer.RespondedAt);
        Assert.NotEqual(default, offer.SentAt);
    }

    [Fact]
    public void Accept_PendingOffer_ShouldSetAcceptedAndRespondedAt()
    {
        var offer = new DeliveryOffer(Guid.NewGuid(), Guid.NewGuid(), 1);
        offer.Accept();
        Assert.Equal(DeliveryOfferStatus.Accepted, offer.Status);
        Assert.NotNull(offer.RespondedAt);
    }

    [Fact]
    public void Accept_NonPendingOffer_ShouldThrow()
    {
        var offer = new DeliveryOffer(Guid.NewGuid(), Guid.NewGuid(), 1);
        offer.Expire();
        Assert.Throws<InvalidOperationException>(() => offer.Accept());
    }

    [Fact]
    public void Decline_And_Expire_ShouldTransitionFromPendingOnly()
    {
        var declined = new DeliveryOffer(Guid.NewGuid(), Guid.NewGuid(), 1);
        declined.Decline();
        Assert.Equal(DeliveryOfferStatus.Declined, declined.Status);

        var expired = new DeliveryOffer(Guid.NewGuid(), Guid.NewGuid(), 1);
        expired.Expire();
        Assert.Equal(DeliveryOfferStatus.Expired, expired.Status);

        Assert.Throws<InvalidOperationException>(() => expired.Expire());
    }
}
