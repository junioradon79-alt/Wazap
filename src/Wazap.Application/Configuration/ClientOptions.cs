namespace Wazap.Application.Configuration;

/// <summary>
/// Parcours acheteur (section « Client ») : URL publique de la page de suivi PWA.
/// </summary>
public sealed class ClientOptions
{
    public const string SectionName = "Client";

    /// <summary>URL de base de la page de suivi (SPA `/app/suivi/`) envoyée au client.</summary>
    public string TrackingBaseUrl { get; set; } = "https://wazap-api.onrender.com/app/suivi";
}
