namespace Wazap.Domain.Enums;

/// <summary>
/// Cycle de vie d'un lot de livraison groupée (plusieurs commandes d'un même vendeur).
/// </summary>
public enum DeliveryBatchStatus
{
    /// <summary>Lot ouvert : il accumule encore les commandes confirmées (fenêtre de groupage).</summary>
    Open = 1,

    /// <summary>Un livreur a accepté le lot : toutes les commandes lui sont assignées.</summary>
    Assigned = 2,

    /// <summary>Lot annulé (plus de commande active).</summary>
    Cancelled = 3
}
