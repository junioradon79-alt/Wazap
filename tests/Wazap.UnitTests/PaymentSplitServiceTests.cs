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

    public PaymentSplitServiceTests()
    {
        _context = TestInfra.NewContext("paymentsplit-" + Guid.NewGuid().ToString("N"));
        _sender = new RecordingWhatsAppSender();
        _service = new PaymentSplitService(_context, _sender, NullLogger<PaymentSplitService>.Instance);
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
        var result = await _service.InitiateSplitPaymentAsync(order.Id);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(26500m, result.TotalAmount);
        Assert.Equal(25000m, result.MerchantAmount);
        Assert.Equal(1500m, result.RiderDeliveryFee);
        Assert.NotNull(result.PaymentLink);
        Assert.Contains(order.Id.ToString(), result.PaymentLink);

        var payment = await _context.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == order.Id);
        Assert.NotNull(payment);
        Assert.Equal(26500m, payment.Amount);
    }

    [Fact]
    public async Task CompleteSplitPaymentAsync_NotifieVendeurEtLivreur_EtCloturePaiement()
    {
        // Arrange
        var order = CreateTestOrder(amount: 35000m, deliveryFee: 1500m);
        await _service.InitiateSplitPaymentAsync(order.Id);

        // Act
        var result = await _service.CompleteSplitPaymentAsync(order.Id, "WAVE-TEST-REF-1234");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(35000m, result.MerchantAmount);
        Assert.Equal(1500m, result.RiderDeliveryFee);

        var payment = await _context.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == order.Id);
        Assert.NotNull(payment);
        Assert.Equal(TransactionStatus.Completed, payment.Status);

        // Vérifier que le vendeur et le livreur ont été notifiés par WhatsApp
        var vendorMsg = _sender.TextMessages.FirstOrDefault(m => m.Phone == "+22507112233");
        var riderMsg = _sender.TextMessages.FirstOrDefault(m => m.Phone == "+22507012345");

        Assert.True(vendorMsg != default);
        Assert.Contains("PAIEMENT REÇU", vendorMsg.Message);
        Assert.Contains("35 000", vendorMsg.Message);

        Assert.True(riderMsg != default);
        Assert.Contains("COURSE RÉGLÉE", riderMsg.Message);
        Assert.Contains("1 500", riderMsg.Message);
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
    public async Task CompleteSplitPaymentAsync_OptionA_DeclencheDoubleVirementSortantEtTraces()
    {
        // Arrange : Service avec IPayoutService (Disbursement API Option A)
        var payoutService = new Wazap.Infrastructure.Services.ManualPayoutService(NullLogger<Wazap.Infrastructure.Services.ManualPayoutService>.Instance);
        var serviceWithPayout = new PaymentSplitService(
            _context,
            _sender,
            payoutService,
            paymentService: null,
            NullLogger<PaymentSplitService>.Instance);

        var order = CreateTestOrder(amount: 45000m, deliveryFee: 2500m);
        await serviceWithPayout.InitiateSplitPaymentAsync(order.Id);

        // Act
        var result = await serviceWithPayout.CompleteSplitPaymentAsync(order.Id, "GENIUS-PAY-REF-999");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(45000m, result.MerchantAmount);
        Assert.Equal(2500m, result.RiderDeliveryFee);
        Assert.NotNull(result.VendorDisbursementRef);
        Assert.NotNull(result.RiderDisbursementRef);
        Assert.StartsWith("DISB-VND-", result.VendorDisbursementRef);
        Assert.StartsWith("DISB-RDR-", result.RiderDisbursementRef);

        // Vérifier les messages WhatsApp avec références de virement immédiat
        var vendorMsg = _sender.TextMessages.FirstOrDefault(m => m.Phone == "+22507112233");
        var riderMsg = _sender.TextMessages.FirstOrDefault(m => m.Phone == "+22507012345");

        Assert.True(vendorMsg != default);
        Assert.Contains(result.VendorDisbursementRef, vendorMsg.Message);
        Assert.Contains("45 000", vendorMsg.Message);

        Assert.True(riderMsg != default);
        Assert.Contains(result.RiderDisbursementRef, riderMsg.Message);
        Assert.Contains("2 500", riderMsg.Message);
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



