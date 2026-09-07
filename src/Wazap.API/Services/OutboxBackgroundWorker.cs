using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wazap.API.Health;
using Wazap.Application.Dtos;
using Wazap.Application.Exceptions;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Services;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

public sealed class OutboxBackgroundWorker : BackgroundService
{
    /// <summary>
    /// Réclamation atomique des messages dus. Passée à <c>SqlQueryRaw</c>, cette chaîne est
    /// un gabarit de composition : <c>{0}</c> devient un PARAMÈTRE SQL (taille du lot).
    /// Ne jamais y écrire <c>{nomDeVariable}</c> — la chaîne n'est pas interpolée et la
    /// requête échouerait à l'exécution (« Expected an ASCII digit »), silencieusement,
    /// l'outbox cessant alors de traiter le moindre message.
    /// </summary>
    internal const string ClaimSql = """
        SELECT "Id" FROM "OutboxMessages"
        WHERE "Status" = 1 AND "AvailableAt" <= NOW()
        ORDER BY "CreatedAt"
        LIMIT {0}
        FOR UPDATE SKIP LOCKED
        """;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxBackgroundWorker> _logger;
    private readonly int _maxRetries;
    private readonly int _batchSize;
    private readonly TimeSpan _pollingInterval;
    private DateTime _lastWatchUtc = DateTime.MinValue;

    public OutboxBackgroundWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxBackgroundWorker> logger,
        IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _maxRetries = config.GetValue("Outbox:MaxRetries", 5);
        _batchSize = Math.Max(1, config.GetValue("Outbox:BatchSize", 10));
        _pollingInterval = TimeSpan.FromSeconds(config.GetValue("Outbox:PollingIntervalSeconds", 5));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessPendingAsync(stoppingToken);
                WorkerHeartbeats.Beat(nameof(OutboxBackgroundWorker));

                // Watchdog des workers cœur métier (coût négligeable : ~1 fois/minute).
                var now = DateTime.UtcNow;
                if (now - _lastWatchUtc >= TimeSpan.FromSeconds(60))
                {
                    _lastWatchUtc = now;
                    await WatchWorkersAsync(stoppingToken);
                }

