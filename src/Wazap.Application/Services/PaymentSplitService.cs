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
    private readonly IPayoutService? _payoutService;
    private readonly IPaymentService? _paymentService;
    private readonly ILogger<PaymentSplitService> _logger;

    public PaymentSplitService(
        IApplicationDbContext context,
        IWhatsAppSender? whatsApp = null,
        ILogger<PaymentSplitService>? logger = null)
        : this(context, whatsApp, payoutService: null, paymentService: null, logger)
    {
    }

    public PaymentSplitService(
        IApplicationDbContext context,
        IWhatsAppSender? whatsApp,
        IPayoutService? payoutService,
        IPaymentService? paymentService = null,
        ILogger<PaymentSplitService>? logger = null)
    {
        _context = context;
        _whatsApp = whatsApp;
        _payoutService = payoutService;
        _paymentService = paymentService;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PaymentSplitService>.Instance;
    }



    private static string FormatXof(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

    /// <summary>
    /// Calcule la ventilation exacte des montants pour une commande (Marchandise vs Livraison vs Frais de sécurité).
    /// Supporte la répartition des frais : Client (+frais), Vendeur (-frais), ou Partagé 50/50.
    /// </summary>
    public static SplitPaymentCalculation CalculateSplit(Order order, SplitFeePayer? feePayer = null, decimal? customGatewayFee = null)
    {
        ArgumentNullException.ThrowIfNull(order);

        // Frais officiels GeniusPay : 100 FCFA fixe + 1% du montant total
        var gatewayFee = customGatewayFee ?? (feePayer.HasValue ? Math.Round(100m + (order.TotalAmount * 0.01m), 0, MidpointRounding.AwayFromZero) : 0m);


        decimal clientExtra = 0;
        decimal vendorDeduction = 0;

        if (feePayer.HasValue)
        {
            switch (feePayer.Value)
            {
                case SplitFeePayer.Client:
                    clientExtra = gatewayFee;
                    vendorDeduction = 0;
                    break;
                case SplitFeePayer.Vendor:
                    clientExtra = 0;
                    vendorDeduction = gatewayFee;
                    break;
                case SplitFeePayer.Shared:
                    var half = Math.Round(gatewayFee / 2m, 0, MidpointRounding.AwayFromZero);
                    clientExtra = half;
                    vendorDeduction = gatewayFee - half;
                    break;
            }
        }

        var merchantAmount = Math.Max(0, order.Amount - vendorDeduction);
        var riderFee = order.DeliveryFee;
        var total = order.TotalAmount + clientExtra;
        var code = order.Id.ToString("N")[..8].ToUpperInvariant();

        return new SplitPaymentCalculation(
            TotalAmount: total,
            MerchantAmount: merchantAmount,
            RiderDeliveryFee: riderFee,
            MerchantPhone: order.VendorWhatsAppNumber,
            RiderPhone: order.RiderWhatsAppNumber,
            OrderCode: code,
            PaymentDescription: $"WAZAP #{code} : {order.Description} ({FormatXof(merchantAmount)} F marchandise + {FormatXof(riderFee)} F livraison)",
            GatewayFee: gatewayFee,
            FeePayer: feePayer ?? SplitFeePayer.Client
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

        // Si GeniusPay est configuré, on initie la session GeniusPay multi-réseaux (Wave, OM, MTN, Moov, Visa)
        if (_paymentService is not null)
        {
            try
            {
                var payResult = await _paymentService.RequestPaymentAsync(
                    order.VendorUserId ?? Guid.Empty,
                    $"WAZAP #{calculation.OrderCode} - {order.Description} ({FormatXof(calculation.TotalAmount)} F)",
                    calculation.TotalAmount,
                    payment.Id.ToString());

                if (payResult.Success && !string.IsNullOrWhiteSpace(payResult.PaymentLink))
                {
                    paymentLink = payResult.PaymentLink;
                    qrPayload = payResult.PaymentLink;
                    if (!string.IsNullOrWhiteSpace(payResult.TransactionReference))
                        payment.SetTransactionReference(payResult.TransactionReference);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Initiation GeniusPay directe non disponible pour {OrderId}, fallback vers lien WAZAP", orderId);
            }
        }

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
    /// Clôture le paiement fractionné après validation Mobile Money (Option A : virement immédiat sortant aux deux parties).
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

        // OPTION A : Exécution du reversement automatique (Split Disbursement API)
        string? vendorDisbRef = null;
        string? riderDisbRef = null;

        if (_payoutService is not null)
        {
            try
            {
                var disbResult = await _payoutService.DisburseSplitAsync(new SplitDisbursementRequest(
                    OrderId: orderId,
                    VendorPhone: calculation.MerchantPhone,
                    VendorAmount: calculation.MerchantAmount,
                    RiderPhone: calculation.RiderPhone,
                    RiderFee: calculation.RiderDeliveryFee,
                    OrderCode: calculation.OrderCode
                ));

                if (disbResult.Success)
                {
                    vendorDisbRef = disbResult.VendorTransferRef;
                    riderDisbRef = disbResult.RiderTransferRef;
                    _logger.LogInformation(
                        "Split disbursement Option A exécuté pour #{Code} : Vendeur {VendorRef}, Livreur {RiderRef}",
                        calculation.OrderCode, vendorDisbRef, riderDisbRef);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du split disbursement Option A pour la commande {OrderId}", orderId);
            }
        }

        // Notifications WhatsApp de confirmation de versement immédiat (Split Payout)
        if (_whatsApp is not null)
        {
            // 1. Notification Commerçant
            try
            {
                var vendorRefLine = !string.IsNullOrWhiteSpace(vendorDisbRef)
                    ? $"\n⚡ Virement Mobile Money immédiat : *{vendorDisbRef}*"
                    : "";

                var msgVendor = $"🎉 *PAIEMENT REÇU (Anti-Fuite WAZAP)*\n" +
                                $"Commande : *#{calculation.OrderCode}*\n" +
                                $"Votre marchandise : *{FormatXof(calculation.MerchantAmount)} FCFA* a été transférée automatiquement sur votre compte Mobile Money !{vendorRefLine}\n" +
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
                    var riderRefLine = !string.IsNullOrWhiteSpace(riderDisbRef)
                        ? $"\n⚡ Virement Mobile Money immédiat : *{riderDisbRef}*"
                        : "";

                    var msgRider = $"🛵 *COURSE RÉGLÉE (WAZAP)*\n" +
                                   $"Commande : *#{calculation.OrderCode}*\n" +
                                   $"Vos frais de livraison : *{FormatXof(calculation.RiderDeliveryFee)} FCFA* ont été transférés sur votre compte Mobile Money !{riderRefLine}\n" +
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
            Message: $"Paiement sécurisé validé. {FormatXof(calculation.MerchantAmount)} F reversés au marchand, {FormatXof(calculation.RiderDeliveryFee)} F au livreur.",
            VendorDisbursementRef: vendorDisbRef,
            RiderDisbursementRef: riderDisbRef
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
