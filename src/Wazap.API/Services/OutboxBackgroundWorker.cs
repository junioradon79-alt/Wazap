using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wazap.Application.Dtos;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

public sealed class OutboxBackgroundWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxBackgroundWorker> _logger;
    private readonly int _maxRetries;
    private readonly TimeSpan _pollingInterval;

    public OutboxBackgroundWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxBackgroundWorker> logger,
        IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _maxRetries = config.GetValue("Outbox:MaxRetries", 5);
        _pollingInterval = TimeSpan.FromSeconds(config.GetValue("Outbox:PollingIntervalSeconds", 5));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessPendingAsync(stoppingToken);
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
                await Task.Delay(_pollingInterval, stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessPendingAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notificationService = scope.ServiceProvider.GetRequiredService<WhatsAppOrchestrationService>();

        // Réclamation atomique compatible multi-instances : les lignes sont verrouillées
        // (FOR UPDATE SKIP LOCKED) jusqu'au commit — deux instances ne traitent jamais
        // le même message en parallèle.
        await using var transaction = await context.Database.BeginTransactionAsync(ct);

        var ids = await context.Database
            .SqlQueryRaw<Guid>("""
                SELECT "Id" FROM "OutboxMessages"
                WHERE "Status" = 1 AND "AvailableAt" <= NOW()
                ORDER BY "CreatedAt"
                LIMIT 10
                FOR UPDATE SKIP LOCKED
                """)
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
                var notification = JsonSerializer.Deserialize<OrderCreatedNotification>(message.Payload)
                    ?? throw new InvalidOperationException("Payload outbox invalide.");

                await notificationService.SendOrderCreatedNotificationAsync(notification);

                message.MarkSent();
                _logger.LogInformation("Message outbox {MessageId} envoyé.", message.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Échec d'envoi du message outbox {MessageId} (tentative {Retry}).", message.Id, message.RetryCount + 1);

                if (message.RetryCount >= _maxRetries)
                    message.MarkFailed(ex.Message);
                else
                    message.MarkRetry(ex.Message, DateTime.UtcNow.Add(Backoff(message.RetryCount)));
            }
        }

        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private static TimeSpan Backoff(int retryCount) =>
        TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, retryCount) * 5));
}
