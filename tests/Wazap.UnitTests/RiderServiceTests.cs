using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Services;
using Wazap.Application.Configuration;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Tests du service livreur (RiderService) — certification « Garantie Colis Sûr » :
/// vérification, refus + reprise via nouveau scan, exclusion, stockage/lecture du scan.
/// </summary>
public class RiderServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "wazaptests-" + Guid.NewGuid().ToString("N"));

    public RiderServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
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
            // verrou Windows transitoire : le nettoyage du dossier temp est best-effort
        }
    }

    /// <summary>
    /// Par défaut le stockage en clair est autorisé : les tests historiques éprouvent la
    /// mécanique de stockage, pas le garde-fou de configuration (couvert séparément).
    /// </summary>
    private RiderService CreateService(TestDbContext db, RecordingWhatsAppSender sender,
        string? encryptionKey = null, bool allowUnencrypted = true)
    {
        var scans = new RiderScansOptions
        {
            EncryptionKey = encryptionKey,
            AllowUnencryptedStorage = allowUnencrypted
        };
        return new RiderService(db.Context, new FakeWebHostEnvironment(_tempDir), sender,
            scans, NullLogger<RiderService>.Instance);
    }

    private static User NewRider(TestDbContext db, string username = "rider")
    {
        var rider = new User(username, "hash", UserRole.Rider, "+2250700000002");
        db.Context.Users.Add(rider);
        return rider;
    }

    [Fact]
    public async Task Verify_CreatesIdentity_AndSetsVerified()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var rider = NewRider(db);
        await context.SaveChangesAsync();

        var sender = new RecordingWhatsAppSender();
        var service = CreateService(db, sender);

        await service.VerifyRiderAsync(rider.Id, "Livreur Un", "CI123456", "Moto rouge", reviewerId: Guid.NewGuid());

        var identity = await context.RiderIdentities.FindAsync(rider.Id);
        Assert.NotNull(identity);
        Assert.Equal(RiderIdentityStatus.Verified, identity!.Status);
        Assert.Equal("Livreur Un", identity.FullName);
        Assert.Contains(sender.TextMessages, m => m.Phone == rider.PhoneNumber && m.Message.Contains("certifié"));
    }

    [Fact]
    public async Task Reject_ThenNewScan_ReopensPending()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var rider = NewRider(db);
        await context.SaveChangesAsync();

        var sender = new RecordingWhatsAppSender();
        var service = CreateService(db, sender);

        await service.RejectRiderAsync(rider.Id, "Scan illisible", reviewerId: Guid.NewGuid());
        var identity = await context.RiderIdentities.FindAsync(rider.Id);
        Assert.Equal(RiderIdentityStatus.Rejected, identity!.Status);

        // Nouveau scan (upload admin) → le dossier revient « à vérifier ».
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        await service.StoreScanAsync(rider.Id, stream, "cni.png");

        var reopened = await context.RiderIdentities.FindAsync(rider.Id);
        Assert.Equal(RiderIdentityStatus.Pending, reopened!.Status);
        Assert.NotNull(reopened.ScanFileName);
        Assert.NotNull(reopened.ScanReceivedAt);
    }

    [Fact]
    public async Task StoreScan_ThenGetStoredScanPath_ReturnsExistingFile()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var rider = NewRider(db);
        await context.SaveChangesAsync();

        var sender = new RecordingWhatsAppSender();
        var service = CreateService(db, sender);

        using var stream = new MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        await service.StoreScanAsync(rider.Id, stream, "cni.png");

        var path = await service.GetStoredScanPathAsync(rider.Id);
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.Contains(rider.Id.ToString("N"), path);
    }

    [Fact]
    public async Task StoreScan_WithUnsupportedExtension_Throws()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var rider = NewRider(db);
        await context.SaveChangesAsync();

        var sender = new RecordingWhatsAppSender();
        var service = CreateService(db, sender);

        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StoreScanAsync(rider.Id, stream, "cni.exe"));
    }

    [Fact]
    public async Task StoreScan_WithEncryptionKey_EncryptsAtRest_AndDecryptsOnRead()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var rider = NewRider(db);
        await context.SaveChangesAsync();

        var keyHex = Convert.ToHexString(Enumerable.Repeat((byte)0x42, 32).ToArray());
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(db, sender, encryptionKey: keyHex);
        var original = System.Text.Encoding.UTF8.GetBytes("scan-cni-binaire");

        using (var stream = new MemoryStream(original))
        {
            await service.StoreScanAsync(rider.Id, stream, "cni.png");
        }

        // Au repos : le fichier est chiffré (en-tête WZSCN1, pas les octets d'origine).
        var path = await service.GetStoredScanPathAsync(rider.Id);
        Assert.NotNull(path);
        var stored = await File.ReadAllBytesAsync(path!);
        Assert.False(stored.SequenceEqual(original));
        Assert.Equal("WZSCN1", System.Text.Encoding.ASCII.GetString(stored, 0, 6));

        // À la lecture : contenu d'origine restitué.
        var content = await service.GetScanContentAsync(rider.Id);
        Assert.NotNull(content);
        Assert.True(content!.Value.Content!.SequenceEqual(original));
    }

    [Fact]
    public async Task StoreScan_WithWrongKey_FailsDecryption_Gracefully()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var rider = NewRider(db);
        await context.SaveChangesAsync();

        var goodKey = Convert.ToHexString(Enumerable.Repeat((byte)0x42, 32).ToArray());
        var sender = new RecordingWhatsAppSender();

        using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("scan")))
        {
            await CreateService(db, sender, encryptionKey: goodKey)
                .StoreScanAsync(rider.Id, stream, "cni.png");
        }

        var wrongKey = Convert.ToHexString(Enumerable.Repeat((byte)0x11, 32).ToArray());
        var content = await CreateService(db, sender, encryptionKey: wrongKey)
            .GetScanContentAsync(rider.Id);

        Assert.NotNull(content);
        Assert.Null(content!.Value.Content); // échec authentification → lecture refusée (pas de fuite)
    }

    /// <summary>
    /// Scan reçu du livreur via WhatsApp : l'URL du média d'origine est conservée comme
    /// provenance (IdScanUrl), à côté du fichier local chiffré.
    /// </summary>
    [Fact]
    public async Task StoreScan_WithSourceUrl_KeepsProvenance()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var rider = NewRider(db);
        await context.SaveChangesAsync();

        var service = CreateService(db, new RecordingWhatsAppSender());
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("scan-whatsapp"));
        await service.StoreScanAsync(rider.Id, stream, "photo.jpg", "https://media.example/cni.jpg");

        var identity = await context.RiderIdentities.FindAsync(rider.Id);
        Assert.NotNull(identity);
        Assert.Equal("https://media.example/cni.jpg", identity!.IdScanUrl);
        Assert.NotNull(identity.ScanFileName);
        Assert.NotNull(identity.ScanReceivedAt);
    }

    /// <summary>
    /// Garde-fou RGPD : sans clé de chiffrement, le téléversement échoue franchement au lieu
    /// de retomber en silence sur l'écriture en clair. Le disque doit rester intact — le
    /// refus intervient AVANT la moindre création de dossier ou de fichier.
    /// </summary>
    [Fact]
    public async Task StoreScan_WithoutKey_IsRefused_AndLeavesNothingOnDisk()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var rider = NewRider(db);
        await context.SaveChangesAsync();

        var service = CreateService(db, new RecordingWhatsAppSender(), allowUnencrypted: false);

        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StoreScanAsync(rider.Id, stream, "cni.png"));

        Assert.Contains("Téléversement refusé", ex.Message);

        // Aucune trace : ni fichier, ni dossier, ni référence en base.
        Assert.False(Directory.Exists(Path.Combine(_tempDir, "App_Data", "rider-scans")));
        Assert.Null(await context.RiderIdentities.FindAsync(rider.Id));
    }

    /// <summary>Une clé illisible est traitée comme une absence de clé : refus, pas de repli.</summary>
    [Fact]
    public async Task StoreScan_WithUnreadableKey_IsRefused()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var rider = NewRider(db);
        await context.SaveChangesAsync();

        var service = CreateService(db, new RecordingWhatsAppSender(),
            encryptionKey: "ceci-n-est-pas-une-cle", allowUnencrypted: false);

        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StoreScanAsync(rider.Id, stream, "cni.png"));

        Assert.False(Directory.Exists(Path.Combine(_tempDir, "App_Data", "rider-scans")));
    }

    /// <summary>
    /// Une clé de longueur non conforme (ici 8 octets) est refusée : elle ne doit pas être
    /// « rattrapée » silencieusement, sans quoi on croirait chiffrer sans le faire.
    /// </summary>
    [Fact]
    public async Task StoreScan_WithWrongLengthKey_IsRefused()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var rider = NewRider(db);
        await context.SaveChangesAsync();

        var shortKey = Convert.ToBase64String(new byte[8]);
        var service = CreateService(db, new RecordingWhatsAppSender(),
            encryptionKey: shortKey, allowUnencrypted: false);

        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StoreScanAsync(rider.Id, stream, "cni.png"));
    }

    /// <summary>
    /// L'écriture en clair reste possible, mais seulement sur autorisation explicite :
    /// une décision écrite dans la configuration, jamais un défaut.
    /// </summary>
    [Fact]
    public async Task StoreScan_WithoutKey_ButExplicitlyAllowed_StoresInClear()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var rider = NewRider(db);
        await context.SaveChangesAsync();

        var service = CreateService(db, new RecordingWhatsAppSender(), allowUnencrypted: true);
        var original = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        using (var stream = new MemoryStream(original))
        {
            await service.StoreScanAsync(rider.Id, stream, "cni.png");
        }

        var path = await service.GetStoredScanPathAsync(rider.Id);
        Assert.NotNull(path);
        Assert.True((await File.ReadAllBytesAsync(path!)).SequenceEqual(original));
    }

    [Fact]
    public async Task Blacklist_SetsUnavailable_AndBlacklisted()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var rider = NewRider(db);
        rider.SetAvailability(true);
        await context.SaveChangesAsync();

        var sender = new RecordingWhatsAppSender();
        var service = CreateService(db, sender);

        await service.BlacklistRiderAsync(rider.Id, "Fraude confirmée", reviewerId: Guid.NewGuid());

        var identity = await context.RiderIdentities.FindAsync(rider.Id);
        Assert.Equal(RiderIdentityStatus.Blacklisted, identity!.Status);
        Assert.False(rider.IsAvailable);
        Assert.Contains(sender.TextMessages, m => m.Phone == rider.PhoneNumber && m.Message.Contains("suspendu"));
    }

    [Fact]
    public async Task Blacklist_WithoutReason_Throws()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var rider = NewRider(db);
        await context.SaveChangesAsync();

        var sender = new RecordingWhatsAppSender();
        var service = CreateService(db, sender);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.BlacklistRiderAsync(rider.Id, "   ", reviewerId: Guid.NewGuid()));
    }
}
