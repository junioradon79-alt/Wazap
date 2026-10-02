using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Controllers;
using Wazap.API.Services;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Dtos;
using Wazap.Application.Exceptions;
using Wazap.Application.Services;
using Wazap.Domain.Configuration;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Tests de bout en bout du contrôleur RidersController pour le Cockpit Livreur :
/// - Chargement du tableau de bord (GET api/riders/dashboard)
/// - Bascule de disponibilité 1-tap (PUT api/riders/{id}/availability)
/// - Sélection de commune 1-tap (PUT api/riders/{id}/zone)
/// - Sécurité et étanchéité de propriété (EnsureOwnership)
/// - Accès administrateur délégué
/// </summary>
public class RidersControllerDashboardTests
{
    private sealed class StubCurrentUser : ICurrentUser
    {
        public Guid? Id { get; set; }
        public string? Username { get; set; }
        public UserRole? Role { get; set; }
    }

    private sealed class NoOpBuyPriorityValidator : AbstractValidator<BuyRiderPriorityRequest>
    {
    }

    private sealed class FakeOcrService : IOcrService
    {
        public Task<OcrIdentityResult> ParseIdentityCardAsync(byte[] imageBytes, string? mimeType = null, CancellationToken ct = default)
        {
            return Task.FromResult(new OcrIdentityResult(
                Success: true,
                FullName: "Bakary Touré",
                IdNumber: "CI-00123984",
                RawText: "REPUBLIQUE DE COTE D'IVOIRE CNI Bakary Touré",
                Error: null,
                DocumentType: "CNI"
            ));
        }
    }

    private sealed class Fixture : IDisposable
    {
        public ApplicationDbContext Context { get; }
        public StubCurrentUser CurrentUser { get; } = new();
        public RecordingWhatsAppSender Sender { get; } = new();
        public RidersController Controller { get; }
        public User Rider { get; }
        public User Vendor { get; }

        private readonly string _tempDir =
            Path.Combine(Path.GetTempPath(), "wazap-riderctrl-" + Guid.NewGuid().ToString("N"));

        public Fixture()
        {
            Directory.CreateDirectory(_tempDir);

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase("rider-ctrl-dash-" + Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            Context = new ApplicationDbContext(options);

            // Création d'un livreur
            Rider = new User("bakary_marcory", "hash:secret", UserRole.Rider, "+2250544051972");
            Rider.SetZone("Marcory");
            Rider.SetAvailability(true);
            Context.Users.Add(Rider);

            // Identité certifiée
            var identity = new RiderIdentity(Rider.Id, "Bakary Touré", "CI-00123984", "Honda Ace 125");
            identity.Verify("Bakary Touré", "CI-00123984", "Honda Ace 125", Guid.NewGuid());
            Context.RiderIdentities.Add(identity);

            // Commerçant pour les courses
            Vendor = new User("patisserie_fatou", "hash:secret", UserRole.Vendor, "+2250500000002");
            Vendor.SetZone("Marcory Boulevard VGE");
            Context.Users.Add(Vendor);

            Context.SaveChanges();

            CurrentUser.Id = Rider.Id;
            CurrentUser.Username = Rider.Username;
            CurrentUser.Role = UserRole.Rider;

            var scans = new RiderScansOptions { AllowUnencryptedStorage = true };
            var riderService = new RiderService(Context, new FakeWebHostEnvironment(_tempDir), Sender,
                scans, NullLogger<RiderService>.Instance);
            var riderProgram = new RiderProgramService(Context,
                new RiderProgramOptions { Enabled = true }, Sender,
                NullLogger<RiderProgramService>.Instance);

            var orchestrator = new WhatsAppOrchestrationService(Sender, new WhatsAppOptions(),
                NullLogger<WhatsAppOrchestrationService>.Instance);

            var priorityService = new RiderPriorityService(
                Context,
                new FakeClientPaymentGateway(),
                new List<RiderPriorityPackConfiguration>(),
                new RiderPriorityOptions(),
                orchestrator,
                NullLogger<RiderPriorityService>.Instance
            );

            var inMemoryConfig = new Dictionary<string, string?>
            {
                ["Prospect:TeamPhone"] = "2250544051972"
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

            var recruitment = new RiderRecruitmentService(Context, new FakePasswordHasher(), riderService,
                Sender, new WebhookHarness.FakeMediaDownloader(), config, NullLogger<RiderRecruitmentService>.Instance);

            Controller = new RidersController(
                riderService,
                riderProgram,
                priorityService,
                new NoOpBuyPriorityValidator(),
                CurrentUser,
                Context,
                recruitment,
                new FakeOcrService(),
                NullLogger<RidersController>.Instance
            );
        }

        public void Dispose()
        {
            Context.Dispose();
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* nettoyage */ }
        }
    }

