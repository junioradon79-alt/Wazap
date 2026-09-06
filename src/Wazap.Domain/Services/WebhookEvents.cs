namespace Wazap.Domain.Services;

/// <summary>Noms d'événements des webhooks sortants (livrés via la file outbox).</summary>
public static class WebhookEvents
{
    public const string OrderCreated = "order.created";
    public const string OrderStatusChanged = "order.status_changed";
    public const string VendorRegistered = "vendor.registered";
    public const string RiderRegistered = "rider.registered";
    public const string CreditPurchased = "credit.purchased";

    public const string TypeWebhookDelivery = "WebhookDelivery";

    public static readonly string[] All = { OrderCreated, OrderStatusChanged, VendorRegistered, RiderRegistered, CreditPurchased };

    public static bool IsKnown(string eventName)
        => All.Contains(eventName, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Enveloppe d'un webhook sortant mise en file (payload d'un OutboxMessage de type
/// « WebhookDelivery ») puis livrée en HTTP par le worker.
/// </summary>
public sealed record WebhookDeliveryEnvelope(
    string Url,
    string? Secret,
    string Event,
    DateTime OccurredAt,
    object Data);
