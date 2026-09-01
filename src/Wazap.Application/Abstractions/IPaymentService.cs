namespace Wazap.Application.Abstractions
{
    /// <summary>
    /// Résultat d'une demande de paiement de pack prépayé.
    /// </summary>
    public sealed record PaymentResult(
        bool Success,
        string? TransactionReference,
        string? PaymentLink,
        string? ErrorMessage);

    /// <summary>
    /// Port de paiement des packs (Mobile Money en production, mock en dev/test).
    /// </summary>
    public interface IPaymentService
    {
        Task<PaymentResult> RequestPaymentAsync(Guid vendorId, string packName, decimal amount);
    }
}
