namespace Wazap.Application.Configuration;

/// <summary>État de la protection au repos des scans de pièce d'identité.</summary>
public enum ScanProtectionStatus
{
    /// <summary>Clé valide : les scans sont chiffrés au repos (AES-GCM).</summary>
    Encrypted,

    /// <summary>Aucune clé, mais le stockage en clair a été autorisé explicitement.</summary>
    UnencryptedAllowed,

    /// <summary>Aucune clé configurée : les téléversements sont refusés.</summary>
    MissingKey,

    /// <summary>Clé présente mais inexploitable : les téléversements sont refusés.</summary>
    InvalidKey
}

/// <summary>
/// Options de protection des scans de pièce d'identité des livreurs (section « RiderScans »).
/// <para>
/// Le scan d'une CNI est une donnée d'identité : il est chiffré au repos (AES-GCM) dès qu'une
/// clé est configurée. Sans clé exploitable, le téléversement est <b>refusé</b> — il n'y a pas
/// de repli silencieux vers l'écriture en clair, car un oubli de configuration ne doit jamais
/// dégrader sans bruit la protection d'une donnée personnelle.
/// </para>
/// <para>
/// <see cref="AllowUnencryptedStorage"/> rouvre l'écriture en clair, mais seulement comme une
/// décision écrite et visible (poste de développement, tests). Ce n'est pas un défaut.
/// </para>
/// </summary>
public sealed class RiderScansOptions
{
    public const string SectionName = "RiderScans";

    /// <summary>
    /// Clé de chiffrement AES. Format accepté : hexadécimal (64 caractères) ou Base64
    /// (44 caractères) → 32 octets. 16 et 24 octets sont également acceptés.
    /// </summary>
    public string? EncryptionKey { get; set; }

    /// <summary>
    /// Autorise l'écriture de scans EN CLAIR quand aucune clé n'est exploitable.
    /// <c>false</c> par défaut : en l'absence de clé, le téléversement échoue franchement.
    /// </summary>
    public bool AllowUnencryptedStorage { get; set; }

    /// <summary>
    /// Résout la clé configurée en octets. Retourne <c>false</c> et renseigne
    /// <paramref name="problem"/> (message lisible, sans jamais divulguer la clé) si elle est
    /// absente, illisible ou de longueur non conforme.
    /// </summary>
    public bool TryResolveKey(out byte[] key, out string? problem)
    {
        key = [];
        problem = null;

        if (string.IsNullOrWhiteSpace(EncryptionKey))
        {
            problem = "aucune clé configurée";
            return false;
        }

        var candidate = EncryptionKey.Trim();
        try
        {
            key = candidate.Length == 64 && candidate.All(Uri.IsHexDigit)
                ? Convert.FromHexString(candidate)
                : Convert.FromBase64String(candidate);
        }
        catch (FormatException)
        {
            key = [];
            problem = "clé illisible (hexadécimal de 64 caractères ou Base64 attendu)";
            return false;
        }

        if (key.Length is not (16 or 24 or 32))
        {
            problem = $"clé de longueur invalide ({key.Length} octets — 16, 24 ou 32 attendus)";
            key = [];
            return false;
        }

        return true;
    }

    /// <summary>État courant de la protection, pour la supervision (<c>/health/details</c>).</summary>
    public ScanProtectionStatus GetStatus()
    {
        if (TryResolveKey(out _, out _))
            return ScanProtectionStatus.Encrypted;

        if (AllowUnencryptedStorage)
            return ScanProtectionStatus.UnencryptedAllowed;

        return string.IsNullOrWhiteSpace(EncryptionKey)
            ? ScanProtectionStatus.MissingKey
            : ScanProtectionStatus.InvalidKey;
    }
}
