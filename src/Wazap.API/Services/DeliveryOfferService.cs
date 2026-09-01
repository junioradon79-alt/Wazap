using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Dtos;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Domain.Services;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services
{
    /// <summary>
    /// Matching des livreurs : diffusion d'offres aux plus proches, acceptation,
    /// calcul de distance (Haversine) et règles de fraîcheur/exclusivité.
    /// </summary>
    public sealed class DeliveryOfferService
    {
        private readonly ApplicationDbContext _context;
        private readonly IWhatsAppSender _whatsApp;
        private readonly WhatsAppOptions _whatsAppOptions;
        private readonly GeoOptions _geo;
        private readonly GroupingOptions _grouping;
        private readonly WhatsAppOrchestrationService _orchestrator;
        private readonly ILogger<DeliveryOfferService> _logger;

        public DeliveryOfferService(
            ApplicationDbContext context,
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
            if (vendor?.Latitude is null || vendor.Longitude is null)
                throw new InvalidOperationException("Position du vendeur introuvable.");

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
        /// Offres de livraison d'une commande (admin / debug).
        /// </summary>
        public async Task<IReadOnlyList<DeliveryOfferDto>> GetOffersAsync(Guid orderId)
        {
            return await _context.DeliveryOffers.AsNoTracking()
                .Where(o => o.OrderId == orderId)
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
                                       && b.CreatedAt >= windowCutoff);

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

            var confirmedOrders = orders.Where(o => o.Status == OrderStatus.VendorConfirmed).ToList();
            if (confirmedOrders.Count == 0)
                throw new InvalidOperationException("Aucune commande confirmée dans le lot.");

            foreach (var order in confirmedOrders)
                order.AwaitRiderAcceptance();

            var vendor = await ResolveVendorAsync(confirmedOrders[0].VendorWhatsAppNumber);
            if (vendor?.Latitude is null || vendor.Longitude is null)
                throw new InvalidOperationException("Position du vendeur introuvable.");

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
                        confirmedOrders.Count,
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

            order.AssignRider(riderPhone);

            var otherPending = await _context.DeliveryOffers
                .Where(o => o.OrderId == order.Id && o.Id != offer.Id && o.Status == DeliveryOfferStatus.Pending)
                .ToListAsync();

            foreach (var other in otherPending)
                other.Expire();

            await _context.SaveChangesAsync();

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

            var orders = await _context.Orders.Where(o => o.BatchId == batch.Id).ToListAsync();

            foreach (var order in orders)
                order.AssignRider(riderPhone);

            batch.AssignRider(rider.Id, riderPhone);

            var otherPending = await _context.DeliveryOffers
                .Where(o => o.BatchId == batch.Id && o.Id != offer.Id && o.Status == DeliveryOfferStatus.Pending)
                .ToListAsync();

            foreach (var other in otherPending)
                other.Expire();

            await _context.SaveChangesAsync();

            _logger.LogInformation("Lot {BatchId} accepté par {Rider} : {Count} commande(s) assignée(s).",
                batch.Id, rider.Username, orders.Count);

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

            if (vendor.Latitude is null || vendor.Longitude is null)
                throw new InvalidOperationException("Le vendeur n'a pas de position.");

            var freshnessThreshold = DateTime.UtcNow.AddMinutes(-_geo.LocationFreshnessMinutes);

            var riders = await _context.Users.AsNoTracking()
                .Where(u => u.Role == UserRole.Rider
                         && u.IsAvailable
                         && u.LocationSharingEnabled
                         && u.Latitude != null
                         && u.Longitude != null
                         && u.LocationUpdatedAt >= freshnessThreshold)
                .Select(u => new { u.Id, u.Latitude, u.Longitude })
                .ToListAsync();

            var exclude = excludeRiderIds?.ToHashSet() ?? new HashSet<Guid>();

            var byGps = riders
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
                .Take(count)
                .ToList();

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
            var normalized = Normalize(vendorWhatsApp);
            var vendors = await _context.Users.AsNoTracking()
                .Where(u => u.Role == UserRole.Vendor && u.PhoneNumber != null)
                .ToListAsync();

            return vendors.FirstOrDefault(v => Normalize(v.PhoneNumber) == normalized);
        }

        private static string Normalize(string? phone)
            => new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
    }
}

