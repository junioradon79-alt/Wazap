namespace Wazap.Application.Configuration;

/// <summary>
/// Configuration de l'API publique versionnée pour partenaires (section « PublicApi »).
/// L'API v1 est en lecture seule et protégée par une clé (en-tête <c>X-Api-Key</c>).
/// </summary>
public sealed class PublicApiOptions
{
    public const string SectionName = "PublicApi";

    /// <summary>Clés API autorisées (ex. « PublicApi__Keys__0 »). Vide = API indisponible (503).</summary>
    public List<string> Keys { get; set; } = new();

    /// <summary>Nombre maximal de requêtes par minute (rate limit global de la politique « publicapi »).</summary>
    public int RateLimitPerMinute { get; set; } = 240;
}
