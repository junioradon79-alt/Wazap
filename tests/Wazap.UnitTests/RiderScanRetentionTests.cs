using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Services;
using Wazap.Application.Configuration;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// RGPD : les scans de pièce d'identité ne se conservent pas indéfiniment. Après le délai
/// suivant la décision de certification, le fichier est supprimé du disque et sa référence
/// effacée — la décision, elle, reste tracée (elle fonde la « Garantie Colis Sûr »).
/// </summary>
public sealed class RiderScanRetentionTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "wazap-scan-retention-" + Guid.NewGuid().ToString("N"));

    public RiderScanRetentionTests()
        // FakeWebHostEnvironment exige une racine existante.
        => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private RiderService CreateService(TestDbContext db)
        => new(db.Context, new FakeWebHostEnvironment(_tempDir), new RecordingWhatsAppSender(),
            new RiderScansOptions { AllowUnencryptedStorage = true },
            NullLogger<RiderService>.Instance);

    /// <summary>Dossier certifié il y a <paramref name="reviewedDaysAgo"/> jours, avec un scan sur le disque.</summary>
    private async Task<(RiderIdentity Identity, string ScanPath)> CreateReviewedScanAsync(
        TestDbContext db, int reviewedDaysAgo, RiderIdentityStatus status = RiderIdentityStatus.Verified)
    {
        var rider = new User("rider" + Guid.NewGuid().ToString("N")[..6], "hash", UserRole.Rider, "+2250700000002");
        db.Context.Users.Add(rider);

        var identity = new RiderIdentity(rider.Id, "Ibrahim Traoré", "CI123456", "Yamaha rouge");
        identity.SubmitScanFile($"{rider.Id:N}.png");

        if (status == RiderIdentityStatus.Verified)
            identity.Verify("Ibrahim Traoré", "CI123456", "Yamaha rouge", Guid.NewGuid());
        else if (status == RiderIdentityStatus.Rejected)
            identity.Reject("dossier incomplet", Guid.NewGuid());

        // Recule la date de décision (propriété privée : on passe par la réflexion, les
        // entités n'exposent volontairement pas de setter public).
        typeof(RiderIdentity).GetProperty(nameof(RiderIdentity.ReviewedAt))!
            .SetValue(identity, DateTime.UtcNow.AddDays(-reviewedDaysAgo));

        db.Context.RiderIdentities.Add(identity);
        await db.Context.SaveChangesAsync();

        Directory.CreateDirectory(Path.Combine(_tempDir, "App_Data", "rider-scans"));
        var scanPath = Path.Combine(_tempDir, "App_Data", "rider-scans", identity.ScanFileName!);
        await File.WriteAllTextAsync(scanPath, "contenu du scan");

        return (identity, scanPath);
    }

    [Fact]
    public async Task ExpiredScan_IsDeletedFromDiskAndDereferenced()
    {
        var db = new TestDbContext();
        var (identity, scanPath) = await CreateReviewedScanAsync(db, reviewedDaysAgo: 120);

        var purged = await CreateService(db).PurgeExpiredScansAsync(retentionDays: 90);

        Assert.Equal(1, purged);
        Assert.False(File.Exists(scanPath));
        Assert.Null(identity.ScanFileName);
        Assert.Null(identity.IdScanUrl);
        Assert.NotNull(identity.ScanPurgedAt);
    }

    [Fact]
    public async Task PurgedScan_KeepsCertificationDecision()
    {
        var db = new TestDbContext();
        var (identity, _) = await CreateReviewedScanAsync(db, reviewedDaysAgo: 120);

        await CreateService(db).PurgeExpiredScansAsync(retentionDays: 90);

        // Ce qui fonde la Garantie Colis Sûr survit à l'effacement du document.
        Assert.Equal(RiderIdentityStatus.Verified, identity.Status);
        Assert.NotNull(identity.ReviewedAt);
        Assert.NotNull(identity.ScanReceivedAt);
    }

    [Fact]
    public async Task RecentScan_IsKept()
    {
        var db = new TestDbContext();
        var (identity, scanPath) = await CreateReviewedScanAsync(db, reviewedDaysAgo: 10);

        var purged = await CreateService(db).PurgeExpiredScansAsync(retentionDays: 90);

        Assert.Equal(0, purged);
        Assert.True(File.Exists(scanPath));
        Assert.NotNull(identity.ScanFileName);
    }

    [Fact]
    public async Task PendingDossier_IsNeverPurged()
    {
        var db = new TestDbContext();
        var rider = new User("rider", "hash", UserRole.Rider, "+2250700000002");
        db.Context.Users.Add(rider);

        var identity = new RiderIdentity(rider.Id);
        identity.SubmitScanFile($"{rider.Id:N}.png");
        db.Context.RiderIdentities.Add(identity);
        await db.Context.SaveChangesAsync();

        // Un dossier en attente n'a pas de ReviewedAt : son scan sert encore à l'examen.
        var purged = await CreateService(db).PurgeExpiredScansAsync(retentionDays: 1);

        Assert.Equal(0, purged);
        Assert.NotNull(identity.ScanFileName);
    }

    [Fact]
    public async Task RetentionDisabled_PurgesNothing()
    {
        var db = new TestDbContext();
        var (identity, scanPath) = await CreateReviewedScanAsync(db, reviewedDaysAgo: 3650);

        var purged = await CreateService(db).PurgeExpiredScansAsync(retentionDays: 0);

        Assert.Equal(0, purged);
        Assert.True(File.Exists(scanPath));
        Assert.NotNull(identity.ScanFileName);
    }

    [Fact]
    public async Task Purge_IsIdempotent()
    {
        var db = new TestDbContext();
        await CreateReviewedScanAsync(db, reviewedDaysAgo: 120);
        var service = CreateService(db);

        Assert.Equal(1, await service.PurgeExpiredScansAsync(retentionDays: 90));
        Assert.Equal(0, await service.PurgeExpiredScansAsync(retentionDays: 90));
    }

    [Fact]
    public async Task MissingFile_StillDereferences()
    {
        var db = new TestDbContext();
        var (identity, scanPath) = await CreateReviewedScanAsync(db, reviewedDaysAgo: 120);
        File.Delete(scanPath);

        // Fichier déjà disparu (restauration partielle, nettoyage manuel) : la référence
        // en base doit quand même être effacée.
        Assert.Equal(1, await CreateService(db).PurgeExpiredScansAsync(retentionDays: 90));
        Assert.Null(identity.ScanFileName);
    }
}
