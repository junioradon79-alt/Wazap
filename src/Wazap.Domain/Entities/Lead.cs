using Wazap.Domain.Enums;

namespace Wazap.Domain.Entities;

/// <summary>
/// Lead d'acquisition capté par la page de vente : un commerçant intéressé
/// (nom du commerce, WhatsApp, zone/quartier). Statut suivi par l'équipe de vente.
/// </summary>
public class Lead
{
    public Guid Id { get; private set; }
    public string BusinessName { get; private set; } = default!;
    public string? ContactName { get; private set; }
    public string? ReferralCode { get; private set; }
    public string WhatsAppNumber { get; private set; } = default!;
    public string Zone { get; private set; } = default!;
    public string Source { get; private set; } = default!;
    public LeadStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private Lead() { }

    public Lead(string businessName, string whatsappNumber, string zone, string source, string? contactName = null)
    {
        Id = Guid.NewGuid();
        BusinessName = businessName;
        WhatsAppNumber = whatsappNumber;
        Zone = zone;
        Source = source;
        ContactName = string.IsNullOrWhiteSpace(contactName) ? null : contactName.Trim();
        Status = LeadStatus.New;
        CreatedAt = DateTime.UtcNow;
    }

    public void SetStatus(LeadStatus status) => Status = status;

    public void Update(string businessName, string? contactName, string source)
    {
        BusinessName = businessName;
        ContactName = string.IsNullOrWhiteSpace(contactName) ? null : contactName.Trim();
        Source = source;
    }

    public void SetZone(string zone) => Zone = zone;

    /// <summary>Enregistre le code de parrainage mentionné par le prospect (ex : WA-AB12).</summary>
    public void SetReferralCode(string? referralCode)
    {
        if (string.IsNullOrWhiteSpace(referralCode))
            return;

        var code = referralCode.Trim().ToUpperInvariant();
        ReferralCode = code.Length > 12 ? code[..12] : code;
    }
}
