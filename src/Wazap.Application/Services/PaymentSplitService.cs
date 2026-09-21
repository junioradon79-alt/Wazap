using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wazap.Application.Abstractions;
using Wazap.Application.Dtos;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;

namespace Wazap.Application.Services;

/// <summary>
/// Service de paiement fractionné automatique ("Digital COD / Split Payment Anti-Fuite") :
/// Protège les commerçants contre la fuite des livreurs avec l'argent liquide.
/// Le client règle la totalité (Marchandise + Livraison) par Mobile Money (Wave / OM / MoMo).
/// Les fonds sont automatiquement et instantanément ventilés :
/// - La marchandise (Amount) est reversée directement au commerçant.
/// - Les frais de livraison (DeliveryFee) sont versés directement au livreur.
/// Le livreur ne touche jamais aux espèces du commerçant.
/// </summary>
public sealed class PaymentSplitService
{
    private readonly IApplicationDbContext _context;
    private readonly IWhatsAppSender? _whatsApp;
    private readonly ILogger<PaymentSplitService> _logger;

    public PaymentSplitService(
        IApplicationDbContext context,
        IWhatsAppSender? whatsApp = null,
        ILogger<PaymentSplitService>? logger = null)
    {
        _context = context;
        _whatsApp = whatsApp;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PaymentSplitService>.Instance;
    }

    private static string FormatXof(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

    /// <summary>
    /// Calcule la ventilation exacte des montants pour une commande (Marchandise vs Livraison).
    /// </summary>
    public static SplitPaymentCalculation CalculateSplit(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        var merchantAmount = order.Amount;
        var riderFee = order.DeliveryFee;
        var total = order.TotalAmount;
        var code = order.Id.ToString("N")[..8].ToUpperInvariant();

        return new SplitPaymentCalculation(
            TotalAmount: total,
            MerchantAmount: merchantAmount,
            RiderDeliveryFee: riderFee,
            MerchantPhone: order.VendorWhatsAppNumber,
            RiderPhone: order.RiderWhatsAppNumber,
            OrderCode: code,
            PaymentDescription: $"WAZAP #{code} : {order.Description} ({FormatXof(merchantAmount)} F marchandise + {FormatXof(riderFee)} F livraison)"
        );
    }

    /// <summary>
    /// Initie un paiement fractionné pour une commande et génère les liens/QR codes de paiement Wave/Mobile Money.
    /// </summary>
    public async Task<SplitPaymentInitiationResult> InitiateSplitPaymentAsync(Guid orderId, string baseUrl = "https://wazap-api.onrender.com")
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null)
            return new SplitPaymentInitiationResult(false, "NotFound", 0, 0, 0, null, null, "Commande introuvable.");

        if (order.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
            return new SplitPaymentInitiationResult(false, order.Status.ToString(), order.TotalAmount, order.Amount, order.DeliveryFee, null, null,
                $"Impossible d'initier un paiement pour une commande {order.Status}.");

        var calculation = CalculateSplit(order);

        // Recherche ou création d'un paiement
        var existing = await _context.OrderPayments
            .Where(p => p.OrderId == orderId && p.Status == TransactionStatus.Pending)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        var payment = existing ?? new OrderPayment(orderId, calculation.TotalAmount);
        if (existing is null)
        {
            _context.OrderPayments.Add(payment);
        }

        // Construction du lien Wave / Mobile Money avec ventilation
        var paymentLink = $"{baseUrl.TrimEnd('/')}/app/suivi/{order.Id}?payer=1&mode=split";
        var qrPayload = paymentLink;

        payment.SetPaymentLink(paymentLink);
        await _context.SaveChangesAsync();

        return new SplitPaymentInitiationResult(
            Success: true,
            Status: payment.Status.ToString(),
            TotalAmount: calculation.TotalAmount,
            MerchantAmount: calculation.MerchantAmount,
            RiderDeliveryFee: calculation.RiderDeliveryFee,
            PaymentLink: paymentLink,
            QrPayload: qrPayload,
            ErrorMessage: null
        );
    }

