using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.Application.Dtos;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Xunit;


namespace Wazap.UnitTests;

public class PaymentSplitServiceTests
{
    private readonly ApplicationDbContext _context;
    private readonly RecordingWhatsAppSender _sender;
    private readonly PaymentSplitService _service;
    private readonly ClientPaymentService _payments;

    public PaymentSplitServiceTests()
    {
        _context = TestInfra.NewContext("paymentsplit-" + Guid.NewGuid().ToString("N"));
        _sender = new RecordingWhatsAppSender();
        _payments = new ClientPaymentService(_context, new FakeClientPaymentGateway(), _sender, null!,
            new Wazap.Application.Configuration.ClientPaymentOptions { Enabled = true }, NullLogger<ClientPaymentService>.Instance);
        _service = new PaymentSplitService(_context, _sender,
            NullLogger<PaymentSplitService>.Instance, _payments);
    }

    private Order CreateTestOrder(decimal amount = 35000m, decimal deliveryFee = 1500m)
    {
        var order = new Order(
            clientName: "Mme Konan",
            clientWhatsAppNumber: "+22507443322",
            vendorWhatsAppNumber: "+22507112233",
            description: "Coffret Parfums Sublime",
            amount: amount,
            deliveryFee: deliveryFee
        );
        order.ConfirmByVendor();
        order.AwaitRiderAcceptance();
        order.AssignRider("+22507012345");
        _context.Orders.Add(order);
        _context.SaveChanges();
        return order;
    }

    [Fact]
    public void CalculateSplit_CalculeVentilationExacte_SansPerte()
    {
        // Arrange
        var order = CreateTestOrder(amount: 50000m, deliveryFee: 2000m);

        // Act
        var calc = PaymentSplitService.CalculateSplit(order);

        // Assert
        Assert.Equal(50000m, calc.MerchantAmount);
        Assert.Equal(2000m, calc.RiderDeliveryFee);
        Assert.Equal(52000m, calc.TotalAmount);
        Assert.Equal("+22507112233", calc.MerchantPhone);
        Assert.Equal("+22507012345", calc.RiderPhone);
        Assert.Contains("50 000", calc.PaymentDescription);
        Assert.Contains("2 000", calc.PaymentDescription);
    }

