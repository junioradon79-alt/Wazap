namespace Wazap.Domain.Enums;

/// <summary>
/// Statut d'un dossier de sinistre « Garantie Colis Sûr ».
/// </summary>
public enum DeliveryClaimStatus
{
    /// <summary>Déclaré par le vendeur — en cours d'enquête (le livreur est suspendu).</summary>
    Pending = 0,

    /// <summary>Sinistre confirmé : livreur exclu, vendeur remboursé (crédits).</summary>
    Approved = 1,

    /// <summary>Rejeté après enquête (aucun remboursement, livreur dégelé).</summary>
    Rejected = 2,
}
