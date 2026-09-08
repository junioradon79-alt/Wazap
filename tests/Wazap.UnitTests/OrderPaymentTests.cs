using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Xunit;

namespace Wazap.UnitTests;

public class OrderPaymentTests
{
    [Fact]
    public void NewPayment_ShouldBePending_WithProvisionalReference()
    {
        var orderId = Guid.NewGuid();
        var payment = new OrderPayment(orderId, 5000m);

        Assert.Equal(orderId, payment.OrderId);
        Assert.Equal(5000m, payment.Amount);
        Assert.Equal(TransactionStatus.Pending, payment.Status);
        Assert.StartsWith(OrderPayment.PendingReferencePrefix, payment.TransactionReference);
        Assert.Contains(payment.Id.ToString("N"), payment.TransactionReference);
        Assert.Null(payment.PaymentLink);
        Assert.Null(payment.CompletedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void NewPayment_WithNonPositiveAmount_ShouldThrow(decimal amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrderPayment(Guid.NewGuid(), amount));
    }

    [Fact]
    public void NewPayment_WithoutOrderId_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => new OrderPayment(Guid.Empty, 5000m));
    }

    [Fact]
    public void Complete_ShouldComputeCommission_AndVendorPayout()
    {
        var payment = new OrderPayment(Guid.NewGuid(), 5000m);

        payment.Complete("GENIUS-REF-1", 2m);

        Assert.Equal(TransactionStatus.Completed, payment.Status);
        Assert.Equal("GENIUS-REF-1", payment.TransactionReference);
        Assert.Equal(100m, payment.CommissionAmount);   // 2 % de 5 000
        Assert.Equal(4900m, payment.VendorPayoutDue);
        Assert.NotNull(payment.CompletedAt);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Complete_WithInvalidCommissionPercent_ShouldThrow(decimal commissionPercent)
    {
        var payment = new OrderPayment(Guid.NewGuid(), 5000m);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => payment.Complete("REF", commissionPercent));
    }

    [Fact]
    public void Complete_Twice_ShouldThrow()
    {
        var payment = new OrderPayment(Guid.NewGuid(), 5000m);
        payment.Complete("REF-1", 2m);

        Assert.Throws<InvalidOperationException>(() => payment.Complete("REF-2", 2m));
    }

    [Fact]
    public void Complete_AfterFailed_ShouldThrow()
    {
        var payment = new OrderPayment(Guid.NewGuid(), 5000m);
        payment.MarkFailed();

        Assert.Throws<InvalidOperationException>(() => payment.Complete("REF", 2m));
    }

    [Fact]
    public void MarkFailed_AfterCompleted_ShouldThrow()
    {
        var payment = new OrderPayment(Guid.NewGuid(), 5000m);
        payment.Complete("REF", 2m);

        Assert.Throws<InvalidOperationException>(() => payment.MarkFailed());
    }

    [Fact]
    public void SetPaymentLink_WithEmptyLink_ShouldThrow()
    {
        var payment = new OrderPayment(Guid.NewGuid(), 5000m);

        Assert.Throws<ArgumentException>(() => payment.SetPaymentLink("   "));
    }

    [Fact]
    public void SetTransactionReference_ShouldReplaceProvisionalReference()
    {
        var payment = new OrderPayment(Guid.NewGuid(), 5000m);

        payment.SetTransactionReference("AGG-42");

        Assert.Equal("AGG-42", payment.TransactionReference);
        Assert.Equal(TransactionStatus.Pending, payment.Status);
    }
}