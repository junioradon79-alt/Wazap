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
}
