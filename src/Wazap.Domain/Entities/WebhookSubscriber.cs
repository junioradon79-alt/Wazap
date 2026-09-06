namespace Wazap.Domain.Entities;

/// <summary>
/// Abonné aux webhooks sortants (intégrations partenaires). Le secret (optionnel) sert à
/// signer les livraisons (HMAC-SHA256) ; « Events » est une liste CSV d'événements
/// (voir <see cref="Wazap.Domain.Services.WebhookEvents"/>).
/// </summary>
public class WebhookSubscriber
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = default!;
    public string Url { get; private set; } = default!;
    public string? Secret { get; private set; }
    public string Events { get; private set; } = default!;
    public bool Enabled { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private WebhookSubscriber() { }

    public WebhookSubscriber(string name, string url, string? secret, IEnumerable<string> events)
    {
        Id = Guid.NewGuid();
        Name = name;
        Url = url;
        Secret = string.IsNullOrWhiteSpace(secret) ? null : secret;
        Events = string.Join(",", events.Distinct(StringComparer.OrdinalIgnoreCase));
        Enabled = true;
        CreatedAt = DateTime.UtcNow;
    }

    public void Update(string name, string? secret, IEnumerable<string> events)
    {
        Name = name;
        Secret = string.IsNullOrWhiteSpace(secret) ? null : secret;
        Events = string.Join(",", events.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public void SetEnabled(bool enabled) => Enabled = enabled;

    public bool Wants(string eventName)
        => Events.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(eventName, StringComparer.OrdinalIgnoreCase);
}
