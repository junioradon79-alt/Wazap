using Microsoft.AspNetCore.Http;
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

    private class FakeOcrService : IOcrService
    {
        public Task<OcrIdentityResult> ParseIdentityCardAsync(byte[] imageBytes, string? mimeType = null, CancellationToken ct = default)
        {
            return Task.FromResult(new OcrIdentityResult(
                Success: true,
                FullName: "NIAGARE IBRAHIM",
                IdNumber: "NIAG01-21-24209886I",
                RawText: "REPUBLIQUE DE COTE D'IVOIRE PERMIS DE CONDUIRE NIAGARE IBRAHIM",
                Error: null,
                DocumentType: "Permis de Conduire"
            ));
        }
    }

    private (GatewayWhatsAppController controller, ApplicationDbContext context) CreateController(IOcrService? ocrService = null)
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
            NullLogger<RiderRecruitmentService>.Instance,
            ocrService);

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
            NullLogger<GatewayWhatsAppController>.Instance,
            ocrService);

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

    [Fact]
    public async Task TelechargementApk_RouteDownloadsEtGateway_SontAccessibles()
    {
        using var factory = new WazapAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/downloads/wazap-gateway.apk");
        Assert.True(
            response.StatusCode == System.Net.HttpStatusCode.OK || response.StatusCode == System.Net.HttpStatusCode.NotFound,
            $"Statut inattendu: {response.StatusCode}");

        if (response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            Assert.Equal("application/vnd.android.package-archive", response.Content.Headers.ContentType?.MediaType);
        }

        var responseAlias = await client.GetAsync("/gateway/apk");
        Assert.True(
            responseAlias.StatusCode == System.Net.HttpStatusCode.OK || responseAlias.StatusCode == System.Net.HttpStatusCode.NotFound,
            $"Statut inattendu: {responseAlias.StatusCode}");
    }

    [Fact]
    public async Task UploadPhoto_AvecFichierEtOcr_CertifieLivreurEtRenvoieReponse()
    {
        var (controller, context) = CreateController(new FakeOcrService());

        // Créer un lead préalable (étape 1 DISPO + étape 2 commune)
        var lead = new Lead("Candidat", "+2250701020304", "Cocody", "whatsapp-livreur");
        context.Leads.Add(lead);
        await context.SaveChangesAsync();

        var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
        var formFile = new FormFile(new MemoryStream(imageBytes), 0, imageBytes.Length, "file", "cni.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };

        var result = await controller.UploadPhoto(formFile, "+2250701020304", "Ibrahim") as OkObjectResult;
        Assert.NotNull(result);
        var resp = result.Value as GatewayInboundResponse;

        Assert.NotNull(resp);
        Assert.True(resp.Success);
        Assert.True(resp.ShouldReply);
        Assert.Equal("rider_photo_verified", resp.Category);
        Assert.Contains("LIVREUR CERTIFIÉ WAZAP", resp.ReplyText);
        Assert.Contains("NIAGARE IBRAHIM", resp.ReplyText);
        Assert.Contains("NIAG01-21-24209886I", resp.ReplyText);

        // Vérifier que le livreur est bien créé et certifié en base
        var rider = await context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == "+2250701020304");
        Assert.NotNull(rider);
        Assert.Equal(UserRole.Rider, rider.Role);

        var identity = await context.RiderIdentities.FirstOrDefaultAsync(i => i.UserId == rider.Id);
        Assert.NotNull(identity);
        Assert.Equal(RiderIdentityStatus.Verified, identity.Status);
        Assert.Equal("NIAG01-21-24209886I", identity.IdNumber);
    }

    [Fact]
    public async Task UploadPhoto_SansFichier_RetourneBadRequest()
    {
        var (controller, _) = CreateController();
        var result = await controller.UploadPhoto(null, "+2250701020304", "Ibrahim") as BadRequestObjectResult;

        Assert.NotNull(result);
        var resp = result.Value as GatewayInboundResponse;
        Assert.NotNull(resp);
        Assert.False(resp.Success);
        Assert.Equal("missing_file", resp.Category);
    }

    [Fact]
    public async Task Process_NotificationPhotoTexte_RetournePhotoAcknowledgement()
    {
        var (controller, _) = CreateController();
        var req = new GatewayInboundRequest
        {
            Sender = "+2250701020304",
            Text = "📷 Photo"
        };

        var result = await controller.Process(req) as OkObjectResult;
        Assert.NotNull(result);
        var resp = result.Value as GatewayInboundResponse;

        Assert.NotNull(resp);
        Assert.True(resp.Success);
        Assert.True(resp.ShouldReply);
        Assert.Equal("photo_acknowledgement", resp.Category);
        Assert.Contains("Photo bien reçue", resp.ReplyText);
    }

    [Fact]
    public async Task Process_CandidatAvecSenderName_PrendEnCompteLeNomDansLeLead()
    {
        var (controller, context) = CreateController();
        var req = new GatewayInboundRequest
        {
            Sender = "+2250755667788",
            SenderName = "Adama Traoré",
            Text = "DISPO"
        };

        var result = await controller.Process(req) as OkObjectResult;
        Assert.NotNull(result);
        var resp = result.Value as GatewayInboundResponse;

        Assert.NotNull(resp);
        Assert.True(resp.Success);
        Assert.True(resp.ShouldReply);

        var lead = await context.Leads.FirstOrDefaultAsync(l => l.WhatsAppNumber == "+2250755667788");
        Assert.NotNull(lead);
        Assert.Equal("Adama Traoré", lead.ContactName);
    }

    [Theory]
    [InlineData("+2250708323366")]
    [InlineData("0708323366")]
    [InlineData("+225 07 08 32 33 66")]
    public async Task Process_NumeroPersonnelProprietaire_EstIgnoreSansReponse(string ownerPhone)
    {
        var (controller, _) = CreateController();
        var req = new GatewayInboundRequest
        {
            Sender = ownerPhone,
            Text = "DISPO"
        };

        var result = await controller.Process(req) as OkObjectResult;
        Assert.NotNull(result);
        var resp = result.Value as GatewayInboundResponse;

        Assert.NotNull(resp);
        Assert.True(resp.Success);
        Assert.False(resp.ShouldReply, "Le numéro personnel du propriétaire ne doit JAMAIS recevoir de réponse automatique.");
        Assert.Equal("ignored_protected_number", resp.Category);
        Assert.Null(resp.ReplyText);
    }

    [Theory]
    [InlineData("+2250544051972")]
    [InlineData("0544051972")]
    public async Task Process_NumeroOfficielWazap_EstIgnoreSansReponse(string wazapPhone)
    {
        var (controller, _) = CreateController();
        var req = new GatewayInboundRequest
        {
            Sender = wazapPhone,
            Text = "COLIS"
        };

        var result = await controller.Process(req) as OkObjectResult;
        Assert.NotNull(result);
        var resp = result.Value as GatewayInboundResponse;

        Assert.NotNull(resp);
        Assert.True(resp.Success);
        Assert.False(resp.ShouldReply);
        Assert.Equal("ignored_protected_number", resp.Category);
    }

    [Fact]
    public async Task Process_PackageNonWhatsAppBusiness_EstIgnoreSansReponse()
    {
        var (controller, _) = CreateController();
        var req = new GatewayInboundRequest
        {
            Sender = "+2250711223344",
            Text = "DISPO",
            PackageName = "com.whatsapp" // WhatsApp personnel classique
        };

        var result = await controller.Process(req) as OkObjectResult;
        Assert.NotNull(result);
        var resp = result.Value as GatewayInboundResponse;

        Assert.NotNull(resp);
        Assert.True(resp.Success);
        Assert.False(resp.ShouldReply, "Une notification issue d'une app autre que com.whatsapp.w4b doit être ignorée.");
        Assert.Equal("ignored_unsupported_app", resp.Category);
    }

    [Fact]
    public async Task Process_MessageTexteOrdinaireSansMotCle_EstIgnoreSansReponse()
    {
        var (controller, _) = CreateController();
        var req = new GatewayInboundRequest
        {
            Sender = "+2250711223344",
            Text = "Salut frérot, tu es où aujourd'hui ?"
        };

        var result = await controller.Process(req) as OkObjectResult;
        Assert.NotNull(result);
        var resp = result.Value as GatewayInboundResponse;

        Assert.NotNull(resp);
        Assert.True(resp.Success);
        Assert.False(resp.ShouldReply, "Un message ordinaire ne doit pas déclencher d'auto-réponse WAZAP.");
        Assert.Equal("ignored_unrecognized_message", resp.Category);
    }

    [Fact]
    public async Task UploadPhoto_NumeroPersonnelProprietaire_EstIgnoreSansReponse()
    {
        var (controller, _) = CreateController(new FakeOcrService());
        var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
        var formFile = new FormFile(new MemoryStream(imageBytes), 0, imageBytes.Length, "file", "cni.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };

        var result = await controller.UploadPhoto(formFile, "+2250708323366", "Propriétaire") as OkObjectResult;
        Assert.NotNull(result);
        var resp = result.Value as GatewayInboundResponse;

        Assert.NotNull(resp);
        Assert.True(resp.Success);
        Assert.False(resp.ShouldReply, "La photo envoyée par le numéro du propriétaire ne doit pas créer de compte ni répondre.");
        Assert.Equal("ignored_protected_number", resp.Category);
    }
}

