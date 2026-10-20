using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Dtos;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Xunit;

namespace Wazap.UnitTests;

public class PostgresPaymentSecurityTests
{
    private static ClientPaymentService Service(ApplicationDbContext db, RecordingWhatsAppSender sender, FakeClientPaymentGateway gateway)
        => new(db, gateway, sender, null!, new ClientPaymentOptions { Enabled = true }, NullLogger<ClientPaymentService>.Instance);

    private static async Task<Order> NewOrder(ApplicationDbContext db)
    {
        var order = new Order("Client", "+2250700000901", "+2250700000902", "Colis", 25000, deliveryFee: 1500);
        order.ConfirmByVendor();
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    [PostgresFact]
    public async Task ConcurrentInitiations_CreateOneCheckoutAndOneImmutableQuote()
    {
        using var harness = new PostgresHarness();
        var order = await NewOrder(harness.Context!);
        using var first = harness.NewContext();
        using var second = harness.NewContext();
        var gateway = new FakeClientPaymentGateway();
        var sender = new RecordingWhatsAppSender();
        var results = await Task.WhenAll(
            Service(first, sender, gateway).RequestPaymentAsync(order.Id, SplitFeePayer.Shared),
            Service(second, sender, gateway).RequestPaymentAsync(order.Id, SplitFeePayer.Client));
        Assert.All(results, r => Assert.True(r.Success || r.Status == "Processing", r.ErrorMessage));
        Assert.Single(results.Where(r => r.PaymentLink is not null).Select(r => r.PaymentLink).Distinct());
        Assert.Equal(results[0].Amount, results[1].Amount);
        Assert.Single(gateway.Requests);
        var payment = await harness.Context!.OrderPayments.AsNoTracking().SingleAsync(p => p.OrderId == order.Id);
        Assert.Equal(payment.Amount, payment.QuotedVendorAmount + payment.QuotedRiderAmount + payment.QuotedGatewayFee);
    }

    [PostgresFact]
    public async Task ConcurrentConfirmations_CompleteExactPaymentOnce_AndKeepQuotedDues()
    {
        using var harness = new PostgresHarness();
        var order = await NewOrder(harness.Context!);
        var sender = new RecordingWhatsAppSender();
        var gateway = new FakeClientPaymentGateway();
        await Service(harness.Context!, sender, gateway).RequestPaymentAsync(order.Id, SplitFeePayer.Shared);
        var paymentId = await harness.Context!.OrderPayments.Where(p => p.OrderId == order.Id).Select(p => p.Id).SingleAsync();
        sender.TextMessages.Clear();
        using var first = harness.NewContext();
        using var second = harness.NewContext();
        await Task.WhenAll(Service(first, sender, gateway).CompletePaymentAsync(paymentId, "VERIFIED"),
            Service(second, sender, gateway).CompletePaymentAsync(paymentId, "VERIFIED"));
        var payment = await harness.Context.OrderPayments.AsNoTracking().SingleAsync(p => p.Id == paymentId);
        Assert.Equal(TransactionStatus.Completed, payment.Status);
        Assert.Equal(24818, payment.VendorPayoutDue);
        Assert.Equal(1500, payment.RiderPayoutDue);
        Assert.Equal(2, await harness.Context.PaymentPayouts.CountAsync(p => p.PaymentId == paymentId));
        Assert.Equal(2, await harness.Context.OutboxMessages.CountAsync(m => m.Type == "PaymentNotification"));
        Assert.Equal(2, sender.TextMessages.Count); // client et vendeur ; aucun livreur affecté.
        Assert.DoesNotContain(sender.TextMessages, m => m.Message.Contains("transféré"));
    }

    [PostgresFact]
    public async Task OldCheckoutConfirmation_DoesNotCompleteNewerCheckout()
    {
        using var harness = new PostgresHarness();
        var db = harness.Context!;
        var order = await NewOrder(db);
        var older = new OrderPayment(order.Id, 25000);
        db.OrderPayments.Add(older);
        await db.SaveChangesAsync();
        var newer = new OrderPayment(order.Id, 25000);
        db.OrderPayments.Add(newer);
        await db.SaveChangesAsync();
        await Service(db, new RecordingWhatsAppSender(), new FakeClientPaymentGateway()).CompletePaymentAsync(older.Id, "OLDER-VERIFIED");
        Assert.Equal(TransactionStatus.Completed, (await db.OrderPayments.AsNoTracking().SingleAsync(p => p.Id == older.Id)).Status);
        Assert.Equal(TransactionStatus.Pending, (await db.OrderPayments.AsNoTracking().SingleAsync(p => p.Id == newer.Id)).Status);
    }

    [PostgresFact]
    public async Task TwoCheckoutsPaidAtOnce_RecordOnePaymentAndFlagTheOtherForRefund()
    {
        using var harness = new PostgresHarness();
        var db = harness.Context!;
        var order = await NewOrder(db);
        var firstPayment = new OrderPayment(order.Id, 25000);
        var secondPayment = new OrderPayment(order.Id, 25000);
        db.AddRange(firstPayment, secondPayment);
        await db.SaveChangesAsync();
        using var first = harness.NewContext();
        using var second = harness.NewContext();
        var sender = new RecordingWhatsAppSender();
        var gateway = new FakeClientPaymentGateway();
        await Task.WhenAll(
            Service(first, sender, gateway).CompletePaymentAsync(firstPayment.Id, "FIRST-VERIFIED"),
            Service(second, sender, gateway).CompletePaymentAsync(secondPayment.Id, "SECOND-VERIFIED"));
        var payments = await db.OrderPayments.AsNoTracking().Where(p => p.OrderId == order.Id).ToListAsync();
        Assert.Single(payments, p => p.Status == TransactionStatus.Completed);
        var duplicate = Assert.Single(payments, p => p.Status == TransactionStatus.Failed);
        Assert.Contains("remboursement", duplicate.ErrorMessage);
        Assert.Equal(2, sender.TextMessages.Count);
    }
}
