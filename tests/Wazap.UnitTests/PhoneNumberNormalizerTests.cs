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
}
