using Wazap.Domain.Enums;

namespace Wazap.Domain.Entities;

public class OutboxMessage
{
    public Guid Id { get; private set; }
    public string Type { get; private set; } = default!;
    public string Payload { get; private set; } = default!;
    public OutboxStatus Status { get; private set; }
    public int RetryCount { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime AvailableAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }
    public string? LastError { get; private set; }

    private OutboxMessage() { }

    public OutboxMessage(string type, string payload)
    {
        Id = Guid.NewGuid();
        Type = type;
        Payload = payload;
        Status = OutboxStatus.Pending;
        CreatedAt = DateTime.UtcNow;
        AvailableAt = DateTime.UtcNow;
    }

    public void MarkSent()
    {
        Status = OutboxStatus.Sent;
        ProcessedAt = DateTime.UtcNow;
    }

    public void MarkRetry(string error, DateTime availableAt)
    {
        RetryCount++;
        LastError = error;
        AvailableAt = availableAt;
        Status = OutboxStatus.Pending;
    }

    public void MarkFailed(string error)
    {
        RetryCount++;
        LastError = error;
        Status = OutboxStatus.Failed;
        ProcessedAt = DateTime.UtcNow;
    }
}
