using Microsoft.EntityFrameworkCore;
using Wazap.API.Health;
using Wazap.Application.Configuration;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

/// <summary>
/// Rétention / archivage des données (opt-in via <c>Retention:Enabled</c>) :
/// purge les commandes livrées anciennes (+ leurs offres), les lots vides anciens et
/// les messages outbox envoyés. Logging des volumes purgés à chaque passage.
/// </summary>
public sealed class RetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RetentionOptions _retention;
    private readonly ILogger<RetentionWorker> _logger;

    public RetentionWorker(
        IServiceScopeFactory scopeFactory,
        RetentionOptions retention,
        ILogger<RetentionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _retention = retention;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_retention.Enabled)
        {
            _logger.LogInformation("Rétention désactivée (Retention:Enabled=false).");
            return;
        }

        var intervalHours = Math.Max(1, _retention.RunIntervalHours);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(intervalHours));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Multi-instances : une seule instance purge à la fois (verrou advisory de session).
            await using var guard = await AdvisoryLockScope.TryAcquireAsync(db, 77_002, stoppingToken);
            if (!guard.Acquired)
            {
                _logger.LogDebug("Rétention sautée (une autre instance la réalise).");
                continue;
            }

            try
            {
                await PurgeAsync(db, stoppingToken);
                await guard.CompleteAsync(stoppingToken);
                WorkerHeartbeats.Beat(nameof(RetentionWorker));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la purge de rétention.");
                WorkerHeartbeats.Fail(nameof(RetentionWorker), ex.Message);
            }
        }
    }

    private async Task PurgeAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var ordersCutoff = now.AddDays(-Math.Max(0, _retention.DeliveredOrdersDays));
        var batchesCutoff = now.AddDays(-Math.Max(0, _retention.EmptyBatchesDays));
        var outboxCutoff = now.AddDays(-Math.Max(0, _retention.SentOutboxDays));

        // 1. Commandes livrées anciennes (leur historique d'offres est supprimé avec elles).
        var oldOrderIds = await db.Orders
            .Where(o => o.Status == OrderStatus.Delivered && o.DeliveredAt < ordersCutoff)
            .Select(o => o.Id)
            .ToListAsync(ct);

        var ordersPurged = 0;
        if (oldOrderIds.Count > 0)
        {
            await db.DeliveryOffers
                .Where(x => x.OrderId != null && oldOrderIds.Contains(x.OrderId.Value))
                .ExecuteDeleteAsync(ct);
            ordersPurged = await db.Orders
                .Where(o => oldOrderIds.Contains(o.Id))
                .ExecuteDeleteAsync(ct);
        }

        // 2. Lots vides anciens (aucune commande restante) + leurs offres de lot.
        var oldBatchIds = await db.DeliveryBatches
            .Where(b => b.CreatedAt < batchesCutoff && !db.Orders.Any(o => o.BatchId == b.Id))
            .Select(b => b.Id)
            .ToListAsync(ct);

        var batchesPurged = 0;
        if (oldBatchIds.Count > 0)
        {
            await db.DeliveryOffers
                .Where(x => x.BatchId != null && oldBatchIds.Contains(x.BatchId.Value))
                .ExecuteDeleteAsync(ct);
            batchesPurged = await db.DeliveryBatches
                .Where(b => oldBatchIds.Contains(b.Id))
                .ExecuteDeleteAsync(ct);
        }

        // 3. Messages outbox envoyés (statistiques conservées dans les logs, payload inutile ensuite).
        var outboxPurged = await db.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Sent && m.CreatedAt < outboxCutoff)
            .ExecuteDeleteAsync(ct);

        if (ordersPurged + batchesPurged + outboxPurged > 0)
            _logger.LogInformation(
                "Rétention : {Orders} commande(s), {Batches} lot(s) vide(s), {Outbox} message(s) outbox purgés.",
                ordersPurged, batchesPurged, outboxPurged);
    }
}
