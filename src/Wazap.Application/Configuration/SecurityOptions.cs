namespace Wazap.Application.Configuration;

/// <summary>
/// Politique de sécurité des comptes (section « Security »).
/// </summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>Nombre d'échecs de connexion avant verrouillage du compte.</summary>
    public int MaxFailedLoginAttempts { get; set; } = 5;

    /// <summary>Durée du verrouillage après dépassement (minutes).</summary>
    public int LockoutMinutes { get; set; } = 15;
}
