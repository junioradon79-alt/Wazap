using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Wazap.API.Services;
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
    private readonly WhatsAppOrchestrationService _whatsApp;
    private readonly ILogger<WebhookWhatsAppController> _logger;
    private readonly string _webhookToken;

    public WebhookWhatsAppController(
        ApplicationDbContext context,
        RiderService riderService,
        VendorService vendorService,
        DeliveryOfferService deliveryOfferService,
        WhatsAppOrchestrationService whatsApp,
        ILogger<WebhookWhatsAppController> logger,
        IConfiguration config)
    {
        _context = context;
        _riderService = riderService;
        _vendorService = vendorService;
        _deliveryOfferService = deliveryOfferService;
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

        var phone = Str(subscriber, "phoneNumber");
        var text = Str(message, "text");
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

                // Groupage : la commande rejoint le lot ouvert du vendeur.
                // Le broadcast du lot est déclenché par le worker quand la fenêtre est écoulée.
                try
                {
                    await _deliveryOfferService.JoinOrCreateBatchAsync(order.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Groupage impossible après confirmation de la commande {OrderId}.", order.Id);
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

        if (upper is "AIDE" or "HELP" or "MENU")
        {
            await ReplyAsync(user, "📱 Menu livreur :\n• ZONE <quartier> : définir ta zone\n• DISPO / INDISPO : en ligne / hors ligne\n• ACCEPTE <code> : accepter une course");
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

    private async Task<Order?> FindPendingOrderAsync(string vendorWhatsApp)
    {
        var normalized = new string((vendorWhatsApp ?? string.Empty).Where(char.IsDigit).ToArray());
        if (normalized.Length == 0) return null;

        var orders = await _context.Orders
            .Where(o => o.Status == OrderStatus.PendingVendorConfirmation)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        return orders.FirstOrDefault(o =>
            new string(o.VendorWhatsAppNumber.Where(char.IsDigit).ToArray()) == normalized);
    }

    private async Task<User?> FindUserByPhoneAsync(string? phone, UserRole role)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsDigit).ToArray());

        var users = await _context.Users.AsNoTracking()
            .Where(u => u.Role == role && u.PhoneNumber != null)
            .ToListAsync();

        return users.FirstOrDefault(u => new string(u.PhoneNumber!.Where(char.IsDigit).ToArray()) == digits);
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
