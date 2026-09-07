using Wazap.Application.Configuration;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Résolution de la clé de chiffrement des scans d'identité et état de conformité exposé
/// par <c>/health/details</c>. L'enjeu : distinguer « protégé », « non protégé mais assumé »
/// et « non configuré » — trois situations que l'ancien code confondait en un seul silence.
/// </summary>
public sealed class RiderScansOptionsTests
{
    private static string HexKey(byte fill, int length = 32)
        => Convert.ToHexString(Enumerable.Repeat(fill, length).ToArray());

    [Fact]
    public void HexKey_OfSupportedLength_IsResolved()
    {
        var options = new RiderScansOptions { EncryptionKey = HexKey(0x42) };

        Assert.True(options.TryResolveKey(out var key, out var problem));
        Assert.Equal(32, key.Length);
        Assert.Null(problem);
        Assert.Equal(ScanProtectionStatus.Encrypted, options.GetStatus());
    }

    [Fact]
    public void Base64Key_IsResolved()
    {
        var options = new RiderScansOptions { EncryptionKey = Convert.ToBase64String(new byte[32]) };

        Assert.True(options.TryResolveKey(out var key, out _));
        Assert.Equal(32, key.Length);
        Assert.Equal(ScanProtectionStatus.Encrypted, options.GetStatus());
    }

    [Fact]
    public void SurroundingWhitespace_IsTolerated()
    {
        // Une clé copiée-collée dans le web.config traîne souvent un espace ou un retour ligne.
        var options = new RiderScansOptions { EncryptionKey = "  " + HexKey(0x7A) + "\n" };

        Assert.True(options.TryResolveKey(out var key, out _));
        Assert.Equal(32, key.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void MissingKey_BlocksUploads(string? value)
    {
        var options = new RiderScansOptions { EncryptionKey = value };

        Assert.False(options.TryResolveKey(out _, out var problem));
        Assert.Equal("aucune clé configurée", problem);
        Assert.Equal(ScanProtectionStatus.MissingKey, options.GetStatus());
    }

    [Fact]
    public void UnreadableKey_IsReportedAsInvalid_NotMissing()
    {
        var options = new RiderScansOptions { EncryptionKey = "ceci-n-est-pas-une-cle" };

        Assert.False(options.TryResolveKey(out _, out var problem));
        Assert.Contains("illisible", problem!);
        Assert.Equal(ScanProtectionStatus.InvalidKey, options.GetStatus());
    }

    [Fact]
    public void KeyOfWrongLength_IsRejected()
    {
        // 8 octets : décodable, mais inutilisable en AES — ne doit jamais passer pour valide.
        var options = new RiderScansOptions { EncryptionKey = Convert.ToBase64String(new byte[8]) };

        Assert.False(options.TryResolveKey(out var key, out var problem));
        Assert.Empty(key);
        Assert.Contains("longueur invalide", problem!);
        Assert.Equal(ScanProtectionStatus.InvalidKey, options.GetStatus());
    }

    [Fact]
    public void UnencryptedStorage_RequiresExplicitOptIn()
    {
        var refused = new RiderScansOptions { EncryptionKey = null };
        Assert.Equal(ScanProtectionStatus.MissingKey, refused.GetStatus());

        var allowed = new RiderScansOptions { EncryptionKey = null, AllowUnencryptedStorage = true };
        Assert.Equal(ScanProtectionStatus.UnencryptedAllowed, allowed.GetStatus());
    }

    [Fact]
    public void ValidKey_WinsOverAllowUnencrypted()
    {
        // Autoriser le clair ne doit pas désactiver un chiffrement correctement configuré.
        var options = new RiderScansOptions
        {
            EncryptionKey = HexKey(0x11),
            AllowUnencryptedStorage = true
        };

        Assert.Equal(ScanProtectionStatus.Encrypted, options.GetStatus());
    }
}
