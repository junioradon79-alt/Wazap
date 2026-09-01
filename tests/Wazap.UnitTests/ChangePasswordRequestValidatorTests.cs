using Wazap.Application.Dtos;
using Wazap.Application.Validators;
using Xunit;

namespace Wazap.UnitTests;

public class ChangePasswordRequestValidatorTests
{
    private readonly ChangePasswordRequestValidator _validator = new();

    [Fact]
    public void ValidRequest_ShouldPass()
    {
        var request = new ChangePasswordRequest
        {
            CurrentPassword = "Ancien#Pass1",
            NewPassword = "Nouveau#Pass2"
        };
        var result = _validator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void MissingCurrentPassword_ShouldFail()
    {
        var request = new ChangePasswordRequest
        {
            CurrentPassword = "",
            NewPassword = "Nouveau#Pass2"
        };
        var result = _validator.Validate(request);
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("court")]
    [InlineData("sans_majuscule_ou_chiffre")]
    [InlineData("PasDeCaractereSpecial1")]
    public void WeakNewPassword_ShouldFail(string newPassword)
    {
        var request = new ChangePasswordRequest
        {
            CurrentPassword = "Ancien#Pass1",
            NewPassword = newPassword
        };
        var result = _validator.Validate(request);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void SameAsCurrentPassword_ShouldFail()
    {
        var request = new ChangePasswordRequest
        {
            CurrentPassword = "Meme#Pass1",
            NewPassword = "Meme#Pass1"
        };
        var result = _validator.Validate(request);
        Assert.False(result.IsValid);
    }
}
