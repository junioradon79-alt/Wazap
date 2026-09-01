using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services
{
    /// <summary>
    /// Réconciliation des paiements : si un webhook GeniusPay a été perdu (serveur down,
    /// latence), une transaction reste Pending indéfiniment. Ce worker interroge le statut
    /// des transactions Pending auprès de l'agrégateur et complète/échoit celles qui ont abouti.
    /// Actif uniquement si GeniusPay est activé.
    /// </summary>
    public sealed class PaymentReconciliationWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly GeniusPayOptions _geniusPay;
        private readonly ILogger<PaymentReconciliationWorker> _logger;

        public PaymentReconciliationWorker(
            IServiceScopeFactory scopeFactory,
            GeniusPayOptions geniusPay,
            ILogger<PaymentReconciliationWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _geniusPay = geniusPay;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_geniusPay.Enabled)
            {
                _logger.LogInformation("Réconciliation désactivée (GeniusPay non activé).");
                return;
            }

            var intervalMinutes = Math.Max(1, _geniusPay.ReconciliationMinutes);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await ReconcileAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors de la réconciliation des paiements.");
                }
            }
        }

        private async Task ReconcileAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var paymentService = scope.ServiceProvider.GetRequiredService<IPaymentService>();
            var packService = scope.ServiceProvider.GetRequiredService<PackService>();

            // Transactions Pending avec une vraie référence (pas la provisoire PENDING-…).
            var pending = await db.CreditTransactions
                .Where(t => t.Status == TransactionStatus.Pending
                         && !t.TransactionReference.StartsWith("PENDING-"))
                .OrderBy(t => t.CreatedAt)
                .Take(20)
                .ToListAsync(ct);

            if (pending.Count == 0)
                return;

            foreach (var transaction in pending)
            {
                var status = await paymentService.CheckPaymentStatusAsync(transaction.TransactionReference);
                if (status is null || string.IsNullOrWhiteSpace(status.Status))
                    continue; // agrégateur injoignable ou statut inconnu : on retentera plus tard

                _logger.LogInformation("Réconciliation {Ref} : statut {Status}.",
                    transaction.TransactionReference, status.Status);

                if (status.Status.Equals("completed", StringComparison.OrdinalIgnoreCase))
                    await packService.CompletePurchaseAsync(transaction.Id, transaction.TransactionReference);
                else if (status.Status is "failed" or "cancelled" or "refunded")
                    await packService.FailPurchaseAsync(transaction.Id);
            }
        }
    }
}
