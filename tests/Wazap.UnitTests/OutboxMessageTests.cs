using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Xunit;

namespace Wazap.UnitTests;

public class OutboxMessageTests
{
    [Fact]
    public void NewMessage_ShouldBePending()
    {
        var message = new OutboxMessage("OrderCreated", "{}");
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal(0, message.RetryCount);
    }

    [Fact]
    public void MarkSent_ShouldSetSentAndProcessedAt()
    {
        var message = new OutboxMessage("OrderCreated", "{}");
        message.MarkSent();
        Assert.Equal(OutboxStatus.Sent, message.Status);
        Assert.NotNull(message.ProcessedAt);
    }

    [Fact]
    public void MarkRetry_ShouldIncrementRetryAndStayPending()
    {
        var message = new OutboxMessage("OrderCreated", "{}");
        message.MarkRetry("error", DateTime.UtcNow.AddSeconds(5));
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal(1, message.RetryCount);
        Assert.Equal("error", message.LastError);
    }

    [Fact]
    public void MarkFailed_ShouldSetFailed()
    {
        var message = new OutboxMessage("OrderCreated", "{}");
        message.MarkFailed("error");
        Assert.Equal(OutboxStatus.Failed, message.Status);
        Assert.Equal(1, message.RetryCount);
        Assert.Equal("error", message.LastError);
    }
}
