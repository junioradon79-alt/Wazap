using System.Text.RegularExpressions;
using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

public class MockPaymentServiceTests
{
    [Fact]
    public async Task RequestPaymentAsync_ShouldSucceed_WithFormattedReference()
    {
        var service = new MockPaymentService();

        var result = await service.RequestPaymentAsync(Guid.NewGuid(), "Découverte", 2500m);

        Assert.True(result.Success);
        Assert.NotNull(result.TransactionReference);
        Assert.Matches(new Regex(@"^PAY-\d{4}-\d{4}$"), result.TransactionReference);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task RequestPaymentAsync_ShouldProduceDistinctReferences()
    {
        var service = new MockPaymentService();

        var first = await service.RequestPaymentAsync(Guid.NewGuid(), "Petit", 5000m);
        var second = await service.RequestPaymentAsync(Guid.NewGuid(), "Petit", 5000m);

        Assert.NotEqual(first.TransactionReference, second.TransactionReference);
    }
}
