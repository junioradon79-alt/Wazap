using Microsoft.EntityFrameworkCore;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Xunit;

namespace Wazap.UnitTests;

public class VendorCatalogImportTests
{
    private const string VendorPhone = "+2250700112233";

    private static User AddVendor(WebhookHarness harness)
    {
        var vendor = new User("boutique_awa", "hash", UserRole.Vendor, VendorPhone);
        harness.Context.Users.Add(vendor);
        harness.Context.SaveChanges();
        return vendor;
    }

    [Fact]
    public async Task Vendor_SendsImportCommandWithText_ExtractsAndCreatesProducts()
    {
        using var harness = new WebhookHarness();
        var vendor = AddVendor(harness);

        await harness.SendAsync(VendorPhone, "IMPORT Robe soirée dorée 15000 FCFA; Escarpins noirs 18000 F");

        var products = await harness.Context.VendorProducts
            .Where(p => p.VendorId == vendor.Id)
            .OrderBy(p => p.Name)
            .ToListAsync();

        Assert.Equal(2, products.Count);
        Assert.Contains(products, p => p.Name.Contains("Robe") && p.Price == 15000m && p.Emoji == "👗");
        Assert.Contains(products, p => p.Name.Contains("Escarpins") && p.Price == 18000m && p.Emoji == "👠");

        var reply = harness.LastMessageTo(VendorPhone);
        Assert.NotNull(reply);
        Assert.Contains("Magie WAZAP", reply);
        Assert.Contains("Robe soirée", reply);
        Assert.Contains("15 000 FCFA", reply);
    }

    [Fact]
    public async Task Vendor_SendsEmptyImportCommand_ReturnsExplainingHelpMessage()
    {
        using var harness = new WebhookHarness();
        AddVendor(harness);

        await harness.SendAsync(VendorPhone, "IMPORT");

        var reply = harness.LastMessageTo(VendorPhone);
        Assert.NotNull(reply);
        Assert.Contains("WAZAP Magic Importer", reply);
        Assert.Contains("capture d'écran", reply);
    }

    [Fact]
    public async Task Vendor_SendsPhotoOfMenu_ExtractsAndCreatesProducts()
    {
        using var harness = new WebhookHarness();
        var vendor = AddVendor(harness);

        await harness.SendImageAsync(VendorPhone, "https://media.example/marketplace-screenshot.jpg");

        var products = await harness.Context.VendorProducts
            .Where(p => p.VendorId == vendor.Id)
            .ToListAsync();

        Assert.NotEmpty(products);

        var reply = harness.LastMessageTo(VendorPhone);
        Assert.NotNull(reply);
        Assert.Contains("Magie WAZAP", reply);
        Assert.Contains("Mini-Boutique", reply);
    }
}
