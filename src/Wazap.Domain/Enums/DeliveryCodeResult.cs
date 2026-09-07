namespace Wazap.Domain.Enums;

/// <summary>
/// Issue de la vérification du code de livraison remis au client
/// (preuve de remise, « Garantie Colis Sûr »).
/// </summary>
public enum DeliveryCodeResult
{
    /// <summary>Code exact : la remise est prouvée.</summary>
    Ok = 1,

    /// <summary>Code erroné : la tentative est comptabilisée.</summary>
    Mismatch = 2,

    /// <summary>Trop de tentatives erronées : clôture par le vendeur ou l'admin requise.</summary>
    Locked = 3,

    /// <summary>Aucun code n'a été généré pour cette commande (courses antérieures).</summary>
    NotSet = 4
}