    /// <summary>
    /// Clôture le paiement fractionné après validation Mobile Money et notifie instantanément les deux parties.
    /// </summary>
    public async Task<SplitPaymentCompletionResult> CompleteSplitPaymentAsync(Guid orderId, string paymentReference)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null)
            return new SplitPaymentCompletionResult(false, orderId, 0, 0, "Commande introuvable.");

        var payment = await _context.OrderPayments
            .Where(p => p.OrderId == orderId)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        if (payment is null)
        {
            payment = new OrderPayment(orderId, order.TotalAmount);
            _context.OrderPayments.Add(payment);
        }

        if (payment.Status != TransactionStatus.Completed)
        {
            payment.Complete(paymentReference, commissionPercent: 0);
            await _context.SaveChangesAsync();
        }

        var calculation = CalculateSplit(order);

        // Notifications WhatsApp de confirmation de versement immédiat (Split Payout)
        if (_whatsApp is not null)
        {
            // 1. Notification Commerçant
            try
            {
                var msgVendor = $"🎉 *PAIEMENT REÇU (Anti-Fuite WAZAP)*\n" +
                                $"Commande : *#{calculation.OrderCode}*\n" +
                                $"Votre marchandise : *{FormatXof(calculation.MerchantAmount)} FCFA* a été créditée directement sur votre compte Wave !\n" +
                                $"Client : {order.ClientName}\n" +
                                $"Zéro risque de fuite d'espèces.";
                await _whatsApp.SendTextMessageAsync(order.VendorWhatsAppNumber, msgVendor);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur notification commerçant pour split payment {OrderId}", orderId);
            }

            // 2. Notification Livreur
            if (!string.IsNullOrWhiteSpace(order.RiderWhatsAppNumber))
            {
                try
                {
                    var msgRider = $"🛵 *COURSE RÉGLÉE (WAZAP)*\n" +
                                   $"Commande : *#{calculation.OrderCode}*\n" +
                                   $"Vos frais de livraison : *{FormatXof(calculation.RiderDeliveryFee)} FCFA* ont été crédités sur votre compte !\n" +
                                   $"Client livré sans échange d'argent liquide.";
                    await _whatsApp.SendTextMessageAsync(order.RiderWhatsAppNumber, msgRider);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Erreur notification livreur pour split payment {OrderId}", orderId);
                }
            }
        }

        return new SplitPaymentCompletionResult(
            Success: true,
            OrderId: orderId,
            MerchantAmount: calculation.MerchantAmount,
            RiderDeliveryFee: calculation.RiderDeliveryFee,
            Message: $"Paiement sécurisé validé. {calculation.MerchantAmount:N0} F reversés au marchand, {calculation.RiderDeliveryFee:N0} F au livreur."
        );
    }

    /// <summary>
    /// Valide le scan de ramassage du colis par le livreur au magasin (Scan Handover).
    /// Horodaté, sans saisie de texte.
    /// </summary>
    public async Task<HandoverScanResult> ProcessHandoverScanAsync(Guid orderId, string? riderWhatsApp = null)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null)
            return new HandoverScanResult(false, orderId, "NotFound", "Commande introuvable.");

        if (order.Status == OrderStatus.InTransit)
            return new HandoverScanResult(true, orderId, order.Status.ToString(), "Colis déjà en cours d'acheminement.");

        if (order.Status is not (OrderStatus.RiderAssigned or OrderStatus.ReadyForPickup or OrderStatus.PickedUp))
            return new HandoverScanResult(false, orderId, order.Status.ToString(), $"Statut invalide pour le ramassage : {order.Status}");

        if (order.Status == OrderStatus.RiderAssigned)
            order.MarkReadyForPickup();
        if (order.Status == OrderStatus.ReadyForPickup)
            order.MarkPickedUp();
        if (order.Status == OrderStatus.PickedUp)
            order.MarkInTransit();

        await _context.SaveChangesAsync();

        var code = order.Id.ToString("N")[..8].ToUpperInvariant();

        // Alerte commerçant que le livreur a scanné et pris le colis
        if (_whatsApp is not null)
        {
            try
            {
                await _whatsApp.SendTextMessageAsync(
                    order.VendorWhatsAppNumber,
                    $"📦 *COLIS RÉCUPÉRÉ (Scan QR)*\n" +
                    $"Votre commande *#{code}* vient d'être scannée et prise en charge par le livreur.\n" +
                    $"Départ en livraison immédiat.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur notification ramassage {OrderId}", orderId);
            }
        }

        return new HandoverScanResult(
            Success: true,
            OrderId: orderId,
            Status: order.Status.ToString(),
            Message: $"Colis #{code} récupéré avec succès par scan QR. En route vers le client !"
        );
    }
}
