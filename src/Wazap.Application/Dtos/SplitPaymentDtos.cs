namespace Wazap.Application.Dtos;

/// <summary>
/// Détermine qui prend en charge les frais de sécurité Mobile Money / GeniusPay.
/// </summary>
public enum SplitFeePayer
{
    Client = 0,   // Les frais sont ajoutés au panier du client (+300 F) : le vendeur reçoit 100% de sa marge
    Vendor = 1,   // Le vendeur absorbe les frais (-300 F) : geste commercial pour le client
    Shared = 2    // Partage 50/50 (+150 F client / -150 F vendeur) : compromis parfait et transparent
}

public sealed record SplitPaymentCalculation(
    decimal TotalAmount,
    decimal MerchantAmount,
    decimal RiderDeliveryFee,
    string MerchantPhone,
    string? RiderPhone,
    string OrderCode,
    string PaymentDescription,
    decimal GatewayFee = 0,
    SplitFeePayer FeePayer = SplitFeePayer.Client
);


public sealed record SplitPaymentInitiationResult(
    bool Success,
    string Status,
    decimal TotalAmount,
    decimal MerchantAmount,
    decimal RiderDeliveryFee,
    string? PaymentLink,
    string? QrPayload,
    string? ErrorMessage,
    decimal GatewayFee = 0,
    SplitFeePayer FeePayer = SplitFeePayer.Client
);

public sealed record SplitPaymentCompletionResult(
    bool Success,
    Guid OrderId,
    decimal MerchantAmount,
    decimal RiderDeliveryFee,
    string Message,
    string? VendorDisbursementRef = null,
    string? RiderDisbursementRef = null
);


public sealed record HandoverScanResult(
    bool Success,
    Guid OrderId,
    string Status,
    string Message
);
