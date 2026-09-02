using Wazap.Application.Helpers;
using Wazap.Domain.Entities;
using Xunit;

namespace Wazap.UnitTests;

public class SecurityHelperTests
{
    [Fact]
    public void Sha256Hex_ShouldBeStable()
    {
        Assert.Equal(SecurityHelper.Sha256Hex("abc"), SecurityHelper.Sha256Hex("abc"));
        Assert.NotEqual(SecurityHelper.Sha256Hex("abc"), SecurityHelper.Sha256Hex("abd"));
        Assert.Equal(64, SecurityHelper.Sha256Hex("abc").Length);
    }

    [Fact]
    public void GenerateOpaqueToken_ShouldBeUniqueAndUrlSafe()
    {
        var a = SecurityHelper.GenerateOpaqueToken();
        var b = SecurityHelper.GenerateOpaqueToken();
        Assert.NotEqual(a, b);
        Assert.DoesNotContain('+', a);
        Assert.DoesNotContain('/', a);
    }

    [Fact]
    public void GenerateNumericCode_ShouldHaveExpectedLength()
    {
        Assert.Equal(6, SecurityHelper.GenerateNumericCode().Length);
        Assert.Equal(8, SecurityHelper.GenerateNumericCode(8).Length);
    }

    [Fact]
    public void Totp_VerifyCurrentCode_ShouldSucceed()
    {
        var secret = Totp.GenerateSecret();
        var code = Totp.CurrentCode(secret);
        Assert.True(Totp.Verify(secret, code));
    }

    [Fact]
    public void Totp_VerifyWrongCode_ShouldFail()
    {
        var secret = Totp.GenerateSecret();
        Assert.False(Totp.Verify(secret, "000000"));
    }

    [Fact]
    public void Totp_Secrets_ShouldBeUniqueAndBase32()
    {
        Assert.NotEqual(Totp.GenerateSecret(), Totp.GenerateSecret());
        Assert.Contains(Totp.GenerateSecret(), s => char.IsUpper(s));
    }

    [Fact]
    public void RefreshToken_ShouldBeActive_ThenRevoked()
    {
        var token = new RefreshToken(Guid.NewGuid(), "hash", DateTime.UtcNow.AddDays(30));
        Assert.True(token.IsActive);

        token.Revoke();
        Assert.False(token.IsActive);
        Assert.NotNull(token.RevokedAtUtc);
    }

    [Fact]
    public void RefreshToken_Expired_ShouldNotBeActive()
    {
        var token = new RefreshToken(Guid.NewGuid(), "hash", DateTime.UtcNow.AddMinutes(-1));
        Assert.False(token.IsActive);
    }

    [Fact]
    public void User_ResetCode_Lifecycle()
    {
        var user = new User("vendor1", "hash", Domain.Enums.UserRole.Vendor, "+2250102030405");
        Assert.False(user.HasActiveResetCode());

        user.SetResetCode("codehash", DateTime.UtcNow.AddMinutes(15));
        Assert.True(user.HasActiveResetCode());

        user.ClearResetCode();
        Assert.False(user.HasActiveResetCode());
    }

    [Fact]
    public void User_TwoFactor_Lifecycle()
    {
        var user = new User("admin", "hash", Domain.Enums.UserRole.Admin);
        Assert.False(user.TwoFactorEnabled);

        user.EnableTwoFactor("SECRETBASE32");
        Assert.True(user.TwoFactorEnabled);

        user.DisableTwoFactor();
        Assert.False(user.TwoFactorEnabled);
        Assert.Null(user.TwoFactorSecret);
    }
}
