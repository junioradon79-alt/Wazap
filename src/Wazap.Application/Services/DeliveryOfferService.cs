using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Dtos;
using Wazap.Application.Exceptions;
using Wazap.Application.Helpers;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Domain.Services;

namespace Wazap.Application.Services
{
    /// <summary>
    /// Matching des livreurs : diffusion d'offres aux plus proches, acceptation,
    /// calcul de distance (Haversine) et règles de fraîcheur/exclusivité.
    /// </summary>
    public sealed class DeliveryOfferService
    {
        private readonly IApplicationDbContext _context;
        private readonly IWhatsAppSender _whatsApp;
        private readonly WhatsAppOptions _whatsAppOptions;
        private readonly GeoOptions _geo;
        private readonly GroupingOptions _grouping;
        private readonly WhatsAppOrchestrationService _orchestrator;
        private readonly ILogger<DeliveryOfferService> _logger;

        public DeliveryOfferService(
            IApplicationDbContext context,
            IWhatsAppSender whatsApp,
            WhatsAppOptions whatsAppOptions,
            GeoOptions geo,
            GroupingOptions grouping,
            WhatsAppOrchestrationService orchestrator,
            ILogger<DeliveryOfferService> logger)
        {
            _context = context;
            _whatsApp = whatsApp;
            _whatsAppOptions = whatsAppOptions;
            _geo = geo;
            _grouping = grouping;
            _orchestrator = orchestrator;
            _logger = logger;
        }

        /// <summary>
        /// Diffuse une vague d'offres aux 5 livreurs disponibles/actifs/frais les plus
        /// proches du vendeur, puis envoie le template WhatsApp « rider_offer » (code court).
        /// </summary>
        public async Task<BroadcastResultDto> BroadcastAsync(Guid orderId)
        {
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId)
                ?? throw new InvalidOperationException("Commande introuvable.");

            // Si la commande appartient à un lot groupé → diffuser le lot entier.
            if (order.BatchId is not null)
                return await BroadcastBatchAsync(order.BatchId.Value);

            // La commande doit d'abord être confirmée par le vendeur.
            if (order.Status == OrderStatus.PendingVendorConfirmation)
                throw new InvalidOperationException("Le vendeur n'a pas encore confirmé la commande.");

            if (order.Status == OrderStatus.VendorConfirmed)
                order.AwaitRiderAcceptance();

            var vendor = await ResolveVendorAsync(order.VendorWhatsAppNumber);
            if (vendor is null)
                throw new InvalidOperationException("Vendeur introuvable.");
            if ((vendor.Latitude is null || vendor.Longitude is null) && string.IsNullOrWhiteSpace(vendor.Zone))
                throw new InvalidOperationException("Le vendeur n'a ni position GPS ni zone déclarée.");

            // Expirer la vague précédente en attente.
            var previousPending = await _context.DeliveryOffers
                .Where(o => o.OrderId == order.Id && o.Status == DeliveryOfferStatus.Pending)
                .ToListAsync();

            foreach (var offer in previousPending)
                offer.Expire();

            // Livreurs déjà contactés (toutes vagues confondues).
            var contactedRiderIds = await _context.DeliveryOffers.AsNoTracking()
                .Where(o => o.OrderId == order.Id)
                .Select(o => o.RiderUserId)
                .ToListAsync();

            var nextBatch = (await _context.DeliveryOffers.AsNoTracking()
                .Where(o => o.OrderId == order.Id)
                .Select(o => (int?)o.BatchNumber)
                .MaxAsync() ?? -1) + 1;

            var nearest = await GetNearestAvailableRidersAsync(vendor.Id, count: 5, contactedRiderIds);

            var offers = nearest
                .Select(r => new DeliveryOffer(order.Id, r.RiderUserId, nextBatch))
                .ToList();

            _context.DeliveryOffers.AddRange(offers);
            await _context.SaveChangesAsync();

            // Envoi des offres via WhatsApp (best effort).
            foreach (var offer in offers)
            {
                var rider = await _context.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == offer.RiderUserId);

                if (rider?.PhoneNumber is null)
                    continue;

                try
                {
                    await _whatsApp.SendTemplateAsync(
                        rider.PhoneNumber,
                        _whatsAppOptions.TemplateRiderOffer,
                        new Dictionary<string, string>
                        {
                            ["1"] = offer.Id.ToString("N")[..8].ToUpperInvariant()
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Envoi WhatsApp échoué pour l'offre {OfferId}.", offer.Id);
                }
            }

            return new BroadcastResultDto(offers.Count, nextBatch);
        }

