using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Xunit;

namespace Wazap.UnitTests;

public class CreditTransactionTests
{
    [Fact]
    public void NewTransaction_ShouldBePending_AndStoreFields()
    {
        var vendorId = Guid.NewGuid();
        var transaction = new CreditTransaction(vendorId, 5000m, 35, "REF-2026-001");

        Assert.Equal(vendorId, transaction.VendorId);
        Assert.Equal(5000m, transaction.Amount);
        Assert.Equal(35, transaction.CreditsPurchased);
        Assert.Equal("REF-2026-001", transaction.TransactionReference);
        Assert.Equal(TransactionStatus.Pending, transaction.Status);
        Assert.NotEqual(default, transaction.Id);
        Assert.NotEqual(default, transaction.CreatedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void NewTransaction_WithNonPositiveAmount_ShouldThrow(decimal amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CreditTransaction(Guid.NewGuid(), amount, 10, "REF"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NewTransaction_WithNonPositiveCredits_ShouldThrow(int credits)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CreditTransaction(Guid.NewGuid(), 1000m, credits, "REF"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NewTransaction_WithoutReference_ShouldThrow(string? reference)
    {
        Assert.Throws<ArgumentException>(
            () => new CreditTransaction(Guid.NewGuid(), 1000m, 10, reference!));
    }

    [Fact]
    public void MarkCompleted_AfterCompleted_ShouldKeepCompleted()
    {
        var transaction = new CreditTransaction(Guid.NewGuid(), 2500m, 15, "REF-A");
        transaction.MarkCompleted();
        transaction.MarkCompleted();
        Assert.Equal(TransactionStatus.Completed, transaction.Status);
    }

    [Fact]
    public void MarkFailed_OnFailedTransaction_ShouldThrow()
    {
        var transaction = new CreditTransaction(Guid.NewGuid(), 2500m, 15, "REF-B");
        transaction.MarkCompleted();
        Assert.Throws<InvalidOperationException>(() => transaction.MarkFailed());
    }

    [Fact]
    public void MarkCompleted_OnFailedTransaction_ShouldThrow()
    {
        var transaction = new CreditTransaction(Guid.NewGuid(), 2500m, 15, "REF-C");
        transaction.MarkFailed();
        Assert.Throws<InvalidOperationException>(() => transaction.MarkCompleted());
    }

    [Fact]
    public void Complete_ShouldReplaceReference_AndSetCompleted()
    {
        var transaction = new CreditTransaction(Guid.NewGuid(), 2500m, 15, "PENDING-abc");
        transaction.Complete("PAY-1234-5678");

        Assert.Equal("PAY-1234-5678", transaction.TransactionReference);
        Assert.Equal(TransactionStatus.Completed, transaction.Status);
    }

    [Fact]
    public void Complete_WithEmptyReference_ShouldThrow()
    {
        var transaction = new CreditTransaction(Guid.NewGuid(), 2500m, 15, "PENDING-abc");
        Assert.Throws<ArgumentException>(() => transaction.Complete("   "));
    }

    [Fact]
    public void Complete_OnFailedTransaction_ShouldThrow()
    {
        var transaction = new CreditTransaction(Guid.NewGuid(), 2500m, 15, "PENDING-abc");
        transaction.MarkFailed();
        Assert.Throws<InvalidOperationException>(() => transaction.Complete("PAY-1234-5678"));
    }
}
