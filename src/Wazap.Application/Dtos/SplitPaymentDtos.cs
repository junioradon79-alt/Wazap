namespace Wazap.Application.Dtos;

public sealed record SplitPaymentCalculation(
    decimal TotalAmount,
    decimal MerchantAmount,
    decimal RiderDeliveryFee,
    string MerchantPhone,
    string? RiderPhone,
    string OrderCode,
    string PaymentDescription
);

public sealed record SplitPaymentInitiationResult(
    bool Success,
    string Status,
    decimal TotalAmount,
    decimal MerchantAmount,
    decimal RiderDeliveryFee,
    string? PaymentLink,
    string? QrPayload,
    string? ErrorMessage
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
