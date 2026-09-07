using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Services;
using Wazap.Application.Configuration;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Webhook média : un livreur envoie la photo de sa pièce d'identité sur WhatsApp et le
/// système doit la stocker par le même chemin sécurisé que le téléversement admin.
/// Le format réel du payload média WhatChimp n'est pas documenté — ces tests verrouillent
/// les candidats tolérés ET les règles de garde : inconnu ignoré, kill-switch, échec de
/// téléchargement annoncé, dossier exclu refusé.
/// </summary>
public class WebhookMediaTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "wazap-webhook-media-" + Guid.NewGuid().ToString("N"));

    public WebhookMediaTests()
        => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // verrou Windows transitoire : le nettoyage du dossier temp est best-effort
        }
    }

    private static User NewRider(WebhookHarness harness, string phone = "+2250700000002")
    {
        var rider = new User("rider-media", "hash", UserRole.Rider, phone);
        harness.Context.Users.Add(rider);
        harness.Context.SaveChanges();
        return rider;
    }

    [Fact]
    public async Task KnownRider_Photo_StoresScan_AndConfirms()
    {
        var harness = new WebhookHarness();
        var rider = NewRider(harness);

        await harness.SendImageAsync(rider.PhoneNumber!, "https://media.example/cni.jpg");

        var identity = await harness.Context.RiderIdentities.FindAsync(rider.Id);
        Assert.NotNull(identity);
        Assert.Equal(RiderIdentityStatus.Pending, identity!.Status);
        Assert.NotNull(identity.ScanFileName);
        Assert.Equal("https://media.example/cni.jpg", identity.IdScanUrl);
        Assert.NotNull(identity.ScanReceivedAt);
        Assert.True(Directory.Exists(Path.Combine(harness.TempDir, "App_Data", "rider-scans")));
        Assert.Contains("reçue", harness.LastMessageTo(rider.PhoneNumber!));
    }

    [Fact]
    public async Task UnknownSender_Photo_IgnoredSilently()
    {
        var harness = new WebhookHarness();

        await harness.SendImageAsync("+2250799999999", "https://media.example/cni.jpg");

        Assert.Empty(harness.Context.RiderIdentities);
        Assert.DoesNotContain(harness.Sender.TextMessages, m => m.Phone == "+2250799999999");
    }

    [Fact]
    public async Task KillSwitchDisabled_Photo_SilentlyIgnored()
    {
        var harness = new WebhookHarness(scans: new RiderScansOptions
        {
            AllowUnencryptedStorage = true,
            WhatsAppInboundEnabled = false
        });
        var rider = NewRider(harness);

        await harness.SendImageAsync(rider.PhoneNumber!, "https://media.example/cni.jpg");

        Assert.Empty(harness.Context.RiderIdentities);
        Assert.DoesNotContain(harness.Sender.TextMessages, m => m.Phone == rider.PhoneNumber);
    }

    [Fact]
    public async Task DownloadFailure_RiderIsInformed()
    {
        var downloader = new WebhookHarness.FakeMediaDownloader { Fail = true };
        var harness = new WebhookHarness(mediaDownloader: downloader);
        var rider = NewRider(harness);

        await harness.SendImageAsync(rider.PhoneNumber!, "https://media.example/cni.jpg");

        Assert.Empty(harness.Context.RiderIdentities);
        Assert.Contains("pas pu récupérer", harness.LastMessageTo(rider.PhoneNumber!));
    }

    [Fact]
    public async Task BlacklistedRider_Photo_Refused()
    {
        var harness = new WebhookHarness();
        var rider = NewRider(harness);
        var service = new RiderService(harness.Context,
            new FakeWebHostEnvironment(harness.TempDir), harness.Sender,
            new RiderScansOptions { AllowUnencryptedStorage = true },
            NullLogger<RiderService>.Instance);
        await service.BlacklistRiderAsync(rider.Id, "Fraude", reviewerId: Guid.NewGuid());

        await harness.SendImageAsync(rider.PhoneNumber!, "https://media.example/cni.jpg");

        Assert.Contains("exclu", harness.LastMessageTo(rider.PhoneNumber!));
        Assert.Equal(RiderIdentityStatus.Blacklisted,
            (await harness.Context.RiderIdentities.FindAsync(rider.Id))!.Status);
    }
}
