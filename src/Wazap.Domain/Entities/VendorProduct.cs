namespace Wazap.Domain.Entities;

/// <summary>
/// Produit d'un vendeur (élément du catalogue). Chaque <see cref="VendorProduct"/> appartient
/// à un seul <see cref="User"/> avec le rôle Vendor. Le prix est enregistré en FCFA. Le champ
/// <see cref="Emoji"/> est optionnel et sert à l'affichage du menu WhatsApp.
/// </summary>
public class VendorProduct
{
    public Guid Id { get; private set; }
    public Guid VendorId { get; private set; }

    /// <summary>Nom du produit (ex. « Poulet Braisé »).</summary>
    public string Name { get; private set; } = default!;

    /// <summary>Description courte (ex. « portion 1 »).</summary>
    public string Description { get; private set; } = default!;

    /// <summary>Prix en FCFA.</summary>
    public decimal Price { get; private set; }

    /// <summary>Emoji d'affichage optionnel (ex. « 🍗 »).</summary>
    public string? Emoji { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>Lignes de commande liées à ce produit (navigation EF Core).</summary>
    public ICollection<OrderLine> OrderLines { get; private set; } = new List<OrderLine>();

    private VendorProduct() { }

    public VendorProduct(Guid vendorId, string name, string description, decimal price, string? emoji = null)
    {
        if (vendorId == Guid.Empty)
            throw new ArgumentException("Le vendeur est obligatoire.", nameof(vendorId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentNullException(nameof(description));
        if (price < 0)
            throw new ArgumentOutOfRangeException(nameof(price), "Le prix ne peut pas être négatif.");

        Id = Guid.NewGuid();
        VendorId = vendorId;
        Name = name.Trim();
        Description = description.Trim();
        Price = price;
        Emoji = emoji?.Trim();
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Met à jour la fiche produit (nom, description, prix, emoji).</summary>
    public void Update(string name, string description, decimal price, string? emoji)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentNullException(nameof(description));
        if (price < 0)
            throw new ArgumentOutOfRangeException(nameof(price), "Le prix ne peut pas être négatif.");

        Name = name.Trim();
        Description = description.Trim();
        Price = price;
        Emoji = emoji?.Trim();
    }

    /// <summary>Représentation textuelle pour l'affichage du menu WhatsApp.</summary>
    public string DisplayText => string.IsNullOrEmpty(Emoji)
        ? $"{Name} — {Price:N0} FCFA"
        : $"{Emoji} {Name} — {Price:N0} FCFA";
}