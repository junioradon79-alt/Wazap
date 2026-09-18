using Microsoft.EntityFrameworkCore;
using Wazap.API.Health;
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
                    WorkerHeartbeats.Beat(nameof(DeliveryOfferWorker));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur dans DeliveryOfferWorker.");
                    WorkerHeartbeats.Fail(nameof(DeliveryOfferWorker), ex.Message);
                }
            }
        }

        internal async Task ProcessAsync(CancellationToken ct)
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
                .Select(b => new { b.Id, b.CreatedAt })
                .ToListAsync(ct);

            foreach (var batch in openBatches)
            {
                // Court-circuit AVANT tout autre requête : un lot déjà diffusé ne le sera jamais
                // deux fois. L'ordre précédent chargeait d'abord ses commandes — une requête
                // par lot ouvert, toutes les 5 s, pour un résultat aussitôt jeté.
                if (await db.DeliveryOffers.AnyAsync(o => o.BatchId == batch.Id, ct))
                    continue;

                // Projection minimale (2 colonnes) au lieu du chargement des entités complètes.
                var activeOrders = await db.Orders.AsNoTracking()
                    .Where(o => o.BatchId == batch.Id
                        && (o.Status == OrderStatus.VendorConfirmed || o.Status == OrderStatus.AwaitingRiderAcceptance))
                    .Select(o => new { o.ClientLatitude, o.ClientLongitude })
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

                try
                {
                    var result = await deliveryOfferService.BroadcastBatchAsync(batch.Id, ct);
                    _logger.LogInformation("Lot {BatchId} : diffusion initiale → {Count} offre(s) (vague {Batch}).",
                        batch.Id, result.OffersCreated, result.BatchNumber);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Diffusion impossible pour le lot {BatchId}.", batch.Id);
                }
            }

            // 2) Vagues dont l'exclusivité (30 s) est dépassée → élargir (lots puis commandes).
            //    Les candidats sont déterminés EN SQL (statut + horodatage) : l'ancienne version
            //    chargeait l'INTÉGRALITÉ de la table DeliveryOffers toutes les 5 secondes puis
            //    filtrait en mémoire — une table qui croît sans borne (la rétention des offres
            //    n'existe pas) et une latence qui augmente avec elle.
            var batchIdsToAdvance = await db.DeliveryOffers.AsNoTracking()
                .Where(o => o.Status == DeliveryOfferStatus.Pending
                            && o.BatchId != null
                            && o.SentAt < exclusivityCutoff)
                .Select(o => o.BatchId!.Value)
                .Distinct()
                .ToListAsync(ct);

            foreach (var batchId in batchIdsToAdvance)
            {
                try
                {
                    var result = await deliveryOfferService.BroadcastBatchAsync(batchId, ct);
                    _logger.LogInformation("Lot {BatchId} : vague élargie → {Count} nouvelle(s) offre(s) (vague {Batch}).",
                        batchId, result.OffersCreated, result.BatchNumber);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Élargissement impossible pour le lot {BatchId}.", batchId);
                }
            }

            var orderIdsToAdvance = await db.DeliveryOffers.AsNoTracking()
                .Where(o => o.Status == DeliveryOfferStatus.Pending
                            && o.OrderId != null
                            && o.SentAt < exclusivityCutoff)
                .Select(o => o.OrderId!.Value)
                .Distinct()
                .ToListAsync(ct);

            foreach (var orderId in orderIdsToAdvance)
            {
                try
                {
                    var result = await deliveryOfferService.BroadcastAsync(orderId, ct);
                    _logger.LogInformation("Commande {OrderId} : vague élargie → {Count} nouvelle(s) offre(s) (vague {Batch}).",
                        orderId, result.OffersCreated, result.BatchNumber);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Élargissement impossible pour la commande {OrderId}.", orderId);
                }
            }

            // 3) Timeout global : TOUTE commande encore en attente d'un livreur au-delà du délai
            //    est annulée et le vendeur prévenu.
            //    Le déclencheur est l'ÂGE DE LA COMMANDE, et non plus l'âge d'une offre : quand
            //    aucun livreur n'était éligible dans le rayon, aucune ligne DeliveryOffers n'était
            //    créée, le délai n'était donc JAMAIS atteint — la commande restait bloquée à vie,
            //    le vendeur n'était jamais prévenu et la diffusion était retentée toutes les 5 s.
            //    Une commande acceptée passe en RiderAssigned et sort naturellement de ce filtre.
            var timedOutOrders = await db.Orders
                .Where(o => o.Status == OrderStatus.AwaitingRiderAcceptance && o.CreatedAt < globalCutoff)
                .ToListAsync(ct);

            foreach (var order in timedOutOrders)
                await FailDispatchAsync(order);

            if (timedOutOrders.Count > 0)
                await db.SaveChangesAsync(ct);

            // Annule la commande et notifie le vendeur (aucun livreur trouvé).
            async Task FailDispatchAsync(Wazap.Domain.Entities.Order order)
            {
                order.Cancel(OrderCancellationReason.TimeoutNoRider, "Délai global dépassé sans acceptation de livreur.");
                _logger.LogWarning("Commande {OrderId} : aucun livreur (timeout global) — annulée.", order.Id);

                var code = order.Id.ToString("N")[..8].ToUpperInvariant();
                try
                {
                    var orchestrator = scope.ServiceProvider.GetRequiredService<WhatsAppOrchestrationService>();
                    await orchestrator.SendNoRiderFoundAsync(order.VendorWhatsAppNumber, code);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Notification « aucun livreur » impossible pour {OrderId}.", order.Id);
                }
            }
        }
    }
}
