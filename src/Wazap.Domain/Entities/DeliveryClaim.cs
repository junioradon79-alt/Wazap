using Wazap.Domain.Enums;

namespace Wazap.Domain.Entities;

/// <summary>
/// Dossier de sinistre « Garantie Colis Sûr » : un vendeur signale un colis perdu/volé
/// après l'avoir remis à un livreur certifié. Tant que le dossier est <see cref="Pending"/>,
/// le livreur ne reçoit plus d'offres ; si le sinistre est confirmé (Approved), le livreur
/// est exclu définitivement et le vendeur est remboursé en crédits.
/// </summary>
public class DeliveryClaim
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid VendorUserId { get; private set; }
    public Guid RiderUserId { get; private set; }

    /// <summary>Message brut du vendeur (ex. « SINISTRE A1B2C3D4 — le livreur a disparu »).</summary>
    public string? VendorNote { get; private set; }

    public DeliveryClaimStatus Status { get; private set; }

    /// <summary>Crédits de compensation accordés (hors remboursement du crédit consommé).</summary>
    public int? CompensationCredits { get; private set; }
    public string? ReviewNote { get; private set; }

    /// <summary>Indemnisation en FCFA (étape 3), calculée depuis le barème puis versée au vendeur.</summary>
    public decimal? CompensationAmountFcfa { get; private set; }

    /// <summary>Part prélevée sur la caution du livreur au moment de la confirmation.</summary>
    public decimal? RiderDepositDebitedFcfa { get; private set; }

    public ClaimPayoutStatus PayoutStatus { get; private set; }

    /// <summary>Référence du virement (Orange Money, ou transaction GeniusPay le jour venu).</summary>
    public string? PayoutReference { get; private set; }
    public DateTime? PaidAt { get; private set; }
    public string? PayoutError { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? ReviewedAt { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }

    private DeliveryClaim() { }

    public DeliveryClaim(Guid orderId, Guid vendorUserId, Guid riderUserId, string? vendorNote)
    {
        Id = Guid.NewGuid();
        OrderId = orderId;
        VendorUserId = vendorUserId;
        RiderUserId = riderUserId;
        VendorNote = string.IsNullOrWhiteSpace(vendorNote) ? null : vendorNote.Trim();
        Status = DeliveryClaimStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Confirme le sinistre : exclusion du livreur + remboursement au vendeur.</summary>
    public void Approve(int compensationCredits, string? note, Guid reviewerId,
        decimal compensationAmountFcfa = 0m, decimal riderDepositDebitedFcfa = 0m)
    {
        CompensationCredits = Math.Max(0, compensationCredits);
        CompensationAmountFcfa = Math.Max(0m, compensationAmountFcfa);
        RiderDepositDebitedFcfa = Math.Max(0m, riderDepositDebitedFcfa);
        ReviewNote = Normalize(note);
        Status = DeliveryClaimStatus.Approved;
        ReviewedAt = DateTime.UtcNow;
        ReviewedByUserId = reviewerId;

        // Un versement n'est dû que s'il y a un montant : une indemnisation en crédits
        // seuls ne crée pas de ligne de versement à suivre.
        PayoutStatus = CompensationAmountFcfa > 0m ? ClaimPayoutStatus.Pending : ClaimPayoutStatus.None;
    }

    /// <summary>Enregistre le versement effectué (référence du virement Orange Money / GeniusPay).</summary>
    public void MarkPayoutPaid(string reference)
    {
        if (PayoutStatus is not (ClaimPayoutStatus.Pending or ClaimPayoutStatus.Failed))
            throw new InvalidOperationException($"Aucun versement en attente sur ce dossier (état : {PayoutStatus}).");

        if (string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("Une référence de versement est requise (traçabilité comptable).", nameof(reference));

        PayoutStatus = ClaimPayoutStatus.Paid;
        PayoutReference = Normalize(reference);
        PayoutError = null;
        PaidAt = DateTime.UtcNow;
    }

    /// <summary>Marque une tentative de versement en échec : le dossier reste à reprendre.</summary>
    public void MarkPayoutFailed(string? error)
    {
        if (PayoutStatus == ClaimPayoutStatus.Paid)
            throw new InvalidOperationException("Versement déjà effectué : impossible de le passer en échec.");

        PayoutStatus = ClaimPayoutStatus.Failed;
        PayoutError = Normalize(error);
    }

    /// <summary>Rejette après enquête : le livreur est dégelé, aucun remboursement.</summary>
    public void Reject(string? note, Guid reviewerId)
    {
        CompensationCredits = null;
        CompensationAmountFcfa = null;
        RiderDepositDebitedFcfa = null;
        PayoutStatus = ClaimPayoutStatus.None;
        ReviewNote = Normalize(note);
        Status = DeliveryClaimStatus.Rejected;
        ReviewedAt = DateTime.UtcNow;
        ReviewedByUserId = reviewerId;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length > 300 ? trimmed[..300] : trimmed;
    }
}
