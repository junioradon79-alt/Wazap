using Wazap.Domain.Enums;

namespace Wazap.Domain.Entities;

/// <summary>
/// Paiement du panier d'une COMMANDE par le client final (Mobile Money via GeniusPay,
/// ou espèces à la livraison — le paiement n'est pas bloquant par défaut).
/// WAZAP encaisse le panier, conserve une commission (pourcentage configurable) et
/// reverse le solde au vendeur : le versement est tracé (manuel tant que GeniusPay
/// n'expose pas d'endpoint de disbursement — voir <c>IPayoutService</c>).
/// </summary>
public class OrderPayment
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Order? Order { get; private set; }

    /// <summary>Montant du panier encaissé (FCFA).</summary>
    public decimal Amount { get; private set; }

    /// <summary>Commission WAZAP calculée à la complétion (pourcentage du montant).</summary>
    public decimal CommissionAmount { get; private set; }

    /// <summary>Montant net dû au vendeur (montant − commission), à verser.</summary>
    public decimal VendorPayoutDue { get; private set; }

    /// <summary>Référence de transaction (provisoire avant initiation, agrégateur ensuite).</summary>
    public string TransactionReference { get; private set; } = default!;

    /// <summary>Lien de paiement de l'agrégateur (permet de rouvrir la session au client).</summary>
    public string? PaymentLink { get; private set; }

    public TransactionStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    /// <summary>Message d'erreur en cas d'échec du paiement (traçabilité).</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Préfixe des références provisoires : une référence ainsi préfixée n'a jamais été
    /// initiée chez l'agrégateur → elle n'est pas soumise à la réconciliation.
    /// </summary>
    public const string PendingReferencePrefix = "ORDP-PENDING-";

    // Constructeur privé pour EF Core
    private OrderPayment() { }

    public OrderPayment(Guid orderId, decimal amount)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("L'identifiant de commande est requis.", nameof(orderId));
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Le montant doit être positif.");

        Id = Guid.NewGuid();
        OrderId = orderId;
        Amount = amount;
        TransactionReference = PendingReferencePrefix + Id.ToString("N");
        Status = TransactionStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Mémorise le lien de paiement renvoyé par l'agrégateur (rouvrable par le client).</summary>
    public void SetPaymentLink(string paymentLink)
    {
        if (string.IsNullOrWhiteSpace(paymentLink))
            throw new ArgumentException("Le lien de paiement est requis.", nameof(paymentLink));

        PaymentLink = paymentLink;
    }

    /// <summary>
    /// Remplace la référence provisoire par la référence de l'agrégateur
    /// (à l'initiation d'un paiement asynchrone).
    /// </summary>
    public void SetTransactionReference(string transactionReference)
    {
        if (string.IsNullOrWhiteSpace(transactionReference))
            throw new ArgumentException("La référence de transaction est requise.", nameof(transactionReference));

        TransactionReference = transactionReference;
    }

    /// <summary>
    /// Complète le paiement : calcule la commission WAZAP et le montant net dû au vendeur.
    /// Idempotence assurée par le service (le webhook peut arriver plusieurs fois).
    /// </summary>
    public void Complete(string paymentReference, decimal commissionPercent)
    {
        if (Status == TransactionStatus.Completed)
            throw new InvalidOperationException("Ce paiement est déjà complété.");
        if (Status == TransactionStatus.Failed)
            throw new InvalidOperationException("Un paiement en échec ne peut pas être complété.");
        if (string.IsNullOrWhiteSpace(paymentReference))
            throw new ArgumentException("La référence de paiement est requise.", nameof(paymentReference));
        if (commissionPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(commissionPercent), "La commission doit être un pourcentage entre 0 et 100.");

        // Arrondi au franc CFA le plus proche : pas de centimes en XOF.
        CommissionAmount = Math.Round(Amount * commissionPercent / 100m, 0, MidpointRounding.AwayFromZero);
        VendorPayoutDue = Amount - CommissionAmount;
        TransactionReference = paymentReference;
        Status = TransactionStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        ErrorMessage = null;
    }

    public void MarkFailed(string? errorMessage = null)
    {
        if (Status == TransactionStatus.Completed)
            throw new InvalidOperationException("Un paiement complété ne peut pas être marqué en échec.");

        Status = TransactionStatus.Failed;
        ErrorMessage = errorMessage;
    }
}