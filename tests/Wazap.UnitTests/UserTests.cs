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
}
