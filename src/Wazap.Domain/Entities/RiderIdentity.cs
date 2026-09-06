using Wazap.Domain.Enums;

namespace Wazap.Domain.Entities;

/// <summary>
/// Dossier d'identité d'un livreur (1:1 avec <see cref="User"/> de rôle Rider).
/// Programme « Garantie Colis Sûr » : pièce d'identité scannée/photo (CNI, passeport…),
/// nom complet et description de la moto (les motos de particuliers ne sont souvent
/// pas immatriculées : la « plaque » est donc optionnelle, intégrée à la description).
/// </summary>
public class RiderIdentity
{
    public Guid UserId { get; private set; }
    public RiderIdentityStatus Status { get; private set; }
    public string? FullName { get; private set; }
    public string? IdNumber { get; private set; }
    public string? Motorcycle { get; private set; }

    /// <summary>URL du scan/photo reçue via messagerie (WhatsApp) — si fournie.</summary>
    public string? IdScanUrl { get; private set; }

    /// <summary>Fichier local du scan téléversé par l'équipe (App_Data/rider-scans).</summary>
    public string? ScanFileName { get; private set; }
    public DateTime? ScanReceivedAt { get; private set; }

    public string? BlacklistReason { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ReviewedAt { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }

    private RiderIdentity() { }

    public RiderIdentity(Guid userId, string? fullName = null, string? idNumber = null, string? motorcycle = null)
    {
        UserId = userId;
        FullName = Normalize(fullName);
        IdNumber = Normalize(idNumber, 40);
        Motorcycle = Normalize(motorcycle, 80);
        Status = RiderIdentityStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Marque le dossier vérifié : le livreur devient « certifié ».</summary>
    public void Verify(string? fullName, string? idNumber, string? motorcycle, Guid? reviewerId)
    {
        FullName = Normalize(fullName) ?? FullName;
        IdNumber = Normalize(idNumber, 40) ?? IdNumber;
        Motorcycle = Normalize(motorcycle, 80) ?? Motorcycle;
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

    /// <summary>
    /// Nouveau scan envoyé (URL messagerie). Un dossier refusé est rouvert en
    /// « à vérifier » : le livreur peut corriger son dossier.
    /// </summary>
    public void SubmitScanUrl(string url)
    {
        if (Status == RiderIdentityStatus.Blacklisted)
            throw new InvalidOperationException("Dossier exclu : aucun scan accepté.");

        IdScanUrl = Normalize(url, 500);
        ScanReceivedAt = DateTime.UtcNow;
        ReopenIfRejected();
    }

    /// <summary>
    /// Scan téléversé par l'équipe (fichier local). Un dossier refusé est rouvert
    /// en « à vérifier » si un nouveau scan est fourni.
    /// </summary>
    public void SubmitScanFile(string fileName)
    {
        if (Status == RiderIdentityStatus.Blacklisted)
            throw new InvalidOperationException("Dossier exclu : aucun scan accepté.");

        ScanFileName = Normalize(fileName, 120);
        ScanReceivedAt = DateTime.UtcNow;
        ReopenIfRejected();
    }

    private void ReopenIfRejected()
    {
        if (Status != RiderIdentityStatus.Rejected)
            return;

        Status = RiderIdentityStatus.Pending;
        ReviewedAt = null;
        ReviewedByUserId = null;
        BlacklistReason = null;
    }

    private static string? Normalize(string? value, int maxLength = 80)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}
