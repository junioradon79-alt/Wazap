namespace Wazap.Application.Configuration;

/// <summary>
/// Parcours acheteur (section « Client ») : URL publique de la page de suivi PWA.
/// </summary>
public sealed class ClientOptions
{
    public const string SectionName = "Client";

    /// <summary>URL de base de la page de suivi (PWA) envoyée au client.</summary>
    public string TrackingBaseUrl { get; set; } = "https://junioradon79gm-001-site1.jtempurl.com/suivi.html";
}
