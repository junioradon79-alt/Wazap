namespace Wazap.Domain.Services;

/// <summary>Noms d'événements des webhooks sortants (livrés via la file outbox).</summary>
public static class WebhookEvents
{
    // Commandes
    public const string OrderCreated = "order.created";
    public const string OrderStatusChanged = "order.status_changed";

    // Acteurs
    public const string VendorRegistered = "vendor.registered";
    public const string RiderRegistered = "rider.registered";
    public const string RiderCertified = "rider.certified";

    // Crédits / Paiements
    public const string CreditPurchased = "credit.purchased";
    public const string ClientPaymentCompleted = "client_payment.completed";
    public const string ClientPaymentFailed = "client_payment.failed";

    // Sinistres (Colis Sûr)
    public const string ClaimFiled = "claim.filed";
    public const string ClaimResolved = "claim.resolved";

    public const string TypeWebhookDelivery = "WebhookDelivery";

    public static readonly string[] All = {
        OrderCreated, OrderStatusChanged,
        VendorRegistered, RiderRegistered, RiderCertified,
        CreditPurchased, ClientPaymentCompleted, ClientPaymentFailed,
        ClaimFiled, ClaimResolved
    };

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
