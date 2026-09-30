using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Controllers;
using Wazap.API.Services;
using Wazap.Application.Configuration;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Xunit;

namespace Wazap.UnitTests;

public class GatewayWhatsAppControllerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "wazap-gateway-" + Guid.NewGuid().ToString("N"));

    public GatewayWhatsAppControllerTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private (GatewayWhatsAppController controller, ApplicationDbContext context) CreateController()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("gateway-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = new ApplicationDbContext(options);

        var sender = new RecordingWhatsAppSender();
        var downloader = new WebhookHarness.FakeMediaDownloader();
        var passwordHasher = new FakePasswordHasher();
        var scans = new RiderScansOptions { AllowUnencryptedStorage = true };
        var config = new ConfigStub();

        var riderService = new RiderService(
            context,
            new FakeWebHostEnvironment(_tempDir),
            sender,
            scans,
            NullLogger<RiderService>.Instance);

        var recruitment = new RiderRecruitmentService(
            context,
            passwordHasher,
            riderService,
            sender,
            downloader,
            config,
            NullLogger<RiderRecruitmentService>.Instance);

        var riderProgram = new RiderProgramService(
            context,
            new RiderProgramOptions(),
            sender,
            NullLogger<RiderProgramService>.Instance);

        var controller = new GatewayWhatsAppController(
            context,
            recruitment,
            riderService,
            riderProgram,
            NullLogger<GatewayWhatsAppController>.Instance);

        return (controller, context);
    }

    [Fact]
    public void Ping_RetourneStatusOnline()
    {
        var (controller, _) = CreateController();
        var result = controller.Ping() as OkObjectResult;

        Assert.NotNull(result);
        Assert.Equal(200, result.StatusCode);
    }

    [Fact]
    public async Task Process_RequeteVide_RetourneBadRequest()
    {
        var (controller, _) = CreateController();
        var result = await controller.Process(new GatewayInboundRequest { Sender = "", Text = "" }) as BadRequestObjectResult;

        Assert.NotNull(result);
        var resp = result.Value as GatewayInboundResponse;
        Assert.NotNull(resp);
        Assert.False(resp.Success);
    }

    [Fact]
    public async Task Process_CandidatDispo_RetourneMessageRecrutementEtChoixCommune()
    {
        var (controller, context) = CreateController();
        var req = new GatewayInboundRequest
        {
            Sender = "+2250700112233",
            Text = "DISPO"
        };

        var result = await controller.Process(req) as OkObjectResult;
        Assert.NotNull(result);
        var resp = result.Value as GatewayInboundResponse;

        Assert.NotNull(resp);
        Assert.True(resp.Success);
        Assert.True(resp.ShouldReply);
        Assert.Equal("rider_recruitment", resp.Category);
        Assert.Contains("Bienvenue chez WAZAP Livreur", resp.ReplyText);
        Assert.Contains("Cocody", resp.ReplyText);

        // Vérification qu'un lead a été créé en base
        var lead = await context.Leads.FirstOrDefaultAsync(l => l.WhatsAppNumber == "+2250700112233");
        Assert.NotNull(lead);
    }

    [Fact]
    public async Task Process_MotCleColis_RetourneAccueilCommercant()
    {
        var (controller, _) = CreateController();
        var req = new GatewayInboundRequest
        {
            Sender = "+2250501020304",
            Text = "COLIS"
        };

        var result = await controller.Process(req) as OkObjectResult;
        Assert.NotNull(result);
        var resp = result.Value as GatewayInboundResponse;

        Assert.NotNull(resp);
        Assert.True(resp.Success);
        Assert.True(resp.ShouldReply);
        Assert.Equal("merchant_welcome", resp.Category);
        Assert.Contains("15 premières livraisons sont OFFERTES", resp.ReplyText);
    }

    [Fact]
    public async Task Process_MotCleTarifs_RetourneGrilleTarifaire()
    {
        var (controller, _) = CreateController();
        var req = new GatewayInboundRequest
        {
            Sender = "+2250501020304",
            Text = "TARIFS"
        };

        var result = await controller.Process(req) as OkObjectResult;
        Assert.NotNull(result);
        var resp = result.Value as GatewayInboundResponse;

        Assert.NotNull(resp);
        Assert.True(resp.Success);
        Assert.True(resp.ShouldReply);
        Assert.Equal("pricing_info", resp.Category);
        Assert.Contains("1 000 FCFA net", resp.ReplyText);
    }

    [Fact]
    public async Task Process_LivreurExistantDispo_ActiveDisponibilite()
    {
        var (controller, context) = CreateController();

        // Créer un livreur existant
        var rider = new User("Bakary Livreur", "hash", UserRole.Rider, "+2250708091011");
        rider.SetZone("Marcory");
        rider.SetAvailability(false);
        context.Users.Add(rider);
        await context.SaveChangesAsync();

        var req = new GatewayInboundRequest
        {
            Sender = "+2250708091011",
            Text = "DISPO"
        };

        var result = await controller.Process(req) as OkObjectResult;
        Assert.NotNull(result);
        var resp = result.Value as GatewayInboundResponse;

        Assert.NotNull(resp);
        Assert.True(resp.Success);
        Assert.True(resp.ShouldReply);
        Assert.Equal("rider_status", resp.Category);
        Assert.Contains("EN LIGNE à Marcory", resp.ReplyText);

        // Vérification de la disponibilité mise à jour en base
        var updated = await context.Users.FindAsync(rider.Id);
        Assert.NotNull(updated);
        Assert.True(updated.IsAvailable);
    }

    [Fact]
    public async Task Process_LivreurExistantIndispo_DesactiveDisponibilite()
    {
        var (controller, context) = CreateController();

        var rider = new User("Koffi Livreur", "hash", UserRole.Rider, "+2250506070809");
        rider.SetAvailability(true);
        context.Users.Add(rider);
        await context.SaveChangesAsync();

        var req = new GatewayInboundRequest
        {
            Sender = "+2250506070809",
            Text = "INDISPO"
        };

        var result = await controller.Process(req) as OkObjectResult;
        Assert.NotNull(result);
        var resp = result.Value as GatewayInboundResponse;

        Assert.NotNull(resp);
        Assert.True(resp.Success);
        Assert.True(resp.ShouldReply);
        Assert.Equal("rider_status", resp.Category);
        Assert.Contains("HORS LIGNE", resp.ReplyText);

        var updated = await context.Users.FindAsync(rider.Id);
        Assert.NotNull(updated);
        Assert.False(updated.IsAvailable);
    }
}
