using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wazap.Application.Abstractions;
using Wazap.Application.Exceptions;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;

namespace Wazap.Application.Services
{
    /// <summary>
    /// Acceptation d'une offre par un livreur (P2 / C-13 : extrait de
    /// <see cref="DeliveryOfferService"/>, qui portait 1 078 lignes).
    ///
    /// C'est le chemin de l'ARGENT du produit : c'est ici, et nulle part ailleurs, que le vendeur
    /// est débité d'un crédit par course acceptée. Trois invariants y sont tenus :
    /// <list type="number">
    /// <item>la réclamation de l'offre est <b>atomique</b> (UPDATE conditionnel <c>Pending</c> →
    /// <c>Accepted</c>) : deux livreurs qui répondent « ACCEPTE » au même instant ne peuvent pas
    /// accepter la même course ;</item>
    /// <item>le débit est <b>conditionnel</b> (<c>Credits &gt;= n</c>) : il ne peut ni rendre le
    /// solde négatif, ni consommer deux fois le même crédit ;</item>
    /// <item>les deux vivent dans la <b>même transaction</b> : jamais de course assignée sans
    /// crédit consommé, et l'échec du débit annule la réclamation.</item>
    /// </list>
    ///
    /// Les notifications (client, vendeur, livreur, alerte de crédits bas) sont volontairement
    /// « best effort » : leur échec ne remet pas en cause une acceptation déjà validée en base.
    /// </summary>
    public sealed class OfferAcceptanceService
    {
        private readonly IApplicationDbContext _context;
        private readonly WhatsAppOrchestrationService _orchestrator;
        private readonly ILogger _logger;

        /// <summary>
        /// Le journal est reçu comme <see cref="ILogger"/> (et non <c>ILogger&lt;OfferAcceptanceService&gt;</c>)
        /// pour que son propriétaire transmette le sien : les lignes d'acceptation gardent ainsi
        /// exactement la même catégorie de journal qu'avant l'extraction.
        /// </summary>
        public OfferAcceptanceService(
            IApplicationDbContext context,
            WhatsAppOrchestrationService orchestrator,
            ILogger logger)
        {
            _context = context;
            _orchestrator = orchestrator;
            _logger = logger;
        }

        /// <summary>
        /// Accepte une offre (livreur) : assigne le livreur à la commande (ou à toutes les
        /// commandes du lot groupé) et expire les autres offres.
        /// <para>
        /// La réclamation de l'offre et le débit des crédits sont ATOMIQUES (UPDATE conditionnel
        /// en base, dans une transaction) : sans cela, deux livreurs qui répondent « ACCEPTE »
        /// au même instant acceptaient tous les deux la même course — le vendeur était débité
        /// deux fois et un livreur se déplaçait pour rien.
        /// </para>
        /// </summary>
        public async Task AcceptOfferAsync(Guid offerId)
        {
            var offer = await _context.DeliveryOffers.AsNoTracking().FirstOrDefaultAsync(o => o.Id == offerId)
                ?? throw new InvalidOperationException("Offre introuvable.");

            var rider = await _context.Users.FirstOrDefaultAsync(u => u.Id == offer.RiderUserId)
                ?? throw new InvalidOperationException("Livreur introuvable.");

            var riderPhone = rider.PhoneNumber
                ?? throw new InvalidOperationException("Le livreur n'a pas de numéro WhatsApp.");

            // La transaction (relationnelle) garantit que la réclamation de l'offre est annulée
            // si le débit des crédits échoue : jamais de course assignée sans crédit consommé.
            var relational = _context.SupportsConditionalUpdates;
            await using var transaction = relational
                ? await _context.Database.BeginTransactionAsync()
                : null;

            if (!await TryClaimOfferAsync(offerId))
                throw new InvalidOperationException("Cette offre n'est plus disponible (déjà acceptée ou expirée).");

            // Offre de lot groupé : le livreur prend TOUTES les commandes du lot.
            if (offer.BatchId is not null)
            {
                await AcceptBatchAsync(offer, rider, riderPhone);

                if (transaction is not null)
                    await transaction.CommitAsync();
                return;
            }

            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == offer.OrderId)
                ?? throw new InvalidOperationException("Commande introuvable.");

            // Débit du crédit UNIQUEMENT à l'acceptation de la course par le livreur.
            var vendor = await DebitVendorForOrdersAsync(order.VendorUserId, 1);

            order.AssignRider(riderPhone);
            order.LinkRider(rider.Id);

            // Preuve de livraison : le code est généré AVANT le SaveChanges pour être
            // persisté, puis envoyé au client dans les notifications d'acceptation.
            order.EnsureDeliveryCode();

            var otherPending = await _context.DeliveryOffers
                .Where(o => o.OrderId == order.Id && o.Id != offer.Id && o.Status == DeliveryOfferStatus.Pending)
                .ToListAsync();

            foreach (var other in otherPending)
                other.Expire();

            await _context.SaveChangesAsync();

            if (transaction is not null)
                await transaction.CommitAsync();

            // Alerte crédits bas/épuisés après débit (best effort).
            await NotifyVendorCreditAsync(vendor);

            // Notifications post-acceptation (best effort) : client + vendeur + livreur.
            try
            {
                var pickupLink = vendor is { Latitude: not null, Longitude: not null }
                    ? MapLink(vendor.Latitude.Value, vendor.Longitude.Value)
                    : null;
                var dropoffLink = order.ClientLatitude is { } lat && order.ClientLongitude is { } lng
                    ? MapLink(lat, lng)
                    : null;

                var profileLine = await BuildRiderProfileLineAsync(rider.Id);
                await _orchestrator.SendRiderAssignedAsync(order, rider, pickupLink, dropoffLink, profileLine);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Notifications d'acceptation impossibles pour la commande {OrderId}.", order.Id);
            }
        }

