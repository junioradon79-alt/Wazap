using Wazap.Domain.Enums;

namespace Wazap.Domain.Entities;

/// <summary>
/// Trace un achat de pack de crédits par un vendeur (paiement Mobile Money, payé à l'usage).
/// Les vendeurs sont des <see cref="User"/> avec <see cref="UserRole.Vendor"/> : pas de table dédiée.
/// </summary>
public class CreditTransaction
{
    public Guid Id { get; private set; }
    public Guid VendorId { get; private set; }
    public decimal Amount { get; private set; }
    public int CreditsPurchased { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string TransactionReference { get; private set; } = default!;
    public TransactionStatus Status { get; private set; }

    // Navigation vers le vendeur (User de rôle Vendor)
    public User? Vendor { get; private set; }

    private CreditTransaction() { }

    public CreditTransaction(Guid vendorId, decimal amount, int creditsPurchased, string transactionReference)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Le montant doit être positif.");
        if (creditsPurchased <= 0)
            throw new ArgumentOutOfRangeException(nameof(creditsPurchased), "Le nombre de crédits doit être positif.");
        if (string.IsNullOrWhiteSpace(transactionReference))
            throw new ArgumentException("La référence de transaction est requise.", nameof(transactionReference));

        Id = Guid.NewGuid();
        VendorId = vendorId;
        Amount = amount;
        CreditsPurchased = creditsPurchased;
        CreatedAt = DateTime.UtcNow;
        TransactionReference = transactionReference;
        Status = TransactionStatus.Pending;
    }

    public void MarkCompleted()
    {
        if (Status == TransactionStatus.Failed)
            throw new InvalidOperationException("Une transaction en échec ne peut pas être complétée.");
        Status = TransactionStatus.Completed;
    }

    /// <summary>
    /// Complète la transaction avec la référence de paiement fournie par le fournisseur
    /// (la référence provisoire « PENDING-… » est remplacée).
    /// </summary>
    public void Complete(string paymentReference)
    {
        if (Status == TransactionStatus.Failed)
            throw new InvalidOperationException("Une transaction en échec ne peut pas être complétée.");
        if (string.IsNullOrWhiteSpace(paymentReference))
            throw new ArgumentException("La référence de paiement est requise.", nameof(paymentReference));

        TransactionReference = paymentReference;
        Status = TransactionStatus.Completed;
    }

    public void MarkFailed()
    {
        if (Status == TransactionStatus.Completed)
            throw new InvalidOperationException("Une transaction complétée ne peut pas être marquée en échec.");
        Status = TransactionStatus.Failed;
    }
}
