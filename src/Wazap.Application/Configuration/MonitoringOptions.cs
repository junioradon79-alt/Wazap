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
}
