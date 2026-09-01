namespace Wazap.Domain.Configuration;

/// <summary>
/// Pack prépayé du catalogue (payé à l'usage, sans abonnement mensuel).
/// Représente un élément de la section « Packs » d'appsettings.json.
/// </summary>
public class PackConfiguration
{
    public string Name { get; set; } = default!;
    public decimal Price { get; set; }
    public int Credits { get; set; }
}
