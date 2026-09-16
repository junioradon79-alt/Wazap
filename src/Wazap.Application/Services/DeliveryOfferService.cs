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
        private readonly ClientPaymentOptions _clientPayments;
        private readonly ILogger<DeliveryOfferService> _logger;

        /// <summary>
        /// Sélection des candidats (P2 / C-13) : extraite avec les options qu'elle utilise
        /// (zone/rayon, certification obligatoire, réputation, priorité payante). Construite ici
        /// plutôt qu'injectée : ses dépendances sont exactement celles de ce service, et l'ajouter
        /// au constructeur aurait imposé de modifier neuf sites de construction sans rien apporter.
        /// </summary>
        private readonly RiderMatchingService _matching;

        /// <summary>
        /// Acceptation d'une offre (P2 / C-13) : chemin de l'argent (débit du crédit vendeur),
        /// extrait dans sa propre classe. Construit ici pour la même raison que le matching :
        /// dépendances identiques, aucun appelant à modifier.
        /// </summary>
        private readonly OfferAcceptanceService _acceptance;

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
            _clientPayments = clientPayments;
            _logger = logger;
            _matching = new RiderMatchingService(context, geo, riderSecurity, reputation, priority);
            // Le journal de ce service est transmis tel quel : les lignes d'acceptation gardent
            // leur catégorie d'origine (« DeliveryOfferService »), utile au diagnostic en prod.
            _acceptance = new OfferAcceptanceService(context, orchestrator, logger);
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

            var nearest = await _matching.FindNearestAsync(vendor.Id, count: 5, contactedRiderIds);

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

            var nearest = await _matching.FindNearestAsync(vendor.Id, count: 5, contactedRiderIds);

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
        /// Accepte une offre (livreur) : voir <see cref="OfferAcceptanceService"/> (P2 / C-13).
        /// Cette façade est conservée pour ne pas modifier les appelants (webhook, workers, API) :
        /// l'acceptation reste exposée par le service de diffusion, mais vit désormais dans une
        /// classe dédiée avec ses invariants (réclamation atomique, débit conditionnel, transaction).
        /// </summary>
        public Task AcceptOfferAsync(Guid offerId) => _acceptance.AcceptOfferAsync(offerId);

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

