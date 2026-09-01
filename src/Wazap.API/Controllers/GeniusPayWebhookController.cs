using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wazap.API.Services;
using Wazap.Application.Configuration;
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
    private readonly GeniusPayOptions _options;
    private readonly ILogger<GeniusPayWebhookController> _logger;

    public GeniusPayWebhookController(
        ApplicationDbContext context,
        PackService packService,
        GeniusPayOptions options,
        ILogger<GeniusPayWebhookController> logger)
    {
        _context = context;
        _packService = packService;
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

    private async Task HandleSuccessAsync(JsonElement raw)
    {
        var transactionId = ExtractWazapTransactionId(raw);
        var reference = ExtractReference(raw);

        var transaction = transactionId.HasValue
            ? await _context.CreditTransactions.FirstOrDefaultAsync(t => t.Id == transactionId.Value)
            : reference is null
                ? null
                : await _context.CreditTransactions.FirstOrDefaultAsync(t => t.TransactionReference == reference);

        if (transaction is null)
        {
            _logger.LogWarning("Aucune transaction WAZAP pour le webhook GeniusPay (id={Tid}, ref={Ref}).",
                transactionId, reference);
            return;
        }

        if (transaction.Status == TransactionStatus.Completed)
        {
            _logger.LogInformation("Transaction {Id} déjà complétée (webhook dupliqué ignoré).", transaction.Id);
            return;
        }

        await _packService.CompletePurchaseAsync(transaction.Id, reference ?? $"GENIUS-{transaction.Id:N}");
    }

    private async Task HandleFailedAsync(JsonElement raw)
    {
        var transactionId = ExtractWazapTransactionId(raw);
        if (transactionId.HasValue)
            await _packService.FailPurchaseAsync(transactionId.Value);
    }

    /// <summary>
    /// Extrait l'identifiant interne WAZAP depuis data.metadata.wazap_transaction_id
    /// (recherche tolérante à la casse).
    /// </summary>
    private static Guid? ExtractWazapTransactionId(JsonElement raw)
    {
        var data = Find(raw, "data");
        var metadata = Find(data, "metadata");
        var value = Find(metadata, "wazap_transaction_id");
        if (value is null || value.Value.ValueKind != JsonValueKind.String)
            return null;

        return Guid.TryParse(value.Value.GetString(), out var id) ? id : null;
    }

    /// <summary>
    /// Extrait l'identifiant de transaction GeniusPay (data.id).
    /// </summary>
    private static string? ExtractReference(JsonElement raw)
    {
        var data = Find(raw, "data");
        var value = Find(data, "id");
        if (value is null || value.Value.ValueKind != JsonValueKind.String)
            return null;
        return value.Value.GetString();
    }

    private static JsonElement? Find(JsonElement? node, string name)
    {
        if (!node.HasValue || node.Value.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var prop in node.Value.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                return prop.Value;
        }
        return null;
    }
}
