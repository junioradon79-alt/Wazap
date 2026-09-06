using Wazap.Domain.Entities;
using Wazap.Domain.Services;
using Xunit;

namespace Wazap.UnitTests;

public class WebhookSubscriberTests
{
    [Fact]
    public void Create_ShouldBeEnabled_WithCsvEvents()
    {
        var subscriber = new WebhookSubscriber("Partenaire A", "https://exemple.com/hook", "secret", ["order.created", "order.status_changed"]);
        Assert.Equal("order.created,order.status_changed", subscriber.Events);
        Assert.True(subscriber.Enabled);
    }

    [Theory]
    [InlineData("order.created", true)]
    [InlineData("ORDER.STATUS_CHANGED", true)]
    [InlineData("order.delivered", false)]
    public void Wants_ShouldMatchIgnoringCase(string eventName, bool expected)
    {
        var subscriber = new WebhookSubscriber("P", "https://exemple.com/hook", null, ["order.created", "order.status_changed"]);
        Assert.Equal(expected, subscriber.Wants(eventName));
    }

    [Fact]
    public void Update_And_Disable_ShouldApply()
    {
        var subscriber = new WebhookSubscriber("P", "https://exemple.com/hook", null, ["order.created"]);
        subscriber.Update("P2", "new-secret", ["order.status_changed"]);
        subscriber.SetEnabled(false);

        Assert.Equal("P2", subscriber.Name);
        Assert.Equal("new-secret", subscriber.Secret);
        Assert.Equal("order.status_changed", subscriber.Events);
        Assert.False(subscriber.Enabled);
    }

    [Fact]
    public void WebhookEvents_KnownNames()
    {
        Assert.True(WebhookEvents.IsKnown(WebhookEvents.OrderCreated));
        Assert.True(WebhookEvents.IsKnown(WebhookEvents.OrderStatusChanged));
        Assert.False(WebhookEvents.IsKnown("order.deleted"));
    }
}
