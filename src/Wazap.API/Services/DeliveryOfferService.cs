using Microsoft.EntityFrameworkCore;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Dtos;
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
        private readonly ILogger<DeliveryOfferService> _logger;

        public DeliveryOfferService(
            ApplicationDbContext context,
            IWhatsAppSender whatsApp,
            WhatsAppOptions whatsAppOptions,
            GeoOptions geo,
            ILogger<DeliveryOfferService> logger)
        {
            _context = context;
            _whatsApp = whatsApp;
            _whatsAppOptions = whatsAppOptions;
            _geo = geo;
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
        /// Accepte une offre (livreur), assigne le livreur à la commande et expire les autres offres.
        /// </summary>
        public async Task AcceptOfferAsync(Guid offerId)
        {
            var offer = await _context.DeliveryOffers.FirstOrDefaultAsync(o => o.Id == offerId)
                ?? throw new InvalidOperationException("Offre introuvable.");

            offer.Accept();

            var rider = await _context.Users.FirstOrDefaultAsync(u => u.Id == offer.RiderUserId)
                ?? throw new InvalidOperationException("Livreur introuvable.");

            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == offer.OrderId)
                ?? throw new InvalidOperationException("Commande introuvable.");

            var riderPhone = rider.PhoneNumber
                ?? throw new InvalidOperationException("Le livreur n'a pas de numéro WhatsApp.");

            order.AssignRider(riderPhone);

            var otherPending = await _context.DeliveryOffers
                .Where(o => o.OrderId == order.Id && o.Id != offer.Id && o.Status == DeliveryOfferStatus.Pending)
                .ToListAsync();

            foreach (var other in otherPending)
                other.Expire();

            await _context.SaveChangesAsync();
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

            return riders
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

