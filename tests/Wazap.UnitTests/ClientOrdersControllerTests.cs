using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Controllers;
using Wazap.API.Services;
using Wazap.Application.Configuration;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Xunit;

namespace Wazap.UnitTests;

public class ClientOrdersControllerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "wazapclienttests-" + Guid.NewGuid().ToString("N"));
    private readonly ApplicationDbContext _db;
    private readonly RiderService _riderService;
    private readonly ClientOrdersController _controller;

    public ClientOrdersControllerTests()
    {
        Directory.CreateDirectory(_tempDir);
        _db = TestInfra.NewContext(Guid.NewGuid().ToString("N"));
        var scans = new RiderScansOptions { AllowUnencryptedStorage = true };
        _riderService = new RiderService(_db, new FakeWebHostEnvironment(_tempDir), new RecordingWhatsAppSender(),
            scans, NullLogger<RiderService>.Instance);
        _controller = new ClientOrdersController(_db, null!, null!, _riderService);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // Nettoyage temp best-effort
        }
    }

    [Fact]
    public async Task Get_OrderNotFound_ReturnsNotFound()
    {
        var result = await _controller.Get(Guid.NewGuid());
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Get_ExistingOrder_ReturnsEnrichedDetailsWithDeliveryCodeAndTimestamps()
    {
        var vendor = new User("vendor_test", "hash", UserRole.Vendor, "+2250700000001");
        _db.Users.Add(vendor);

        var order = new Order("Client A", "+2250500000001", "+2250700000001", "Robe wax", 15000m);
        order.LinkVendor(vendor.Id);
        order.EnsureDeliveryCode();
        order.ConfirmByVendor();
        order.AwaitRiderAcceptance();
        order.AssignRider("+2250100000001");
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var actionResult = await _controller.Get(order.Id);
        var okResult = Assert.IsType<OkObjectResult>(actionResult);

        dynamic val = okResult.Value!;
        Assert.Equal(order.Id, (Guid)val.id);
        Assert.Equal("vendor_test", (string)val.vendorName);
        Assert.Equal(15000m, (decimal)val.amount);
        Assert.Equal(order.DeliveryCode, (string)val.deliveryCode);
        Assert.NotNull(val.timestamps);
        Assert.NotNull(val.timestamps.vendorConfirmedAt);
        Assert.NotNull(val.timestamps.riderAssignedAt);
    }

    [Fact]
    public async Task SubmitRating_WhenScoreInvalid_ReturnsBadRequest()
    {
        var orderId = Guid.NewGuid();
        var result = await _controller.SubmitRating(orderId, new SubmitClientRatingRequest(0, "Trop bas"));
        Assert.IsType<BadRequestObjectResult>(result);

        var result6 = await _controller.SubmitRating(orderId, new SubmitClientRatingRequest(6, "Trop haut"));
        Assert.IsType<BadRequestObjectResult>(result6);
    }

    [Fact]
    public async Task SubmitRating_WhenOrderNotDelivered_ReturnsBadRequest()
    {
        var order = new Order("Client B", "+2250500000002", "+2250700000001", "Colis", 5000m);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var result = await _controller.SubmitRating(order.Id, new SubmitClientRatingRequest(5, "Super"));
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SubmitRating_WhenDeliveredWithRider_CreatesRatingAndReturnsSuccess()
    {
        var rider = new User("rider_jean", "hash", UserRole.Rider, "+2250100000002");
        _db.Users.Add(rider);

        var order = new Order("Client C", "+2250500000003", "+2250700000001", "Chaussures", 20000m);
        order.ConfirmByVendor();
        order.AwaitRiderAcceptance();
        order.AssignRider(rider.PhoneNumber!);
        order.LinkRider(rider.Id);
        order.MarkReadyForPickup();
        order.MarkPickedUp();
        order.MarkInTransit();
        order.MarkDelivered();
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var result = await _controller.SubmitRating(order.Id, new SubmitClientRatingRequest(5, "Livreur ultra ponctuel et poli !"));
        var okResult = Assert.IsType<OkObjectResult>(result);
        dynamic val = okResult.Value!;
        Assert.True((bool)val.success);
        Assert.Equal(5, (int)val.score);

        // Deuxième tentative -> Conflit (déjà noté)
        var duplicateResult = await _controller.SubmitRating(order.Id, new SubmitClientRatingRequest(4, "Autre note"));
        Assert.IsType<ConflictObjectResult>(duplicateResult);
    }

    [Fact]
    public async Task GetProofPhoto_WhenNoPhoto_ReturnsNotFound()
    {
        var order = new Order("Client D", "+2250500000004", "+2250700000001", "Colis sans photo", 5000m);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var result = await _controller.GetProofPhoto(order.Id);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetDeliveryQr_ReturnsPngFileBytes()
    {
        var order = new Order("Client E", "+2250500000005", "+2250700000001", "Colis QR", 8000m);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var result = await _controller.GetDeliveryQr(order.Id);
        var fileResult = Assert.IsType<FileContentResult>(result);

        Assert.Equal("image/png", fileResult.ContentType);
        Assert.NotEmpty(fileResult.FileContents);
        Assert.NotNull(order.DeliveryCode);
    }

    [Fact]
    public async Task StartDelivery_TransitionsOrderToInTransit()
    {
        var rider = new User("rider_express", "hash", UserRole.Rider, "+2250100000009");
        _db.Users.Add(rider);

        var order = new Order("Client F", "+2250500000006", "+2250700000001", "Colis Express", 9000m);
        order.ConfirmByVendor();
        order.AwaitRiderAcceptance();
        order.AssignRider(rider.PhoneNumber!);
        order.LinkRider(rider.Id);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        var result = await _controller.StartDelivery(order.Id);
        var okResult = Assert.IsType<OkObjectResult>(result);

        dynamic val = okResult.Value!;
        Assert.Equal("InTransit", (string)val.status);
        Assert.Equal(OrderStatus.InTransit, order.Status);
    }

    [Fact]
    public async Task ValidateDelivery_WhenCodeMatches_MarksDelivered()
    {
        var rider = new User("rider_speedy", "hash", UserRole.Rider, "+2250100000010");
        _db.Users.Add(rider);

        var order = new Order("Client G", "+2250500000007", "+2250700000001", "Colis Validé", 12000m);
        order.ConfirmByVendor();
        order.AwaitRiderAcceptance();
        order.AssignRider(rider.PhoneNumber!);
        order.LinkRider(rider.Id);
        var pin = order.EnsureDeliveryCode();
        order.MarkReadyForPickup();
        order.MarkPickedUp();
        order.MarkInTransit();
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        // 1. Mauvais code -> BadRequest
        var badResult = await _controller.ValidateDelivery(order.Id, new ValidateDeliveryRequest("9999"));
        Assert.IsType<BadRequestObjectResult>(badResult);

        // 2. Bon code -> Succès et Delivered
        var goodResult = await _controller.ValidateDelivery(order.Id, new ValidateDeliveryRequest(pin));
        var okResult = Assert.IsType<OkObjectResult>(goodResult);
        dynamic val = okResult.Value!;
        Assert.Equal("Delivered", (string)val.status);
        Assert.Equal(OrderStatus.Delivered, order.Status);
    }
}

