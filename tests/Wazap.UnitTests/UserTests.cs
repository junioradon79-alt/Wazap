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
}
