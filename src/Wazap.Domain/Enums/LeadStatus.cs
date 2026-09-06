namespace Wazap.Domain.Enums;

/// <summary>Cycle de vie d'un lead d'acquisition (page de vente / formulaire).</summary>
public enum LeadStatus
{
    New = 0,
    Contacted = 1,
    Converted = 2,
    Discarded = 3,
}
