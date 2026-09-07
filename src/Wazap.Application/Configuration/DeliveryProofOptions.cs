namespace Wazap.Application.Configuration;

/// <summary>
/// Preuve de livraison (section « DeliveryProof ») : code à 4 chiffres remis au client
/// à l'assignation du livreur, restitué par le livreur pour clôturer la course.
/// </summary>
public sealed class DeliveryProofOptions
{
    public const string SectionName = "DeliveryProof";

    /// <summary>
    /// Quand true, « LIVRE » n'est accepté qu'accompagné du code du client
    /// (« LIVRE &lt;code course&gt; CODE &lt;4 chiffres&gt; ») et « LIVRE TOUT » est refusé :
    /// chaque course se clôture individuellement avec son code.
    /// Défaut false — le code est généré et envoyé, vérifié s'il est fourni, mais la
    /// clôture reste possible sans lui (pas de rupture du flux livreur en place).
    /// </summary>
    public bool RequireClientCode { get; set; }
}
