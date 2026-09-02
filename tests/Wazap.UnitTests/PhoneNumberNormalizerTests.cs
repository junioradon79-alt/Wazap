using Wazap.Application.Helpers;
using Xunit;

namespace Wazap.UnitTests;

public class PhoneNumberNormalizerTests
{
    [Theory]
    [InlineData("+33612345678", "+33612345678")]
    [InlineData("0033612345678", "+33612345678")]
    [InlineData("0612345678", "+33612345678")]
    [InlineData("33612345678", "+33612345678")]
    [InlineData("+33 6 12 34 56 78", "+33612345678")]
    [InlineData("06.12.34.56.78", "+33612345678")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalize_ShouldReturnE164(string? input, string? expected)
    {
        Assert.Equal(expected, PhoneNumberNormalizer.Normalize(input));
    }

    [Fact]
    public void SameSubscriber_IvoryCoastOldVsNew_ShouldMatch()
    {
        // Même ligne : ancien format (8 chiffres) vs nouveau format (10 chiffres, préfixe + ancien).
        Assert.True(PhoneNumberNormalizer.SameSubscriber("+22508323366", "+2250708323366"));
        Assert.True(PhoneNumberNormalizer.SameSubscriber("+2250708323366", "22508323366"));
        Assert.True(PhoneNumberNormalizer.SameSubscriber("22508323366", "2250708323366"));
    }

    [Fact]
    public void SameSubscriber_DifferentLines_ShouldNotMatch()
    {
        Assert.False(PhoneNumberNormalizer.SameSubscriber("+2250708323366", "+2250508123456"));
        Assert.False(PhoneNumberNormalizer.SameSubscriber("+33612345678", "+33699887766"));
        Assert.False(PhoneNumberNormalizer.SameSubscriber("+33612345678", "+22508323366"));
        Assert.False(PhoneNumberNormalizer.SameSubscriber(null, "+22508323366"));
        Assert.False(PhoneNumberNormalizer.SameSubscriber("", "+22508323366"));
    }
}
