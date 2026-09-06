namespace Wazap.Domain.Enums;

/// <summary>
/// Statut de certification d'un livreur (programme « Garantie Colis Sûr »).
/// Seuls les livreurs <see cref="Verified"/> portent le badge « Livreur certifié »
/// et peuvent (selon la configuration) recevoir des offres de courses.
/// </summary>
public enum RiderIdentityStatus
{
    /// <summary>Compte créé, identité non encore contrôlée par l'équipe.</summary>
    Pending = 0,

    /// <summary>Identité (CNI/moto) vérifiée par l'équipe → badge « Livreur certifié ».</summary>
    Verified = 1,

    /// <summary>Vérification refusée (dossier incomplet, informations incohérentes…).</summary>
    Rejected = 2,

    /// <summary>Exclu définitivement (vol/fraude/sinistre grave) — ne reçoit plus de courses.</summary>
    Blacklisted = 3,
}
