using System.ComponentModel.DataAnnotations;

namespace Wazap.Application.Dtos;

/// <summary>
/// Demande de création de commande. Soit :
/// - un libellé libre + montant fournis (mode texte),
/// - soit une liste de lignes produit (mode catalogue) : le montant est recalculé.
/// </summary>
public class CreateOrderRequest
{
    public string ClientName { get; set; } = default!;
    public string ClientWhatsAppNumber { get; set; } = default!;
    public string VendorWhatsAppNumber { get; set; } = default!;
    public string Description { get; set; } = default!;
    public decimal Amount { get; set; }

    /// <summary>
    /// Lignes de commande issues du catalogue vendeur (Optionnel).
    /// Si renseigné, le montant total est calculé à partir de ces lignes.
    /// </summary>
    public List<OrderLineRequest>? Lines { get; set; }
}

public class OrderLineRequest
{
    /// <summary>Nom affiché du produit (ex. « Poulet braisé »), pour l'historique.</summary>
    [Required, MaxLength(100)]
    public string ProductName { get; set; } = default!;

    public string? ProductEmoji { get; set; }
    public string? ProductDescription { get; set; }

    /// <summary>Identifiant du produit catalogue (peut être null en mode texte libre).</summary>
    public Guid? VendorProductId { get; set; }

    /// <summary>Quantité commandée.</summary>
    [Range(1, 99)]
    public int Quantity { get; set; } = 1;

    /// <summary>Prix unitaire saisi par le vendeur au moment de la commande (FCFA).</summary>
    [Range(0, 9999999)]
    public decimal UnitPrice { get; set; }
}
