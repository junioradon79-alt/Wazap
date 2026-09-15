using Wazap.Domain.Enums;

namespace Wazap.Domain.Entities;

/// <summary>
/// Trace l'achat d'un pack prioritaire par un livreur (« pack prioritaire » de la politique
/// commerciale). WAZAP vend la <b>visibilité</b> : le livreur est proposé en priorité aux
/// vendeurs de son rayon pendant N jours. La contrepartie n'est <b>jamais</b> une attribution
/// garantie (l'attribution reste à l'acceptation), et la priorité ne contourne ni le rayon
/// de diffusion ni la disponibilité.
/// Les livreurs sont des <see cref="User"/> de rôle <see cref="UserRole.Rider"/> : pas de table
/// dédiée au livreur, comme pour les crédits vendeur (<see cref="CreditTransaction"/>).
/// </summary>
public class RiderPriorityPurchase
{
    public Guid Id { get; private set; }
    public Guid RiderUserId { get; private set; }
    public string? PackName { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>Durée de priorité offerte par le pack (jours).</summary>
    public int Days { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public string TransactionReference { get; private set; } = default!;
    public TransactionStatus Status { get; private set; }

    /// <summary>Date de complétion du paiement (null tant que la transaction est en attente).</summary>
    public DateTime? CompletedAt { get; private set; }

    // Navigation vers le livreur (User de rôle Rider)
    public User? Rider { get; private set; }

    /// <summary>
    /// Préfixe des références provisoires : une référence ainsi préfixée n'a jamais été initiée
    /// chez l'agrégateur → elle n'est pas soumise à la réconciliation.
    /// </summary>
    public const string PendingReferencePrefix = "RDRP-PENDING-";

    private RiderPriorityPurchase() { }

    public RiderPriorityPurchase(
        Guid riderUserId,
        decimal amount,
        int days,
        string? transactionReference = null,
        string? packName = null)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Le montant doit être positif.");
        if (days <= 0)
            throw new ArgumentOutOfRangeException(nameof(days), "La durée de priorité doit être positive.");

        Id = Guid.NewGuid();
        RiderUserId = riderUserId;
        PackName = packName;
        Amount = amount;
        Days = days;
        CreatedAt = DateTime.UtcNow;
        TransactionReference = string.IsNullOrWhiteSpace(transactionReference)
            ? PendingReferencePrefix + Id.ToString("N")
            : transactionReference;
        Status = TransactionStatus.Pending;
    }

    /// <summary>
    /// Remplace la référence provisoire par celle de l'agrégateur (statut inchangé) —
    /// utilisé à l'initiation d'un paiement asynchrone.
    /// </summary>
    public void SetTransactionReference(string transactionReference)
    {
        if (string.IsNullOrWhiteSpace(transactionReference))
            throw new ArgumentException("La référence de transaction est requise.", nameof(transactionReference));

        TransactionReference = transactionReference;
    }

    /// <summary>
    /// Complète la transaction avec la référence de paiement fournie par l'agrégateur.
    /// Idempotence gérée par l'appelant (Service) : ici, une transaction en échec ne peut pas
    /// être complétée.
    /// </summary>
    public void Complete(string paymentReference)
    {
        if (Status == TransactionStatus.Failed)
            throw new InvalidOperationException("Une transaction en échec ne peut pas être complétée.");
        if (string.IsNullOrWhiteSpace(paymentReference))
            throw new ArgumentException("La référence de paiement est requise.", nameof(paymentReference));

        TransactionReference = paymentReference;
        Status = TransactionStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    public void MarkFailed()
    {
        if (Status == TransactionStatus.Completed)
            throw new InvalidOperationException("Une transaction complétée ne peut pas être marquée en échec.");

        Status = TransactionStatus.Failed;
    }
}