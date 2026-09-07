namespace Wazap.Application.Configuration;

/// <summary>
/// Barème de la « Garantie Colis Sûr » (section « ColisSur ») : indemnisation en FCFA
/// d'un colis perdu ou volé, et caution des livreurs.
/// </summary>
public sealed class ColisSurOptions
{
    public const string SectionName = "ColisSur";

    /// <summary>
    /// Plafond d'indemnisation par sinistre, en FCFA. Borne l'exposition de WAZAP :
    /// un colis déclaré à 500 000 F n'engage pas la plateforme au-delà de ce montant.
    /// </summary>
    public decimal MaxCompensationFcfa { get; set; } = 50_000m;

    /// <summary>
    /// Franchise en FCFA, déduite de la valeur déclarée. Décourage les déclarations
    /// abusives sur les petits montants. <c>0</c> = pas de franchise.
    /// </summary>
    public decimal DeductibleFcfa { get; set; }

    /// <summary>
    /// Caution attendue d'un livreur, en FCFA. Débitée à hauteur du disponible quand un
    /// sinistre est confirmé contre lui. <c>0</c> (défaut) = aucune caution exigée —
    /// exiger un dépôt pèse sur le recrutement, qui est la contrainte de croissance.
    /// </summary>
    public decimal RiderDepositFcfa { get; set; }
}
