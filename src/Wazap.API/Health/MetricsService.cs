using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Health;

/// <summary>
/// Exposition Prometheus minimaliste au format texte (0.0.4) sur <c>/metrics</c>,
/// sans dépendance externe : processus, base, file outbox et workers.
/// </summary>
public sealed class MetricsService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public MetricsService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<string> BuildTextAsync(CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        var now = DateTime.UtcNow;
        var inv = CultureInfo.InvariantCulture;

        sb.AppendLine("# HELP wazap_up 1 si le processus répond.");
        sb.AppendLine("# TYPE wazap_up gauge");
        sb.AppendLine("wazap_up 1");

        var uptime = Math.Max(0, (now - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds);
        sb.AppendLine("# HELP wazap_uptime_seconds Durée de fonctionnement du processus (s).");
        sb.AppendLine("# TYPE wazap_uptime_seconds gauge");
        sb.AppendLine("wazap_uptime_seconds " + uptime.ToString("0.0", inv));

        // Base + file outbox.
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (await db.Database.CanConnectAsync(ct))
            {
                sb.AppendLine("# HELP wazap_database_ok 1 si la base répond.");
                sb.AppendLine("# TYPE wazap_database_ok gauge");
                sb.AppendLine("wazap_database_ok 1");

                var pendingDue = await db.OutboxMessages.CountAsync(m => m.Status == OutboxStatus.Pending && m.AvailableAt <= now, ct);
                var retrying = await db.OutboxMessages.CountAsync(m => m.Status == OutboxStatus.Pending && m.AvailableAt > now, ct);
                var failed = await db.OutboxMessages.CountAsync(m => m.Status == OutboxStatus.Failed, ct);

                sb.AppendLine("# HELP wazap_outbox_pending_due Messages outbox en attente (disponibles).");
                sb.AppendLine("# TYPE wazap_outbox_pending_due gauge");
                sb.AppendLine("wazap_outbox_pending_due " + pendingDue.ToString(inv));
                sb.AppendLine("# HELP wazap_outbox_retrying Messages outbox en backoff.");
                sb.AppendLine("# TYPE wazap_outbox_retrying gauge");
                sb.AppendLine("wazap_outbox_retrying " + retrying.ToString(inv));
                sb.AppendLine("# HELP wazap_outbox_failed Messages outbox en échec définitif.");
                sb.AppendLine("# TYPE wazap_outbox_failed gauge");
                sb.AppendLine("wazap_outbox_failed " + failed.ToString(inv));
            }
            else
            {
                sb.AppendLine("# HELP wazap_database_ok 1 si la base répond.");
                sb.AppendLine("# TYPE wazap_database_ok gauge");
                sb.AppendLine("wazap_database_ok 0");
            }
        }
        catch (Exception)
        {
            sb.AppendLine("# HELP wazap_database_ok 1 si la base répond.");
            sb.AppendLine("# TYPE wazap_database_ok gauge");
            sb.AppendLine("wazap_database_ok 0");
        }

        // Workers : lag du dernier cycle (secondes).
        sb.AppendLine("# HELP wazap_worker_last_cycle_seconds Secondes depuis le dernier cycle du worker.");
        sb.AppendLine("# TYPE wazap_worker_last_cycle_seconds gauge");
        foreach (var beat in WorkerHeartbeats.Snapshot())
        {
            var lag = Math.Max(0, (now - beat.LastBeatUtc).TotalSeconds);
            sb.Append("wazap_worker_last_cycle_seconds{worker=\"")
              .Append(beat.Name)
              .Append("\"} ")
              .AppendLine(lag.ToString("0.0", inv));
        }

        return sb.ToString();
    }
}
