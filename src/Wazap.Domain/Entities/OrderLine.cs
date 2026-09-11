namespace Wazap.Domain.Entities;

/// <summary>
/// Ligne d'une commande : produit commandé, quantité, prix unitaire au moment de la commande,
/// et calcul du total. Une ligne appartient toujours à un seul Order. Elle réfère un
/// VendorProduct (le produit tel qu'il existait dans le catalogue du vendeur au moment de
/// la commande), mais conserve aussi une copie du nom/emoji/prix pour l'affichage historique,
/// même si le catalogue est modifié plus tard.
/// </summary>
public class OrderLine
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid VendorProductId { get; private set; }

    /// <summary>Nom du produit au moment de la commande (copie pour l'historique).</summary>
    public string ProductName { get; private set; } = default!;

    /// <summary>Emoji du produit au moment de la commande (copie pour l'historique).</summary>
    public string? ProductEmoji { get; private set; }

    /// <summary>Description du produit au moment de la commande (copie pour l'historique).</summary>
    public string? ProductDescription { get; private set; }

    /// <summary>Quantité commandée.</summary>
    public int Quantity { get; private set; }

    /// <summary>Prix unitaire au moment de la commande (FCFA).</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>Total de la ligne (qty × unitPrice), modifié automatiquement.</summary>
    public decimal TotalPrice => Quantity * UnitPrice;

    public DateTime CreatedAt { get; private set; }

    // Navigation
    public Order Order { get; private set; } = default!;
    public VendorProduct VendorProduct { get; private set; } = default!;

    private OrderLine() { }

    public OrderLine(
        Guid vendorProductId,
        string productName,
        string? productEmoji,
        string? productDescription,
        int quantity,
        decimal unitPrice)
    {
        if (vendorProductId == Guid.Empty)
            throw new ArgumentException("Le produit catalogue est obligatoire.", nameof(vendorProductId));
        if (string.IsNullOrWhiteSpace(productName))
            throw new ArgumentNullException(nameof(productName));
        if (quantity < 1)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantité ≥ 1.");
        if (unitPrice < 0)
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Prix unitaire ≥ 0.");

        Id = Guid.NewGuid();
        VendorProductId = vendorProductId;
        ProductName = productName.Trim();
        ProductEmoji = productEmoji?.Trim();
        ProductDescription = productDescription?.Trim();
        Quantity = quantity;
        UnitPrice = unitPrice;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Rattache la ligne a sa commande (appelee par Order.AddLine).</summary>
    public void AttachToOrder(Guid orderId)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("Commande obligatoire.", nameof(orderId));
        OrderId = orderId;
    }

    /// <summary>Représentation compacte pour les notifications WhatsApp / affichage.</summary>
    public string DisplayText =>
        string.IsNullOrEmpty(ProductEmoji)
            ? $"▪ {Quantity}× {ProductName} — {TotalPrice:N0} FCFA"
            : $"▪ {ProductEmoji} {Quantity}× {ProductName} — {TotalPrice:N0} FCFA";
}