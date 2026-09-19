using Wazap.Infrastructure.Data;
using Xunit;

namespace Wazap.UnitTests;

public class PostgresConnectionStringNormalizerTests
{
    [Fact]
    public void Normalize_UriFormat_ConvertsToAdoNetFormat()
    {
        var input = "postgresql://wazap:nkp66PUIT6bS7Oto3RbVh4iXdGjx8fso@dpg-danfbb6k1f9s738htj30-a/wazap_9jf6";
        var result = PostgresConnectionStringNormalizer.Normalize(input);

        Assert.Contains("Host=dpg-danfbb6k1f9s738htj30-a", result);
        Assert.Contains("Port=5432", result);
        Assert.Contains("Database=wazap_9jf6", result);
        Assert.Contains("Username=wazap", result);
        Assert.Contains("Password=nkp66PUIT6bS7Oto3RbVh4iXdGjx8fso", result);
    }

    [Fact]
    public void Normalize_StandardAdoNetFormat_PreservedUnchanged()
    {
        var input = "Host=localhost;Database=wazap;Username=postgres;Password=secret";
        var result = PostgresConnectionStringNormalizer.Normalize(input);

        Assert.Equal(input, result);
    }

    [Fact]
    public void Normalize_NullOrWhitespace_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, PostgresConnectionStringNormalizer.Normalize(null));
        Assert.Equal(string.Empty, PostgresConnectionStringNormalizer.Normalize("   "));
    }
}
