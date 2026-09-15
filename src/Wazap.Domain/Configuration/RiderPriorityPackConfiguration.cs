namespace Wazap.Domain.Configuration;

/// <summary>
/// Pack prioritaire LIVREUR du catalogue (option payante, sans abonnement) : le livreur
/// achète une priorité de proposition dans son rayon pendant <see cref="Days"/> jours.
/// Représente un élément de la section « RiderPriorityPacks » d'appsettings.json.
/// </summary>
public class RiderPriorityPackConfiguration
{
    public string Name { get; set; } = default!;
    public decimal Price { get; set; }
    public int Days { get; set; }
}