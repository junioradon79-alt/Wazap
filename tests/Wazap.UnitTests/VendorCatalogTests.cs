using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Services;
using Wazap.Application.Dtos;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>Catalogue vendeur : règles de la fiche produit (validation, mise à jour, affichage).</summary>
public class VendorProductTests
{
    [Fact]
    public void NewProduct_TrimsFields_AndBuildsDisplayText()
    {
        var vendorId = Guid.NewGuid();

        var product = new VendorProduct(vendorId, "  Poulet braisé  ", "  portion 1  ", 2500m, "🍗");

        Assert.Equal(vendorId, product.VendorId);
        Assert.Equal("Poulet braisé", product.Name);
        Assert.Equal("portion 1", product.Description);
        Assert.Equal(2500m, product.Price);
        Assert.Equal("🍗", product.Emoji);
        Assert.StartsWith("🍗 Poulet braisé", product.DisplayText);
        Assert.Contains("FCFA", product.DisplayText);
    }

    [Fact]
    public void NewProduct_WithoutEmoji_DisplayTextHasNoEmoji()
    {
        var product = new VendorProduct(Guid.NewGuid(), "Attiéké", "portion 1", 500m);

        Assert.Null(product.Emoji);
        Assert.StartsWith("Attiéké — ", product.DisplayText);
    }

    [Fact]
    public void NewProduct_WithoutVendor_Throws()
        => Assert.Throws<ArgumentException>(() =>
            new VendorProduct(Guid.Empty, "Poulet", "portion 1", 1000m));

    [Fact]
    public void NewProduct_WithoutName_Throws()
        => Assert.Throws<ArgumentNullException>(() =>
            new VendorProduct(Guid.NewGuid(), "  ", "portion 1", 1000m));

    [Fact]
    public void NewProduct_WithNegativePrice_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            new VendorProduct(Guid.NewGuid(), "Poulet", "portion 1", -1m));

    [Fact]
    public void Update_ReplacesFields()
    {
        var product = new VendorProduct(Guid.NewGuid(), "Poulet", "portion 1", 1000m, "🍗");

        product.Update("Poulet entier", "portion 2", 3200m, null);

        Assert.Equal("Poulet entier", product.Name);
        Assert.Equal("portion 2", product.Description);
        Assert.Equal(3200m, product.Price);
        Assert.Null(product.Emoji);
    }

    [Fact]
    public void Update_WithNegativePrice_Throws()
    {
        var product = new VendorProduct(Guid.NewGuid(), "Poulet", "portion 1", 1000m);

        Assert.Throws<ArgumentOutOfRangeException>(() => product.Update("Poulet", "portion 1", -5m, null));
    }
}

/// <summary>Lignes de commande et commande « mode catalogue » (montant calculé).</summary>
public class OrderCatalogTests
{
    [Fact]
    public void OrderLine_TotalPrice_IsQuantityTimesUnitPrice()
    {
        var line = new OrderLine(Guid.NewGuid(), "Poulet braisé", "🍗", "portion 1", 3, 2500m);

        Assert.Equal(7500m, line.TotalPrice);
        Assert.Contains("3× Poulet braisé", line.DisplayText);
    }

    [Fact]
    public void OrderLine_WithZeroQuantity_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OrderLine(Guid.NewGuid(), "Poulet", null, null, 0, 1000m));

    [Fact]
    public void OrderLine_WithEmptyProduct_Throws()
        => Assert.Throws<ArgumentException>(() =>
            new OrderLine(Guid.Empty, "Poulet", null, null, 1, 1000m));

    [Fact]
    public void CatalogOrder_ComputesAmountFromLines_AndAttachesThem()
    {
        var vendorId = Guid.NewGuid();
        var lines = new List<OrderLine>
        {
            new(Guid.NewGuid(), "Poulet braisé", "🍗", "portion 1", 2, 2500m),
            new(Guid.NewGuid(), "Attiéké", null, "portion 1", 1, 500m)
        };

        var order = new Order("Client", "+2250700000001", "+2250700000002", vendorId, lines, "desc");

        Assert.Equal(5500m, order.Amount);
        Assert.Equal(vendorId, order.VendorUserId);
        Assert.Equal(OrderStatus.PendingVendorConfirmation, order.Status);
        Assert.Equal(2, order.OrderLines.Count);
        Assert.All(order.OrderLines, l => Assert.Equal(order.Id, l.OrderId));
    }
}

