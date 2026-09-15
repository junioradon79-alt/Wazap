namespace Wazap.Application.Configuration;

/// <summary>
/// Options de monitoring/alertes (section « Monitoring »).
/// </summary>
public sealed class MonitoringOptions
{
    public const string SectionName = "Monitoring";

    /// <summary>
    /// URL d'un webhook générique d'alerte (ex. Slack/Teams/ntfy/Gotify) recevant un POST JSON
    /// <c>{ type, message, at }</c>. Vide = pas d'envoi HTTP (alerte = log structuré uniquement).
    /// </summary>
    public string WebhookUrl { get; set; } = string.Empty;

    /// <summary>Anti-rebond : délai minimum entre deux alertes du même type (défaut 15 min).</summary>
    public int AlertCooldownMinutes { get; set; } = 15;

    /// <summary>
    /// Jeton optionnel protégeant <c>/metrics</c> (query <c>?token=</c> ou en-tête
    /// <c>X-Metrics-Token</c>, comparaison à temps constant). Vide = endpoint ouvert, comme
    /// historiquement : les métriques exposent la profondeur de la file d'échecs et l'uptime,
    /// utiles à un attaquant pour caler une opération. Renseignez-le et configurez le même
    /// jeton côté Prometheus/scraper.
    /// </summary>
    public string? MetricsToken { get; set; }
}
