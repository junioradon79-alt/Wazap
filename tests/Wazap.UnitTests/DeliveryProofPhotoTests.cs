using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Services;
using Wazap.Application.Configuration;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Preuve photo de livraison (chantier C) : un livreur avec une course en cours (assignée
/// ou en transit) envoie la photo du colis sur WhatsApp → stockage chiffré, provenance
/// gardée, consultation admin. Sans course en cours, la photo reste un scan CNI.
/// </summary>
public class DeliveryProofPhotoTests
{
    private const string Phone = "+2250700000005";

    private static async Task<(WebhookHarness Harness, User Rider, Order Order)> PrepareAsync(OrderStatus status)
    {
        var harness = new WebhookHarness();
        var rider = new User("rider-proof", "hash", UserRole.Rider, Phone);
        harness.Context.Users.Add(rider);

        var order = new Order("Client A", "+2250100000001", "+2250500000002", "Colis test", 5000m);
        order.ConfirmByVendor();
        order.AwaitRiderAcceptance();
        order.AssignRider(Phone);
        order.LinkRider(rider.Id);
        if (status is OrderStatus.InTransit or OrderStatus.Delivered)
        {
            order.MarkReadyForPickup();
            order.MarkPickedUp();
            order.MarkInTransit();
        }
        if (status == OrderStatus.Delivered)
            order.MarkDelivered();

        harness.Context.Orders.Add(order);
        await harness.Context.SaveChangesAsync();
        return (harness, rider, order);
    }

    [Fact]
    public async Task RiderInTransit_Photo_StoredAsProof_NoIdentityCreated()
    {
        var (harness, _, order) = await PrepareAsync(OrderStatus.InTransit);

        await harness.SendImageAsync(Phone, "https://media.example/colis.jpg");

        var reloaded = await harness.Context.Orders.AsNoTracking().SingleAsync();
        Assert.NotNull(reloaded.DeliveryProofPhotoFileName);
        Assert.Equal("https://media.example/colis.jpg", reloaded.DeliveryProofPhotoSourceUrl);
        Assert.NotNull(reloaded.DeliveryProofPhotoReceivedAt);
        Assert.True(File.Exists(Path.Combine(harness.TempDir, "App_Data", "delivery-proof-photos",
            $"{order.Id:N}.jpg")));
        Assert.Contains("Photo du colis enregistrée", harness.LastMessageTo(Phone));
        Assert.False(await harness.Context.RiderIdentities.AnyAsync());
    }

    [Fact]
    public async Task RiderAssigned_Photo_AlsoAccepted()
    {
        var (harness, _, order) = await PrepareAsync(OrderStatus.RiderAssigned);

        await harness.SendImageAsync(Phone, "https://media.example/colis-retrait.jpg");

        var reloaded = await harness.Context.Orders.AsNoTracking().SingleAsync();
        Assert.NotNull(reloaded.DeliveryProofPhotoFileName);
        Assert.Equal("https://media.example/colis-retrait.jpg", reloaded.DeliveryProofPhotoSourceUrl);
        Assert.NotNull(reloaded.DeliveryProofPhotoReceivedAt);
    }

    [Fact]
    public async Task DeliveredOrder_ProofPhoto_RejectedByDomain()
    {
        var (harness, rider, order) = await PrepareAsync(OrderStatus.Delivered);
        var service = NewService(harness, new RiderScansOptions { AllowUnencryptedStorage = true });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.StoreDeliveryProofPhotoAsync(rider.Id, order.Id, new MemoryStream([1, 2, 3]), "colis.jpg", null));

        Assert.Contains("aucune course en cours", ex.Message);
        Assert.Null((await harness.Context.Orders.AsNoTracking().SingleAsync()).DeliveryProofPhotoFileName);
    }

    [Fact]
    public async Task RiderWithoutActiveOrder_Photo_RemainsCniScan()
    {
        var harness = new WebhookHarness();
        var rider = new User("rider-cni", "hash", UserRole.Rider, Phone);
        harness.Context.Users.Add(rider);
        await harness.Context.SaveChangesAsync();

        await harness.SendImageAsync(Phone, "https://media.example/cni.jpg");

        var identity = await harness.Context.RiderIdentities.SingleAsync();
        Assert.Equal("whatsapp", identity.ConsentMethod);
        Assert.False(await harness.Context.Orders.AnyAsync(o => o.DeliveryProofPhotoFileName != null));
        Assert.Contains("Photo de votre pièce", harness.LastMessageTo(Phone));
    }

    [Fact]
    public async Task DownloadFailure_ProofPhoto_ErrorReply()
    {
        var downloader = new WebhookHarness.FakeMediaDownloader { Fail = true };
        var harness = new WebhookHarness(mediaDownloader: downloader);
        var rider = new User("rider-proof", "hash", UserRole.Rider, Phone);
        harness.Context.Users.Add(rider);
        var order = new Order("Client A", "+2250100000001", "+2250500000002", "Colis", 5000m);
        order.ConfirmByVendor();
        order.AwaitRiderAcceptance();
        order.AssignRider(Phone);
        order.LinkRider(rider.Id);
        order.MarkReadyForPickup();
        order.MarkPickedUp();
        order.MarkInTransit();
        harness.Context.Orders.Add(order);
        await harness.Context.SaveChangesAsync();

        await harness.SendImageAsync(Phone, "https://media.example/colis.jpg");

        Assert.Contains("Impossible de récupérer", harness.LastMessageTo(Phone));
        Assert.Null((await harness.Context.Orders.AsNoTracking().SingleAsync()).DeliveryProofPhotoFileName);
    }

    [Fact]
    public async Task StoreDeliveryProofPhoto_WrongRider_Throws()
    {
        var (harness, _, order) = await PrepareAsync(OrderStatus.InTransit);
        var otherRider = new User("autre-rider", "hash", UserRole.Rider, "+2250700000006");
        harness.Context.Users.Add(otherRider);
        await harness.Context.SaveChangesAsync();

        var service = NewService(harness, new RiderScansOptions { AllowUnencryptedStorage = true });
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.StoreDeliveryProofPhotoAsync(otherRider.Id, order.Id, new MemoryStream([1, 2, 3]), "colis.jpg", null));

        Assert.Contains("Course introuvable", ex.Message);
    }

    [Fact]
    public async Task ProofPhoto_Roundtrip_EncryptedStorage()
    {
        var (harness, rider, order) = await PrepareAsync(OrderStatus.InTransit);
        var keyHex = Convert.ToHexString(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
        var service = NewService(harness, new RiderScansOptions { EncryptionKey = keyHex });

        var content = new byte[] { 10, 20, 30, 40, 50 };
        await service.StoreDeliveryProofPhotoAsync(rider.Id, order.Id, new MemoryStream(content), "colis.png", null);

        // Fichier chiffré : les octets sur disque ne sont pas ceux d'origine.
        var path = Path.Combine(harness.TempDir, "App_Data", "delivery-proof-photos", $"{order.Id:N}.png");
        var onDisk = await File.ReadAllBytesAsync(path);
        Assert.False(onDisk.AsSpan().StartsWith(content));

        var photo = await service.GetDeliveryProofPhotoAsync(order.Id);
        Assert.NotNull(photo);
        Assert.Equal(content, photo!.Value.Content);
        Assert.Equal($"{order.Id:N}.png", photo.Value.FileName);
    }

    private static RiderService NewService(WebhookHarness harness, RiderScansOptions scans)
        => new(harness.Context, new FakeWebHostEnvironment(harness.TempDir), harness.Sender,
            scans, NullLogger<RiderService>.Instance);
}
