using System.ComponentModel.DataAnnotations;

namespace Wazap.Application.Dtos;

/// <summary>
/// Produit du catalogue d'un vendeur (lecture). Le catalogue alimente le menu du bot de
/// commande WhatsApp des clients et l'espace vendeur.
/// </summary>
public class VendorProductDto
{
    public Guid Id { get; set; }
    public Guid VendorId { get; set; }
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public decimal Price { get; set; }
    public string? Emoji { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Création / mise à jour d'un produit du catalogue vendeur. La description est optionnelle :
/// à défaut, le nom est repris (elle reste obligatoire en base).
/// </summary>
public class VendorProductRequest
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = default!;

    [MaxLength(300)]
    public string? Description { get; set; }

    /// <summary>Prix en FCFA.</summary>
    [Range(0, 9999999)]
    public decimal Price { get; set; }

    [MaxLength(10)]
    public string? Emoji { get; set; }
}
