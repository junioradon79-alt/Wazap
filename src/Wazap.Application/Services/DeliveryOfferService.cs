using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
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
        private readonly ClientOptions _client;
        private readonly WhatsAppOrchestrationService _orchestrator;
        private readonly RiderSecurityOptions _riderSecurity;
        private readonly RiderReputationOptions _reputation;
        private readonly ClientPaymentOptions _clientPayments;
        private readonly RiderPriorityOptions _priority;
        private readonly ILogger<DeliveryOfferService> _logger;

        public DeliveryOfferService(
            IApplicationDbContext context,
            IWhatsAppSender whatsApp,
            WhatsAppOptions whatsAppOptions,
            GeoOptions geo,
            GroupingOptions grouping,
            ClientOptions client,
            WhatsAppOrchestrationService orchestrator,
            RiderSecurityOptions riderSecurity,
            RiderReputationOptions reputation,
            ClientPaymentOptions clientPayments,
            RiderPriorityOptions priority,
            ILogger<DeliveryOfferService> logger)
        {
            _context = context;
            _whatsApp = whatsApp;
            _whatsAppOptions = whatsAppOptions;
            _geo = geo;
            _grouping = grouping;
            _client = client;
            _orchestrator = orchestrator;
            _riderSecurity = riderSecurity;
            _reputation = reputation;
            _clientPayments = clientPayments;
            _priority = priority;
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

            // Envoi des offres via WhatsApp (best effort). Une SEULE requête pour résoudre les
            // numéros : l'ancienne boucle faisait une requête Users par offre diffusée.
            var ridersById = await LoadRiderPhonesAsync(offers.Select(o => o.RiderUserId));

            foreach (var offer in offers)
            {
                if (!ridersById.TryGetValue(offer.RiderUserId, out var riderPhone)
                    || string.IsNullOrWhiteSpace(riderPhone))
                    continue;

                try
                {
                    // Passe par l'orchestrateur : repli texte si « rider_offer » est refusé
                    // ou en cours d'examen chez Meta.
                    await _orchestrator.SendRiderOfferAsync(
                        riderPhone,
                        offer.Id.ToString("N")[..8].ToUpperInvariant());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Envoi WhatsApp échoué pour l'offre {OfferId}.", offer.Id);
                }
            }

            return new BroadcastResultDto(offers.Count, nextBatch);
        }

        /// <summary>
        /// Numéros WhatsApp des livreurs ciblés, en UNE requête (évite le N+1 : une requête par
        /// offre diffusée, jusqu'à 5 par vague et par lot).
        /// </summary>
        private async Task<IReadOnlyDictionary<Guid, string?>> LoadRiderPhonesAsync(IEnumerable<Guid> riderIds)
        {
            var ids = riderIds.Distinct().ToList();
            if (ids.Count == 0)
                return new Dictionary<Guid, string?>();

            return await _context.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, u.PhoneNumber })
                .ToDictionaryAsync(u => u.Id, u => u.PhoneNumber);
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

            // Paiement client bloquant : pas de diffusion tant que le panier n'est pas réglé.
            if (await IsWaitingForClientPaymentAsync(orderId))
                throw new InvalidOperationException(RequiresClientPaymentMessage);

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
        /// Routage après confirmation vendeur :
        /// - parcours acheteur (coordonnées client attendues) → envoi du lien de suivi au
        ///   client, PAS de diffusion (elle attendra la validation des coordonnées) ;
        /// - sinon → groupage classique (le worker diffuse).
        /// </summary>
        public async Task ConfirmAndRouteAsync(Guid orderId)
        {
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId)
                ?? throw new InvalidOperationException("Commande introuvable.");

            if (!order.RequiresClientCoordinates || string.IsNullOrWhiteSpace(order.ClientWhatsAppNumber))
            {
                await JoinOrCreateBatchAsync(orderId);
                return;
            }

            // Parcours acheteur : on envoie le lien de la page de suivi au client.
            var vendor = order.VendorUserId is { } vendorId
                ? await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == vendorId)
                : null;

            var code = order.Id.ToString("N")[..8].ToUpperInvariant();
            var url = $"{_client.TrackingBaseUrl.TrimEnd('/')}/{order.Id}";

            try
            {
                await _orchestrator.SendClientTrackingLinkAsync(
                    order.ClientWhatsAppNumber, code, vendor?.Username ?? "le vendeur", url);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lien de suivi impossible pour {OrderId}.", order.Id);
            }
        }

        /// <summary>
        /// Lance la recherche des livreurs dès que le client a validé ses coordonnées
        /// (parcours acheteur). Diffusion immédiate + notification au client.
        /// </summary>
        public async Task<BroadcastResultDto> DispatchConfirmedOrderAsync(Guid orderId)
        {
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId)
                ?? throw new InvalidOperationException("Commande introuvable.");

            if (order.ClientLatitude is null || order.ClientLongitude is null)
                throw new InvalidOperationException("Coordonnées du client manquantes.");

            if (order.Status != OrderStatus.VendorConfirmed)
                throw new InvalidOperationException($"État actuel : {order.Status}. Diffusion impossible.");

            // Paiement client bloquant : pas de diffusion tant que le panier n'est pas réglé.
            if (await IsWaitingForClientPaymentAsync(orderId))
                throw new InvalidOperationException(RequiresClientPaymentMessage);

            var batchId = await JoinOrCreateBatchAsync(order.Id);

            // Diffusion différée : le worker (DeliveryOfferWorker) diffusera le lot après le
            // délai de groupage (Grouping:BuyerDispatchDelaySeconds) afin de laisser les autres
            // clients du même vendeur rejoindre la tournée multi-clients. Les notifications
            // « recherche en cours » partent immédiatement pour rassurer le client.

            try
            {
                await _orchestrator.SendDispatchStartedAsync(
                    order.ClientWhatsAppNumber,
                    order.Id.ToString("N")[..8].ToUpperInvariant());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Notification de lancement impossible pour {OrderId}.", order.Id);
            }

            // Le vendeur est notifié : coordonnées reçues, recherche lancée (best effort).
            try
            {
                await _orchestrator.SendVendorDispatchStartedAsync(
                    order.VendorWhatsAppNumber,
                    order.Id.ToString("N")[..8].ToUpperInvariant(),
                    order.ClientAddress);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Notification vendeur impossible pour {OrderId}.", order.Id);
            }

            return new BroadcastResultDto(0, 0);
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

            // Paiement client bloquant : les commandes impayées du lot sont laissées de côté
            // (elles seront diffusées quand leur paiement sera complété — ClientPaymentService).
            if (_clientPayments.RequirePaymentBeforeDispatch && activeOrders.Count > 0)
            {
                var batchOrderIds = activeOrders.Select(o => o.Id).ToList();
                var paidOrderIds = (await _context.OrderPayments.AsNoTracking()
                        .Where(p => p.Status == TransactionStatus.Completed && batchOrderIds.Contains(p.OrderId))
                        .Select(p => p.OrderId)
                        .ToListAsync())
                    .ToHashSet();

                activeOrders = activeOrders.Where(o => paidOrderIds.Contains(o.Id)).ToList();

                if (activeOrders.Count == 0)
                {
                    _logger.LogInformation("Lot {BatchId} non diffusé : aucun paiement client complété (option bloquante).",
                        batchId);
                    return new BroadcastResultDto(0, 0);
                }
            }

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

            // Envoi des offres via WhatsApp (best effort). Une seule requête pour les numéros.
            var ridersById = await LoadRiderPhonesAsync(offers.Select(o => o.RiderUserId));

            foreach (var offer in offers)
            {
                if (!ridersById.TryGetValue(offer.RiderUserId, out var riderPhone)
                    || string.IsNullOrWhiteSpace(riderPhone))
                    continue;

                try
                {
                    await _orchestrator.SendBatchOfferAsync(
                        riderPhone,
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

        /// <summary>
        /// Boîte englobante (degrés) du rayon de diffusion, <b>garantie contenir le cercle</b> :
        /// la marge en longitude est calculée à la latitude la plus HAUTE de la boîte (cas le
        /// plus défavorable, où un degré de longitude est le plus court), ce qui évite d'écarter
        /// un livreur réellement dans le rayon. Le filtre Haversine reste appliqué ensuite.
        /// Près des pôles (ou si la boîte franchit l'antiméridien) on ne filtre pas : mieux vaut
        /// charger quelques candidats de trop que d'en perdre un.
        /// </summary>
        public static (double MinLat, double MaxLat, double MinLon, double MaxLon) BoundingBox(
            double latitude, double longitude, double radiusKm)
        {
            const double kmPerDegreeLatitude = 111.32;
            const double fullRange = 180.0;

            var deltaLat = radiusKm / kmPerDegreeLatitude;

            if (Math.Abs(latitude) + deltaLat >= 89.0)
                return (-90, 90, -fullRange, fullRange);

            var worstLatitude = Math.Min(89.0, Math.Abs(latitude) + deltaLat);
            var cosinus = Math.Max(0.01, Math.Cos(worstLatitude * Math.PI / 180.0));
            var deltaLon = radiusKm / (kmPerDegreeLatitude * cosinus);

            if (longitude - deltaLon < -fullRange || longitude + deltaLon > fullRange)
                return (latitude - deltaLat, latitude + deltaLat, -fullRange, fullRange);

            return (latitude - deltaLat, latitude + deltaLat, longitude - deltaLon, longitude + deltaLon);
        }

        /// <summary>Livreurs disponibles, partage activé, position fraîche (&lt; Geo:LocationFreshnessMinutes)
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

            var nowUtc = DateTime.UtcNow;
            var freshnessThreshold = nowUtc.AddMinutes(-_geo.LocationFreshnessMinutes);

            var exclude = excludeRiderIds?.ToHashSet() ?? new HashSet<Guid>();
            var byGps = new List<NearestRiderDto>();

            // Exclusions exprimées EN SQL (sous-requêtes EXISTS) : charger toutes les identités
            // blacklistées et tous les sinistres en cours pour les croiser en mémoire faisait
            // croître le coût du matching avec l'historique de la plateforme.
            var blacklisted = _context.RiderIdentities.AsNoTracking()
                .Where(i => i.Status == RiderIdentityStatus.Blacklisted);

            // Sinistre en cours d'enquête : le livreur est suspendu tant que le
            // dossier « Garantie Colis Sûr » n'est pas tranché.
            var underInvestigation = _context.DeliveryClaims.AsNoTracking()
                .Where(c => c.Status == DeliveryClaimStatus.Pending);

            // Réputation : écarte les livreurs sous la moyenne minimale. Désactivé par
            // défaut (MinimumAverageScore = 0) et jamais appliqué en dessous d'un nombre
            // suffisant d'avis — un nouveau livreur ne doit pas sortir du vivier sur une
            // seule mauvaise note. Ce filtre agrégé reste chargé en mémoire (il n'est actif
            // que si l'option est activée explicitement, et ne concerne que les notés).
            if (_reputation.MinimumAverageScore > 0)
            {
                var poorlyRated = await _context.RiderRatings.AsNoTracking()
                    .GroupBy(r => r.RiderUserId)
                    .Where(g => g.Count() >= _reputation.MinimumRatingsBeforeFiltering
                             && g.Average(r => r.Score) < _reputation.MinimumAverageScore)
                    .Select(g => g.Key)
                    .ToListAsync();
                exclude.UnionWith(poorlyRated);
            }

            // Tier 1 — GPS (Haversine) : uniquement si le vendeur a une position.
            if (vendor.Latitude is not null && vendor.Longitude is not null)
            {
                var latitude = vendor.Latitude.Value;
                var longitude = vendor.Longitude.Value;

                // Boîte englobante : réduit en SQL l'ensemble des candidats avant le calcul de
                // distance, au lieu de charger tous les livreurs géolocalisés de la plateforme.
                // Elle CONTIENT le cercle (marge de longitude calculée à la latitude la plus
                // haute de la boîte), donc aucun livreur éligible n'est écarté ; le filtre
                // Haversine ci-dessous reste l'arbitre.
                var box = BoundingBox(latitude, longitude, _geo.MaxDistanceKm);

                var gpsQuery = _context.Users.AsNoTracking()
                    .Where(u => u.Role == UserRole.Rider
                             && u.IsAvailable
                             && u.LocationSharingEnabled
                             && u.Latitude != null
                             && u.Longitude != null
                             && u.LocationUpdatedAt >= freshnessThreshold
                             && u.Latitude >= box.MinLat && u.Latitude <= box.MaxLat
                             && u.Longitude >= box.MinLon && u.Longitude <= box.MaxLon
                             && !blacklisted.Any(i => i.UserId == u.Id)
                             && !underInvestigation.Any(c => c.RiderUserId == u.Id));

                // Certification obligatoire (« Garantie Colis Sûr ») : seuls les livreurs dont
                // le dossier d'identité est Verified reçoivent des offres.
                if (_riderSecurity.RequireCertifiedRiders)
                {
                    gpsQuery = gpsQuery.Where(u => _context.RiderIdentities
                        .Any(i => i.UserId == u.Id && i.Status == RiderIdentityStatus.Verified));
                }

                var riders = await gpsQuery
                    .Select(u => new { u.Id, u.Latitude, u.Longitude, u.PriorityUntilUtc })
                    .ToListAsync();

                byGps.AddRange(riders
                    .Where(r => !exclude.Contains(r.Id))
                    .Select(r => new NearestRiderDto(
                        r.Id,
                        GeoDistance.DistanceKm(
                            latitude,
                            longitude,
                            r.Latitude.GetValueOrDefault(),
                            r.Longitude.GetValueOrDefault()),
                        r.PriorityUntilUtc))
                    .Where(x => x.DistanceKm <= _geo.MaxDistanceKm));
            }

            // Tier 2 (téléphones basiques sans GPS) : compléter avec les livreurs
            // dont la ZONE déclarée correspond à celle du vendeur — comparaison faite EN SQL.
            if (byGps.Count < count && !string.IsNullOrWhiteSpace(vendor.Zone))
            {
                var contacted = new HashSet<Guid>(exclude);
                contacted.UnionWith(byGps.Select(r => r.RiderUserId));

                var targetZone = vendor.Zone.Trim().ToLowerInvariant();

                var zoneQuery = _context.Users.AsNoTracking()
                    .Where(u => u.Role == UserRole.Rider
                             && u.IsAvailable
                             && u.LocationSharingEnabled
                             && u.Zone != null
                             && u.Zone.Trim().ToLower() == targetZone
                             && !blacklisted.Any(i => i.UserId == u.Id)
                             && !underInvestigation.Any(c => c.RiderUserId == u.Id));

                if (_riderSecurity.RequireCertifiedRiders)
                {
                    zoneQuery = zoneQuery.Where(u => _context.RiderIdentities
                        .Any(i => i.UserId == u.Id && i.Status == RiderIdentityStatus.Verified));
                }

                var zoneRiders = await zoneQuery
                    .Select(u => new { u.Id, u.PriorityUntilUtc })
                    .ToListAsync();

                byGps.AddRange(zoneRiders
                    .Where(r => !contacted.Contains(r.Id))
                    .Select(r => new NearestRiderDto(r.Id, double.MaxValue, r.PriorityUntilUtc)));
            }

            // Ordre final : pondération par réputation (optionnelle) OU strictement
            // géographique. Le tri par réputation charge les moyennes en une seule requête.
            if (byGps.Count > 1)
            {
                if (_reputation.PreferHigherRatedRiders)
                {
                    var scores = await LoadAverageScoresAsync(byGps.Select(r => r.RiderUserId).ToList());
                    byGps.Sort((a, b) => CompareWithReputation(a, b, scores));
                }
                else
                {
                    byGps.Sort((a, b) => CompareDouble(a.DistanceKm, b.DistanceKm));
                }
            }

            // Pack prioritaire livreur (option payante) : la priorité ACTIVE passe en tête,
            // plafonnée par vague pour ne pas assécher les non-abonnés. Elle ne modifie ni le
            // rayon, ni la disponibilité, ni les exclusions — l'attribution reste à l'acceptation.
            byGps = ApplyPriorityOrdering(byGps, nowUtc);

            return byGps.Take(count).ToList();
        }

        /// <summary>
        /// Note moyenne par livreur (seulement à partir de <see cref="RiderReputationOptions.MinimumRatingsBeforeFiltering"/>
        /// avis — sinon <c>null</c> : le livreur est « neutre » pour la pondération). Les livreurs
        /// sans candidature ne figurent pas dans la map (accès = null).
        /// </summary>
        private async Task<IReadOnlyDictionary<Guid, double?>> LoadAverageScoresAsync(
            IReadOnlyCollection<Guid> riderIds)
        {
            var scores = riderIds.ToDictionary(id => id, id => null as double?);
            if (riderIds.Count == 0)
                return scores;

            var rows = await _context.RiderRatings.AsNoTracking()
                .GroupBy(r => r.RiderUserId)
                .Where(g => g.Count() >= _reputation.MinimumRatingsBeforeFiltering)
                .Select(g => new { RiderId = g.Key, Average = g.Average(r => r.Score) })
                .ToListAsync();

            foreach (var row in rows)
                if (scores.TryGetValue(row.RiderId, out _))
                    scores[row.RiderId] = row.Average;

            return scores;
        }

        /// <summary>
        /// Comparateur du matching pondéré : réputation décroissante (les « neutres », sans assez
        /// d'avis, passent après les notés), puis distance croissante (déterministe).
        /// </summary>
        public static int CompareWithReputation(
            NearestRiderDto a, NearestRiderDto b, IReadOnlyDictionary<Guid, double?> scores)
        {
            var scoreA = scores.TryGetValue(a.RiderUserId, out var rawA) ? rawA : null;
            var scoreB = scores.TryGetValue(b.RiderUserId, out var rawB) ? rawB : null;

            if (scoreA is null && scoreB is null)
                return CompareDouble(a.DistanceKm, b.DistanceKm);
            if (scoreA is null)
                return 1;
            if (scoreB is null)
                return -1;

            var byScore = CompareDouble(scoreB.Value, scoreA.Value);
            return byScore != 0 ? byScore : CompareDouble(a.DistanceKm, b.DistanceKm);
        }

        /// <summary>Comparaison numérique réutilisable (le langage n'a pas de CompareTo sur double).</summary>
        private static int CompareDouble(double a, double b)
            => a < b ? -1 : (a > b ? 1 : 0);

        /// <summary>
        /// Remonte en tête les livreurs dont la priorité (« pack prioritaire ») est active, dans
        /// l'ordre déjà établi par le tri de base (distance ou réputation). Le nombre de places
        /// réservées est plafonné par <see cref="RiderPriorityOptions.MaxPriorityRidersPerWave"/>
        /// (équité) : au-delà, les prioritaires gardent leur rang géographique.
        /// Cette réorganisation n'ajoute ni ne retire aucun candidat.
        /// </summary>
        private List<NearestRiderDto> ApplyPriorityOrdering(List<NearestRiderDto> ordered, DateTime utcNow)
            => ApplyPriorityOrdering(ordered, utcNow, _priority.MaxPriorityRidersPerWave);

        /// <summary>
        /// Variante statique du tri prioritaire (testable isolément, comme
        /// <see cref="CompareWithReputation"/>) : <paramref name="maxPriorityRidersPerWave"/> place
        /// le plafond d'équité. L'ordre relatif des non-prioritaires est préservé.
        /// </summary>
        public static List<NearestRiderDto> ApplyPriorityOrdering(
            List<NearestRiderDto> ordered, DateTime utcNow, int maxPriorityRidersPerWave)
        {
            var cap = maxPriorityRidersPerWave;
            if (cap <= 0)
                return ordered;

            var boosted = new List<NearestRiderDto>(cap);
            var rest = new List<NearestRiderDto>(ordered.Count);

            foreach (var rider in ordered)
            {
                if (boosted.Count < cap && IsPriorityActive(rider, utcNow))
                    boosted.Add(rider);
                else
                    rest.Add(rider);
            }

            if (boosted.Count == 0)
                return ordered;

            boosted.AddRange(rest);
            return boosted;
        }

        /// <summary>Priorité de proposition active pour ce candidat (pack livreur non expiré).</summary>
        public static bool IsPriorityActive(NearestRiderDto rider, DateTime utcNow)
            => rider.PriorityUntilUtc is { } until && until > utcNow;

        /// <summary>Message d'erreur quand la diffusion attend un paiement client (option bloquante).</summary>
        private const string RequiresClientPaymentMessage =
            "Le paiement du client est requis avant la diffusion des livreurs " +
            "(ClientPayments:RequirePaymentBeforeDispatch).";

        /// <summary>
        /// Option bloquante activée ET aucun paiement client complété pour la commande.
        /// </summary>
        private async Task<bool> IsWaitingForClientPaymentAsync(Guid orderId)
            => _clientPayments.RequirePaymentBeforeDispatch
               && !await _context.OrderPayments.AsNoTracking()
                   .AnyAsync(p => p.OrderId == orderId && p.Status == TransactionStatus.Completed);

        private async Task<User?> ResolveVendorAsync(string vendorWhatsApp)
        {
            if (string.IsNullOrWhiteSpace(vendorWhatsApp))
                return null;

            // Pré-filtre INDEXÉ (8 derniers chiffres) puis confirmation exacte : la diffusion
            // chargeait auparavant TOUTE la table des vendeurs, à chaque vague et par lot.
            var suffix = PhoneNumberNormalizer.SubscriberSuffix(vendorWhatsApp);
            if (suffix.Length == 0)
                return null;

            var vendors = await _context.Users.AsNoTracking()
                .Where(u => u.Role == UserRole.Vendor && u.PhoneSuffix == suffix)
                .ToListAsync();

            return vendors.FirstOrDefault(v =>
                PhoneNumberNormalizer.SameSubscriber(v.PhoneNumber, vendorWhatsApp));
        }
    }
}

