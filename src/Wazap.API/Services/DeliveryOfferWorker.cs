using Microsoft.EntityFrameworkCore;
using Wazap.Application.Configuration;
using Wazap.Application.Services;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services
{
    /// <summary>
    /// Fait avancer le matching des livraisons :
    /// - diffuse les lots groupés dont la fenêtre d'accumulation est écoulée ou qui sont pleins ;
    /// - expire les vagues après l'exclusivité (30 s) et élargit aux livreurs suivants
    ///   (par lot pour les livraisons groupées, par commande sinon) ;
    /// - gère le timeout global (5 min).
    /// </summary>
    public sealed class DeliveryOfferWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DeliveryOfferWorker> _logger;
        private readonly GeoOptions _geo;
        private readonly GroupingOptions _grouping;

        public DeliveryOfferWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<DeliveryOfferWorker> logger,
            GeoOptions geo,
            GroupingOptions grouping)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _geo = geo;
            _grouping = grouping;
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
            var windowCutoff = now.AddMinutes(-_grouping.WindowMinutes);

            // 1) Diffusion initiale des lots groupés prêts (fenêtre écoulée OU taille max atteinte)
            var openBatches = await db.DeliveryBatches.AsNoTracking()
                .Where(b => b.Status == DeliveryBatchStatus.Open)
                .ToListAsync(ct);

            foreach (var batch in openBatches)
            {
                // Taille du lot = commandes actives uniquement (les annulées ne comptent pas).
                var activeOrders = await db.Orders
                    .Where(o => o.BatchId == batch.Id
                        && (o.Status == OrderStatus.VendorConfirmed || o.Status == OrderStatus.AwaitingRiderAcceptance))
                    .ToListAsync(ct);
                var orderCount = activeOrders.Count;
                // Lot issu du parcours acheteur (au moins une commande avec coordonnées client) :
                // diffusion après le court délai de groupage (tournée multi-clients).
                var hasBuyerCoords = activeOrders.Any(o => o.ClientLatitude != null && o.ClientLongitude != null);
                var buyerCutoff = now.AddSeconds(-_grouping.BuyerDispatchDelaySeconds);
                var ready = hasBuyerCoords
                    ? batch.CreatedAt <= buyerCutoff || orderCount >= _grouping.MaxOrdersPerBatch
                    : batch.CreatedAt <= windowCutoff || orderCount >= _grouping.MaxOrdersPerBatch;
                if (!ready)
                    continue;

                var alreadyBroadcast = await db.DeliveryOffers.AnyAsync(o => o.BatchId == batch.Id, ct);
                if (alreadyBroadcast)
                    continue;

                try
                {
                    var result = await deliveryOfferService.BroadcastBatchAsync(batch.Id);
                    _logger.LogInformation("Lot {BatchId} : diffusion initiale → {Count} offre(s) (vague {Batch}).",
                        batch.Id, result.OffersCreated, result.BatchNumber);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Diffusion impossible pour le lot {BatchId}.", batch.Id);
                }
            }

            var offers = await db.DeliveryOffers.AsNoTracking().ToListAsync(ct);

            // 2) Vagues dont l'exclusivité (30 s) est dépassée → élargir (lots puis commandes)
            var batchIdsToAdvance = offers
                .Where(o => o.Status == DeliveryOfferStatus.Pending && o.BatchId != null)
                .GroupBy(o => o.BatchId!.Value)
                .Where(g => g.Min(o => o.SentAt) < exclusivityCutoff)
                .Select(g => g.Key)
                .ToList();

            foreach (var batchId in batchIdsToAdvance)
            {
                try
                {
                    var result = await deliveryOfferService.BroadcastBatchAsync(batchId);
                    _logger.LogInformation("Lot {BatchId} : vague élargie → {Count} nouvelle(s) offre(s) (vague {Batch}).",
                        batchId, result.OffersCreated, result.BatchNumber);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Élargissement impossible pour le lot {BatchId}.", batchId);
                }
            }

            var orderIdsToAdvance = offers
                .Where(o => o.Status == DeliveryOfferStatus.Pending && o.OrderId != null)
                .GroupBy(o => o.OrderId!.Value)
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

            // 3) Timeout global (aucune acceptation après X min) — lots et commandes
            var acceptedBatchIds = offers
                .Where(o => o.Status == DeliveryOfferStatus.Accepted && o.BatchId != null)
                .Select(o => o.BatchId!.Value)
                .ToHashSet();

            var timedOutBatchIds = offers
                .Where(o => o.BatchId != null && !acceptedBatchIds.Contains(o.BatchId!.Value))
                .GroupBy(o => o.BatchId!.Value)
                .Where(g => g.Min(o => o.SentAt) < globalCutoff)
                .Select(g => g.Key)
                .ToList();

            foreach (var batchId in timedOutBatchIds)
                _logger.LogWarning("Aucun livreur n'a accepté le lot {BatchId} (timeout global).", batchId);

            var acceptedOrderIds = offers
                .Where(o => o.Status == DeliveryOfferStatus.Accepted && o.OrderId != null)
                .Select(o => o.OrderId!.Value)
                .ToHashSet();

            var timedOutOrderIds = offers
                .Where(o => o.OrderId != null && !acceptedOrderIds.Contains(o.OrderId!.Value))
                .GroupBy(o => o.OrderId!.Value)
                .Where(g => g.Min(o => o.SentAt) < globalCutoff)
                .Select(g => g.Key)
                .ToList();

            foreach (var orderId in timedOutOrderIds)
                _logger.LogWarning("Aucun livreur n'a accepté la commande {OrderId} (timeout global).", orderId);
        }
    }
}
