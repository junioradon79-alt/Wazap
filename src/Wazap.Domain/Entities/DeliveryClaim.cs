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
    public void Approve(int compensationCredits, string? note, Guid reviewerId)
    {
        CompensationCredits = Math.Max(0, compensationCredits);
        ReviewNote = Normalize(note);
        Status = DeliveryClaimStatus.Approved;
        ReviewedAt = DateTime.UtcNow;
        ReviewedByUserId = reviewerId;
    }

    /// <summary>Rejette après enquête : le livreur est dégelé, aucun remboursement.</summary>
    public void Reject(string? note, Guid reviewerId)
    {
        CompensationCredits = null;
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
