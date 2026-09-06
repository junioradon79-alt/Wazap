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

    // Onboarding vendeur séquencé (J+1 / J+3 / J+7 via WhatsApp) : étape en attente
    // et prochaine échéance (worker dédié, templates Meta requis pour les envois sortants).
    public int? OnboardingStage { get; private set; }
    public DateTime? OnboardingNextAtUtc { get; private set; }

    // Parrainage : code promo du compte (ex : WA-XXXX) + parrain éventuel
    public string? ReferralCode { get; private set; }
    public Guid? ReferredByUserId { get; private set; }

    // Sécurité : verrouillage anti force-brute après échecs répétés de connexion
    public int FailedLoginAttempts { get; private set; }
    public DateTime? LockedUntilUtc { get; private set; }

    // Authentification renforcée : 2FA (TOTP) et code de réinitialisation de mot de passe
    public bool TwoFactorEnabled { get; private set; }
    public string? TwoFactorSecret { get; private set; }
    public string? ResetCodeHash { get; private set; }
    public DateTime? ResetCodeExpiresAtUtc { get; private set; }

    // Navigation : achats de crédits (vendeur)
    public ICollection<CreditTransaction> Transactions { get; } = new List<CreditTransaction>();

    // Certification livreur (Garantie Colis Sûr) : dossier 1:1 optionnel
    public RiderIdentity? RiderIdentity { get; private set; }

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

    /// <summary>Active la 2FA (TOTP) avec le secret déjà validé par l'utilisateur.</summary>
    public void EnableTwoFactor(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new ArgumentException("Le secret 2FA est requis.", nameof(secret));

        TwoFactorSecret = secret;
        TwoFactorEnabled = true;
    }

    /// <summary>Désactive la 2FA.</summary>
    public void DisableTwoFactor()
    {
        TwoFactorEnabled = false;
        TwoFactorSecret = null;
    }

    /// <summary>Enregistre un code de réinitialisation (hashé) avec sa date d'expiration.</summary>
    public void SetResetCode(string codeHash, DateTime expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(codeHash))
            throw new ArgumentException("Le hash du code est requis.", nameof(codeHash));

        ResetCodeHash = codeHash;
        ResetCodeExpiresAtUtc = expiresAtUtc;
    }

    /// <summary>Un code de réinitialisation est-il actif à cet instant ?</summary>
    public bool HasActiveResetCode()
        => ResetCodeHash is not null
           && ResetCodeExpiresAtUtc is { } expiry
           && expiry > DateTime.UtcNow;

    /// <summary>Consomme (efface) le code de réinitialisation après usage.</summary>
    public void ClearResetCode()
    {
        ResetCodeHash = null;
        ResetCodeExpiresAtUtc = null;
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

    /// <summary>
    /// Met à jour le numéro de téléphone (auto-réparation depuis le wa_id reçu
    /// au webhook quand l'ancienne/nouvelle numérotation ivoirienne diffère).
    /// </summary>
    public void UpdatePhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new ArgumentException("Le numéro de téléphone est requis.", nameof(phoneNumber));

        PhoneNumber = phoneNumber;
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

    /// <summary>Lance la séquence d'onboarding vendeur (1re étape programmée à J+1).</summary>
    public void StartVendorOnboarding()
    {
        if (Role != UserRole.Vendor)
            return;

        OnboardingStage = 1;
        OnboardingNextAtUtc = DateTime.UtcNow.AddDays(1);
    }

    /// <summary>
    /// Programme l'étape suivante à une date fixe depuis la création du compte
    /// (J+3 puis J+7). Si la date cible est déjà passée, reporte la tentative à +6 h.
    /// </summary>
    public void AdvanceVendorOnboarding(DateTime createdAtUtc)
    {
        if (OnboardingStage is not (1 or 2))
        {
            CompleteVendorOnboarding();
            return;
        }

        var nextStage = OnboardingStage.Value + 1;
        var offsetDays = nextStage switch
        {
            2 => 3,
            3 => 7,
            _ => 7
        };

        OnboardingStage = nextStage;
        var target = createdAtUtc.AddDays(offsetDays);
        OnboardingNextAtUtc = target > DateTime.UtcNow ? target : DateTime.UtcNow.AddHours(6);
    }

    /// <summary>L'envoi a échoué (fenêtre 24 h, template indisponible…) : on réessaie dans 6 h.</summary>
    public void RetryVendorOnboarding()
        => OnboardingNextAtUtc = DateTime.UtcNow.AddHours(6);

    /// <summary>Termine la séquence (dernière étape envoyée ou compte non concerné).</summary>
    public void CompleteVendorOnboarding()
    {
        OnboardingStage = null;
        OnboardingNextAtUtc = null;
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
