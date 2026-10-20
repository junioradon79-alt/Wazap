namespace Wazap.Application.Abstractions
{
    /// <summary>
    /// Résultat d'une demande de paiement de pack prépayé.
    /// </summary>
    public sealed record PaymentResult(
        bool Success,
        string? TransactionReference,
        string? PaymentLink,
        string? ErrorMessage,
        bool OutcomeUnknown = false);

    /// <summary>
    /// Statut d'une transaction côté agrégateur (utilisé par la réconciliation).
    /// </summary>
    public sealed record PaymentStatusResult(string? Status, decimal? Amount, string? Currency = null, string? Reference = null, Guid? WazapTransactionId = null)
    {
        public bool Confirms(decimal expectedAmount, string expectedReference) =>
            string.Equals(Status, "completed", StringComparison.OrdinalIgnoreCase)
            && Amount == expectedAmount
            && string.Equals(Currency, "XOF", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Reference, expectedReference, StringComparison.Ordinal);
    }

    /// <summary>
    /// Port de paiement des packs (GeniusPay en production, mock en dev/test).
    /// <paramref name="reference"/> identifie la transaction interne WAZAP (utilisée
    /// comme metadata côté agrégateur pour la corrélation au webhook).
    /// </summary>
    public interface IPaymentService
    {
        Task<PaymentResult> RequestPaymentAsync(Guid vendorId, string packName, decimal amount, string reference);

        /// <summary>
        /// Interroge le statut d'une transaction (null = non renseigné, ex. mock).
        /// </summary>
        Task<PaymentStatusResult?> CheckPaymentStatusAsync(string reference);
    }
}
