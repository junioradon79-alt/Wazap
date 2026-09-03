using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Wazap.API.Services;
using Wazap.Application.Exceptions;
using Wazap.Application.Helpers;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Controllers;

[ApiController]
[Route("api/webhook/whatsapp")]
public class WebhookWhatsAppController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly RiderService _riderService;
    private readonly VendorService _vendorService;
    private readonly DeliveryOfferService _deliveryOfferService;
    private readonly OrderService _orderService;
    private readonly WhatsAppOrchestrationService _whatsApp;
    private readonly ILogger<WebhookWhatsAppController> _logger;
    private readonly string _webhookToken;

    public WebhookWhatsAppController(
        ApplicationDbContext context,
        RiderService riderService,
        VendorService vendorService,
        DeliveryOfferService deliveryOfferService,
        OrderService orderService,
        WhatsAppOrchestrationService whatsApp,
        ILogger<WebhookWhatsAppController> logger,
        IConfiguration config)
    {
        _context = context;
        _riderService = riderService;
        _vendorService = vendorService;
        _deliveryOfferService = deliveryOfferService;
        _orderService = orderService;
        _whatsApp = whatsApp;
        _logger = logger;
        _webhookToken = config["WhatChimp:WebhookToken"] ?? "MonTokenSecret123";
    }

    // GET: api/webhook/whatsapp — vérification WhatChimp
    [HttpGet]
    public IActionResult Verify([FromQuery] string token, [FromQuery] string challenge)
        => token == _webhookToken ? Ok(challenge) : BadRequest("Token invalide.");

    // POST: api/webhook/whatsapp — événements (live location, boutons vendeur, ACCEPTE livreur)
    [HttpPost]
    [EnableRateLimiting("webhook")]
    public async Task<IActionResult> Handle([FromBody] JsonElement raw)
    {
        // Log du payload brut : permet de valider le format exact envoyé par WhatChimp.
        _logger.LogInformation("Webhook RAW reçu : {Raw}", raw.GetRawText());

        // Lecture tolérante du payload : accepte camelCase ET snake_case
        var data = Find(raw, "data");
        var subscriber = Find(data, "subscriber");
        var message = Find(data, "message");
        var location = Find(message, "location");
        var interactive = Find(message, "interactive");
        var buttonReply = Find(interactive, "buttonReply");

        var phone = Str(subscriber, "phoneNumber")
                    ?? Str(raw, "chat_id")
                    ?? Str(raw, "chatId");
        var text = Str(message, "text")
                   ?? Str(raw, "user_message")
                   ?? Str(raw, "userMessage");
        var latitude = Dbl(location, "latitude");
        var longitude = Dbl(location, "longitude");
        var buttonId = Str(buttonReply, "id");
        var buttonTitle = Str(buttonReply, "title");

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

        // 2) Confirmation / refus du vendeur (boutons « Confirmer » / « Refuser »)
        //    On inspecte TOUS les candidats (id du bouton, titre du bouton, texte brut) :
        //    l'id peut être un payload court (« confirm ») alors que le titre est « Confirmer ».
        var replies = new[] { buttonId, buttonTitle, text }
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim().ToLowerInvariant())
            .ToList();

        if (replies.Any(r => r.Contains("confirmer")))
        {
            await ConfirmOrRejectAsync(phone, confirm: true);
            return Ok();
        }

        if (replies.Any(r => r.Contains("refuser")))
        {
            await ConfirmOrRejectAsync(phone, confirm: false);
            return Ok();
        }

        // 3) Commandes texte (téléphones basiques sans GPS) : ZONE, DISPO, INDISPO, AIDE
        if (!string.IsNullOrWhiteSpace(text))
        {
            var user = await FindUserByPhoneAsync(phone, UserRole.Rider)
                       ?? await FindUserByPhoneAsync(phone, UserRole.Vendor);

            if (user is not null && await TryHandleTextCommandAsync(user, text))
                return Ok();
        }

        // 4) Acceptation d'une offre (texte « ACCEPTE {code_court} » ou bouton « ACCEPT_{id} »)
        var offerId = await ExtractOfferIdAsync(buttonId, buttonTitle, text);

        // 4b) Bouton générique « Accepter » (template à bouton) : le clic n'embarque pas le code,
        //     on résout l'offre en attente la plus récente du livreur.
        if (offerId is null)
        {
            var buttonReplies = new[] { buttonId, buttonTitle }
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim().ToLowerInvariant())
                .ToList();
            if (buttonReplies.Any(r => r.Contains("accept")))
                offerId = await FindPendingOfferForRiderAsync(phone);
        }

        if (offerId is not null)
        {
            await _deliveryOfferService.AcceptOfferAsync(offerId.Value);
            _logger.LogInformation("Offre {OfferId} acceptée via webhook.", offerId);
        }

        return Ok();
    }

    private async Task ConfirmOrRejectAsync(string? phone, bool confirm)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            _logger.LogWarning("Numéro de vendeur manquant pour la réponse.");
            return;
        }

        var order = await FindPendingOrderAsync(phone);
        if (order is null)
        {
            _logger.LogWarning("Aucune commande en attente pour ce vendeur.");
            return;
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
                order.Cancel();
                await _context.SaveChangesAsync();
                _logger.LogInformation("Commande refusée par le vendeur {Phone}.", phone);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Traitement de la réponse vendeur impossible ({Action}).", confirm ? "confirmer" : "refuser");
        }
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

        if (upper == "DISPO" && user.Role == UserRole.Rider)
        {
            await _riderService.SetAvailabilityAsync(user.Id, true);
            await ReplyAsync(user, "✅ Vous êtes en ligne.");
            return true;
        }

        if (upper == "INDISPO" && user.Role == UserRole.Rider)
        {
            await _riderService.SetAvailabilityAsync(user.Id, false);
            await ReplyAsync(user, "🚫 Vous êtes hors ligne.");
            return true;
        }

        // Livraison à la demande (vendeur) : « LIVRAISON <ce qu'il faut livrer> à <adresse client> ».
        // Un crédit est consommé, la course est diffusée immédiatement aux livreurs proches.
        if (user.Role == UserRole.Vendor && (upper == "LIVRAISON" || upper.StartsWith("LIVRAISON ")))
        {
            var instruction = command.Length > "LIVRAISON ".Length
                ? command["LIVRAISON ".Length..].Trim()
                : string.Empty;

            if (instruction.Length < 3)
            {
                await ReplyAsync(user,
                    "📦 Format : LIVRAISON <ce qu'il faut livrer> à <quartier/adresse du client>\n" +
                    "Exemple : LIVRAISON 2 poulets à Marcory, rue Princesse");
                return true;
            }

            try
            {
                // Extraction optionnelle du téléphone du client (ex : « … tél 0708091011 »)
                // → notifications automatiques possibles (livreur assigné, livraison effectuée).
                var clientPhone = TryExtractClientPhone(instruction);

                var order = await _orderService.CreateDispatchRequestAsync(user.Id, instruction, clientPhone);
                var creditsLeft = await _context.Users.AsNoTracking()
                    .Where(u => u.Id == user.Id)
                    .Select(u => u.Credits)
                    .FirstOrDefaultAsync();

                var code = order.Id.ToString("N")[..8].ToUpperInvariant();
                await ReplyAsync(user,
                    $"✅ Course #{code} enregistrée ! Un livreur proche est contacté. Crédits restants : {creditsLeft}.");
            }
            catch (PaymentRequiredException ex)
            {
                await ReplyAsync(user, "❌ " + ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                await ReplyAsync(user, "❌ " + ex.Message);
            }

            return true;
        }

        // Statuts automatiques livreur : « RECU » = colis récupéré (en route),
        // « LIVRE » = livraison effectuée. Option : code court de la course.
        if (user.Role == UserRole.Rider
            && (upper == "RECU" || upper.StartsWith("RECU ") || upper == "LIVRE" || upper.StartsWith("LIVRE ")))
        {
            var marker = upper.StartsWith("RECU") ? "RECU" : "LIVRE";
            var code = command.Length > marker.Length ? command[marker.Length..].Trim() : string.Empty;
            var targetStatus = marker == "RECU"
                ? OrderStatus.RiderAssigned
                : OrderStatus.InTransit;

            var orders = await _context.Orders
                .Where(o => o.RiderUserId == user.Id && o.Status == targetStatus)
                .ToListAsync();

            var closeAll = marker == "LIVRE" && code.Equals("TOUT", StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(code) && !closeAll)
                orders = orders.Where(o => o.Id.ToString("N").StartsWith(code, StringComparison.OrdinalIgnoreCase)).ToList();

            // Tournée multi-clients : on ne clôture JAMAIS toutes les livraisons d'un coup
            // (sauf « LIVRE TOUT » explicite) pour notifier chaque client au bon moment.
            if (marker == "LIVRE" && orders.Count > 1 && string.IsNullOrWhiteSpace(code))
            {
                await ReplyAsync(user,
                    "ℹ️ Plusieurs livraisons en cours.\n" +
                    "Envoyez LIVRE <code> après CHAQUE livraison (ex : LIVRE A1B2C3D4), ou LIVRE TOUT pour tout clôturer.");
                return true;
            }

            if (orders.Count == 0)
            {
                await ReplyAsync(user, marker == "RECU"
                    ? "ℹ️ Aucune course à récupérer pour le moment."
                    : "ℹ️ Aucune course en cours de livraison.");
                return true;
            }

            foreach (var order in orders)
            {
                if (marker == "RECU")
                {
                    order.MarkReadyForPickup();
                    order.MarkPickedUp();
                    order.MarkInTransit();
                }
                else
                {
                    order.MarkDelivered();
                }
            }

            await _context.SaveChangesAsync();

            // Notification « livré » à CHAQUE client concerné (best-effort).
            if (marker == "LIVRE")
            {
                foreach (var order in orders)
                {
                    try
                    {
                        await _whatsApp.SendDeliveredNotificationAsync(order);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Notification de livraison impossible pour {OrderId}.", order.Id);
                    }
                }
            }

            await ReplyAsync(user, marker == "RECU"
                ? $"✅ Colis récupéré ({orders.Count} course(s)) — en route !"
                : $"✅ {orders.Count} course(s) livrée(s). Merci ! 🎉");

            return true;
        }

        if (upper is "AIDE" or "HELP" or "MENU")
        {
            var menu = user.Role == UserRole.Vendor
                ? "📱 Menu vendeur :\n• LIVRAISON <détail + adresse client> : demander une course (1 crédit)\n• ZONE <quartier> : votre zone de livraison\n• AIDE : ce menu"
                : "📱 Menu livreur :\n• ZONE <quartier> : définir ta zone\n• DISPO / INDISPO : en ligne / hors ligne\n• ACCEPTE <code> : accepter une course";

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

    private async Task<Order?> FindPendingOrderAsync(string vendorWhatsApp)
    {
        if (string.IsNullOrWhiteSpace(vendorWhatsApp)) return null;

        var orders = await _context.Orders
            .Where(o => o.Status == OrderStatus.PendingVendorConfirmation)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        return orders.FirstOrDefault(o =>
            PhoneNumberNormalizer.SameSubscriber(o.VendorWhatsAppNumber, vendorWhatsApp));
    }

    private async Task<User?> FindUserByPhoneAsync(string? phone, UserRole role)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;

        var users = await _context.Users.AsNoTracking()
            .Where(u => u.Role == role && u.PhoneNumber != null)
            .ToListAsync();

        var user = users.FirstOrDefault(u =>
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

    private static string? TryExtractClientPhone(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Candidats : +225XXXXXXXXXX (nouveau), +225XXXXXXXX (ancien),
        // 0XXXXXXXXX (nouveau 10) / 0XXXXXXXX (ancien 8).
        var match = Regex.Match(text, @"(?:\+?\s?225[\s.-]?)?(?:0[157]\d{8}|0\d{7})");
        if (!match.Success)
            return null;

        var digits = new string(match.Value.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("225") && digits.Length is 11 or 13)
            return "+" + digits;

        // Numéro national ivoirien (8 ou 10 chiffres commençant par 0) : on conserve le 0
        // après l'indicatif (E.164 CI : +225 07 08 … → 2250708…).
        if (digits.Length is 8 or 10 && digits.StartsWith("0"))
            return "+225" + digits;

        return null;
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

    // ---- Lecture tolérante du payload (camelCase ET snake_case) ----

    private static JsonElement? Find(JsonElement? node, string name)
    {
        if (!node.HasValue) return null;
        var obj = node.Value;
        if (obj.ValueKind != JsonValueKind.Object) return null;

        var target = NormalizeKey(name);
        foreach (var prop in obj.EnumerateObject())
        {
            if (NormalizeKey(prop.Name) == target)
                return prop.Value;
        }
        return null;
    }

    private static string NormalizeKey(string name)
        => new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string? Str(JsonElement? node, string name)
    {
        var v = Find(node, name);
        if (v is null || v.Value.ValueKind != JsonValueKind.String)
            return null;
        return v.Value.GetString();
    }

    private static double? Dbl(JsonElement? node, string name)
    {
        var v = Find(node, name);
        if (v is null) return null;

        return v.Value.ValueKind switch
        {
            JsonValueKind.Number => v.Value.GetDouble(),
            JsonValueKind.String when double.TryParse(
                v.Value.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var d) => d,
            _ => null
        };
    }
}
