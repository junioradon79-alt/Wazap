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
/// Les montants dus sont figés dans le paiement ; les reversements restent à effectuer :
/// - La marchandise est due au commerçant.
/// - Les frais de livraison sont dus au livreur.
/// Le livreur ne touche jamais aux espèces du commerçant.
/// </summary>
public sealed class PaymentSplitService
{
    private readonly IApplicationDbContext _context;
    private readonly IWhatsAppSender? _whatsApp;
    private readonly ClientPaymentService? _clientPayments;
    private readonly ILogger<PaymentSplitService> _logger;

    public PaymentSplitService(
        IApplicationDbContext context,
        IWhatsAppSender? whatsApp = null,
        ILogger<PaymentSplitService>? logger = null,
        ClientPaymentService? clientPayments = null)
    {
        _context = context;
        _whatsApp = whatsApp;
        _clientPayments = clientPayments;
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

        if (feePayer.HasValue && !Enum.IsDefined(feePayer.Value))
            throw new ArgumentOutOfRangeException(nameof(feePayer));
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

        if (vendorDeduction > order.Amount || gatewayFee < 0)
            throw new InvalidOperationException("Le montant marchand ne couvre pas les frais de paiement.");
        var merchantAmount = order.Amount - vendorDeduction;
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
    public async Task<SplitPaymentInitiationResult> InitiateSplitPaymentAsync(Guid orderId, string baseUrl = "https://junioradon79gm-001-site1.jtempurl.com", SplitFeePayer? feePayer = null)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order is null)
            return new SplitPaymentInitiationResult(false, "NotFound", 0, 0, 0, null, null, "Commande introuvable.");

        if (order.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
            return new SplitPaymentInitiationResult(false, order.Status.ToString(), order.TotalAmount, order.Amount, order.DeliveryFee, null, null,
                $"Impossible d'initier un paiement pour une commande {order.Status}.");

        if (_clientPayments is null)
            return new SplitPaymentInitiationResult(false, "Unavailable", 0, 0, 0, null, null,
                "Le paiement sécurisé n'est pas disponible.");

        var result = await _clientPayments.RequestPaymentAsync(orderId, feePayer ?? SplitFeePayer.Shared);
        var payment = await _context.OrderPayments.AsNoTracking()
            .Where(p => p.OrderId == orderId && p.Status != TransactionStatus.Failed)
            .OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync();
        return new SplitPaymentInitiationResult(result.Success, result.Status, result.Amount,
            payment?.QuotedVendorAmount ?? 0, payment?.QuotedRiderAmount ?? 0,
            result.PaymentLink, result.PaymentLink, result.ErrorMessage,
            payment?.QuotedGatewayFee ?? 0,
            Enum.TryParse<SplitFeePayer>(payment?.FeePayer, out var payer) ? payer : SplitFeePayer.Shared);
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
