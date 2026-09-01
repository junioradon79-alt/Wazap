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
