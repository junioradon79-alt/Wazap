namespace Wazap.Application.Dtos;

/// <summary>Résultat d'une demande de paiement du panier client (page de suivi).</summary>
public sealed record ClientPaymentResultDto(
    bool Success,
    string Status,
    decimal Amount,
    string? PaymentLink,
    string? ErrorMessage);