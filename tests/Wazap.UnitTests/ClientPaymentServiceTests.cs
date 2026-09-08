using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>Port de paiement factice : succès configurable + statut de réconciliation.</summary>
internal sealed class FakeClientPaymentGateway : IPaymentService
{
    public bool FailRequest { get; set; }
    public PaymentStatusResult? NextStatus { get; set; }
    public List<string> Requests { get; } = new();

    public Task<PaymentResult> RequestPaymentAsync(
        Guid vendorId, string packName, decimal amount, string reference)
    {
        Requests.Add(reference);
        return Task.FromResult(FailRequest
            ? new PaymentResult(false, null, null, "Solde insuffisant")
            : new PaymentResult(true, $"PAY-{Requests.Count}", $"https://checkout.test/{Requests.Count}", null));
    }

    public Task<PaymentStatusResult?> CheckPaymentStatusAsync(string reference)
        => Task.FromResult(NextStatus);
}

/// <summary>Monte <see cref="ClientPaymentService"/> sur une base InMemory isolée.</summary>
internal sealed class ClientPaymentHarness
{
    public ApplicationDbContext Context { get; }
    public RecordingWhatsAppSender Sender { get; } = new();
    public FakeClientPaymentGateway Gateway { get; } = new();
    public ClientPaymentOptions Options { get; }
    public ClientPaymentService Service { get; }
    public DeliveryOfferService Offers { get; }

    public ClientPaymentHarness(ClientPaymentOptions? options = null)
    {
        Options = options ?? new ClientPaymentOptions { Enabled = true };
        Context = TestInfra.NewContext("clientpay-" + Guid.NewGuid().ToString("N"));

        var whatsAppOptions = new WhatsAppOptions();
        var orchestrator = new WhatsAppOrchestrationService(
            Sender, whatsAppOptions, NullLogger<WhatsAppOrchestrationService>.Instance);
        Offers = new DeliveryOfferService(Context, Sender, whatsAppOptions,
            new GeoOptions(), new GroupingOptions(), new ClientOptions(), orchestrator,
            new RiderSecurityOptions(), new RiderReputationOptions(), Options,
            NullLogger<DeliveryOfferService>.Instance);

        Service = new ClientPaymentService(Context, Gateway, Sender, Offers, Options,
            NullLogger<ClientPaymentService>.Instance);
    }

    public Order NewConfirmedOrder(decimal amount = 5000m)
    {
        var order = new Order("Client Test", "+2250700000001", "+2250700000002", "2 attiékés", amount);
        order.LinkVendor(Guid.NewGuid());
        order.ConfirmByVendor();
        Context.Orders.Add(order);
        Context.SaveChanges();
        return order;
    }

    public OrderPayment? PaymentFor(Guid orderId)
        => Context.OrderPayments.FirstOrDefault(p => p.OrderId == orderId);
}

public class ClientPaymentServiceTests
{
    [Fact]
    public async Task RequestPayment_WhenDisabled_ShouldFailWithoutCharge()
    {
        var harness = new ClientPaymentHarness(new ClientPaymentOptions());
        var order = harness.NewConfirmedOrder();

        var result = await harness.Service.RequestPaymentAsync(order.Id);

        Assert.False(result.Success);
        Assert.Equal("Disabled", result.Status);
        Assert.Empty(harness.Gateway.Requests);
        Assert.Null(harness.PaymentFor(order.Id));
    }

