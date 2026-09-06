using Wazap.Domain.Enums;

namespace Wazap.Domain.Entities;

/// <summary>
/// Dossier d'identité d'un livreur (1:1 avec <see cref="User"/> de rôle Rider).
/// Contient les éléments collectés/vérifiés par l'équipe dans le cadre de la
/// certification « Garantie Colis Sûr » : nom complet, n° CNI, plaque de la moto.
/// </summary>
public class RiderIdentity
{
    public Guid UserId { get; private set; }
    public RiderIdentityStatus Status { get; private set; }
    public string? FullName { get; private set; }
    public string? CniNumber { get; private set; }
    public string? MotorcyclePlate { get; private set; }
    public string? BlacklistReason { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ReviewedAt { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }

    private RiderIdentity() { }

    public RiderIdentity(Guid userId, string? fullName = null, string? cniNumber = null, string? motorcyclePlate = null)
    {
        UserId = userId;
        FullName = Normalize(fullName);
        CniNumber = Normalize(cniNumber);
        MotorcyclePlate = Normalize(motorcyclePlate);
        Status = RiderIdentityStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Marque le dossier vérifié : le livreur devient « certifié ».</summary>
    public void Verify(string? fullName, string? cniNumber, string? motorcyclePlate, Guid? reviewerId)
    {
        FullName = Normalize(fullName) ?? FullName;
        CniNumber = Normalize(cniNumber) ?? CniNumber;
        MotorcyclePlate = Normalize(motorcyclePlate) ?? MotorcyclePlate;
        Status = RiderIdentityStatus.Verified;
        ReviewedAt = DateTime.UtcNow;
        ReviewedByUserId = reviewerId;
        BlacklistReason = null;
    }

    /// <summary>Refuse la certification (dossier incomplet, incohérences…).</summary>
    public void Reject(string? reason, Guid? reviewerId)
    {
        Status = RiderIdentityStatus.Rejected;
        ReviewedAt = DateTime.UtcNow;
        ReviewedByUserId = reviewerId;
        BlacklistReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    /// <summary>Exclut définitivement (vol/fraude) et bloque toute nouvelle offre.</summary>
    public void Blacklist(string reason, Guid? reviewerId)
    {
        Status = RiderIdentityStatus.Blacklisted;
        ReviewedAt = DateTime.UtcNow;
        ReviewedByUserId = reviewerId;
        BlacklistReason = string.IsNullOrWhiteSpace(reason) ? "Exclusion." : reason.Trim();
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length > 80 ? trimmed[..80] : trimmed;
    }
}