        /// <summary>
        /// Réclame l'offre de façon atomique : un seul appelant peut passer son statut de
        /// « Pending » à « Accepted ». Retourne <c>false</c> si un autre livreur l'a déjà prise
        /// (ou si elle a expiré) — le second perdant ne doit RIEN débiter ni assigner.
        /// </summary>
        private async Task<bool> TryClaimOfferAsync(Guid offerId)
        {
            if (_context.SupportsConditionalUpdates)
            {
                var respondedAt = DateTime.UtcNow;
                var affected = await _context.DeliveryOffers
                    .Where(o => o.Id == offerId && o.Status == DeliveryOfferStatus.Pending)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(o => o.Status, DeliveryOfferStatus.Accepted)
                        .SetProperty(o => o.RespondedAt, (DateTime?)respondedAt));

                return affected == 1;
            }

            // Fournisseur non relationnel (tests InMemory) : pas d'UPDATE conditionnel.
            var offer = await _context.DeliveryOffers.FirstOrDefaultAsync(o => o.Id == offerId);
            if (offer is null || offer.Status != DeliveryOfferStatus.Pending)
                return false;

            offer.Accept();
            return true;
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
                // L'offre déjà réclamée par ce livreur est exclue (elle n'est plus « Pending »).
                var stalePending = await _context.DeliveryOffers
                    .Where(o => o.BatchId == batch.Id && o.Id != offer.Id && o.Status == DeliveryOfferStatus.Pending)
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

                // Un code de livraison distinct par client de la tournée.
                order.EnsureDeliveryCode();
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
                var pickupLink = vendor is { Latitude: not null, Longitude: not null }
                    ? MapLink(vendor.Latitude.Value, vendor.Longitude.Value)
                    : null;

                var profileLine = await BuildRiderProfileLineAsync(rider.Id);
                await _orchestrator.SendBatchAssignedAsync(rider, orders, pickupLink, profileLine);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Notifications d'acceptation impossibles pour le lot {BatchId}.", batch.Id);
            }
        }

        /// <summary>
        /// Débite le vendeur de <paramref name="ordersCount"/> crédit(s) (un par course acceptée).
        /// Ne fait rien si la commande n'a pas de vendeur lié (données historiques).
        /// <para>
        /// Le débit est un UPDATE conditionnel (<c>Credits &gt;= n</c>) : il est donc atomique et
        /// ne peut ni rendre le solde négatif, ni consommer deux fois le même crédit quand deux
        /// acceptations arrivent en parallèle. Le vendeur est relu SANS suivi ensuite, car la
        /// valeur en base vient d'être modifiée hors du change tracker.
        /// </para>
        /// </summary>
        private async Task<User?> DebitVendorForOrdersAsync(Guid? vendorUserId, int ordersCount)
        {
            if (vendorUserId is null)
                return null;

            var vendorId = vendorUserId.Value;

            if (_context.SupportsConditionalUpdates)
            {
                // Un seul UPDATE : « décrémente si le solde suffit ». affected == 0 ⇒ crédits
                // insuffisants (ou vendeur inexistant) — rien n'a été modifié.
                var affected = await _context.Users
                    .Where(u => u.Id == vendorId && u.Role == UserRole.Vendor && u.Credits >= ordersCount)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(u => u.Credits, u => u.Credits - ordersCount));

                if (affected != 1)
                {
                    var remaining = await _context.Users.AsNoTracking()
                        .Where(u => u.Id == vendorId)
                        .Select(u => (int?)u.Credits)
                        .FirstOrDefaultAsync();

                    if (remaining is null)
                        throw new InvalidOperationException("Vendeur introuvable.");

                    throw new PaymentRequiredException(
                        $"Crédits insuffisants ({remaining} restant(s)) pour {ordersCount} course(s). Rechargez sur /api/packs.");
                }

                return await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == vendorId);
            }

            // Fournisseur non relationnel (tests InMemory) : pas d'UPDATE conditionnel.
            var vendor = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == vendorId && u.Role == UserRole.Vendor)
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

        private static string? MapLink(double latitude, double longitude)
            => $"https://www.google.com/maps/search/?api=1&query={latitude.ToString("F6", CultureInfo.InvariantCulture)},{longitude.ToString("F6", CultureInfo.InvariantCulture)}";

        /// <summary>
        /// Ligne « profil livreur » affichée au vendeur avant la remise du colis :
        /// certification (Garantie Colis Sûr) + nombre de livraisons réalisées.
        /// </summary>
        private async Task<string> BuildRiderProfileLineAsync(Guid riderId)
        {
            var cert = await _context.RiderIdentities.AsNoTracking()
                .FirstOrDefaultAsync(i => i.UserId == riderId);
            var deliveries = await _context.Orders
                .CountAsync(o => o.RiderUserId == riderId && o.Status == OrderStatus.Delivered);

            var statusText = cert?.Status switch
            {
                RiderIdentityStatus.Verified => "Livreur certifié 🛡️",
                RiderIdentityStatus.Blacklisted => "Compte suspendu",
                _ => "Livreur WAZAP (vérification en cours)"
            };

            var line = $"{statusText} · {deliveries} livraison(s)";

            // Réputation : affichée seulement s'il existe des notes, pour ne pas afficher
            // « 0/5 » à un livreur qui n'a simplement jamais été noté.
            var scores = await _context.RiderRatings.AsNoTracking()
                .Where(r => r.RiderUserId == riderId)
                .Select(r => r.Score)
                .ToListAsync();

            if (scores.Count > 0)
                line += $" · ⭐ {scores.Average():0.#}/5 ({scores.Count} avis)";

            return line;
        }
    }
}
