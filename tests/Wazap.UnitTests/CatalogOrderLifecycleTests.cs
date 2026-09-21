using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Controllers;
using Wazap.API.Services;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Xunit;

namespace Wazap.UnitTests;

public class CatalogOrderLifecycleTests
{
    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid? Id { get; set; }
        public string? Username { get; set; }
        public UserRole? Role { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Zone { get; set; }
        public bool IsAuthenticated => Id.HasValue;
    }

    [Fact]
    public async Task VendorsController_ConfirmOrder_TransitionsToVendorConfirmed()
    {
        var db = TestInfra.NewContext(Guid.NewGuid().ToString("N"));
        var vendor = new User("vendor_alpha", "hash", UserRole.Vendor, "+2250700000021");
        db.Users.Add(vendor);

        var order = new Order("Client Test", "+2250500000021", vendor.PhoneNumber!, "Articles", 15000m);
        order.LinkVendor(vendor.Id);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var currentUser = new TestCurrentUser
        {
            Id = vendor.Id,
            Username = vendor.Username,
            Role = UserRole.Vendor,
            PhoneNumber = vendor.PhoneNumber
        };

        var controller = new VendorsController(
            null!, null!, null!, null!, currentUser, db,
            deliveryOfferService: null, whatsApp: null);

        var result = await controller.ConfirmOrder(order.Id);
        var okResult = Assert.IsType<OkObjectResult>(result);

        dynamic val = okResult.Value!;
        Assert.Equal("VendorConfirmed", (string)val.status);
        Assert.Equal(OrderStatus.VendorConfirmed, order.Status);
        Assert.NotNull(order.VendorConfirmedAt);
    }

    [Theory]
    [InlineData("RECU")]
    [InlineData("EN ROUTE")]
    [InlineData("PARTI")]
    [InlineData("DECLENCHER")]
    [InlineData("LIVRAISON DECLENCHEE")]
    public async Task RiderDeliveryCommands_PickupAliases_TransitionOrderToInTransit(string command)
    {
        var db = TestInfra.NewContext(Guid.NewGuid().ToString("N"));
        var rider = new User("rider_bob", "hash", UserRole.Rider, "+2250700000031");
        db.Users.Add(rider);

        var order = new Order("Client Awa", "+2250500000031", "+2250700000022", "Robe", 10000m);
        order.ConfirmByVendor();
        order.AwaitRiderAcceptance();
        order.AssignRider(rider.PhoneNumber!);
        order.LinkRider(rider.Id);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var sender = new RecordingWhatsAppSender();
        var orchestrator = new WhatsAppOrchestrationService(sender, new WhatsAppOptions(),
            NullLogger<WhatsAppOrchestrationService>.Instance);
        var program = new RiderProgramService(db, new RiderProgramOptions(), sender,
            NullLogger<RiderProgramService>.Instance);

        var commands = new RiderDeliveryCommands(
            db, new DeliveryProofOptions(), orchestrator, program,
            NullLogger<RiderDeliveryCommands>.Instance);

        string lastReply = "";
        await commands.HandleAsync(rider, command, (u, msg) =>
        {
            lastReply = msg;
            return Task.CompletedTask;
        });

        Assert.Equal(OrderStatus.InTransit, order.Status);
        Assert.Contains("récupéré", lastReply);
    }

    [Fact]
    public void VendorProduct_SetAvailability_UpdatesStockAndDisplayText()
    {
        var vendorId = Guid.NewGuid();
        var product = new VendorProduct(
            vendorId,
            "Attiéké Poisson",
            "Portion individuelle",
            2500m,
            "🐟",
            isAvailable: true,
            imageUrl: "https://wazap.ci/img/poisson.jpg");

        Assert.True(product.IsAvailable);
        Assert.Equal("https://wazap.ci/img/poisson.jpg", product.ImageUrl);
        Assert.StartsWith("🐟 Attiéké Poisson — ", product.DisplayText);
        Assert.EndsWith("FCFA", product.DisplayText);
        Assert.DoesNotContain("(Épuisé)", product.DisplayText);

        // Bascule vers épuisé
        product.SetAvailability(false);
        Assert.False(product.IsAvailable);
        Assert.Contains("(Épuisé)", product.DisplayText);

        // Remise en stock
        product.SetAvailability(true);
        Assert.True(product.IsAvailable);
        Assert.DoesNotContain("(Épuisé)", product.DisplayText);
    }

    [Fact]
    public async Task VendorProductService_SetAvailabilityAsync_TogglesStockInDatabase()
    {
        var db = TestInfra.NewContext(Guid.NewGuid().ToString("N"));
        var vendor = new User("chez_awa", "hash", UserRole.Vendor, "+2250700000088");
        db.Users.Add(vendor);

        var product = new VendorProduct(vendor.Id, "Poulet Braisé", "1/2 poulet", 4000m, "🍗");
        db.VendorProducts.Add(product);
        await db.SaveChangesAsync();

        var service = new VendorProductService(db, NullLogger<VendorProductService>.Instance);

        var toggled = await service.SetAvailabilityAsync(vendor.Id, product.Id, false);
        Assert.True(toggled);

        var updated = await service.GetProductAsync(vendor.Id, product.Id);
        Assert.NotNull(updated);
        Assert.False(updated!.IsAvailable);

        // Vendeur tiers ne peut pas modifier la disponibilité
        var rogueVendorId = Guid.NewGuid();
        var denied = await service.SetAvailabilityAsync(rogueVendorId, product.Id, true);
        Assert.False(denied);
    }
}
