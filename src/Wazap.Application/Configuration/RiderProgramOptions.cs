namespace Wazap.Application.Configuration;

/// <summary>
/// Programme « Ambassadeur WAZAP » (section « RiderProgram ») : un livreur qui remplit trois
/// conditions reçoit une récompense. Les seuils sont ici, donc ajustables sans redéploiement.
/// </summary>
public sealed class RiderProgramOptions
{
    public const string SectionName = "RiderProgram";

    /// <summary>Active le suivi du programme (commande « PROGRAMME » + notifications). Défaut : true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Condition 2 — nombre de livraisons terminées exigé. Défaut : 250.</summary>
    public int DeliveriesTarget { get; set; } = 250;

    /// <summary>Condition 3 — nombre de filleuls VALIDÉS exigé. Défaut : 5.</summary>
    public int ReferralsTarget { get; set; } = 5;

    /// <summary>
    /// Activité minimale d'un filleul pour compter comme « validé » (anti-faux comptes).
    /// Défaut : 25 livraisons.
    /// </summary>
    public int MinFilleulDeliveries { get; set; } = 25;

    /// <summary>
    /// Note moyenne minimale exigée (0 = condition de qualité désactivée). Défaut : 0.
    /// </summary>
    public double MinAverageRating { get; set; }

    /// <summary>Libellé de la récompense communiqué aux livreurs.</summary>
    public string RewardLabel { get; set; } = "1 smartphone (type Redmi 15C)";
}