        /// <summary>
        /// Offres de livraison d'une commande (admin / debug). Pour une commande groupée,
        /// expose les offres de SON lot (les offres de lot ont OrderId = null).
        /// </summary>
        public async Task<IReadOnlyList<DeliveryOfferDto>> GetOffersAsync(Guid orderId)
        {
            var order = await _context.Orders.AsNoTracking()
                .Select(o => new { o.Id, o.BatchId })
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order is null)
                return Array.Empty<DeliveryOfferDto>();

            var query = order.BatchId is { } batchId
                ? _context.DeliveryOffers.AsNoTracking().Where(o => o.BatchId == batchId)
                : _context.DeliveryOffers.AsNoTracking().Where(o => o.OrderId == order.Id);

            return await query
                .OrderBy(o => o.SentAt)
                .Select(o => new DeliveryOfferDto(
                    o.Id,
                    o.RiderUserId,
                    o.Status,
                    o.BatchNumber,
                    o.SentAt,
                    o.RespondedAt))
                .ToListAsync();
        }

        /// <summary>
        /// Rattache une commande confirmée au lot ouvert de son vendeur (fenêtre de groupage),
        /// ou crée un nouveau lot si aucun n'est ouvert. Le broadcast du lot est déclenché
        /// par le <see cref="DeliveryOfferWorker"/> quand la fenêtre est écoulée ou le lot plein.
        /// </summary>
        public async Task<Guid> JoinOrCreateBatchAsync(Guid orderId)
        {
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId)
                ?? throw new InvalidOperationException("Commande introuvable.");

            if (order.VendorUserId is null)
                throw new InvalidOperationException("La commande n'a pas de vendeur lié.");

            var windowCutoff = DateTime.UtcNow.AddMinutes(-_grouping.WindowMinutes);

            var openBatch = await _context.DeliveryBatches
                .FirstOrDefaultAsync(b => b.VendorUserId == order.VendorUserId.Value
                                       && b.Status == DeliveryBatchStatus.Open
                                       && b.CreatedAt >= windowCutoff
                                       // Un lot déjà diffusé ne doit plus accepter de commandes :
                                       // elles ne seraient jamais proposées aux livreurs.
                                       && !_context.DeliveryOffers.Any(o => o.BatchId == b.Id));

            if (openBatch is null)
            {
                openBatch = new DeliveryBatch(order.VendorUserId.Value);
                _context.DeliveryBatches.Add(openBatch);
            }

