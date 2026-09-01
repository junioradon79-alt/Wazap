using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Xunit;

namespace Wazap.UnitTests;

public class UserTests
{
    [Fact]
    public void NewUser_ShouldStorePhoneNumber()
    {
        var user = new User("vendor1", "hash", UserRole.Vendor, "+123456789");
        Assert.Equal("+123456789", user.PhoneNumber);
    }

    [Fact]
    public void NewAdmin_ShouldHaveNullPhoneNumberByDefault()
    {
        var user = new User("admin", "hash", UserRole.Admin);
        Assert.Null(user.PhoneNumber);
    }

    [Fact]
    public void NewUser_ShouldStartWithZeroCredits()
    {
        var user = new User("vendor1", "hash", UserRole.Vendor, "+123456789");
        Assert.Equal(0, user.Credits);
    }

    [Fact]
    public void AddCredits_ShouldIncreaseBalance()
    {
        var user = new User("vendor1", "hash", UserRole.Vendor, "+123456789");
        user.AddCredits(35);
        Assert.Equal(35, user.Credits);
    }

    [Fact]
    public void AddCredits_WithNonPositiveValue_ShouldThrow()
    {
        var user = new User("vendor1", "hash", UserRole.Vendor, "+123456789");
        Assert.Throws<ArgumentOutOfRangeException>(() => user.AddCredits(0));
    }

    [Fact]
    public void TryConsumeCredit_WhenNoCredits_ShouldReturnFalse()
    {
        var user = new User("vendor1", "hash", UserRole.Vendor, "+123456789");
        Assert.False(user.TryConsumeCredit());
        Assert.Equal(0, user.Credits);
    }

    [Fact]
    public void TryConsumeCredit_WithCredits_ShouldReturnTrueAndDecrement()
    {
        var user = new User("vendor1", "hash", UserRole.Vendor, "+123456789");
        user.AddCredits(3);
        Assert.True(user.TryConsumeCredit());  // 3 -> 2
        Assert.True(user.TryConsumeCredit());  // 2 -> 1
        Assert.True(user.TryConsumeCredit());  // 1 -> 0
        Assert.False(user.TryConsumeCredit()); // 0 -> refus
        Assert.Equal(0, user.Credits);
    }

    [Fact]
    public void SetZone_ShouldTrimAndStore()
    {
        var user = new User("rider1", "hash", UserRole.Rider, "+123456789");
        user.SetZone("  Cocody  ");
        Assert.Equal("Cocody", user.Zone);
    }

    [Fact]
    public void SetZone_WithEmptyValue_ShouldClear()
    {
        var user = new User("rider1", "hash", UserRole.Rider, "+123456789");
        user.SetZone("Cocody");
        user.SetZone("   ");
        Assert.Null(user.Zone);
    }

    [Fact]
    public void ChangePassword_ShouldReplaceHash()
    {
        var user = new User("admin", "ancien_hash", UserRole.Admin);
        user.ChangePassword("nouveau_hash");
        Assert.Equal("nouveau_hash", user.PasswordHash);
    }

    [Fact]
    public void ChangePassword_WithNullOrEmptyHash_ShouldThrow()
    {
        var user = new User("admin", "ancien_hash", UserRole.Admin);
        Assert.Throws<ArgumentException>(() => user.ChangePassword("   "));
    }

    [Fact]
    public void NewUser_ShouldHaveReferralCode()
    {
        var user = new User("vendor1", "hash", UserRole.Vendor, "+123456789");
        Assert.NotNull(user.ReferralCode);
        Assert.Matches("^WA-[A-Z2-9]{4}$", user.ReferralCode);
    }

    [Fact]
    public void GenerateReferralCode_ShouldBeMostlyUnique()
    {
        var codes = Enumerable.Range(0, 50)
            .Select(_ => User.GenerateReferralCode())
            .ToHashSet();
        // 50 codes sur ~1 M de combinaisons : quasi-unanimité attendue (seuil large pour éviter tout flaky).
        Assert.True(codes.Count >= 48, $"Codes distincts attendus ≥ 48, obtenus {codes.Count}.");
    }

    [Fact]
    public void SetReferral_ShouldStoreReferrer()
    {
        var user = new User("vendor1", "hash", UserRole.Vendor);
        var sponsor = new User("sponsor", "hash", UserRole.Vendor);
        user.SetReferral(sponsor.Id);
        Assert.Equal(sponsor.Id, user.ReferredByUserId);
    }
}
