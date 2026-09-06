namespace Wazap.Application.Configuration;

/// <summary>
/// Programme « Garantie Colis Sûr » côté livreur (section « RiderSecurity ») :
/// exiger que seuls les livreurs certifiés reçoivent des offres de courses.
/// </summary>
public sealed class RiderSecurityOptions
{
    public const string SectionName = "RiderSecurity";

    /// <summary>
    /// Quand true, seuls les livreurs dont le dossier d'identité est <c>Verified</c>
    /// reçoivent des offres (badge « Livreur certifié »). À activer une fois le pool
    /// certifié suffisant (risque : plus aucune offre si aucun livreur certifié).
    /// </summary>
    public bool RequireCertifiedRiders { get; set; }
}
