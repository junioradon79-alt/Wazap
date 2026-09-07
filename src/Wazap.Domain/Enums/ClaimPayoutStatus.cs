namespace Wazap.Domain.Enums;

/// <summary>
/// Cycle de vie du versement d'indemnisation (FCFA) d'un sinistre « Garantie Colis Sûr ».
/// </summary>
public enum ClaimPayoutStatus
{
    /// <summary>Aucun versement dû (dossier non approuvé, ou indemnisation en crédits seuls).</summary>
    None = 0,

    /// <summary>Versement dû, pas encore effectué.</summary>
    Pending = 1,

    /// <summary>Versement effectué (référence de transaction enregistrée).</summary>
    Paid = 2,

    /// <summary>Tentative de versement échouée — à reprendre.</summary>
    Failed = 3
}
