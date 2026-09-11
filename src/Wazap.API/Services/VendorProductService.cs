using Microsoft.EntityFrameworkCore;
using Wazap.Application.Dtos;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Services;

/// <summary>
/// Résultat d'une suppression de produit : un produit déjà présent dans des commandes passées
/// ne peut pas être retiré (les lignes de commande le référencent pour l'historique).
/// </summary>
public enum VendorProductDeleteResult
{
    Deleted,
    NotFound,
    InUse
}

/// <summary>
/// Catalogue produits des vendeurs (menu du bot de commande WhatsApp + espace vendeur) :
/// lecture, création, mise à jour et suppression, toujours restreintes au vendeur propriétaire.
/// </summary>
public sealed class VendorProductService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<VendorProductService> _logger;

    public VendorProductService(ApplicationDbContext context, ILogger<VendorProductService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>Catalogue complet du vendeur, trié par nom (ordre du menu WhatsApp).</summary>
    public async Task<List<VendorProductDto>> GetProductsAsync(Guid vendorId)
    {
        var products = await _context.VendorProducts.AsNoTracking()
            .Where(p => p.VendorId == vendorId)
            .OrderBy(p => p.Name)
            .ToListAsync();

        return products.Select(ToDto).ToList();
    }

    public async Task<VendorProductDto?> GetProductAsync(Guid vendorId, Guid productId)
    {
        var product = await _context.VendorProducts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId && p.VendorId == vendorId);

        return product is null ? null : ToDto(product);
    }

    public async Task<VendorProductDto> CreateAsync(Guid vendorId, VendorProductRequest request)
    {
        await EnsureVendorAsync(vendorId);

        var product = new VendorProduct(vendorId, request.Name, Describe(request), request.Price, request.Emoji);
        _context.VendorProducts.Add(product);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Catalogue : produit « {Name} » ajouté par le vendeur {VendorId}.",
            product.Name, vendorId);
        return ToDto(product);
    }

    public async Task<bool> UpdateAsync(Guid vendorId, Guid productId, VendorProductRequest request)
    {
        var product = await _context.VendorProducts
            .FirstOrDefaultAsync(p => p.Id == productId && p.VendorId == vendorId);
        if (product is null)
            return false;

        product.Update(request.Name, Describe(request), request.Price, request.Emoji);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<VendorProductDeleteResult> DeleteAsync(Guid vendorId, Guid productId)
    {
        var product = await _context.VendorProducts
            .FirstOrDefaultAsync(p => p.Id == productId && p.VendorId == vendorId);
        if (product is null)
            return VendorProductDeleteResult.NotFound;

        // Les lignes de commande référencent le produit (FK Restrict) pour préserver l'historique.
        if (await _context.OrderLines.AsNoTracking().AnyAsync(l => l.VendorProductId == productId))
            return VendorProductDeleteResult.InUse;

        _context.VendorProducts.Remove(product);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // Course entre la vérification ci-dessus et l'enregistrement : la FK protège l'historique.
            _logger.LogWarning(ex, "Suppression du produit {ProductId} refusée (commande liée).", productId);
            return VendorProductDeleteResult.InUse;
        }

        return VendorProductDeleteResult.Deleted;
    }

    /// <summary>La description est optionnelle à la saisie : à défaut on reprend le nom.</summary>
    private static string Describe(VendorProductRequest request)
        => string.IsNullOrWhiteSpace(request.Description) ? request.Name : request.Description;

    private async Task EnsureVendorAsync(Guid vendorId)
    {
        var exists = await _context.Users.AsNoTracking()
            .AnyAsync(u => u.Id == vendorId && u.Role == UserRole.Vendor);
        if (!exists)
            throw new InvalidOperationException("Vendeur introuvable.");
    }

    private static VendorProductDto ToDto(VendorProduct product) => new()
    {
        Id = product.Id,
        VendorId = product.VendorId,
        Name = product.Name,
        Description = product.Description,
        Price = product.Price,
        Emoji = product.Emoji,
        CreatedAt = product.CreatedAt
    };
}
