using Microsoft.EntityFrameworkCore;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Health;

/// <summary>
/// Point d'entrée unique des métriques de supervision exposées par <c>/health/details</c> :
/// accès base, file outbox (pending/retry/failed) et battements des workers. Le endpoint
/// <c>/health</c> du framework reste minimal (compatible sondes/scripts existants).
/// </summary>
public sealed class HealthDetailsService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public HealthDetailsService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<object> BuildAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var uptime = Math.Max(0, (now - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds);
        var details = new Dictionary<string, object?>
        {
            ["at"] = now.ToString("O"),
            ["uptimeSeconds"] = Math.Round(uptime, 0)
        };

        await ProbeDatabaseAndOutboxAsync(details, ct);
        AddWorkerDetails(details, now);

        // Statut de synthèse : healthy sauf si la base est injoignable ou l'outbox a des échecs.
        details["status"] = details.TryGetValue("outbox", out var outbox)
                && outbox is OutboxSnapshot { Failed: > 0 }
            ? "degraded"
            : details.TryGetValue("database", out var database) && !Equals(database, "ok")
                ? "degraded"
                : "healthy";

        return details;
    }

    private async Task ProbeDatabaseAndOutboxAsync(IDictionary<string, object?> details, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            if (!await db.Database.CanConnectAsync(ct))
            {
                details["database"] = "unreachable";
                return;
            }

            details["database"] = "ok";

            var now = DateTime.UtcNow;
            var pendingDue = await db.OutboxMessages
                .CountAsync(m => m.Status == OutboxStatus.Pending && m.AvailableAt <= now, ct);
            var retrying = await db.OutboxMessages
                .CountAsync(m => m.Status == OutboxStatus.Pending && m.AvailableAt > now, ct);
            var failed = await db.OutboxMessages
                .CountAsync(m => m.Status == OutboxStatus.Failed, ct);

            details["outbox"] = new OutboxSnapshot(pendingDue, retrying, failed);
        }
        catch (Exception ex)
        {
            details["database"] = "error: " + ex.Message;
        }
    }

    private static void AddWorkerDetails(IDictionary<string, object?> details, DateTime now)
    {
        var workers = WorkerHeartbeats.Snapshot().ToDictionary(
            beat => beat.Name,
            beat =>
            {
                var lagSeconds = Math.Max(0, (now - beat.LastBeatUtc).TotalSeconds);
                return beat.LastError is null
                    ? $"dernier cycle il y a {lagSeconds:0}s"
                    : $"dernier cycle il y a {lagSeconds:0}s (erreur : {beat.LastError})";
            },
            StringComparer.Ordinal);

        details["workers"] = workers;
    }

    public sealed record OutboxSnapshot(int PendingDue, int Retrying, int Failed);
}
