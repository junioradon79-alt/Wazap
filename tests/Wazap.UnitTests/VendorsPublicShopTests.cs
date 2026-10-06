using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Controllers;
using Wazap.API.Services;
using Wazap.Application.Abstractions;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Xunit;

namespace Wazap.UnitTests;

public class VendorsPublicShopTests
{
    private class DummyCurrentUser : ICurrentUser
    {
        public Guid? Id => null;
        public string? Username => null;
        public UserRole? Role => null;
    }

    [Fact]
    public async Task GetPublicShop_Demo_ReturnsDemoCatalog()
    {
        using var context = TestInfra.NewContext("public-shop-demo");
        var controller = CreateController(context);

        var result = await controller.GetPublicShop("demo");

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task GetPublicShop_ValidVendorUsername_ReturnsShopAndProducts()
    {
        using var context = TestInfra.NewContext("public-shop-vendor");
        var vendor = new User("AwaBijoux", "hash", UserRole.Vendor, "+2250700000088");
        vendor.SetZone("Cocody");
        context.Users.Add(vendor);
        await context.SaveChangesAsync();

        var p1 = new VendorProduct(vendor.Id, "Collier Or", "Parure", 25000m, "✨");
        var p2 = new VendorProduct(vendor.Id, "Bague Argent", "Bague", 10000m, "💍");
        p2.SetAvailability(false);
        context.VendorProducts.AddRange(p1, p2);
        await context.SaveChangesAsync();

        var controller = CreateController(context);

        var result = await controller.GetPublicShop("AwaBijoux");

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task GetPublicShop_UnknownVendor_ReturnsNotFound()
    {
        using var context = TestInfra.NewContext("public-shop-unknown");
        var controller = CreateController(context);

        var result = await controller.GetPublicShop("BoutiqueInexistante");

        Assert.IsType<NotFoundObjectResult>(result);
    }

    private static VendorsController CreateController(ApplicationDbContext context)
    {
        var products = new VendorProductService(context, NullLogger<VendorProductService>.Instance);
        var currentUser = new DummyCurrentUser();

        return new VendorsController(null!, null!, null!, products, currentUser, context);
    }
}