    [Fact]
    public async Task InitiateSplitPaymentAsync_CreePaiementEtGenereLien()
    {
        // Arrange
        var order = CreateTestOrder(amount: 25000m, deliveryFee: 1500m);

        // Act
        var result = await _service.InitiateSplitPaymentAsync(order.Id, feePayer: SplitFeePayer.Client);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(26865m, result.TotalAmount);
        Assert.Equal(25000m, result.MerchantAmount);
        Assert.Equal(1500m, result.RiderDeliveryFee);
        Assert.NotNull(result.PaymentLink);
        Assert.StartsWith("https://checkout.test/", result.PaymentLink);

        var payment = await _context.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == order.Id);
        Assert.NotNull(payment);
        Assert.Equal(26865m, payment.Amount);
    }

    [Fact]
    public async Task Confirmation_ConserveDevisEtNotifieSeulementLesSommesDues_UneSeuleFois()
    {
        var order = CreateTestOrder();
        var result = await _service.InitiateSplitPaymentAsync(order.Id, feePayer: SplitFeePayer.Shared);
        var payment = await _context.OrderPayments.SingleAsync(p => p.OrderId == order.Id);
        Assert.Equal(36733m, payment.Amount);
        Assert.Equal(34768m, payment.QuotedVendorAmount);
        Assert.Equal(1500m, payment.QuotedRiderAmount);
        Assert.Equal(465m, payment.QuotedGatewayFee);
        await _payments.CompletePaymentAsync(payment.Id, "VERIFIED-REF");
        var messageCount = _sender.TextMessages.Count;
        await _payments.CompletePaymentAsync(payment.Id, "VERIFIED-REF");
        Assert.Equal(messageCount, _sender.TextMessages.Count);
        Assert.Equal(TransactionStatus.Completed, payment.Status);
        Assert.Equal(34768m, payment.VendorPayoutDue);
        Assert.Equal(1500m, payment.RiderPayoutDue);
        Assert.DoesNotContain(_sender.TextMessages, m => m.Message.Contains("transféré"));
        Assert.Contains(_sender.TextMessages, m => m.Message.Contains("reste à effectuer"));
        Assert.Equal(result.TotalAmount, payment.VendorPayoutDue + payment.RiderPayoutDue + payment.QuotedGatewayFee);
    }

    [Fact]
    public async Task ProcessHandoverScanAsync_PasseCommandeEnTransit_EtNotifieVendeur()
    {
        // Arrange
        var order = CreateTestOrder();
        Assert.Equal(OrderStatus.RiderAssigned, order.Status);

        // Act
        var result = await _service.ProcessHandoverScanAsync(order.Id);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("InTransit", result.Status);

        var updated = await _context.Orders.FindAsync(order.Id);
        Assert.NotNull(updated);
        Assert.Equal(OrderStatus.InTransit, updated.Status);

        // Notification envoyée au vendeur
        var vendorMsg = _sender.TextMessages.FirstOrDefault(m => m.Phone == "+22507112233");
        Assert.True(vendorMsg != default);
        Assert.Contains("COLIS RÉCUPÉRÉ (Scan QR)", vendorMsg.Message);
    }

    [Fact]
    public async Task VersementManuel_NeProduitPasDeReferenceNiDeConfirmationFictive()
    {
        var payout = new Wazap.Infrastructure.Services.ManualPayoutService(
            NullLogger<Wazap.Infrastructure.Services.ManualPayoutService>.Instance);
        var result = await payout.DisburseSplitAsync(new Wazap.Application.Abstractions.SplitDisbursementRequest(
            Guid.NewGuid(), "+2250700000001", 10000, "+2250700000002", 1500, "TEST"));
        Assert.False(result.Success);
        Assert.True(result.RequiresManualTransfer);
        Assert.Null(result.VendorTransferRef);
        Assert.Null(result.RiderTransferRef);
    }

    [Fact]
    public async Task Initiation_ReutiliseLeDevisEtLeLienSansNouvelEncaissement()
    {
        var order = CreateTestOrder();
        var first = await _service.InitiateSplitPaymentAsync(order.Id, feePayer: SplitFeePayer.Shared);
        var second = await _service.InitiateSplitPaymentAsync(order.Id, feePayer: SplitFeePayer.Client);
        Assert.Equal(first, second);
        Assert.Single(await _context.OrderPayments.ToListAsync());
    }

    [Fact]
    public void CalculateSplit_FormuleOfficielleGeniusPay_100FCFA_Plus_1Pourcent()
    {
        // Arrange : Commande 25 000 F marchandise + 1 500 F livraison = 26 500 F total
        var order = CreateTestOrder(amount: 25000m, deliveryFee: 1500m);
        // Formule GeniusPay : 100 FCFA + 1% de 26 500 = 100 + 265 = 365 FCFA

        // 1. Mode Client (+365 F payés par le client, 100% net vendeur)
        var calcClient = PaymentSplitService.CalculateSplit(order, SplitFeePayer.Client);
        Assert.Equal(365m, calcClient.GatewayFee);
        Assert.Equal(25000m, calcClient.MerchantAmount); // 100% net vendeur
        Assert.Equal(1500m, calcClient.RiderDeliveryFee);
        Assert.Equal(26865m, calcClient.TotalAmount);    // 26 500 + 365

        // 2. Mode Vendeur (-365 F déduits de la marge vendeur)
        var calcVendor = PaymentSplitService.CalculateSplit(order, SplitFeePayer.Vendor);
        Assert.Equal(365m, calcVendor.GatewayFee);
        Assert.Equal(24635m, calcVendor.MerchantAmount); // 25 000 - 365
        Assert.Equal(1500m, calcVendor.RiderDeliveryFee);
        Assert.Equal(26500m, calcVendor.TotalAmount);

        // 3. Mode Partagé 50/50 (183 F client / 182 F vendeur)
        var calcShared = PaymentSplitService.CalculateSplit(order, SplitFeePayer.Shared);
        Assert.Equal(365m, calcShared.GatewayFee);
        Assert.Equal(24818m, calcShared.MerchantAmount); // 25 000 - 182
        Assert.Equal(1500m, calcShared.RiderDeliveryFee);
        Assert.Equal(26683m, calcShared.TotalAmount);    // 26 500 + 183
    }
}



