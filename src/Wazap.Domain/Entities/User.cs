using Wazap.Domain.Enums;

namespace Wazap.Domain.Entities;

public class User
{
    public Guid Id { get; private set; }
    public string Username { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public UserRole Role { get; private set; }
    public string? PhoneNumber { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Géolocalisation (livreurs dynamiques + vendeurs statiques)
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public DateTime? LocationUpdatedAt { get; private set; }
    public bool IsAvailable { get; private set; }
    public bool LocationSharingEnabled { get; private set; }

    // Zone/quartier déclaré (téléphones basiques sans GPS)
    public string? Zone { get; private set; }

    // Packs prépayés : nombre de commandes restantes (payé à l'usage, sans abonnement)
    public int Credits { get; private set; }

    // Parrainage : code promo du compte (ex : WA-XXXX) + parrain éventuel
    public string? ReferralCode { get; private set; }
    public Guid? ReferredByUserId { get; private set; }

    // Sécurité : verrouillage anti force-brute après échecs répétés de connexion
    public int FailedLoginAttempts { get; private set; }
    public DateTime? LockedUntilUtc { get; private set; }

    // Navigation : achats de crédits (vendeur)
    public ICollection<CreditTransaction> Transactions { get; } = new List<CreditTransaction>();

    private User() { }

    public User(string username, string passwordHash, UserRole role, string? phoneNumber = null)
    {
        Id = Guid.NewGuid();
        Username = username;
        PasswordHash = passwordHash;
        Role = role;
        PhoneNumber = phoneNumber;
        CreatedAt = DateTime.UtcNow;
        LocationSharingEnabled = true;
        ReferralCode = GenerateReferralCode();
    }

    /// <summary>
    /// Génère un code de parrainage unique au format WA-XXXX.
    /// </summary>
    public static string GenerateReferralCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> code = stackalloc char[4];
        for (var i = 0; i < code.Length; i++)
            code[i] = chars[Random.Shared.Next(chars.Length)];
        return "WA-" + new string(code);
    }

    /// <summary>
    /// Régénère le code de parrainage (utilisé si une collision est détectée en base).
    /// </summary>
    public void RegenerateReferralCode() => ReferralCode = GenerateReferralCode();

    /// <summary>
    /// Lie le compte à un parrain (code promo utilisé à l'inscription).
    /// </summary>
    public void SetReferral(Guid referrerId) => ReferredByUserId = referrerId;

    /// <summary>Le compte est-il verrouillé à cet instant ?</summary>
    public bool IsLocked() => LockedUntilUtc.HasValue && LockedUntilUtc.Value > DateTime.UtcNow;

    /// <summary>
    /// Enregistre un échec de connexion ; verrouille le compte après
    /// <paramref name="maxAttempts"/> échecs consécutifs.
    /// </summary>
    public void RegisterFailedLogin(int maxAttempts, TimeSpan lockDuration)
    {
        if (IsLocked())
            return;

        FailedLoginAttempts++;

        if (FailedLoginAttempts < maxAttempts)
            return;

        LockedUntilUtc = DateTime.UtcNow.Add(lockDuration);
        FailedLoginAttempts = 0;
    }

    /// <summary>Réinitialise le compteur d'échecs après une connexion réussie.</summary>
    public void ResetLoginFailures()
    {
        FailedLoginAttempts = 0;
        LockedUntilUtc = null;
    }

    /// <summary>
    /// Met à jour la position GPS (live location).
    /// </summary>
    public void UpdateLocation(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
        LocationUpdatedAt = DateTime.UtcNow;
    }

    public void SetAvailability(bool isAvailable) => IsAvailable = isAvailable;

    /// <summary>
    /// Met à jour le hash du mot de passe (changement de mot de passe du compte).
    /// </summary>
    public void ChangePassword(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
            throw new ArgumentException("Le hash du mot de passe est requis.", nameof(newPasswordHash));

        PasswordHash = newPasswordHash;
    }

    /// <summary>
    /// Définit la zone/quartier déclaré (matching de secours sans GPS).
    /// </summary>
    public void SetZone(string? zone)
        => Zone = string.IsNullOrWhiteSpace(zone) ? null : zone.Trim();

    public void SetLocationSharing(bool enabled)
    {
        LocationSharingEnabled = enabled;
        if (!enabled)
            ClearLocation();
    }

    public void ClearLocation()
    {
        Latitude = null;
        Longitude = null;
        LocationUpdatedAt = null;
    }

    /// <summary>
    /// Ajoute des crédits (après validation d'un achat de pack).
    /// </summary>
    public void AddCredits(int credits)
    {
        if (credits <= 0)
            throw new ArgumentOutOfRangeException(nameof(credits), "Le nombre de crédits doit être positif.");
        Credits += credits;
    }

    /// <summary>
    /// Consomme un crédit (une commande). Retourne false si aucun crédit disponible.
    /// </summary>
    public bool TryConsumeCredit()
    {
        if (Credits <= 0) return false;
        Credits--;
        return true;
    }
}
