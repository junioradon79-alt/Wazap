namespace Wazap.Domain.Enums;

/// <summary>
/// Motif d'annulation d'une commande (chantier T4 — structuration des motifs).
/// </summary>
public enum OrderCancellationReason
{
    /// <summary>Commande active ou non annulée.</summary>
    None = 0,

    /// <summary>Aucun livreur n'a accepté l'offre avant l'expiration du délai global.</summary>
    TimeoutNoRider = 1,

    /// <summary>Refusée par le commerçant (via WhatsApp ou espace de gestion).</summary>
    VendorRejected = 2,

    /// <summary>Annulée à la demande du client final.</summary>
    CustomerCancelled = 3,

    /// <summary>Annulée manuellement par un administrateur, support ou opérateur.</summary>
    Manual = 4,

    /// <summary>Autre motif (précisé dans le commentaire d'annulation).</summary>
    Other = 5
}