    [Fact]
    public async Task RequestPayment_UnknownOrder_ShouldReturnNotFound()
    {
        var harness = new ClientPaymentHarness();

        var result = await harness.Service.RequestPaymentAsync(Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Equal("NotFound", result.Status);
    }

    [Fact]
    public async Task RequestPayment_ShouldCreatePendingPayment_AndSendLinkToClient()
    {
        var harness = new ClientPaymentHarness();
        var order = harness.NewConfirmedOrder();

        var result = await harness.Service.RequestPaymentAsync(order.Id);

        Assert.True(result.Success);
        Assert.Equal("Pending", result.Status);
        Assert.Equal(5000m, result.Amount);
        Assert.Equal("https://checkout.test/1", result.PaymentLink);

        var payment = harness.PaymentFor(order.Id);
        Assert.NotNull(payment);
        Assert.Equal(TransactionStatus.Pending, payment!.Status);
        Assert.Equal("PAY-1", payment.TransactionReference);
        Assert.Equal(order.Amount, payment.Amount);

        // Le client reçoit le lien par WhatsApp (best effort, texte).
        var clientMessage = harness.Sender.TextMessages
            .LastOrDefault(m => m.Phone == order.ClientWhatsAppNumber).Message;
        Assert.NotNull(clientMessage);
        Assert.Contains("https://checkout.test/1", clientMessage);
    }

    [Fact]
    public async Task RequestPayment_PendingWithLink_ShouldReturnSameLink_WithoutNewSession()
    {
        var harness = new ClientPaymentHarness();
        var order = harness.NewConfirmedOrder();
        await harness.Service.RequestPaymentAsync(order.Id);

        var second = await harness.Service.RequestPaymentAsync(order.Id);

        Assert.True(second.Success);
        Assert.Equal("https://checkout.test/1", second.PaymentLink);
        Assert.Single(harness.Gateway.Requests); // pas de seconde session chez l'agrégateur
    }

    [Fact]
    public async Task RequestPayment_WhenGatewayFails_ShouldMarkPaymentFailed()
    {
        var harness = new ClientPaymentHarness { };
        harness.Gateway.FailRequest = true;
        var order = harness.NewConfirmedOrder();

        var result = await harness.Service.RequestPaymentAsync(order.Id);

                Assert.False(result.Success);
        Assert.Equal("Failed", result.Status);
        Assert.Equal(TransactionStatus.Failed, harness.PaymentFor(order.Id)!.Status);
    }

    [Fact]
    public async Task CompletePayment_ShouldComputeCommission_AndNotifyActors()
    {
        var harness = new ClientPaymentHarness();
        var order = harness.NewConfirmedOrder();
        await harness.Service.RequestPaymentAsync(order.Id);
        var payment = harness.PaymentFor(order.Id)!;

        await harness.Service.CompletePaymentAsync(payment.Id, "AGG-42");

        var saved = await harness.Context.OrderPayments.FirstOrDefaultAsync(p => p.Id == payment.Id);
        Assert.Equal(TransactionStatus.Completed, saved!.Status);
        Assert.Equal(100m, saved.CommissionAmount); // 2 % de 5 000
        Assert.Equal(4900m, saved.VendorPayoutDue);
        Assert.NotNull(saved.CompletedAt);

        // Client + vendeur notifiés du paiement reçu.
        var clientMsg = harness.Sender.TextMessages
            .LastOrDefault(m => m.Phone == order.ClientWhatsAppNumber).Message;
        var vendorMsg = harness.Sender.TextMessages
            .LastOrDefault(m => m.Phone == order.VendorWhatsAppNumber).Message;
        Assert.Contains("4900 FCFA", vendorMsg!);
        Assert.Contains("Paiement reçu", clientMsg!);
    }

    [Fact]
    public async Task CompletePayment_DuplicateWebhook_ShouldBeIdempotent()
    {
        var harness = new ClientPaymentHarness();
        var order = harness.NewConfirmedOrder();
        await harness.Service.RequestPaymentAsync(order.Id);
        var payment = harness.PaymentFor(order.Id)!;
        var beforeCount = harness.Sender.TextMessages.Count;

        await harness.Service.CompletePaymentAsync(payment.Id, "AGG-1");
        await harness.Service.CompletePaymentAsync(payment.Id, "AGG-1"); // webhook dupliqué

        Assert.Equal(TransactionStatus.Completed,
            (await harness.Context.OrderPayments.FirstAsync(p => p.Id == payment.Id)).Status);
        // Pas de notifications supplémentaires.
        Assert.Equal(beforeCount + 2, harness.Sender.TextMessages.Count);
        }

    [Fact]
    public async Task CompletePayment_WhenAlreadyPaid_ShouldMarkDuplicateFailed()
    {
        var harness = new ClientPaymentHarness();
        var order = harness.NewConfirmedOrder();

        // Première session de paiement → initiée puis complétée.
        await harness.Service.RequestPaymentAsync(order.Id);
        var payment1 = harness.PaymentFor(order.Id)!;
        await harness.Service.CompletePaymentAsync(payment1.Id, "AGG-1");

        // Deuxième session (simulant un refresh de la page) → Pending, puis complétée par un
        // second webhook d'agrégateur : l'ordre est déjà payé → on échec que la douce.
        var duplicate = new OrderPayment(order.Id, order.Amount);
        duplicate.SetTransactionReference("AGG-DUP");
        harness.Context.OrderPayments.Add(duplicate);
        await harness.Context.SaveChangesAsync();

        await harness.Service.CompletePaymentAsync(duplicate.Id, "AGG-DUP");

        Assert.Equal(TransactionStatus.Failed,
            (await harness.Context.OrderPayments.FirstAsync(p => p.Id == duplicate.Id)).Status);
    }

    [Fact]
    public async Task FailPayment_ShouldMarkPendingAsFailed_AndBeNonEventOnCompleted()
    {
        var harness = new ClientPaymentHarness();
        var order = harness.NewConfirmedOrder();
        await harness.Service.RequestPaymentAsync(order.Id);
        var payment = harness.PaymentFor(order.Id)!;

        // 1) complétion → le paiement passe à Completed (+2 notifs client/vendeur).
        var beforeCount = harness.Sender.TextMessages.Count;
        await harness.Service.CompletePaymentAsync(payment.Id, "AGG-1");
        Assert.Equal(TransactionStatus.Completed,
            (await harness.Context.OrderPayments.FirstAsync(p => p.Id == payment.Id)).Status);

        // 2) un échec sur une transaction déjà complétée est un non-événement.
        await harness.Service.FailPaymentAsync(payment.Id);
        Assert.Equal(TransactionStatus.Completed,
            (await harness.Context.OrderPayments.FirstAsync(p => p.Id == payment.Id)).Status);
                Assert.Equal(beforeCount + 2, harness.Sender.TextMessages.Count);
    }

    [Fact]
    public async Task IsDispatchBlocked_OffByDefault_TrueOnlyWhenOptionOnAndUnpaid()
    {
        var off = new ClientPaymentHarness(new ClientPaymentOptions { Enabled = true });
        var paidOrder = off.NewConfirmedOrder();
        Assert.False(await off.Service.IsDispatchBlockedAsync(paidOrder.Id));

        var blocking = new ClientPaymentHarness(new ClientPaymentOptions
        {
            Enabled = true,
            RequirePaymentBeforeDispatch = true
        });
        var unpaidOrder = blocking.NewConfirmedOrder();
        Assert.True(await blocking.Service.IsDispatchBlockedAsync(unpaidOrder.Id));
    }

    [Fact]
    public async Task DispatchBlocked_UntilPayment_ThenResumes()
    {
        var harness = new ClientPaymentHarness(new ClientPaymentOptions
        {
            Enabled = true,
            CommissionPercent = 2m,
            RequirePaymentBeforeDispatch = true
        });

        var order = harness.NewConfirmedOrder();
        order.SetClientCoordinates(-4.46, 5.45, "Marcory"); // coordonnées validées

        // Diffusion bloquée tant que le paiement n'est pas complété.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Offers.DispatchConfirmedOrderAsync(order.Id));
        Assert.Contains("paiement", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Paiement initié puis complété → la diffusion reprend (création du lot).
        await harness.Service.RequestPaymentAsync(order.Id);
        var payment = harness.Context.OrderPayments.First(p => p.OrderId == order.Id);
        await harness.Service.CompletePaymentAsync(payment.Id, "AGG-OK");

        await harness.Context.Entry(order).ReloadAsync();
        Assert.NotNull(order.BatchId);
        Assert.False(await harness.Service.IsDispatchBlockedAsync(order.Id));
    }
}