            order.JoinBatch(openBatch.Id);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Commande {OrderId} jointe au lot {BatchId} (vendeur {VendorId}).",
                order.Id, openBatch.Id, order.VendorUserId);

            return openBatch.Id;
        }

        /// <summary>
        /// À appeler quand une commande d'un lot est annulée : si plus aucune commande
        /// active ne reste dans le lot, celui-ci est clôturé et ses offres en attente expirées.
        /// </summary>
        public async Task HandleOrderCancelledInBatchAsync(Guid batchId)
        {
            var batch = await _context.DeliveryBatches.FirstOrDefaultAsync(b => b.Id == batchId);
            if (batch is null || batch.Status != DeliveryBatchStatus.Open)
                return;

            var activeCount = await _context.Orders.CountAsync(o =>
                o.BatchId == batch.Id
                && (o.Status == OrderStatus.VendorConfirmed || o.Status == OrderStatus.AwaitingRiderAcceptance));

            if (activeCount > 0)
                return;

            var pendingOffers = await _context.DeliveryOffers
                .Where(o => o.BatchId == batch.Id && o.Status == DeliveryOfferStatus.Pending)
                .ToListAsync();

            foreach (var offer in pendingOffers)
                offer.Expire();

            batch.Cancel();
            await _context.SaveChangesAsync();

            _logger.LogInformation("Lot {BatchId} annulé : plus aucune commande active.", batch.Id);
        }

        /// <summary>
        /// Diffuse une vague d'offres pour un lot groupé : les 5 livreurs disponibles/actifs/frais
        /// les plus proches du vendeur reçoivent une offre pour TOUTES les commandes du lot.
        /// </summary>
        public async Task<BroadcastResultDto> BroadcastBatchAsync(Guid batchId)
        {
            var batch = await _context.DeliveryBatches.FirstOrDefaultAsync(b => b.Id == batchId)
                ?? throw new InvalidOperationException("Lot introuvable.");

            if (batch.Status != DeliveryBatchStatus.Open)
                throw new InvalidOperationException("Le lot n'est plus ouvert.");

            var orders = await _context.Orders
                .Where(o => o.BatchId == batch.Id)
                .ToListAsync();

            // Commandes actives du lot : confirmées (première vague) ou déjà en attente
            // d'un livreur (vagues d'élargissement suivantes). Les annulées sont ignorées.
            var activeOrders = orders
                .Where(o => o.Status is OrderStatus.VendorConfirmed or OrderStatus.AwaitingRiderAcceptance)
                .ToList();

            if (activeOrders.Count == 0)
                throw new InvalidOperationException("Aucune commande active dans le lot.");

            foreach (var order in activeOrders.Where(o => o.Status == OrderStatus.VendorConfirmed))
                order.AwaitRiderAcceptance();

            var vendor = await ResolveVendorAsync(activeOrders[0].VendorWhatsAppNumber);
            if (vendor is null)
                throw new InvalidOperationException("Vendeur introuvable.");

            // Le matching exige une position GPS OU une zone déclarée (livraison à la demande).
            if ((vendor.Latitude is null || vendor.Longitude is null) && string.IsNullOrWhiteSpace(vendor.Zone))
                throw new InvalidOperationException("Le vendeur n'a ni position GPS ni zone déclarée.");

            // Expirer la vague précédente en attente.
            var previousPending = await _context.DeliveryOffers
                .Where(o => o.BatchId == batch.Id && o.Status == DeliveryOfferStatus.Pending)
                .ToListAsync();

            foreach (var offer in previousPending)
                offer.Expire();

            // Livreurs déjà contactés (toutes vagues confondues).
            var contactedRiderIds = await _context.DeliveryOffers.AsNoTracking()
                .Where(o => o.BatchId == batch.Id)
                .Select(o => o.RiderUserId)
                .ToListAsync();

            var nextBatch = (await _context.DeliveryOffers.AsNoTracking()
                .Where(o => o.BatchId == batch.Id)
                .Select(o => (int?)o.BatchNumber)
                .MaxAsync() ?? -1) + 1;

            var nearest = await GetNearestAvailableRidersAsync(vendor.Id, count: 5, contactedRiderIds);

            var offers = nearest
                .Select(r => DeliveryOffer.CreateForBatch(batch.Id, r.RiderUserId, nextBatch))
                .ToList();

            _context.DeliveryOffers.AddRange(offers);
            await _context.SaveChangesAsync();

            // Envoi des offres via WhatsApp (best effort).
            foreach (var offer in offers)
            {
                var rider = await _context.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == offer.RiderUserId);

                if (rider?.PhoneNumber is null)
                    continue;

                try
                {
                    await _orchestrator.SendBatchOfferAsync(
                        rider.PhoneNumber,
                        activeOrders.Count,
                        offer.Id.ToString("N")[..8].ToUpperInvariant());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Envoi WhatsApp échoué pour l'offre de lot {OfferId}.", offer.Id);
                }
            }

            return new BroadcastResultDto(offers.Count, nextBatch);
        }

        /// <summary>
        /// Accepte une offre (livreur) : assigne le livreur à la commande (ou à toutes les
        /// commandes du lot groupé) et expire les autres offres.
        /// </summary>
        public async Task AcceptOfferAsync(Guid offerId)
        {
            var offer = await _context.DeliveryOffers.FirstOrDefaultAsync(o => o.Id == offerId)
                ?? throw new InvalidOperationException("Offre introuvable.");

            offer.Accept();

            var rider = await _context.Users.FirstOrDefaultAsync(u => u.Id == offer.RiderUserId)
                ?? throw new InvalidOperationException("Livreur introuvable.");

            var riderPhone = rider.PhoneNumber
                ?? throw new InvalidOperationException("Le livreur n'a pas de numéro WhatsApp.");

            // Offre de lot groupé : le livreur prend TOUTES les commandes du lot.
            if (offer.BatchId is not null)
            {
                await AcceptBatchAsync(offer, rider, riderPhone);
                return;
            }

            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == offer.OrderId)
                ?? throw new InvalidOperationException("Commande introuvable.");

            // Débit du crédit UNIQUEMENT à l'acceptation de la course par le livreur.
            var vendor = await DebitVendorForOrdersAsync(order.VendorUserId, 1);

            order.AssignRider(riderPhone);
            order.LinkRider(rider.Id);

            var otherPending = await _context.DeliveryOffers
                .Where(o => o.OrderId == order.Id && o.Id != offer.Id && o.Status == DeliveryOfferStatus.Pending)
                .ToListAsync();

            foreach (var other in otherPending)
                other.Expire();

            await _context.SaveChangesAsync();

            // Alerte crédits bas/épuisés après débit (best effort).
            await NotifyVendorCreditAsync(vendor);

            // Notifications post-acceptation (best effort) : client + vendeur + livreur.
            try
            {
                await _orchestrator.SendRiderAssignedAsync(order, rider);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Notifications d'acceptation impossibles pour la commande {OrderId}.", order.Id);
            }
        }

        private async Task AcceptBatchAsync(DeliveryOffer offer, User rider, string riderPhone)
        {
            var batch = await _context.DeliveryBatches.FirstOrDefaultAsync(b => b.Id == offer.BatchId)
                ?? throw new InvalidOperationException("Lot introuvable.");

            // Seules les commandes réellement en attente d'un livreur sont assignées.
            // (Une commande annulée après la diffusion du lot ne doit pas bloquer l'acceptation.)
            var orders = await _context.Orders
                .Where(o => o.BatchId == batch.Id && o.Status == OrderStatus.AwaitingRiderAcceptance)
                .ToListAsync();

            if (orders.Count == 0)
            {
                // Toutes les commandes ont été annulées entre-temps : on expire les offres
                // et on clôt le lot plutôt que de laisser une acceptation sans objet.
                var stalePending = await _context.DeliveryOffers
                    .Where(o => o.BatchId == batch.Id && o.Status == DeliveryOfferStatus.Pending)
                    .ToListAsync();

                foreach (var stale in stalePending)
                    stale.Expire();

                batch.Cancel();
                await _context.SaveChangesAsync();

                _logger.LogWarning("Lot {BatchId} accepté mais sans commande active : lot annulé.", batch.Id);
                return;
            }

            // Débit des crédits (1 par commande du lot) UNIQUEMENT à l'acceptation.
            var vendor = await DebitVendorForOrdersAsync(batch.VendorUserId, orders.Count);

            foreach (var order in orders)
            {
                order.AssignRider(riderPhone);
                order.LinkRider(rider.Id);
            }

            batch.AssignRider(rider.Id, riderPhone);

            var otherPending = await _context.DeliveryOffers
                .Where(o => o.BatchId == batch.Id && o.Id != offer.Id && o.Status == DeliveryOfferStatus.Pending)
                .ToListAsync();

            foreach (var other in otherPending)
                other.Expire();

            await _context.SaveChangesAsync();

            _logger.LogInformation("Lot {BatchId} accepté par {Rider} : {Count} commande(s) assignée(s).",
                batch.Id, rider.Username, orders.Count);

            // Alerte crédits bas/épuisés après débit (best effort).
            await NotifyVendorCreditAsync(vendor);

            // Notifications post-acceptation (best effort) : chaque client + vendeur + livreur.
            try
            {
                await _orchestrator.SendBatchAssignedAsync(rider, orders);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Notifications d'acceptation impossibles pour le lot {BatchId}.", batch.Id);
            }
        }

        /// <summary>
        /// Débite le vendeur de <paramref name="ordersCount"/> crédit(s) (un par course acceptée).
        /// Ne fait rien si la commande n'a pas de vendeur lié (données historiques).
        /// </summary>
        private async Task<User?> DebitVendorForOrdersAsync(Guid? vendorUserId, int ordersCount)
        {
            if (vendorUserId is null)
                return null;

            var vendor = await _context.Users.FirstOrDefaultAsync(u => u.Id == vendorUserId.Value && u.Role == UserRole.Vendor)
                ?? throw new InvalidOperationException("Vendeur introuvable.");

            for (var i = 0; i < ordersCount; i++)
            {
                if (!vendor.TryConsumeCredit())
                    throw new PaymentRequiredException(
                        $"Crédits insuffisants ({vendor.Credits} restant(s)) pour {ordersCount} course(s). Rechargez sur /api/packs.");
            }

            return vendor;
        }

        /// <summary>
        /// Alerte WhatsApp quand les crédits restants atteignent un seuil bas (≤ 5) ou 0 (best effort).
        /// </summary>
        private async Task NotifyVendorCreditAsync(User? vendor)
        {
            if (vendor is null)
                return;

            try
            {
                if (vendor.Credits == 0)
                    await _orchestrator.SendNoCreditAlertAsync(vendor);
                else if (vendor.Credits <= 5)
                    await _orchestrator.SendLowCreditAlertAsync(vendor);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Alerte WhatsApp impossible pour {Vendor} (crédits restants : {Credits}).",
                    vendor.Username, vendor.Credits);
            }
        }

        /// <summary>
        /// Livreurs disponibles, partage activé, position fraîche (&lt; Geo:LocationFreshnessMinutes)
        /// et dans le rayon Geo:MaxDistanceKm, triés par distance (Haversine).
        /// </summary>
        private async Task<IReadOnlyList<NearestRiderDto>> GetNearestAvailableRidersAsync(
            Guid vendorUserId,
            int count,
            IReadOnlyCollection<Guid> excludeRiderIds)
        {
            var vendor = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == vendorUserId)
                ?? throw new InvalidOperationException("Vendeur introuvable.");

            // Position GPS OU zone déclarée exigées (livraison à la demande, téléphones basiques).
            if ((vendor.Latitude is null || vendor.Longitude is null) && string.IsNullOrWhiteSpace(vendor.Zone))
                throw new InvalidOperationException("Le vendeur n'a ni position GPS ni zone déclarée.");

            var freshnessThreshold = DateTime.UtcNow.AddMinutes(-_geo.LocationFreshnessMinutes);

            var exclude = excludeRiderIds?.ToHashSet() ?? new HashSet<Guid>();
            var byGps = new List<NearestRiderDto>();

            // Tier 1 — GPS (Haversine) : uniquement si le vendeur a une position.
            if (vendor.Latitude is not null && vendor.Longitude is not null)
            {
                var riders = await _context.Users.AsNoTracking()
                    .Where(u => u.Role == UserRole.Rider
                             && u.IsAvailable
                             && u.LocationSharingEnabled
                             && u.Latitude != null
                             && u.Longitude != null
                             && u.LocationUpdatedAt >= freshnessThreshold)
                    .Select(u => new { u.Id, u.Latitude, u.Longitude })
                    .ToListAsync();

                byGps.AddRange(riders
                    .Where(r => !exclude.Contains(r.Id))
                    .Select(r => new NearestRiderDto(
                        r.Id,
                        GeoDistance.DistanceKm(
                            vendor.Latitude.Value,
                            vendor.Longitude.Value,
                            r.Latitude.GetValueOrDefault(),
                            r.Longitude.GetValueOrDefault())))
                    .Where(x => x.DistanceKm <= _geo.MaxDistanceKm)
                    .OrderBy(x => x.DistanceKm)
                    .Take(count));
            }

            // Tier 2 (téléphones basiques sans GPS) : compléter avec les livreurs
            // dont la ZONE déclarée correspond à celle du vendeur.
            if (byGps.Count < count && !string.IsNullOrWhiteSpace(vendor.Zone))
            {
                var remaining = count - byGps.Count;
                var contacted = new HashSet<Guid>(exclude);
                contacted.UnionWith(byGps.Select(r => r.RiderUserId));

                var zoneRiders = await _context.Users.AsNoTracking()
                    .Where(u => u.Role == UserRole.Rider
                             && u.IsAvailable
                             && u.LocationSharingEnabled
                             && u.Zone != null)
                    .Select(u => new { u.Id, u.Zone })
                    .ToListAsync();

                byGps.AddRange(zoneRiders
                    .Where(r => !contacted.Contains(r.Id)
                             && string.Equals(r.Zone?.Trim(), vendor.Zone.Trim(), StringComparison.OrdinalIgnoreCase))
                    .Select(r => new NearestRiderDto(r.Id, double.MaxValue))
                    .Take(remaining));
            }

            return byGps;
        }

        private async Task<User?> ResolveVendorAsync(string vendorWhatsApp)
        {
            if (string.IsNullOrWhiteSpace(vendorWhatsApp))
                return null;

            var vendors = await _context.Users.AsNoTracking()
                .Where(u => u.Role == UserRole.Vendor && u.PhoneNumber != null)
                .ToListAsync();

            return vendors.FirstOrDefault(v =>
                PhoneNumberNormalizer.SameSubscriber(v.PhoneNumber, vendorWhatsApp));
        }
    }
}