    [Fact]
    public async Task GetDashboard_ReturnsCompleteRiderDashboard_WhenRiderExists()
    {
        using var f = new Fixture();

        // 1. Ajouter une course livrée aujourd'hui
        var orderDelivered = new Order("Awa Diallo", "+2250700000001", f.Vendor.PhoneNumber!, "Colis gâteau", 15000m, 1500m);
        orderDelivered.LinkVendor(f.Vendor.Id);
        orderDelivered.ConfirmByVendor();
        orderDelivered.AwaitRiderAcceptance();
        orderDelivered.AssignRider(f.Rider.PhoneNumber!);
        orderDelivered.LinkRider(f.Rider.Id);
        orderDelivered.MarkReadyForPickup();
        orderDelivered.MarkPickedUp();
        orderDelivered.MarkInTransit();
        orderDelivered.MarkDelivered();
        f.Context.Orders.Add(orderDelivered);

        // 2. Ajouter une note client 5 étoiles
        var rating = new RiderRating(orderDelivered.Id, f.Rider.Id, "+2250700000001", 5, "Livreur ponctuel et très courtois !");
        f.Context.RiderRatings.Add(rating);

        // 3. Ajouter une course active en transit
        var orderActive = new Order("Kouamé", "+2250700000003", f.Vendor.PhoneNumber!, "Colis express", 8000m, 1000m);
        orderActive.LinkVendor(f.Vendor.Id);
        orderActive.SetClientCoordinates(5.35, -4.01, "Treichville");
        orderActive.ConfirmByVendor();
        orderActive.AwaitRiderAcceptance();
        orderActive.AssignRider(f.Rider.PhoneNumber!);
        orderActive.LinkRider(f.Rider.Id);
        orderActive.MarkReadyForPickup();
        orderActive.MarkPickedUp();
        orderActive.MarkInTransit();
        f.Context.Orders.Add(orderActive);
        await f.Context.SaveChangesAsync();

        var result = await f.Controller.GetDashboard(null);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<RiderDashboardDto>(ok.Value);

        Assert.Equal(f.Rider.Id, dto.Id);
        Assert.Equal("Bakary Touré", dto.FullName);
        Assert.Equal("Marcory", dto.Zone);
        Assert.True(dto.IsAvailable);
        Assert.True(dto.IsVerified);
        Assert.Equal("Verified", dto.IdentityStatus);
        Assert.Equal("CI-00123984", dto.IdNumber);
        Assert.Equal(1, dto.DeliveriesToday);
        Assert.Equal(1500, dto.TotalEarningsEstimated);
        Assert.Equal(5.0, dto.RatingAverage);
        Assert.Equal(1, dto.RatingCount);

        // Vérification de la course active
        Assert.NotNull(dto.ActiveOrder);
        Assert.Equal("Kouamé", dto.ActiveOrder.ClientName);
        Assert.Equal("Treichville", dto.ActiveOrder.DeliveryAddress);
        Assert.Equal("InTransit", dto.ActiveOrder.Status);

        // Vérification du programme ambassadeur
        Assert.NotNull(dto.ProgramProgress);
        Assert.Contains("Redmi 15C", dto.ProgramProgress.RewardLabel);
    }

    [Fact]
    public async Task GetDashboard_ReturnsNotFound_WhenRiderDoesNotExist()
    {
        using var f = new Fixture();
        f.CurrentUser.Id = Guid.NewGuid();

        var result = await f.Controller.GetDashboard(null);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task SetAvailability_TogglesAvailability_1Tap()
    {
        using var f = new Fixture();

        // Bascule vers INDISPO (false)
        var result1 = await f.Controller.SetAvailability(f.Rider.Id, new SetAvailabilityRequest(false));
        Assert.IsType<NoContentResult>(result1);

        var riderDb = await f.Context.Users.FindAsync(f.Rider.Id);
        Assert.NotNull(riderDb);
        Assert.False(riderDb.IsAvailable);

        // Bascule retour vers DISPO (true)
        var result2 = await f.Controller.SetAvailability(f.Rider.Id, new SetAvailabilityRequest(true));
        Assert.IsType<NoContentResult>(result2);

        riderDb = await f.Context.Users.FindAsync(f.Rider.Id);
        Assert.NotNull(riderDb);
        Assert.True(riderDb.IsAvailable);
    }

    [Fact]
    public async Task SetZone_UpdatesRiderCommune_1Tap()
    {
        using var f = new Fixture();

        var result = await f.Controller.SetZone(f.Rider.Id, new SetZoneRequest("Cocody"));
        Assert.IsType<NoContentResult>(result);

        var riderDb = await f.Context.Users.FindAsync(f.Rider.Id);
        Assert.NotNull(riderDb);
        Assert.Equal("Cocody", riderDb.Zone);
    }

    [Fact]
    public async Task EnsureOwnership_ThrowsForbidden_WhenRiderTriesToModifyAnotherRider()
    {
        using var f = new Fixture();
        var otherRiderId = Guid.NewGuid();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            f.Controller.SetAvailability(otherRiderId, new SetAvailabilityRequest(false)));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            f.Controller.SetZone(otherRiderId, new SetZoneRequest("Yopougon")));
    }

    [Fact]
    public async Task Admin_CanAccessAnyRiderDashboard()
    {
        using var f = new Fixture();
        f.CurrentUser.Id = Guid.NewGuid();
        f.CurrentUser.Username = "superadmin";
        f.CurrentUser.Role = UserRole.Admin;

        var result = await f.Controller.GetDashboard(f.Rider.Id);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<RiderDashboardDto>(ok.Value);
        Assert.Equal(f.Rider.Id, dto.Id);
        Assert.Equal("Bakary Touré", dto.FullName);
    }
}
