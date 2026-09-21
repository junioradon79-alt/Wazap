using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Wazap.API.Services;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Exceptions;
using Wazap.Application.Helpers;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Wazap.Infrastructure.Services;

namespace Wazap.API.Controllers;

[ApiController]
[Route("api/webhook/whatsapp")]
public class WebhookWhatsAppController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly RiderService _riderService;
    private readonly RiderRecruitmentService _riderRecruitment;
    private readonly VendorService _vendorService;
    private readonly DeliveryOfferService _deliveryOfferService;
    private readonly WhatsAppOrchestrationService _whatsApp;
    private readonly ProspectAutoService _prospects;
    private readonly ClientOrderBotService _clientOrders;
    private readonly LeadConversionService _leadConversion;
    private readonly IWhatsAppSender _whatsAppSender;
    private readonly IWhatsAppMediaDownloader _mediaDownloader;
    private readonly RiderRatingService _riderRatings;
    private readonly RiderDeliveryCommands _riderDeliveries;
    private readonly VendorTextCommands _vendorCommands;
    private readonly RiderTextCommands _riderCommands;
    private readonly RiderScansOptions _scans;
    private readonly ILogger<WebhookWhatsAppController> _logger;
    private readonly IWhatsAppMessageLogService? _messageLogService;
    private readonly string? _teamPhone;

    // Après les extractions (P2 / C-13), le contrôleur ne dépend PLUS des services qui ont suivi
    // leurs commandes : VendorProductService, ColisSurService, DeliveryProofOptions (commandes
    // vendeur / preuve de livraison) et RiderProgramService (progression Ambassadeur) sont
    // désormais portés par les gestionnaires concernés. Les retirer évite de laisser croire que
    // le contrôleur les utilise — et le conteneur n'a plus à les résoudre pour lui.
    public WebhookWhatsAppController(
        ApplicationDbContext context,
        RiderService riderService,
        RiderRecruitmentService riderRecruitment,
        VendorService vendorService,
        DeliveryOfferService deliveryOfferService,
        WhatsAppOrchestrationService whatsApp,
        ProspectAutoService prospects,
        ClientOrderBotService clientOrders,
        LeadConversionService leadConversion,
        IWhatsAppSender whatsAppSender,
        IWhatsAppMediaDownloader mediaDownloader,
        RiderRatingService riderRatings,
        RiderDeliveryCommands riderDeliveries,
        VendorTextCommands vendorCommands,
        RiderTextCommands riderCommands,
        RiderScansOptions scans,
        ILogger<WebhookWhatsAppController> logger,
        IConfiguration config,
        IWhatsAppMessageLogService? messageLogService = null)
    {
        _riderRatings = riderRatings;
        _riderDeliveries = riderDeliveries;
        _vendorCommands = vendorCommands;
        _riderCommands = riderCommands;
        _context = context;
        _riderService = riderService;
        _riderRecruitment = riderRecruitment;
        _vendorService = vendorService;
        _deliveryOfferService = deliveryOfferService;
        _whatsApp = whatsApp;
        _prospects = prospects;
        _clientOrders = clientOrders;
        _leadConversion = leadConversion;
        _whatsAppSender = whatsAppSender;
        _mediaDownloader = mediaDownloader;
        _scans = scans;
        _logger = logger;
        _messageLogService = messageLogService;
        // AUCUNE valeur de repli : un token codé en dur dans un dépôt public n'authentifie
        // rien. Non configuré => la vérification du webhook échoue (fail closed).
        _metaVerifyToken = config["Meta:WebhookVerifyToken"];
        _wahaToken = config["Waha:WebhookSecret"];
        _whatChimpToken = config["WhatChimp:WebhookToken"];
        _teamPhone = config["Prospect:TeamPhone"];
    }

    private readonly string? _metaVerifyToken;
    private readonly string? _wahaToken;
    private readonly string? _whatChimpToken;

    /// <summary>
    /// Jeton d'annulation de la requête en cours.
    /// <see cref="ControllerBase.HttpContext"/> est nul quand le contrôleur est instancié
    /// directement (tests unitaires du webhook) : on retombe alors sur un jeton inerte.
    /// </summary>
    private CancellationToken RequestAborted => HttpContext?.RequestAborted ?? CancellationToken.None;

    // GET: api/webhook/whatsapp — vérification passerelle (Meta Cloud API, WAHA et WhatChimp legacy)
    [HttpGet]
    public IActionResult Verify(
        [FromQuery(Name = "hub.mode")] string? hubMode,
        [FromQuery(Name = "hub.verify_token")] string? hubVerifyToken,
        [FromQuery(Name = "hub.challenge")] string? hubChallenge,
        [FromQuery] string? token,
        [FromQuery] string? challenge)
    {
        var hasConfiguredToken = !string.IsNullOrWhiteSpace(_metaVerifyToken)
                                 || !string.IsNullOrWhiteSpace(_wahaToken)
                                 || !string.IsNullOrWhiteSpace(_whatChimpToken);

        if (!hasConfiguredToken)
        {
            // Fail closed : mieux vaut un webhook non vérifiable qu'un webhook validé
            // par un secret connu de tous.
            _logger.LogError("Aucun token de vérification de webhook configuré (Meta / WAHA / WhatChimp).");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Webhook non configuré.");
        }

        // Meta Cloud API : hub.mode=subscribe&hub.verify_token=…&hub.challenge=…
        if (string.Equals(hubMode, "subscribe", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(hubChallenge))
                return BadRequest("hub.challenge requis.");

            var isMetaValid = !string.IsNullOrWhiteSpace(_metaVerifyToken)
                              && SecurityHelper.FixedTimeEquals(hubVerifyToken ?? string.Empty, _metaVerifyToken);
            var isWahaValid = !string.IsNullOrWhiteSpace(_wahaToken)
                              && SecurityHelper.FixedTimeEquals(hubVerifyToken ?? string.Empty, _wahaToken);

            return isMetaValid || isWahaValid
                ? Ok(hubChallenge)
                : BadRequest("hub.verify_token invalide.");
        }

        // WhatChimp legacy / WAHA query token : token & challenge en query
        if (!string.IsNullOrWhiteSpace(token))
        {
            var isMetaValid = !string.IsNullOrWhiteSpace(_metaVerifyToken)
                              && SecurityHelper.FixedTimeEquals(token, _metaVerifyToken);
            var isWahaValid = !string.IsNullOrWhiteSpace(_wahaToken)
                              && SecurityHelper.FixedTimeEquals(token, _wahaToken);
            var isWhatChimpValid = !string.IsNullOrWhiteSpace(_whatChimpToken)
                                   && SecurityHelper.FixedTimeEquals(token, _whatChimpToken);

            return isMetaValid || isWahaValid || isWhatChimpValid
                ? Ok(challenge)
                : BadRequest("Token invalide.");
        }

        return BadRequest("Paramètre(s) de vérification manquant(s).");
    }

    // POST: api/webhook/whatsapp — événements (live location, boutons vendeur, ACCEPTE livreur)
    [HttpPost]
    [EnableRateLimiting("webhook")]
    public async Task<IActionResult> Handle([FromBody] JsonElement raw)
    {
        // Log du payload brut (niveau Debug) : utile pour valider un nouveau format de
        // passerelle, mais il contient des numéros et des messages de clients (données
        // personnelles) — il n'a donc rien à faire dans les logs de production.
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Webhook RAW reçu : {Raw}", raw.GetRawText());

        // Passerelle Meta Cloud API : un payload peut porter PLUSIEURS messages
        // (entry[] × changes[] × messages[]). L'ancienne lecture n'en traitait qu'UN SEUL —
        // les autres étaient perdus sans trace ni réessai, désynchronisant la conversation.
        if (MetaWebhookParser.IsMetaPayload(raw))
        {
            var events = MetaWebhookParser.ParseAll(raw);
            if (events.Count == 0)
                return Ok(); // accusés de réception / statuts de livraison : rien à router

            IActionResult? last = null;
            foreach (var metaEvent in events)
            {
                // Déduplication : la passerelle réessaie tout ce qui n'a pas répondu 2xx et
                // peut réémettre un événement déjà livré. Sans ce filtre, un « LIVRAISON »
                // rejoué créait une SECONDE commande et une seconde vague d'offres.
                var messageId = metaEvent.MessageId;
                if (!string.IsNullOrWhiteSpace(messageId) && !await TryClaimMessageAsync(messageId))
                {
                    _logger.LogInformation(
                        "Webhook Meta {MessageId} déjà traité : ignoré (reprise de la passerelle).", messageId);
                    continue;
                }

                try
                {
                    if (_messageLogService != null && !string.IsNullOrWhiteSpace(metaEvent.From))
                    {
                        var msgType = !string.IsNullOrWhiteSpace(metaEvent.MediaId) ? "Media" :
                                      !string.IsNullOrWhiteSpace(metaEvent.ButtonId) ? "Interactive" : "Text";
                        await _messageLogService.LogInboundAsync(
                            metaEvent.From,
                            metaEvent.Text ?? metaEvent.ButtonTitle,
                            msgType,
                            provider: "Meta",
                            providerMessageId: messageId,
                            ct: RequestAborted);
                    }

                    last = await RouteMessageAsync(
                        metaEvent.From, metaEvent.Text, metaEvent.Latitude, metaEvent.Longitude,
                        metaEvent.ButtonId, metaEvent.ButtonTitle,
                        metaEvent.MediaUrl, metaEvent.MediaId, metaEvent.MimeType,
                        message: null);
                }
                catch
                {
                    // Le traitement a échoué : on libère le marqueur pour que la reprise de la
                    // passerelle puisse retraiter le message (sinon il serait perdu).
                    if (!string.IsNullOrWhiteSpace(messageId))
                        await ReleaseClaimAsync(messageId);

                    throw;
                }
            }

            return last ?? Ok();
        }

        // Passerelle WAHA (WhatsApp HTTP API) : payload autonome { event, session, payload }
        if (WahaWebhookParser.IsWahaPayload(raw))
        {
            var events = WahaWebhookParser.ParseAll(raw);
            if (events.Count == 0)
                return Ok(); // messages fromMe ignorés, accusés de réception, etc.

            IActionResult? last = null;
            foreach (var wahaEvent in events)
            {
                var messageId = wahaEvent.MessageId;
                if (!string.IsNullOrWhiteSpace(messageId) && !await TryClaimMessageAsync(messageId))
                {
                    _logger.LogInformation(
                        "Webhook WAHA {MessageId} déjà traité : ignoré (reprise de la passerelle).", messageId);
                    continue;
                }

                try
                {
                    if (_messageLogService != null && !string.IsNullOrWhiteSpace(wahaEvent.From))
                    {
                        var msgType = !string.IsNullOrWhiteSpace(wahaEvent.MediaId) ? "Media" :
                                      !string.IsNullOrWhiteSpace(wahaEvent.ButtonId) ? "Interactive" : "Text";
                        await _messageLogService.LogInboundAsync(
                            wahaEvent.From,
                            wahaEvent.Text ?? wahaEvent.ButtonTitle,
                            msgType,
                            provider: "WAHA",
                            providerMessageId: messageId,
                            ct: RequestAborted);
                    }

                    last = await RouteMessageAsync(
                        wahaEvent.From, wahaEvent.Text, wahaEvent.Latitude, wahaEvent.Longitude,
                        wahaEvent.ButtonId, wahaEvent.ButtonTitle,
                        wahaEvent.MediaUrl, wahaEvent.MediaId, wahaEvent.MimeType,
                        message: null);
                }
                catch
                {
                    if (!string.IsNullOrWhiteSpace(messageId))
                        await ReleaseClaimAsync(messageId);

                    throw;
                }
            }

            return last ?? Ok();
        }

        // Format historique (WhatChimp) : lecture tolérante du payload (camelCase ET snake_case)
        var data = JsonPayloadReader.Find(raw, "data");
        var subscriber = JsonPayloadReader.Find(data, "subscriber");
        // Fallback : certains envois (ex. image) placent « message » à la racine, pas
        // dans « data ». Même tolérance que phone/text ci-dessous (lecture tolérante).
        var message = JsonPayloadReader.Find(data, "message") ?? JsonPayloadReader.Find(raw, "message");
        var location = JsonPayloadReader.Find(message, "location");
        var interactive = JsonPayloadReader.Find(message, "interactive");
        var buttonReply = JsonPayloadReader.Find(interactive, "buttonReply");

        var phone = JsonPayloadReader.Str(subscriber, "phoneNumber")
                    ?? JsonPayloadReader.Str(raw, "chat_id")
                    ?? JsonPayloadReader.Str(raw, "chatId");
        var text = JsonPayloadReader.Str(message, "text")
                   ?? JsonPayloadReader.Str(raw, "user_message")
                   ?? JsonPayloadReader.Str(raw, "userMessage");
        var latitude = JsonPayloadReader.Dbl(location, "latitude");
        var longitude = JsonPayloadReader.Dbl(location, "longitude");
        var buttonId = JsonPayloadReader.Str(buttonReply, "id");
        var buttonTitle = JsonPayloadReader.Str(buttonReply, "title");

        if (_messageLogService != null && !string.IsNullOrWhiteSpace(phone))
        {
            var msgType = !string.IsNullOrWhiteSpace(buttonId) ? "Interactive" : "Text";
            await _messageLogService.LogInboundAsync(
                phone,
                text ?? buttonTitle,
                msgType,
                provider: "WhatChimp",
                providerMessageId: null,
                ct: RequestAborted);
        }

        return await RouteMessageAsync(phone, text, latitude, longitude, buttonId, buttonTitle,
            mediaUrl: null, mediaId: null, mimeType: null, message);
    }

    /// <summary>
    /// Réserve l'identifiant d'un message entrant. Retourne <c>false</c> si le message a déjà
    /// été traité (reprise de la passerelle).
    /// <para>
    /// La clé primaire de <c>ProcessedWebhookMessages</c> garantit l'unicité EN BASE : la
    /// lecture préalable évite l'aller-retour inutile, et la violation d'unicité couvre la
    /// course entre deux livraisons simultanées du même message.
    /// </para>
    /// </summary>
    private async Task<bool> TryClaimMessageAsync(string messageId)
    {
        if (await _context.ProcessedWebhookMessages.AsNoTracking().AnyAsync(m => m.Id == messageId))
            return false;

        try
        {
            _context.ProcessedWebhookMessages.Add(new ProcessedWebhookMessage(messageId));
            await _context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            // Course perdue : un autre traitement a réservé le message en premier.
            _context.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>Libère une réservation dont le traitement a échoué (best-effort).</summary>
    private async Task ReleaseClaimAsync(string messageId)
    {
        try
        {
            var claimed = await _context.ProcessedWebhookMessages.FindAsync(messageId);
            if (claimed is null)
                return;

            _context.ProcessedWebhookMessages.Remove(claimed);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de libérer le marqueur du message {MessageId}.", messageId);
        }
    }

    /// <summary>
    /// Routage d'UN message entrant, commun aux deux passerelles : position livreur, média
    /// (scan d'identité / preuve de livraison), confirmation ou refus vendeur, conversion
    /// d'un lead par l'équipe, commandes texte, acceptation d'offre, note client puis bots.
    /// </summary>
    /// <param name="message">
    /// Nœud brut du message pour la passerelle historique (lecture tolérante des médias) ;
    /// <c>null</c> pour un événement Meta, dont les champs média sont déjà normalisés.
    /// </param>
    private async Task<IActionResult> RouteMessageAsync(
        string? phone,
        string? text,
        double? latitude,
        double? longitude,
        string? buttonId,
        string? buttonTitle,
        string? mediaUrl,
        string? mediaId,
        string? mimeType,
        JsonElement? message)
    {
        // 1) Live location du livreur → mise à jour de sa position
        if (latitude is not null && longitude is not null)
        {
            var rider = await FindUserByPhoneAsync(phone, UserRole.Rider);
            if (rider is not null)
            {
                await _riderService.UpdateLocationAsync(rider.Id, latitude.Value, longitude.Value);
                _logger.LogInformation("Position du livreur {RiderId} mise à jour via webhook.", rider.Id);
            }
            return Ok();
        }

        // 1b) Média entrant — photo de la pièce d'identité envoyée par un livreur
        //     (« Garantie Colis Sûr »). Une image n'emprunte jamais le routage texte :
        //     sans cette branche, elle serait ignorée en silence. Lecture tolérante du
        //     payload (la passerelle n'a pas de forme média unique et documentée).
        var mediaNode = JsonPayloadReader.Find(message, "media") ?? JsonPayloadReader.Find(message, "image") ?? JsonPayloadReader.Find(message, "photo");
        mediaUrl ??= JsonPayloadReader.Str(message, "mediaUrl") ?? JsonPayloadReader.Str(message, "media_url")
            ?? JsonPayloadReader.Str(mediaNode, "url") ?? JsonPayloadReader.Str(mediaNode, "link")
            ?? JsonPayloadReader.Str(message, "url") ?? JsonPayloadReader.Str(message, "link");
        mediaId ??= JsonPayloadReader.Str(mediaNode, "id") ?? JsonPayloadReader.Str(message, "mediaId") ?? JsonPayloadReader.Str(message, "media_id");
        mimeType ??= JsonPayloadReader.Str(message, "mimeType") ?? JsonPayloadReader.Str(message, "mime_type")
            ?? JsonPayloadReader.Str(mediaNode, "mimeType") ?? JsonPayloadReader.Str(mediaNode, "mime_type");

        if (mediaUrl is not null || mediaId is not null)
        {
            await HandleRiderScanPhotoAsync(phone, mediaUrl, mediaId, mimeType);
            return Ok();
        }

        // 2) Confirmation / refus du vendeur (boutons « Confirmer » / « Refuser », ou réponse
        //    texte courte). On inspecte les candidats : id du bouton, titre du bouton, texte.
        //    Deux garde-fous par rapport à une simple recherche de sous-chaîne :
        //      • le texte libre doit être COURT — « je ne peux pas confirmer, je suis fermé »
        //        confirmait la commande ;
        //      • si aucun ordre n'est en attente pour ce numéro, on NE consomme PAS le message
        //        (il poursuit son routage vers les autres bots au lieu d'être perdu).
        var vendorButtonReplies = new[] { buttonId, buttonTitle }
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim().ToLowerInvariant())
            .ToList();

        var shortText = text is { Length: <= 30 } ? text.Trim().ToLowerInvariant() : null;
        var orderCode = ExtractOrderCode(text ?? buttonId);

        var wantsConfirm = vendorButtonReplies.Any(r => r.Contains("confirmer") || r.Contains("confirm") || r == "oui" || r == "valider")
                           || (shortText is not null && (shortText.Contains("confirmer") || shortText.StartsWith("oui") || shortText.Contains("valide") || shortText.StartsWith("accepte")));
        var wantsReject = vendorButtonReplies.Any(r => r.Contains("refuser") || r.Contains("reject") || r == "non")
                          || (shortText is not null && (shortText.Contains("refuser") || shortText.StartsWith("non") || shortText.Contains("annuler")));

        if (wantsConfirm && await ConfirmOrRejectAsync(phone, confirm: true, orderCode))
            return Ok();

        if (wantsReject && await ConfirmOrRejectAsync(phone, confirm: false, orderCode))
            return Ok();

        // 3) Numéro de l'ÉQUIPE (Prospect:TeamPhone) → commande « CONVERTIR [+numéro] » :
        //    « bouton » texte pour créer le compte vendeur d'un lead qualifié directement
        //    depuis l'alerte WhatsApp (résultat renvoyé à l'équipe).
        if (!string.IsNullOrWhiteSpace(phone) && !string.IsNullOrWhiteSpace(text)
            && IsTeamPhone(phone) && await TryHandleTeamConversionAsync(text))
        {
            return Ok();
        }

        // 4) Commandes texte (téléphones basiques sans GPS) : ZONE, DISPO, INDISPO, AIDE
        if (!string.IsNullOrWhiteSpace(text))
        {
            var user = await FindUserByPhoneAsync(phone, UserRole.Rider)
                       ?? await FindUserByPhoneAsync(phone, UserRole.Vendor);

            if (user is not null && await TryHandleTextCommandAsync(user, text))
                return Ok();
        }

        // 4) Réponse à une offre de course (livreur) : acceptation (« ACCEPTE {code} » / bouton « Accepter » / « 1 » / « OUI »)
        //    ou refus explicite (« REFUSE {code} », « NON {code} » / bouton « Refuser » / « 2 ») — chantier T3.
        var trimmedText = text?.Trim();
        var isAccept = (trimmedText?.StartsWith("ACCEPTE", StringComparison.OrdinalIgnoreCase) == true)
                    || (trimmedText?.StartsWith("ACCEPT", StringComparison.OrdinalIgnoreCase) == true)
                    || string.Equals(trimmedText, "1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(trimmedText, "OUI", StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(buttonId) && buttonId.StartsWith("ACCEPT", StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(buttonTitle) && buttonTitle.Contains("accept", StringComparison.OrdinalIgnoreCase));

        var isDecline = (trimmedText?.StartsWith("REFUSE", StringComparison.OrdinalIgnoreCase) == true)
                     || (trimmedText?.StartsWith("DECLINE", StringComparison.OrdinalIgnoreCase) == true)
                     || (trimmedText?.StartsWith("NON", StringComparison.OrdinalIgnoreCase) == true)
                     || string.Equals(trimmedText, "2", StringComparison.OrdinalIgnoreCase)
                     || (!string.IsNullOrWhiteSpace(buttonId) && (buttonId.StartsWith("DECLINE", StringComparison.OrdinalIgnoreCase) || buttonId.StartsWith("REFUSE", StringComparison.OrdinalIgnoreCase)))
                     || (!string.IsNullOrWhiteSpace(buttonTitle) && (buttonTitle.Contains("refus", StringComparison.OrdinalIgnoreCase) || buttonTitle.Contains("decline", StringComparison.OrdinalIgnoreCase)));

        if (isAccept)
        {
            var offerId = await ExtractOfferIdAsync(buttonId, buttonTitle, text);
            if (offerId is null)
                offerId = await FindPendingOfferForRiderAsync(phone);

            if (offerId is not null)
            {
                try
                {
                    await _deliveryOfferService.AcceptOfferAsync(offerId.Value);
                    _logger.LogInformation("Offre {OfferId} acceptée via webhook.", offerId);
                    return Ok();
                }
                catch (PaymentRequiredException ex)
                {
                    // Le vendeur n'a plus de crédits : le livreur DOIT être prévenu.
                    _logger.LogWarning(ex, "Acceptation de l'offre {OfferId} refusée (crédits vendeur épuisés).", offerId);
                    await ReplyToPhoneAsync(phone, "❌ " + ex.Message);
                    return Ok();
                }
                catch (InvalidOperationException ex)
                {
                    // Offre déjà prise par un autre livreur, ou expirée entre-temps.
                    _logger.LogInformation("Acceptation de l'offre {OfferId} impossible : {Reason}", offerId, ex.Message);
                    await ReplyToPhoneAsync(phone, "❌ " + ex.Message);
                    return Ok();
                }
            }
        }
        else if (isDecline)
        {
            var offerId = await ExtractOfferIdAsync(buttonId, buttonTitle, text);
            if (offerId is null)
                offerId = await FindPendingOfferForRiderAsync(phone);

            if (offerId is not null)
            {
                var rider = await FindUserByPhoneAsync(phone, UserRole.Rider);
                var declined = await _deliveryOfferService.DeclineOfferAsync(offerId.Value, rider?.Id, RequestAborted);
                if (declined)
                {
                    await ReplyToPhoneAsync(phone, "ℹ️ Course refusée. Vous restez disponible pour les prochaines offres.");
                }
                else
                {
                    await ReplyToPhoneAsync(phone, "ℹ️ Cette offre n'est plus en attente.");
                }
                return Ok();
            }
        }

        // 5) Note du client à son livreur (« NOTE 5 ») — AVANT le bot prospects : un client
        //    n'est pas un utilisateur enregistré, sa note serait sinon prise pour un
        //    premier contact commercial et créerait un Lead.
        if (!string.IsNullOrWhiteSpace(phone) && RiderRatingService.IsRatingCommand(text))
        {
            var ratingReply = await _riderRatings.TryRateAsync(phone, text!);
            if (ratingReply is not null)
            {
                await _whatsAppSender.SendTextMessageAsync(phone, ratingReply, RequestAborted);
                return Ok();
            }
            // null = aucune course notable pour ce numéro : on laisse suivre le flux normal.
        }

        // 6) Numéro INCONNU → automatisation des échanges prospects (commerçant / livreur /
        //    parrainage) : création/qualification d'un Lead + réponse contextuelle.
        if (!string.IsNullOrWhiteSpace(phone) && !string.IsNullOrWhiteSpace(text))
        {
            // SameSubscriber est une méthode C# : EF Core ne sait pas la traduire en SQL.
            // On emploie donc la clé de rapprochement INDEXÉE (8 derniers chiffres) pour le
            // pré-filtre, puis la comparaison exacte en mémoire.
            var suffix = PhoneNumberNormalizer.SubscriberSuffix(phone);

            var candidates = suffix.Length == 0
                ? new List<string?>()
                : await _context.Users.AsNoTracking()
                    .Where(u => u.PhoneSuffix == suffix)
                    .Select(u => u.PhoneNumber)
                    .ToListAsync();

            var knownUser = candidates.Any(p => PhoneNumberNormalizer.SameSubscriber(p, phone));
            if (!knownUser)
            {
                // 6a) Candidat livreur (bot de recrutement : intention → nom/quartier → photo).
                if (await _riderRecruitment.TryHandleCandidateAsync(phone, text))
                    return Ok();

                // 6b) Bot de COMMANDE CLIENT (article → commerce → adresse → commande réelle),
                //     AVANT le bot prospects : un client qui veut commander n'est pas un prospect.
                if (await _clientOrders.TryHandleAsync(phone, text))
                    return Ok();

                await _prospects.HandleAsync(phone, text);
            }
        }

        return Ok();
    }

    private bool IsTeamPhone(string? phone)
        => !string.IsNullOrWhiteSpace(_teamPhone) && !string.IsNullOrWhiteSpace(phone)
           && PhoneNumberNormalizer.SameSubscriber(_teamPhone, phone);

    /// <summary>
    /// Commande interne « CONVERTIR [+numéro] » envoyée depuis le téléphone de l'équipe.
    /// Sans numéro : le lead commerçant qualifié le plus récent est converti.
    /// </summary>
    private async Task<bool> TryHandleTeamConversionAsync(string text)
    {
        var lower = text.Trim().ToLowerInvariant();
        if (!lower.Contains("convertir"))
            return false;

        var teamPhone = "+" + PhoneNumberNormalizer.DigitsOnly(_teamPhone!);
        try
        {
            var candidates = await _context.Leads
                .Where(l => l.Status != LeadStatus.Discarded && l.Source != "whatsapp-livreur")
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            var target = VendorCommandParser.TryExtractClientPhone(text) ?? teamPhone;
            var byNumber = PhoneNumberNormalizer.DigitsOnly(target) != PhoneNumberNormalizer.DigitsOnly(teamPhone);
            Lead? lead;
            if (byNumber)
            {
                lead = candidates.FirstOrDefault(l =>
                    PhoneNumberNormalizer.SameSubscriber(l.WhatsAppNumber, target));

                if (lead is null)
                {
                    await TeamReplyAsync($"❌ Aucun lead vendeur trouvé pour {target} — vérifiez le numéro.");
                    return true;
                }
            }
            else
            {
                // Sans numéro : le lead commerçant qualifié le plus récent.
                lead = candidates.FirstOrDefault(l => l.Status is LeadStatus.New or LeadStatus.Contacted);
            }

            if (lead is null)
            {
                await TeamReplyAsync("❌ Aucun lead convertissable trouvé (répondez « CONVERTIR +numéro »).");
                return true;
            }

            var result = await _leadConversion.ConvertAsync(lead.Id, sendWelcome: true);
            await TeamReplyAsync(result.AlreadyExisted
                ? $"ℹ️ Le vendeur {result.Username} existait déjà — lead {lead.WhatsAppNumber} marqué Converti."
                : $"✅ Compte vendeur créé pour {lead.BusinessName} ({lead.WhatsAppNumber}) :\n"
                  + $"• Identifiant : {result.Username}\n"
                  + $"• Mot de passe : {result.TemporaryPassword}\n"
                  + $"• Crédits : {result.Credits}\n"
                  + $"• Parrainage : {result.ReferralCode}\n"
                  + "Bienvenue WhatsApp envoyée au vendeur. 🎉");
        }
        catch (InvalidOperationException ex)
        {
            await TeamReplyAsync("❌ " + ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Commande CONVERTIR de l'équipe en échec.");
            await TeamReplyAsync("❌ Conversion en échec (voir logs).");
        }

        return true;
    }

    /// <summary>
    /// Photo de pièce d'identité reçue d'un livreur connu (« Garantie Colis Sûr ») :
    /// téléchargement du média puis stockage chiffré par le même chemin que le
    /// téléversement admin (garde-fous de <see cref="RiderScansOptions"/> inclus).
    /// Numéro inconnu = silence volontaire (la passerelle ne révèle pas qu'elle accepte
    /// des images, et le bot prospects reste textuel) ; dossier exclu = refus explicite
    /// levé par le domaine, transmis tel quel au livreur.
    /// </summary>
    private async Task HandleRiderScanPhotoAsync(string? phone, string? mediaUrl, string? mediaId, string? mimeType)
    {
        if (!_scans.WhatsAppInboundEnabled)
        {
            _logger.LogInformation("Média WhatsApp ignoré (RiderScans:WhatsAppInboundEnabled=false).");
            return;
        }

        if (string.IsNullOrWhiteSpace(phone))
        {
            _logger.LogWarning("Média WhatsApp reçu sans numéro expéditeur — ignoré.");
            return;
        }

        var rider = await FindUserByPhoneAsync(phone, UserRole.Rider);
        if (rider is null)
        {
            // Numéro sans compte : peut-être un candidat livreur en cours de recrutement
            // (bot WhatsApp : intention → nom/quartier → photo CNI → compte créé).
            var candidateHandled = await _riderRecruitment.HandleCandidatePhotoAsync(phone, mediaUrl, mediaId, mimeType);
            if (!candidateHandled)
                _logger.LogInformation("Média WhatsApp ignoré : {Phone} n'est ni un livreur connu ni un candidat livreur.", phone);
            return;
        }

        // Livreur avec une course en cours (assignée ou en transit) : la photo reçue est la
        // PREUVE DE LIVRAISON du colis (au retrait ou à la remise), pas un scan d'identité.
        var activeOrder = await _context.Orders.AsNoTracking()
            .Where(o => o.RiderUserId == rider.Id
                && (o.Status == OrderStatus.RiderAssigned || o.Status == OrderStatus.InTransit))
            .OrderByDescending(o => o.RiderAssignedAt)
            .FirstOrDefaultAsync();

        if (activeOrder is not null)
        {
            await HandleDeliveryProofPhotoAsync(rider, activeOrder.Id, mediaUrl, mediaId, mimeType);
            return;
        }

        var download = await _mediaDownloader.TryDownloadAsync(mediaUrl, mediaId, mimeType, RequestAborted);
        if (download is null)
        {
            await _whatsAppSender.SendTextMessageAsync(phone,
                "❌ Nous n'avons pas pu récupérer votre photo. Réessayez dans un instant — "
                + "elle est indispensable à votre certification (Garantie Colis Sûr).", RequestAborted);
            return;
        }

        try
        {
            await using var stream = new MemoryStream(download.Value.Content);
            await _riderService.StoreScanAsync(rider.Id, stream, download.Value.FileName, mediaUrl);
            await _riderService.RecordWhatsAppConsentAsync(rider.Id);
            _logger.LogInformation("Scan d'identité du livreur {RiderId} reçu via WhatsApp.", rider.Id);
            await _whatsAppSender.SendTextMessageAsync(phone,
                "✅ Photo de votre pièce d'identité reçue ! Notre équipe vérifie votre dossier — "
                + "vous serez notifié dès votre certification (Garantie Colis Sûr 🛡️).", RequestAborted);
        }
        catch (InvalidOperationException ex)
        {
            // Dossier exclu, format refusé, stockage non configuré : le message du
            // domaine est rédigé pour être lu par l'expéditeur.
            await _whatsAppSender.SendTextMessageAsync(phone, "❌ " + ex.Message, RequestAborted);
        }
    }

    /// <summary>
    /// Photo de preuve de livraison : un livreur avec une course en cours (assignée ou en
    /// transit) envoie la photo du colis. Stockage chiffré au repos, même chemin que les
    /// scans CNI ; la photo accompagne la course pour les litiges (Garantie Colis Sûr).
    /// </summary>
    private async Task HandleDeliveryProofPhotoAsync(User rider, Guid orderId, string? mediaUrl, string? mediaId, string? mimeType)
    {
        var download = await _mediaDownloader.TryDownloadAsync(mediaUrl, mediaId, mimeType, RequestAborted);
        if (download is null)
        {
            await ReplyAsync(rider,
                "❌ Impossible de récupérer la photo du colis. Réessayez dans un instant.");
            return;
        }

        try
        {
            await using var stream = new MemoryStream(download.Value.Content);
            await _riderService.StoreDeliveryProofPhotoAsync(rider.Id, orderId, stream, download.Value.FileName, mediaUrl);
            _logger.LogInformation("Photo de preuve de livraison reçue pour la course {OrderId} (livreur {RiderId}).", orderId, rider.Id);
            await ReplyAsync(rider,
                "✅ Photo du colis enregistrée ! Elle accompagne la course et servira de preuve en cas de litige. " +
                "Envoyez LIVRE <code> CODE <4 chiffres> une fois la remise faite.");
        }
        catch (InvalidOperationException ex)
        {
            // Course déjà clôturée, mauvais livreur, format refusé, stockage non configuré :
            // le message du domaine est lisible par le livreur.
            await ReplyAsync(rider, "❌ " + ex.Message);
        }
    }

    private async Task TeamReplyAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(_teamPhone))
            return;

        try
        {
            await _whatsAppSender.SendTextMessageAsync("+" + PhoneNumberNormalizer.DigitsOnly(_teamPhone), message, RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Réponse à l'équipe impossible.");
        }
    }

    /// <summary>
    /// Traite une réponse « Confirmer » / « Refuser » du vendeur.
    /// Retourne <c>true</c> si un ordre en attente a été trouvé pour ce numéro.
    /// </summary>
    private async Task<bool> ConfirmOrRejectAsync(string? phone, bool confirm, string? orderCode = null)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            _logger.LogWarning("Numéro de vendeur manquant pour la réponse.");
            return false;
        }

        var order = await FindPendingOrderAsync(phone, orderCode);
        if (order is null)
        {
            _logger.LogDebug("Aucune commande en attente pour ce numéro : message routé normalement.");
            return false;
        }

        try
        {
            if (confirm)
            {
                order.ConfirmByVendor();

                var vendor = await FindUserByPhoneAsync(phone, UserRole.Vendor);
                if (vendor is not null)
                    order.LinkVendor(vendor.Id);

                await _context.SaveChangesAsync();
                _logger.LogInformation("Commande confirmée par le vendeur {Phone}.", phone);

                var shortCode = order.Id.ToString("N")[..8].ToUpperInvariant();
                await ReplyToPhoneAsync(phone, $"✅ Commande #{shortCode} confirmée ! Recherche des livreurs les plus proches lancée. 🛵⚡");

                if (!string.IsNullOrWhiteSpace(order.ClientWhatsAppNumber))
                {
                    try
                    {
                        await _whatsAppSender.SendTextMessageAsync(order.ClientWhatsAppNumber,
                            $"🏪 {vendor?.Username ?? "Le commerçant"} a validé votre commande #{shortCode} ! Recherche du coursier le plus proche en cours... ⚡", RequestAborted);
                    }
                    catch { /* best-effort */ }
                }

                // Routage : parcours acheteur (envoi du lien de suivi au client) OU groupage
                // classique (worker). La diffusion est déclenchée par le worker/les coordonnées.
                try
                {
                    await _deliveryOfferService.ConfirmAndRouteAsync(order.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Routage impossible après confirmation de la commande {OrderId}.", order.Id);
                }
            }
            else
            {
                order.Cancel(OrderCancellationReason.VendorRejected, "Refusée par le commerçant via WhatsApp.");
                await _context.SaveChangesAsync();
                _logger.LogInformation("Commande refusée par le vendeur {Phone}.", phone);
                await ReplyToPhoneAsync(phone, "❌ Commande refusée et annulée.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Traitement de la réponse vendeur impossible ({Action}).", confirm ? "confirmer" : "refuser");
        }

        return true;
    }

    /// <summary>
    /// Commandes texte WhatsApp pour téléphones basiques (sans GPS) :
    /// ZONE &lt;quartier&gt;, DISPO, INDISPO, AIDE.
    /// </summary>
    private async Task<bool> TryHandleTextCommandAsync(User user, string text)
    {
        var command = text.Trim();
        var upper = command.ToUpperInvariant();

        if (upper == "ZONE")
        {
            await ReplyAsync(user, "Format : ZONE <nom du quartier> (ex : ZONE Cocody)");
            return true;
        }

        if (upper.StartsWith("ZONE "))
        {
            var zone = command[5..].Trim();
            if (user.Role == UserRole.Rider)
                await _riderService.SetZoneAsync(user.Id, zone);
            else
                await _vendorService.SetZoneAsync(user.Id, zone);

            await ReplyAsync(user, $"✅ Zone enregistrée : {zone}.");
            return true;
        }

        // Statuts et réputation du livreur (DISPO, INDISPO, AVIS, REPONDRE, PROGRAMME) :
        // extraits dans RiderTextCommands (P2 / C-13) pour être testables sans HTTP.
        if (user.Role == UserRole.Rider && RiderTextCommands.Matches(upper, text))
        {
            await _riderCommands.HandleAsync(user, text, ReplyAsync);
            return true;
        }

        // Commandes vendeur (livraison à la demande, sinistre, catalogue produits) : extraites
        // dans VendorTextCommands (P2 / C-13) pour être testables sans payload ni signature.
        // « LIVRAISON » consomme un crédit : ce chemin mérite ses propres tests.
        if (user.Role == UserRole.Vendor && VendorTextCommands.Matches(upper))
        {
            await _vendorCommands.HandleAsync(user, command, ReplyAsync);
            return true;
        }

        // Statuts automatiques livreur : « RECU » = colis récupéré (en route),
        // « LIVRE » = livraison effectuée. Option : code court de la course.
        // Logique extraite dans RiderDeliveryCommands (preuve de livraison, tournée
        // multi-clients, programme Ambassadeur) pour la tester sans passer par HTTP.
        if (user.Role == UserRole.Rider && RiderDeliveryCommands.Matches(upper))
        {
            await _riderDeliveries.HandleAsync(user, command, ReplyAsync);
            return true;
        }

        if (upper is "AIDE" or "HELP" or "MENU")
        {
            var menu = user.Role == UserRole.Vendor
                ? "📱 Menu vendeur :\n"
                  + "• LIVRAISON <détail + adresse client> : demander une course (1 crédit)\n"
                  + "• PRODUITS : votre catalogue produits (menu des clients)\n"
                  + "• PRODUIT <nom> | <prix> [| <emoji>] : ajouter un produit\n"
                  + "• SUPPRIMER PRODUIT <n°> : retirer un produit\n"
                  + "• ZONE <quartier> : votre zone de livraison\n"
                  + "• SINISTRE <code> : signaler un colis perdu/volé (Garantie Colis Sûr)\n"
                  + "• AIDE : ce menu"
                : "📱 Menu livreur :\n• DASHBOARD : tes stats, gains, courses et défi Redmi 15C\n• ZONE <quartier> : définir ta zone\n• DISPO / INDISPO : en ligne / hors ligne\n"
                  + "• ACCEPTE <code> : accepter une course\n• RECU : colis récupéré\n"
                  + "• LIVRE <code> CODE <4 chiffres> : livré (code donné par le client)\n"
                  + "• PROGRAMME : ta progression Ambassadeur (livraisons, filleuls)\n"
                  + "• AVIS : tes retours et notes clients";

            await ReplyAsync(user, menu);
            return true;
        }

        return false;
    }
    private async Task ReplyAsync(User user, string message)
    {
        try
        {
            await _whatsApp.SendTextAsync(user, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Réponse WhatsApp impossible pour {User}.", user.Username);
        }
    }

    /// <summary>Réponse à un numéro brut (émetteur d'un message entrant, sans compte connu).</summary>
    private async Task ReplyToPhoneAsync(string? phone, string message)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return;

        try
        {
            await _whatsAppSender.SendTextMessageAsync(phone, message, RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Réponse WhatsApp impossible pour {Phone}.", phone);
        }
    }

    private async Task<Guid?> FindPendingOfferForRiderAsync(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        var rider = await FindUserByPhoneAsync(phone, UserRole.Rider);
        if (rider is null)
            return null;

        // L'offre en attente la plus récente du livreur (bouton « Accepter » sans code).
        var offer = await _context.DeliveryOffers
            .Where(o => o.RiderUserId == rider.Id && o.Status == DeliveryOfferStatus.Pending)
            .OrderByDescending(o => o.SentAt)
            .FirstOrDefaultAsync();

        return offer?.Id;
    }

    private async Task<Order?> FindPendingOrderAsync(string vendorWhatsApp, string? orderCode = null)
    {
        if (string.IsNullOrWhiteSpace(vendorWhatsApp)) return null;

        var query = _context.Orders
            .Where(o => o.Status == OrderStatus.PendingVendorConfirmation);

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(orderCode))
        {
            var match = orders.FirstOrDefault(o =>
                o.Id.ToString("N").StartsWith(orderCode, StringComparison.OrdinalIgnoreCase)
                && PhoneNumberNormalizer.SameSubscriber(o.VendorWhatsAppNumber, vendorWhatsApp));
            if (match is not null)
                return match;
        }

        return orders.FirstOrDefault(o =>
            PhoneNumberNormalizer.SameSubscriber(o.VendorWhatsAppNumber, vendorWhatsApp));
    }

    private static string? ExtractOrderCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(text, @"\b([A-Fa-f0-9]{6,8})\b");
        return match.Success ? match.Groups[1].Value : null;
    }

    private async Task<User?> FindUserByPhoneAsync(string? phone, UserRole role)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;

        var suffix = PhoneNumberNormalizer.SubscriberSuffix(phone);
        if (suffix.Length == 0)
            return null;

        // Pré-filtre INDEXÉ sur les 8 derniers chiffres, puis confirmation exacte en mémoire :
        // SameSubscriber() gère l'ancienne/nouvelle numérotation ivoirienne et écarte deux
        // indicatifs qui partageraient la même terminaison. L'ancienne version chargeait TOUS
        // les utilisateurs du rôle à chaque message entrant.
        var candidates = await _context.Users.AsNoTracking()
            .Where(u => u.Role == role && u.PhoneSuffix == suffix)
            .ToListAsync();

        var user = candidates.FirstOrDefault(u =>
            PhoneNumberNormalizer.SameSubscriber(u.PhoneNumber, phone));

        // Auto-réparation : le wa_id reçu est la référence fiable (ancienne vs nouvelle
        // numérotation ivoirienne). On aligne le numéro stocké pour les réponses sortantes.
        if (user is not null)
            await RefreshAuthoritativePhoneAsync(user, phone);

        return user;
    }

    private async Task RefreshAuthoritativePhoneAsync(User user, string rawPhone)
    {
        var incoming = new string(rawPhone.Where(char.IsDigit).ToArray());
        if (incoming.Length == 0)
            return;

        if (PhoneNumberNormalizer.DigitsOnly(user.PhoneNumber) == incoming
            || !PhoneNumberNormalizer.SameSubscriber(user.PhoneNumber, rawPhone))
        {
            return;
        }

        var canonical = "+" + incoming;
        user.UpdatePhoneNumber(canonical);

        var tracked = await _context.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
        if (tracked is not null)
        {
            tracked.UpdatePhoneNumber(canonical);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Numéro du compte {UserId} aligné sur le wa_id reçu ({Phone}).", user.Id, canonical);
        }
    }

    private async Task<Guid?> ExtractOfferIdAsync(params string?[] candidates)
    {
        var raw = candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var last = raw.Split(' ', '_')[^1];

        // 1) GUID complet
        if (Guid.TryParse(last, out var id)) return id;

        // 2) Code court (6+ caractères = début du GUID)
        if (last.Length >= 6)
        {
            var offers = await _context.DeliveryOffers.AsNoTracking()
                .Where(o => o.Status == DeliveryOfferStatus.Pending)
                .ToListAsync();

            return offers.FirstOrDefault(o =>
                o.Id.ToString("N").StartsWith(last, StringComparison.OrdinalIgnoreCase))?.Id;
        }

        return null;
    }
}
