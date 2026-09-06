using System.Collections.Concurrent;
using System.Text.Json;
using Wazap.Application.Configuration;

namespace Wazap.API.Services;

/// <summary>
/// Alertes de supervision, best-effort et anti-rebond :
/// 1) toujours un log structuré (niveau Warning) — exploitable par n'importe quel collecteur ;
/// 2) optionnellement un POST JSON vers <see cref="MonitoringOptions.WebhookUrl"/>
///    (Slack/Teams/ntfy/Gotify…). Ne lève jamais d'exception (fire-and-forget borné).
/// </summary>
public sealed class MonitoringAlertService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MonitoringOptions _options;
    private readonly ILogger<MonitoringAlertService> _logger;
    private readonly ConcurrentDictionary<string, DateTime> _lastSentByType = new();

    public MonitoringAlertService(
        IHttpClientFactory httpClientFactory,
        MonitoringOptions options,
        ILogger<MonitoringAlertService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task NotifyAsync(string type, string message, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var cooldown = TimeSpan.FromMinutes(Math.Max(1, _options.AlertCooldownMinutes));

        // Anti-rebond : on n'alerte qu'une fois par type et par fenêtre.
        if (!_lastSentByType.TryGetValue(type, out var lastSent) || now - lastSent >= cooldown)
            _lastSentByType[type] = now;
        else
            return;

        _logger.LogWarning("ALERTE [{Type}] {Message}", type, message);

        if (string.IsNullOrWhiteSpace(_options.WebhookUrl))
            return;

        try
        {
            var payload = JsonSerializer.Serialize(new { type, message, at = now.ToString("O") });
            using var client = _httpClientFactory.CreateClient("monitoring");
            client.Timeout = TimeSpan.FromSeconds(10);
            using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            var response = await client.PostAsync(_options.WebhookUrl, content, ct);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("Webhook d'alerte {Type} : HTTP {(int)response.StatusCode}.", type, (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Webhook d'alerte {Type} injoignable.", type);
        }
    }
}
