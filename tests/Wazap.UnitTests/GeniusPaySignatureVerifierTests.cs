using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

public class GeniusPaySignatureVerifierTests
{
    private const string Secret = "whsec_test_123";

    [Fact]
    public void ValidSignature_ShouldPass()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var payload = """{"data":{"id":"GP-123","status":"completed"}}""";
        var signature = GeniusPaySignatureVerifier.ComputeHmacSha256(timestamp + "." + payload, Secret);

        Assert.True(GeniusPaySignatureVerifier.IsValid(payload, signature, timestamp, Secret));
    }

    [Fact]
    public void TamperedPayload_ShouldFail()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var payload = """{"data":{"id":"GP-123","status":"completed"}}""";
        var signature = GeniusPaySignatureVerifier.ComputeHmacSha256(timestamp + "." + payload, Secret);

        Assert.False(GeniusPaySignatureVerifier.IsValid(payload + "x", signature, timestamp, Secret));
    }

    [Fact]
    public void WrongSignature_ShouldFail()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var payload = """{"data":{"id":"GP-123"}}""";

        Assert.False(GeniusPaySignatureVerifier.IsValid(payload, "toto", timestamp, Secret));
    }

    [Fact]
    public void ExpiredTimestamp_ShouldFail()
    {
        var timestamp = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 600).ToString();
        var payload = """{"data":{"id":"GP-123"}}""";
        var signature = GeniusPaySignatureVerifier.ComputeHmacSha256(timestamp + "." + payload, Secret);

        Assert.False(GeniusPaySignatureVerifier.IsValid(payload, signature, timestamp, Secret));
    }

    [Fact]
    public void WrongSecret_ShouldFail()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var payload = """{"data":{"id":"GP-123"}}""";
        var signature = GeniusPaySignatureVerifier.ComputeHmacSha256(timestamp + "." + payload, "wrong");

        Assert.False(GeniusPaySignatureVerifier.IsValid(payload, signature, timestamp, Secret));
    }
}
