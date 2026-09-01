using Microsoft.EntityFrameworkCore;
using Wazap.Application.Configuration;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services
{
    /// <summary>
    /// Fait avancer le matching des offres : expire les vagues après l'exclusivité
    /// (30 s) et élargit aux livreurs suivants, puis gère le timeout global (5 min).
    /// </summary>
    public sealed class DeliveryOfferWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DeliveryOfferWorker> _logger;
        private readonly GeoOptions _geo;

        public DeliveryOfferWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<DeliveryOfferWorker> logger,
            GeoOptions geo)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _geo = geo;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await ProcessAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur dans DeliveryOfferWorker.");
                }
            }
        }

        private async Task ProcessAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var deliveryOfferService = scope.ServiceProvider.GetRequiredService<DeliveryOfferService>();

            var now = DateTime.UtcNow;
            var exclusivityCutoff = now.AddSeconds(-_geo.ExclusivitySeconds);
            var globalCutoff = now.AddMinutes(-_geo.GlobalTimeoutMinutes);

            var offers = await db.DeliveryOffers.AsNoTracking().ToListAsync(ct);

            // Vagues dont l'exclusivité (30 s) est dépassée → élargir
            var orderIdsToAdvance = offers
                .Where(o => o.Status == DeliveryOfferStatus.Pending)
                .GroupBy(o => o.OrderId)
                .Where(g => g.Min(o => o.SentAt) < exclusivityCutoff)
                .Select(g => g.Key)
                .ToList();

            foreach (var orderId in orderIdsToAdvance)
            {
                try
                {
                    var result = await deliveryOfferService.BroadcastAsync(orderId);
                    _logger.LogInformation("Commande {OrderId} : vague élargie → {Count} nouvelle(s) offre(s) (vague {Batch}).",
                        orderId, result.OffersCreated, result.BatchNumber);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Élargissement impossible pour la commande {OrderId}.", orderId);
                }
            }

            // Timeout global (aucune acceptation après X min)
            var acceptedOrderIds = offers
                .Where(o => o.Status == DeliveryOfferStatus.Accepted)
                .Select(o => o.OrderId)
                .ToHashSet();

            var timedOutOrderIds = offers
                .Where(o => !acceptedOrderIds.Contains(o.OrderId))
                .GroupBy(o => o.OrderId)
                .Where(g => g.Min(o => o.SentAt) < globalCutoff)
                .Select(g => g.Key)
                .ToList();

            foreach (var orderId in timedOutOrderIds)
                _logger.LogWarning("Aucun livreur n'a accepté la commande {OrderId} (timeout global).", orderId);
        }
    }
}
