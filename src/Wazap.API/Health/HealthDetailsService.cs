using Microsoft.EntityFrameworkCore;
using Wazap.Application.Configuration;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Health;

/// <summary>
/// Point d'entrée unique des métriques de supervision exposées par <c>/health/details</c> :
/// accès base, file outbox (pending/retry/failed), battements des workers et témoin de
/// conformité RGPD (chiffrement des scans d'identité, purge de rétention). Le endpoint
/// <c>/health</c> du framework reste minimal (compatible sondes/scripts existants).
/// </summary>
public sealed class HealthDetailsService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RiderScansOptions _scans;
    private readonly RetentionOptions _retention;
    private readonly ClientPaymentOptions _clientPayments;

    public HealthDetailsService(
        IServiceScopeFactory scopeFactory,
        RiderScansOptions scans,
        RetentionOptions retention,
        ClientPaymentOptions clientPayments)
    {
        _scopeFactory = scopeFactory;
        _scans = scans;
        _retention = retention;
        _clientPayments = clientPayments;
    }

    /// <param name="includeSensitiveDetail">
    /// Réservé aux administrateurs authentifiés. <c>/health/details</c> est ouvert (sondes de
    /// disponibilité) : le détail de conformité nommerait publiquement la faiblesse exacte
    /// (« clé absente »), ce qui renseignerait un attaquant. Les anonymes n'obtiennent que
    /// le statut de synthèse.
    /// </param>
    public async Task<object> BuildAsync(bool includeSensitiveDetail = false, CancellationToken ct = default)
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
        AddComplianceDetails(details, includeSensitiveDetail);
        AddClientPaymentDetails(details);

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

    /// <summary>
    /// Témoin de conformité RGPD sur les données d'identité des livreurs. Volontairement
    /// séparé du <c>status</c> opérationnel : une protection incomplète est un problème de
    /// conformité, pas une panne — les sondes de disponibilité ne doivent pas s'en alarmer,
    /// mais le fait doit être visible sans avoir à ouvrir le web.config du serveur.
    /// </summary>
    private void AddComplianceDetails(IDictionary<string, object?> details, bool includeSensitiveDetail)
    {
        var scanStatus = _scans.GetStatus();
        var scans = scanStatus switch
        {
            ScanProtectionStatus.Encrypted => "chiffrés au repos (AES-GCM)",
            ScanProtectionStatus.UnencryptedAllowed =>
                "NON CHIFFRÉS — autorisé explicitement (RiderScans:AllowUnencryptedStorage=true)",
            ScanProtectionStatus.MissingKey =>
                "clé absente (RiderScans:EncryptionKey) — téléversements refusés",
            ScanProtectionStatus.InvalidKey =>
                "clé invalide (RiderScans:EncryptionKey) — téléversements refusés",
            _ => "état inconnu"
        };

        var retention = _retention.Enabled
            ? _retention.RiderScansDays > 0
                ? $"active — scans d'identité purgés {_retention.RiderScansDays} j après décision"
                : "active, mais les scans d'identité ne sont JAMAIS purgés (RiderScansDays=0)"
            : "INACTIVE (Retention:Enabled=false) — aucune purge, scans d'identité conservés sans limite";

        // « ok » exige les deux protections : chiffrer sans purger, ou purger sans chiffrer,
        // ne suffit pas — ce sont deux obligations distinctes sur la même donnée.
        var compliant = scanStatus == ScanProtectionStatus.Encrypted
                        && _retention.Enabled
                        && _retention.RiderScansDays > 0;

        details["compliance"] = new ComplianceSnapshot(
            compliant ? "ok" : "attention",
            includeSensitiveDetail ? scans : null,
            includeSensitiveDetail ? retention : null);
    }

    private void AddClientPaymentDetails(IDictionary<string, object?> details)
    {
        details["clientPayments"] = new
        {
            enabled = _clientPayments.Enabled,
            requireBeforeDispatch = _clientPayments.RequirePaymentBeforeDispatch,
            commissionPercent = _clientPayments.CommissionPercent
        };
    }

    public sealed record OutboxSnapshot(int PendingDue, int Retrying, int Failed);

    public sealed record ComplianceSnapshot(string Status, string? RiderScans, string? Retention);
}
