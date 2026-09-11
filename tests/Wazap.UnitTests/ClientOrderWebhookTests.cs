using Microsoft.EntityFrameworkCore;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Intégration du bot de commande client et du catalogue produits dans le webhook WhatsApp :
/// le routage des numéros inconnus (livreur → commande client → prospects) et les commandes
/// vendeur de gestion du catalogue (PRODUIT / PRODUITS / SUPPRIMER PRODUIT).
/// </summary>
public class ClientOrderWebhookTests
{
    private const string ClientPhone = "+2250700001001";
    private const string VendorPhone = "+2250700001002";

    [Fact]
    public async Task UnknownNumber_OrderIntent_RoutedToClientBot_NotToProspects()
    {
        using var harness = new WebhookHarness();

        await harness.SendAsync(ClientPhone, "bonjour, je veux commander");

        Assert.True(await harness.Context.ClientOrderDrafts.AnyAsync());
        Assert.False(await harness.Context.Leads.AnyAsync());
        Assert.Contains("Étape 1/3", harness.LastMessageTo(ClientPhone));
    }

    [Fact]
    public async Task UnknownNumber_MerchantIntent_StillGoesToProspectBot()
    {
        using var harness = new WebhookHarness();

        await harness.SendAsync(ClientPhone, "bonjour je veux activer mon commerce");

        Assert.False(await harness.Context.ClientOrderDrafts.AnyAsync());
        Assert.True(await harness.Context.Leads.AnyAsync());
    }

    [Fact]
    public async Task OrderIntent_WithCatalog_EndToEndThroughWebhook()
    {
        using var harness = new WebhookHarness();
        var vendor = new User("ChezThalia", "hash", UserRole.Vendor, VendorPhone);
        harness.Context.Users.Add(vendor);
        harness.Context.VendorProducts.Add(
            new VendorProduct(vendor.Id, "Poulet braisé", "portion 1", 2500m, "🍗"));
        await harness.Context.SaveChangesAsync();

        await harness.SendAsync(ClientPhone, "je veux commander");
        await harness.SendAsync(ClientPhone, "1 poulet braisé");
        await harness.SendAsync(ClientPhone, "ChezThalia");
        await harness.SendAsync(ClientPhone, "1");
        await harness.SendAsync(ClientPhone, "Marcory, rue Princesse");

        var order = await harness.Context.Orders.SingleAsync();
        Assert.Equal(vendor.Id, order.VendorUserId);
        Assert.Equal(2500m, order.Amount);
        var lines = await harness.Context.OrderLines.Where(l => l.OrderId == order.Id).ToListAsync();
        Assert.Single(lines);
        Assert.Equal("Poulet braisé", lines[0].ProductName);
        Assert.Contains("Nouvelle commande client", harness.LastMessageTo(VendorPhone));
    }

    [Fact]
    public async Task VendorCommand_Product_AddsListsAndRemoves()
    {
        using var harness = new WebhookHarness();
        harness.Context.Users.Add(new User("Chez Awa", "hash", UserRole.Vendor, VendorPhone));
        await harness.Context.SaveChangesAsync();

        await harness.SendAsync(VendorPhone, "PRODUIT Poulet braisé | 2500 | 🍗");

        Assert.Contains("Produit ajouté", harness.LastMessageTo(VendorPhone));
        var product = await harness.Context.VendorProducts.SingleAsync();
        Assert.Equal("Poulet braisé", product.Name);
        Assert.Equal(2500m, product.Price);
        Assert.Equal("🍗", product.Emoji);
        Assert.Equal("Poulet braisé", product.Description); // description par défaut = nom

        await harness.SendAsync(VendorPhone, "PRODUITS");
        Assert.Contains("Poulet braisé", harness.LastMessageTo(VendorPhone));

        await harness.SendAsync(VendorPhone, "SUPPRIMER PRODUIT 1");
        Assert.Contains("Produit retiré", harness.LastMessageTo(VendorPhone));
        Assert.Equal(0, await harness.Context.VendorProducts.CountAsync());
    }

    [Fact]
    public async Task VendorCommand_Product_BadFormat_IsRejected()
    {
        using var harness = new WebhookHarness();
        harness.Context.Users.Add(new User("Chez Awa", "hash", UserRole.Vendor, VendorPhone));
        await harness.Context.SaveChangesAsync();

        await harness.SendAsync(VendorPhone, "PRODUIT poulet");

        Assert.Contains("Format", harness.LastMessageTo(VendorPhone));
        Assert.Equal(0, await harness.Context.VendorProducts.CountAsync());
    }
}
