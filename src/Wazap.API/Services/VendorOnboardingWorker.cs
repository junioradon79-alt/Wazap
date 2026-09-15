using Microsoft.EntityFrameworkCore;
using Wazap.API.Health;
using Wazap.Application.Configuration;
using Wazap.Application.Services;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

/// <summary>
/// Onboarding vendeur séquencé (opt-in via <c>VendorOnboarding:Enabled</c>) : envoie les
/// étapes J+1 / J+3 / J+7 aux nouveaux vendeurs via templates WhatsApp approuvés.
/// Chaque compte vendeur porte sa prochaine échéance (<c>OnboardingNextAtUtc</c>) ;
/// si le template de l'étape n'est pas encore configuré, l'étape est reprogrammée (+6 h)
/// sans être perdue.
/// </summary>
public sealed class VendorOnboardingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly VendorOnboardingOptions _options;
    private readonly ILogger<VendorOnboardingWorker> _logger;

    public VendorOnboardingWorker(
        IServiceScopeFactory scopeFactory,
        VendorOnboardingOptions options,
        ILogger<VendorOnboardingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Onboarding vendeur désactivé (VendorOnboarding:Enabled=false).");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // Tout le cycle sous try/catch (acquisition du verrou comprise) : une erreur de
            // base transitoire ne doit pas remonter hors d'ExecuteAsync — le comportement
            // par défaut de l'hôte .NET est d'arrêter l'application entière.
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Clé advisory DÉDIÉE (77_004). Elle était partagée avec PaymentReconciliationWorker
                // (77_003) : les deux workers se bloquaient mutuellement leur cycle et le sautaient
                // en silence — la réconciliation des paiements et l'onboarding vendeur ne
                // s'exécutaient donc jamais quand l'autre tournait, sans erreur visible.
                await using var guard = await AdvisoryLockScope.TryAcquireAsync(db, 77_004, stoppingToken);
                if (!guard.Acquired)
                {
                    _logger.LogDebug("Onboarding vendeur sauté (une autre instance le réalise).");
                    continue;
                }

                var processed = await ProcessDueAsync(db, scope, stoppingToken);
                await guard.CompleteAsync(stoppingToken);
                if (processed > 0)
                    _logger.LogInformation("Onboarding vendeur : {Count} étape(s) envoyée(s).", processed);
                WorkerHeartbeats.Beat(nameof(VendorOnboardingWorker));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'onboarding vendeur.");
                WorkerHeartbeats.Fail(nameof(VendorOnboardingWorker), ex.Message);
            }
        }
    }

    internal async Task<int> ProcessDueAsync(ApplicationDbContext db, IServiceScope scope, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var due = await db.Users
            .Where(u => u.Role == UserRole.Vendor
                     && u.OnboardingStage != null
                     && u.OnboardingNextAtUtc != null
                     && u.OnboardingNextAtUtc <= now)
            .OrderBy(u => u.OnboardingNextAtUtc)
            .Take(30)
            .ToListAsync(ct);

        if (due.Count == 0)
            return 0;

        var orchestrator = scope.ServiceProvider.GetRequiredService<WhatsAppOrchestrationService>();
        var processed = 0;

        foreach (var vendor in due)
        {
            var stage = vendor.OnboardingStage!.Value;
            if (stage is < 1 or > 3)
            {
                vendor.CompleteVendorOnboarding();
                continue;
            }

            var delivered = await db.Orders
                .CountAsync(o => o.VendorUserId == vendor.Id && o.Status == OrderStatus.Delivered, ct);

            try
            {
                var sent = await orchestrator.TrySendVendorOnboardingAsync(vendor, stage, delivered.ToString());
                if (!sent)
                {
                    // Template de l'étape non configuré/approuvé : on réessaiera plus tard.
                    vendor.RetryVendorOnboarding();
                    continue;
                }

                if (stage >= 3)
                    vendor.CompleteVendorOnboarding();
                else
                    vendor.AdvanceVendorOnboarding(vendor.CreatedAt);

                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Étape J+{Stage} impossible pour {Vendor} — nouvelle tentative dans 6 h.",
                    stage, vendor.Username);
                vendor.RetryVendorOnboarding();
            }
        }

        await db.SaveChangesAsync(ct);
        return processed;
    }
}
