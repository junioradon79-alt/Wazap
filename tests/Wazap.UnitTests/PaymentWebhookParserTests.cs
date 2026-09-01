using System.Text.Json;
using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

public class PaymentWebhookParserTests
{
    [Fact]
    public void Parse_WithMetadata_ShouldExtractWazapTransactionIdAndAmount()
    {
        var id = Guid.NewGuid();
        var payload = $$$"""
            {"data":{"id":"GP-1","reference":"GP-REF-1","amount":10000,"status":"completed",
                      "metadata":{"wazap_transaction_id":"{{{id}}}","vendor_id":"v1"}},
             "environment":"sandbox","api_version":"2024-01-01"}
            """;

        var info = PaymentWebhookParser.Parse(JsonDocument.Parse(payload).RootElement);

        Assert.NotNull(info);
        Assert.Equal(id, info.WazapTransactionId);
        Assert.Equal("GP-REF-1", info.Reference);
        Assert.Equal(10000m, info.Amount);
        Assert.Equal("completed", info.Status);
    }

    [Fact]
    public void Parse_WithoutMetadata_ShouldFallbackToReference()
    {
        var payload = """{"data":{"id":"GP-123","amount":2500,"status":"failed"}}""";

        var info = PaymentWebhookParser.Parse(JsonDocument.Parse(payload).RootElement);

        Assert.NotNull(info);
        Assert.Null(info.WazapTransactionId);
        Assert.Equal("GP-123", info.Reference);
        Assert.Equal(2500m, info.Amount);
        Assert.Equal("failed", info.Status);
    }

    [Fact]
    public void Parse_InvalidPayload_ShouldReturnNull()
    {
        var info = PaymentWebhookParser.Parse(JsonDocument.Parse("""{"event":"ping"}""").RootElement);
        Assert.Null(info);
    }

    [Fact]
    public void Parse_SnakeCaseMetadata_ShouldStillMatch()
    {
        var payload = """{"data":{"id":"GP-9","metadata":{"WAZAP_TRANSACTION_ID":"not-a-guid"}}}""";

        var info = PaymentWebhookParser.Parse(JsonDocument.Parse(payload).RootElement);

        Assert.NotNull(info);
        Assert.Null(info.WazapTransactionId); // guid invalide → null
        Assert.Equal("GP-9", info.Reference);
    }
}
