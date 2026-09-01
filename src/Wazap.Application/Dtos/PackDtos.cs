namespace Wazap.Application.Dtos
{
    /// <summary>
    /// Demande d'achat d'un pack prépayé.
    /// </summary>
    public class BuyPackRequest
    {
        public Guid VendorId { get; set; }
        public string PackName { get; set; } = default!;
    }

    /// <summary>
    /// Pack du catalogue exposé par l'API.
    /// </summary>
    public sealed record PackDto(string Name, decimal Price, int Credits);

    /// <summary>
    /// Réponse d'une tentative d'achat de pack.
    /// </summary>
    public sealed record PaymentResponseDto(bool Success, string TransactionReference, string Message);

    /// <summary>
    /// Ligne d'historique d'achat de crédits d'un vendeur.
    /// </summary>
    public sealed record CreditTransactionDto(
        Guid Id,
        Guid VendorId,
        decimal Amount,
        int CreditsPurchased,
        DateTime CreatedAt,
        string TransactionReference,
        Domain.Enums.TransactionStatus Status);
}
