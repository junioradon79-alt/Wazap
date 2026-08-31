using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Wazap.Application.Helpers;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WebhookWhatsAppController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<WebhookWhatsAppController> _logger;
    private readonly string _webhookToken;

    public WebhookWhatsAppController(
        ApplicationDbContext context,
        ILogger<WebhookWhatsAppController> logger,
        IConfiguration config)
    {
        _context = context;
        _logger = logger;
        _webhookToken = config["WhatChimp:WebhookToken"] ?? throw new InvalidOperationException("WhatChimp:WebhookToken manquant.");
    }

    // GET: api/webhook/whatsapp
    // Utilisé par WhatChimp pour vérifier le webhook
    [HttpGet]
    public IActionResult VerifyWebhook([FromQuery] string token, [FromQuery] string challenge)
    {
        if (token == _webhookToken)
            return Ok(challenge);

        return BadRequest("Token de vérification invalide.");
    }

    // POST: api/webhook/whatsapp
    // Reçoit les événements de WhatChimp
    [HttpPost]
    [EnableRateLimiting("webhook")]
    public async Task<IActionResult> HandleWebhook([FromBody] WebhookPayload payload)
    {
        _logger.LogInformation("Webhook reçu : {EventType}", payload?.EventType);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Payload webhook : {Payload}", System.Text.Json.JsonSerializer.Serialize(payload));
        }

        // Vérifier que c'est bien un événement de message
        if (payload?.EventType != "message" && payload?.EventType != "interactive")
        {
            _logger.LogWarning("Événement non pris en charge : {Event}", payload?.EventType);
            return Ok();
        }

        // Vérifier que c'est une réponse interactive (clic sur bouton)
        var buttonId = payload?.Data?.Message?.Interactive?.ButtonReply?.Id;
        if (string.IsNullOrEmpty(buttonId))
        {
            _logger.LogWarning("Aucun bouton cliqué détecté.");
            return Ok();
        }

        // Extraire l'ID de la commande du bouton (ex: CONFIRM_{orderId})
        var parts = buttonId.Split('_');
        if (parts.Length < 2 || !Guid.TryParse(parts[1], out var orderId))
        {
            _logger.LogWarning("ID de commande invalide dans le bouton : {ButtonId}", buttonId);
            return Ok();
        }

        // Récupérer la commande
        var order = await _context.Orders.FindAsync(orderId);
        if (order == null)
        {
            _logger.LogWarning("Commande non trouvée : {OrderId}", orderId);
            return Ok();
        }

        // Traiter l'action en fonction du bouton.
        // Idempotence : WhatChimp peut livrer le même événement plusieurs fois.
        // Chaque transition est gardée par l'état courant de la commande afin
        // d'ignorer silencieusement les événements déjà traités.
        var action = parts[0].ToUpper();
        switch (action)
        {
            case "CONFIRM":
                if (order.Status == OrderStatus.PendingVendorConfirmation)
                {
                    order.ConfirmByVendor();
                    order.AwaitRiderAcceptance();

                    var vendor = await FindUserByPhoneAsync(payload?.Data?.Subscriber?.PhoneNumber, UserRole.Vendor);
                    if (vendor is not null)
                        order.LinkVendor(vendor.Id);

                    _logger.LogInformation("Commande {OrderId} confirmée par le vendeur.", orderId);
                }
                else
                {
                    _logger.LogInformation("Commande {OrderId} déjà traitée (statut {Status}) : événement CONFIRM ignoré.", orderId, order.Status);
                }
                break;

            case "REJECT":
                if (order.Status == OrderStatus.Cancelled)
                {
                    _logger.LogInformation("Commande {OrderId} déjà annulée : événement REJECT ignoré.", orderId);
                }
                else if (order.Status == OrderStatus.Delivered || order.Status == OrderStatus.InTransit)
                {
                    _logger.LogWarning("Impossible d'annuler la commande {OrderId} en statut {Status}.", orderId, order.Status);
                }
                else
                {
                    order.Cancel();

                    var vendor = await FindUserByPhoneAsync(payload?.Data?.Subscriber?.PhoneNumber, UserRole.Vendor);
                    if (vendor is not null)
                        order.LinkVendor(vendor.Id);

                    _logger.LogInformation("Commande {OrderId} refusée par le vendeur.", orderId);
                }
                break;

            case "ACCEPT":
                if (order.Status == OrderStatus.AwaitingRiderAcceptance)
                {
                    var riderPhone = payload?.Data?.Subscriber?.PhoneNumber;
                    if (!string.IsNullOrEmpty(riderPhone))
                    {
                        order.AssignRider(riderPhone);
                        order.MarkReadyForPickup();

                        var rider = await FindUserByPhoneAsync(riderPhone, UserRole.Rider);
                        if (rider is not null)
                            order.LinkRider(rider.Id);

                        _logger.LogInformation("Commande {OrderId} acceptée par le livreur {Rider}.", orderId, riderPhone);
                    }
                    else
                    {
                        _logger.LogWarning("Numéro de livreur manquant pour la commande {OrderId}.", orderId);
                    }
                }
                else
                {
                    _logger.LogInformation("Commande {OrderId} déjà traitée (statut {Status}) : événement ACCEPT ignoré.", orderId, order.Status);
                }
                break;

            default:
                _logger.LogWarning("Action inconnue : {Action}", action);
                return Ok();
        }

        await _context.SaveChangesAsync();
        return Ok();
    }

    private async Task<User?> FindUserByPhoneAsync(string? phoneNumber, UserRole role)
    {
        var normalized = PhoneNumberNormalizer.Normalize(phoneNumber);
        if (normalized is null)
            return null;

        return await _context.Users
            .FirstOrDefaultAsync(u => u.PhoneNumber == normalized && u.Role == role);
    }

    // Classes DTO pour le payload
    public class WebhookPayload
    {
        public string? EventType { get; set; }  // "message", "interactive", etc.
        public WebhookData? Data { get; set; }
    }

    public class WebhookData
    {
        public WebhookSubscriber? Subscriber { get; set; }
        public WebhookMessage? Message { get; set; }
    }

    public class WebhookSubscriber
    {
        public string? PhoneNumber { get; set; }
        public string? Name { get; set; }
    }

    public class WebhookMessage
    {
        public WebhookInteractive? Interactive { get; set; }
        public string? Text { get; set; }
    }

    public class WebhookInteractive
    {
        public WebhookButtonReply? ButtonReply { get; set; }
    }

    public class WebhookButtonReply
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
    }
}