                if (!processed)
                    await Task.Delay(_pollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du traitement de l'outbox.");
                WorkerHeartbeats.Fail(nameof(OutboxBackgroundWorker), ex.Message);
                await Task.Delay(_pollingInterval, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Vérifie que les workers cœur métier battent toujours (voir <see cref="WorkerLagPolicy"/>)
    /// et déclenche une alerte (log + webhook optionnel) si l'un d'eux est bloqué/mort.
    /// </summary>
    private async Task WatchWorkersAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var alerts = scope.ServiceProvider.GetRequiredService<MonitoringAlertService>();
            var stale = WorkerLagPolicy.EvaluateStale(DateTime.UtcNow, WorkerHeartbeats.Snapshot());
            if (stale.Count > 0)
                await alerts.NotifyAsync("worker.stale", "Workers sans cycle récent : " + string.Join(" ; ", stale), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Watchdog des workers indisponible (cycle ignoré).");
        }
    }

    private async Task<bool> ProcessPendingAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<WhatsAppOrchestrationService>();
        var alerts = scope.ServiceProvider.GetRequiredService<MonitoringAlertService>();
        var httpFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

        // Réclamation atomique compatible multi-instances : les lignes sont verrouillées
        // (FOR UPDATE SKIP LOCKED) jusqu'au commit — deux instances ne traitent jamais
        // le même message en parallèle.
        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        var ids = await context.Database
            .SqlQueryRaw<Guid>(ClaimSql, _batchSize)
            .ToListAsync(ct);

        if (ids.Count == 0)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        var messages = await context.OutboxMessages
            .Where(m => ids.Contains(m.Id))
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        foreach (var message in messages)
        {
            try
            {
                if (message.Type == WebhookEvents.TypeWebhookDelivery)
                    await DeliverWebhookAsync(message, httpFactory, ct);
                else
                    await DeliverWhatsAppNotificationAsync(message, notificationService);

                message.MarkSent();
                _logger.LogInformation("Message outbox {MessageId} ({Type}) envoyé.", message.Id, message.Type);
            }
            catch (Exception ex) when (message.Type == WebhookEvents.TypeWebhookDelivery && ex is WebhookPermanentException)
            {
                // Rejet définitif du destinataire (400/401/403/404/405/410) → inutile de réessayer.
                message.MarkFailed(ex.Message);
                await alerts.NotifyAsync("webhook.failed", Truncate($"Livraison webhook en échec permanent : {ex.Message}"), ct);
            }
            catch (WhatsAppSendException ex) when (ex.IsPermanent)
            {
                // Template non approuvé, variables incorrectes, hors fenêtre 24 h : réessayer
                // ne changera rien. Échec immédiat + alerte, plutôt que 5 tentatives muettes.
                _logger.LogError(ex, "Envoi WhatsApp définitivement refusé pour le message outbox {MessageId}.", message.Id);
                message.MarkFailed(ex.Message);
                await alerts.NotifyAsync("whatsapp.failed",
                    $"Envoi WhatsApp refusé (message outbox {message.Id}) : {Truncate(ex.Message)}", ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Échec d'envoi du message outbox {MessageId} ({Type}, tentative {Retry}).",
                    message.Id, message.Type, message.RetryCount + 1);

                if (message.RetryCount >= _maxRetries)
                {
                    message.MarkFailed(ex.Message);
                    if (message.Type == WebhookEvents.TypeWebhookDelivery)
                        await alerts.NotifyAsync("webhook.failed", Truncate($"Livraison webhook en échec définitif après {_maxRetries + 1} tentatives : {ex.Message}"), ct);
                    else
                        await alerts.NotifyAsync("outbox.failed",
                            $"Message outbox {message.Id} en échec définitif après {_maxRetries + 1} tentatives : {Truncate(ex.Message)}", ct);
                }
                else
                    message.MarkRetry(ex.Message, DateTime.UtcNow.Add(NextRetryDelay(message.RetryCount, (ex as WebhookDeliveryException)?.RetryAfterSeconds)));
            }
        }

        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// Délai avant la prochaine tentative : honore l'en-tête <c>Retry-After</c> du destinataire
    /// (webhook) quand il est fourni, sinon backoff exponentiel borné avec un léger jitter.
    /// </summary>
    private static TimeSpan NextRetryDelay(int retryCount, int? retryAfterSeconds)
    {
        if (retryAfterSeconds is > 0)
            return TimeSpan.FromSeconds(Math.Min(retryAfterSeconds.Value, 3600));

        var baseSeconds = Math.Min(300, Math.Pow(2, retryCount) * 5);
        return TimeSpan.FromSeconds(baseSeconds * (0.9 + Random.Shared.NextDouble() * 0.2));
    }

    private static string Truncate(string? value, int maxLength = 400)
        => value is null ? string.Empty : value.Length <= maxLength ? value : value[..maxLength] + "…";

    // --- Canal WhatsApp (comportement historique) -------------------------------------
    private static async Task DeliverWhatsAppNotificationAsync(
        OutboxMessage message, WhatsAppOrchestrationService notificationService)
    {
        var notification = JsonSerializer.Deserialize<OrderCreatedNotification>(message.Payload)
            ?? throw new InvalidOperationException("Payload outbox invalide.");
        await notificationService.SendOrderCreatedNotificationAsync(notification);
    }

    // --- Canal webhooks sortants (intégrations partenaires) ----------------------------
    private static readonly JsonSerializerOptions WebhookJson = new(JsonSerializerDefaults.Web);

    private static async Task DeliverWebhookAsync(
        OutboxMessage message, IHttpClientFactory httpFactory, CancellationToken ct)
    {
        var envelope = JsonSerializer.Deserialize<WebhookDeliveryEnvelope>(message.Payload, WebhookJson)
            ?? throw new InvalidOperationException("Payload webhook invalide.");

        var body = JsonSerializer.Serialize(new
        {
            @event = envelope.Event,
            occurredAt = envelope.OccurredAt,
            data = envelope.Data
        }, WebhookJson);

        using var http = httpFactory.CreateClient("webhook-delivery");
        http.Timeout = TimeSpan.FromSeconds(10);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        // En-têtes utiles au destinataire : traçabilité (id de livraison/outbox), type d'événement,
        // horodatage et signature HMAC (si secret configuré).
        content.Headers.TryAddWithoutValidation("X-Wazap-Delivery", message.Id.ToString());
        content.Headers.TryAddWithoutValidation("X-Wazap-Event", envelope.Event);
        content.Headers.TryAddWithoutValidation("X-Wazap-Timestamp",
            new DateTimeOffset(envelope.OccurredAt).ToUnixTimeSeconds().ToString());
        if (!string.IsNullOrWhiteSpace(envelope.Secret))
            content.Headers.TryAddWithoutValidation("X-Wazap-Signature", "sha256=" + Sign(envelope.Secret!, body));

        using var response = await http.PostAsync(envelope.Url, content, ct);
        if (response.IsSuccessStatusCode)
            return;

        var code = (int)response.StatusCode;
        if (code is 400 or 401 or 403 or 404 or 405 or 410)
            throw new WebhookPermanentException($"{envelope.Url} a rejeté l'événement {envelope.Event} (HTTP {code}).");

        // Erreurs temporaires (429/5xx/408…) : on repart de l'en-tête Retry-After si présent.
        if (code is 408 or 425 or 429 or 500 or 502 or 503 or 504)
        {
            int? retryAfterSeconds = null;
            if (response.Headers.RetryAfter is { } retryAfter)
            {
                if (retryAfter.Delta is { } delta)
                    retryAfterSeconds = (int)Math.Min(delta.TotalSeconds, 3600);
                else if (retryAfter.Date is { } date)
                    retryAfterSeconds = (int)Math.Max(0, (date.ToUniversalTime() - DateTime.UtcNow).TotalSeconds);
            }

            throw new WebhookDeliveryException(
                $"{envelope.Url} temporairement indisponible pour {envelope.Event} (HTTP {code}).", retryAfterSeconds);
        }

        throw new HttpRequestException($"Webhook {envelope.Url} : HTTP {code}.");
    }

    private static string Sign(string secret, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }
}

/// <summary>Rejet HTTP permanent d'un destinataire webhook (pas de nouvelle tentative).</summary>
public sealed class WebhookPermanentException : Exception
{
    public WebhookPermanentException(string message) : base(message) { }
}

/// <summary>Erreur HTTP temporaire d'un destinataire webhook (nouvelle tentative plus tard).</summary>
public sealed class WebhookDeliveryException : Exception
{
    public WebhookDeliveryException(string message, int? retryAfterSeconds = null) : base(message)
        => RetryAfterSeconds = retryAfterSeconds;

    /// <summary>Valeur de l'en-tête Retry-After du destinataire (secondes), si fournie.</summary>
    public int? RetryAfterSeconds { get; }
}