/// <summary>Service du catalogue vendeur : CRUD restreint au propriétaire + protection de l'historique.</summary>
public class VendorProductServiceTests
{
    [Fact]
    public async Task Create_List_Update_Delete_RoundTrip()
    {
        using var context = TestInfra.NewContext("catalog-crud");
        var vendor = new User("ChezThalia", "hash", UserRole.Vendor, "+2250700000002");
        context.Users.Add(vendor);
        await context.SaveChangesAsync();
        var service = NewService(context);

        var created = await service.CreateAsync(vendor.Id,
            new VendorProductRequest { Name = "Poulet braisé", Price = 2500m, Emoji = "🍗" });
        Assert.Equal("Poulet braisé", created.Description); // description par défaut = nom

        Assert.Single(await service.GetProductsAsync(vendor.Id));

        Assert.True(await service.UpdateAsync(vendor.Id, created.Id,
            new VendorProductRequest { Name = "Poulet entier", Description = "portion 2", Price = 3200m }));

        var reloaded = await service.GetProductAsync(vendor.Id, created.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Poulet entier", reloaded!.Name);
        Assert.Equal(3200m, reloaded.Price);
        Assert.Null(reloaded.Emoji);

        Assert.Equal(VendorProductDeleteResult.Deleted, await service.DeleteAsync(vendor.Id, created.Id));
        Assert.Empty(await service.GetProductsAsync(vendor.Id));
    }

    [Fact]
    public async Task Create_ForUnknownVendor_Throws()
    {
        using var context = TestInfra.NewContext("catalog-unknown-vendor");
        var service = NewService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(Guid.NewGuid(), new VendorProductRequest { Name = "Poulet", Price = 1000m }));
    }

    [Fact]
    public async Task Update_And_Delete_OnForeignProduct_DoNothing()
    {
        using var context = TestInfra.NewContext("catalog-foreign");
        var owner = new User("Owner", "hash", UserRole.Vendor, "+2250700000010");
        var other = new User("Other", "hash", UserRole.Vendor, "+2250700000011");
        context.Users.AddRange(owner, other);
        await context.SaveChangesAsync();
        var service = NewService(context);

        var product = await service.CreateAsync(owner.Id,
            new VendorProductRequest { Name = "Poulet", Price = 1000m });

        Assert.False(await service.UpdateAsync(other.Id, product.Id,
            new VendorProductRequest { Name = "Volé", Price = 1m }));
        Assert.Equal(VendorProductDeleteResult.NotFound, await service.DeleteAsync(other.Id, product.Id));
        Assert.Single(await service.GetProductsAsync(owner.Id));
    }

    [Fact]
    public async Task Delete_WhenProductAppearsInAnOrder_IsRefused()
    {
        using var context = TestInfra.NewContext("catalog-in-use");
        var vendor = new User("ChezThalia", "hash", UserRole.Vendor, "+2250700000002");
        context.Users.Add(vendor);
        await context.SaveChangesAsync();
        var service = NewService(context);

        var product = new VendorProduct(vendor.Id, "Poulet braisé", "portion 1", 2500m, "🍗");
        context.VendorProducts.Add(product);
        await context.SaveChangesAsync();

        var line = new OrderLine(product.Id, product.Name, product.Emoji, product.Description, 1, product.Price);
        var order = new Order("Client", "+2250700000001", "+2250700000002", vendor.Id, [line], "desc");
        context.Orders.Add(order);
        context.OrderLines.Add(line);
        await context.SaveChangesAsync();

        Assert.Equal(VendorProductDeleteResult.InUse, await service.DeleteAsync(vendor.Id, product.Id));
        Assert.Single(await context.VendorProducts.ToListAsync());
    }

    private static VendorProductService NewService(ApplicationDbContext context)
        => new(context, NullLogger<VendorProductService>.Instance);
}
