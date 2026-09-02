namespace Wazap.Application.Configuration;

/// <summary>
/// Offre de découverte aux nouveaux vendeurs (section « Trial ») : N premières
/// commandes offertes à l'inscription pour découvrir la solution avant d'acheter
/// un pack.
/// </summary>
public sealed class TrialOptions
{
    public const string SectionName = "Trial";

    /// <summary>Active l'octroi automatique de l'offre de découverte.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Nombre de commandes offertes à l'inscription d'un nouveau vendeur.</summary>
    public int FreeCreditsOnRegistration { get; set; } = 15;
}
