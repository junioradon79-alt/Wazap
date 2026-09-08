using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wazap.API.Services;
using Wazap.Application.Configuration;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Wazap.Infrastructure.Services;

namespace Wazap.API.Controllers;

[ApiController]
[Route("api/webhook/geniuspay")]
public class GeniusPayWebhookController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly PackService _packService;
    private readonly ClientPaymentService _clientPayments;
    private readonly GeniusPayOptions _options;
    private readonly ILogger<GeniusPayWebhookController> _logger;

    public GeniusPayWebhookController(
        ApplicationDbContext context,
        PackService packService,
        ClientPaymentService clientPayments,
        GeniusPayOptions options,
        ILogger<GeniusPayWebhookController> logger)
    {
        _context = context;
        _packService = packService;
        _clientPayments = clientPayments;
        _options = options;
        _logger = logger;
    }

    // POST: api/webhook/geniuspay — notification de paiement (IPN)
    [HttpPost]
    public async Task<IActionResult> Handle()
    {
        // Lire le corps BRUT : la signature HMAC est calculée sur les octets exacts reçus.
        string payload;
        using (var reader = new StreamReader(Request.Body))
            payload = await reader.ReadToEndAsync();

        var signature = Request.Headers["X-Webhook-Signature"].FirstOrDefault();
        var timestamp = Request.Headers["X-Webhook-Timestamp"].FirstOrDefault();
        var eventName = Request.Headers["X-Webhook-Event"].FirstOrDefault();

        _logger.LogInformation("Webhook GeniusPay reçu : event={Event}", eventName);

        if (!GeniusPaySignatureVerifier.IsValid(payload, signature, timestamp, _options.WebhookSecret))
        {
            _logger.LogWarning("Signature GeniusPay invalide (timestamp={Ts}).", timestamp);
            return Unauthorized(new { status = 401, detail = "Invalid signature" });
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        switch (eventName)
        {
            case "payment.success":
                await HandleSuccessAsync(root);
                break;
            case "payment.failed":
                await HandleFailedAsync(root);
                break;
            default:
                _logger.LogInformation("Événement GeniusPay ignoré : {Event}", eventName);
                break;
        }

        return Ok();
    }

    private async Task HandleSuccessAsync(JsonElement root)
    {
        var info = PaymentWebhookParser.Parse(root);
        if (info is null)
        {
            _logger.LogWarning("Webhook GeniusPay illisible : payload ignoré.");
            return;
        }

        var transaction = info.WazapTransactionId.HasValue
            ? await _context.CreditTransactions.FirstOrDefaultAsync(t => t.Id == info.WazapTransactionId.Value)
            : info.Reference is null
                ? null
                : await _context.CreditTransactions.FirstOrDefaultAsync(t => t.TransactionReference == info.Reference);

        if (transaction is null)
        {
            // Pas une transaction de pack : les paiements du PANIER CLIENT (commandes)
            // partagent la même passerelle — routage par identifiant interne.
            var orderPayment = await FindOrderPaymentAsync(info);
            if (orderPayment is null)
            {
                _logger.LogWarning("Aucune transaction WAZAP pour le webhook GeniusPay (id={Tid}, ref={Ref}).",
                    info.WazapTransactionId, info.Reference);
                return;
            }

            // Intégrité : le montant payé doit correspondre au panier.
            if (info.Amount.HasValue && orderPayment.Amount != info.Amount.Value)
            {
                _logger.LogWarning("Montant webhook {Paid} ≠ paiement commande {Expected} pour {Id}. Ignoré.",
                    info.Amount, orderPayment.Amount, orderPayment.Id);
                return;
            }

            await _clientPayments.CompletePaymentAsync(
                orderPayment.Id, info.Reference ?? $"GENIUS-{orderPayment.Id:N}");
            return;
        }

        if (transaction.Status == TransactionStatus.Completed)
        {
            _logger.LogInformation("Transaction {Id} déjà complétée (webhook dupliqué ignoré).", transaction.Id);
            return;
        }

        // Intégrité : le montant payé doit correspondre à la transaction.
        if (info.Amount.HasValue && transaction.Amount != info.Amount.Value)
        {
            _logger.LogWarning("Montant webhook {Paid} ≠ transaction {Expected} pour {Id}. Ignoré.",
                info.Amount, transaction.Amount, transaction.Id);
            return;
        }

        await _packService.CompletePurchaseAsync(transaction.Id, info.Reference ?? $"GENIUS-{transaction.Id:N}");
    }

    private async Task HandleFailedAsync(JsonElement root)
    {
        var info = PaymentWebhookParser.Parse(root);
        if (info is null)
        {
            _logger.LogWarning("Webhook échec illisible : payload ignoré.");
            return;
        }

        var packTransaction = info.WazapTransactionId.HasValue
            ? await _context.CreditTransactions.FirstOrDefaultAsync(t => t.Id == info.WazapTransactionId.Value)
            : null;

        if (packTransaction is not null)
        {
            await _packService.FailPurchaseAsync(packTransaction.Id);
            return;
        }

        var orderPayment = await FindOrderPaymentAsync(info);
        if (orderPayment is not null)
        {
            await _clientPayments.FailPaymentAsync(orderPayment.Id);
            return;
        }

        _logger.LogWarning("Webhook échec sans transaction WAZAP correspondante (id={Tid}, ref={Ref}) : ignoré.",
            info.WazapTransactionId, info.Reference);
    }

    /// <summary>
    /// Retrouve un paiement de panier client via l'identifiant interne (metadata
    /// wazap_transaction_id) ou la référence de l'agrégateur.
    /// </summary>
    private async Task<OrderPayment?> FindOrderPaymentAsync(PaymentWebhookInfo info)
    {
        if (info.WazapTransactionId.HasValue)
        {
            var byId = await _context.OrderPayments
                .FirstOrDefaultAsync(p => p.Id == info.WazapTransactionId.Value);
            if (byId is not null)
                return byId;
        }

        return info.Reference is null
            ? null
            : await _context.OrderPayments
                .FirstOrDefaultAsync(p => p.TransactionReference == info.Reference);
    }
}
