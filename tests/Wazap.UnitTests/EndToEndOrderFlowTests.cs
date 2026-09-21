using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.Application.Dtos;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

public class EndToEndOrderFlowTests
{
    private readonly ApplicationDbContext _context;
    private readonly RecordingWhatsAppSender _sender;
    private readonly ManualPayoutService _payoutService;
    private readonly PaymentSplitService _splitService;

    public EndToEndOrderFlowTests()
    {
        _context = TestInfra.NewContext("e2e-" + Guid.NewGuid().ToString("N"));
        _sender = new RecordingWhatsAppSender();
        _payoutService = new ManualPayoutService(NullLogger<ManualPayoutService>.Instance);
        _splitService = new PaymentSplitService(_context, _sender, _payoutService, null, NullLogger<PaymentSplitService>.Instance);
    }

    [Fact]
    public async Task CompleteE2EFlow_WhatsAppBot_To_Vendor1Click_To_RiderAccept_To_UniversalPayment_And_Disbursement()
    {
        // 1. Arrange : Un commerçant inscrit avec 10 crédits
        var vendor = new User("Boutique Wax Élégance", "hash", UserRole.Vendor, "+22507112233");
        vendor.AddCredits(10);
        _context.Users.Add(vendor);

        // Un livreur certifié disponible dans la zone
        var rider = new User("Amara Fofana", "hash", UserRole.Rider, "+22507998877");
        _context.Users.Add(rider);
        await _context.SaveChangesAsync();

        // 2. Étape A : Commande passée par le client via le bot WhatsApp (simulée via ClientOrderDraft/Order)
        var clientPhone = "+22507443322";
        var order = new Order(
            clientName: "Mme Konan",
            clientWhatsAppNumber: clientPhone,
            vendorWhatsAppNumber: vendor.PhoneNumber!,
            description: "2x Boubous brodés haut de gamme",
            amount: 25000m,
            deliveryFee: 1500m
        );
        order.LinkVendor(vendor.Id);
        order.SetClientCoordinates(5.352, -3.985, "Plateau, face banque");
        order.EnsureDeliveryCode();
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        // Vérification : la commande démarre bien en attente du vendeur
        Assert.Equal(OrderStatus.PendingVendorConfirmation, order.Status);

        // 3. Étape B : Le commerçant voit la commande dans son espace "Commandes Prêtes"
        var pendingOrders = await _context.Orders.AsNoTracking()
            .Where(o => o.VendorUserId == vendor.Id && o.Status == OrderStatus.PendingVendorConfirmation)
            .ToListAsync();
        Assert.Single(pendingOrders);
        Assert.Equal("Mme Konan", pendingOrders[0].ClientName);
        Assert.Equal(25000m, pendingOrders[0].Amount);

        // Le commerçant clique sur [🚀 Expédier en 1 Clic] avec partage des frais 50/50
        order.ConfirmByVendor();
        order.AwaitRiderAcceptance();
        await _context.SaveChangesAsync();

        // Règle d'or : 0 crédit débité tant qu'aucun livreur n'a accepté
        Assert.Equal(10, vendor.Credits);

        // 4. Étape C : Un livreur accepte la course -> C'est ICI que 1 crédit est débité !
        order.AssignRider(rider.PhoneNumber!);
        order.LinkRider(rider.Id);
        Assert.True(vendor.TryConsumeCredit());
        await _context.SaveChangesAsync();

        Assert.Equal(9, vendor.Credits); // Débité uniquement après acceptation livreur !
        Assert.Equal(OrderStatus.RiderAssigned, order.Status);
        Assert.Equal(rider.PhoneNumber, order.RiderWhatsAppNumber);

        // 5. Étape D : Le client ouvre sa page de suivi PWA et consulte le Paiement Universel
        // Frais GeniusPay : 100 FCFA + 1% de (25 000 + 1 500) = 100 + 265 = 365 FCFA
        // Mode partagé (Shared 50/50) : client paye 26 500 + 183 = 26 683 FCFA
        // Commerçant reçoit 25 000 - 182 = 24 818 FCFA net
        // Livreur reçoit 100% de sa course = 1 500 FCFA net
        var splitCalc = PaymentSplitService.CalculateSplit(order, SplitFeePayer.Shared);
        Assert.Equal(365m, splitCalc.GatewayFee);
        Assert.Equal(26683m, splitCalc.TotalAmount);
        Assert.Equal(24818m, splitCalc.MerchantAmount);
        Assert.Equal(1500m, splitCalc.RiderDeliveryFee);

        // Initiation du paiement universel
        var initResult = await _splitService.InitiateSplitPaymentAsync(order.Id, "https://wazap-api.onrender.com", SplitFeePayer.Shared);
        Assert.True(initResult.Success);
        Assert.Equal(26683m, initResult.TotalAmount);

        // 6. Étape E : Le client scanne le QR Universel ou clique Payer (GeniusPay Webhook / Simulation)
        // Le livreur remet le colis et la livraison est clôturée
        order.MarkReadyForPickup();
        order.MarkPickedUp();
        order.MarkInTransit();
        order.MarkDelivered();
        await _context.SaveChangesAsync();

        var completeResult = await _splitService.CompleteSplitPaymentAsync(order.Id, "WAVE-TEST-E2E-999");

        // Assert : Double reversement automatique sortant (Option A : zéro cash touché par le livreur)
        Assert.True(completeResult.Success);
        Assert.NotNull(completeResult.VendorDisbursementRef);
        Assert.NotNull(completeResult.RiderDisbursementRef);
        Assert.StartsWith("DISB-VND-", completeResult.VendorDisbursementRef);
        Assert.StartsWith("DISB-RDR-", completeResult.RiderDisbursementRef);

        // Messages WhatsApp automatiques envoyés
        Assert.Contains(_sender.TextMessages, m => m.Phone == vendor.PhoneNumber && m.Message.Contains("PAIEMENT REÇU"));
        Assert.Contains(_sender.TextMessages, m => m.Phone == rider.PhoneNumber && m.Message.Contains("COURSE RÉGLÉE"));
    }
}
