using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Services;
using Wazap.Domain.Configuration;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Xunit;

namespace Wazap.UnitTests;

public class WhatsAppOrchestrationServiceTests
{
    private static WhatsAppOrchestrationService CreateService(RecordingWhatsAppSender sender)
        => new(sender, new WhatsAppOptions());
    [Fact]
    public async Task SendCreditPurchaseConfirmation_ShouldSendExpectedMessage()
    {
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(sender);
        var vendor = CreateVendor("Vendeur Test", "+33612345678", credits: 15);
        var pack = new PackConfiguration { Name = "Découverte", Price = 2500m, Credits = 15 };

        await service.SendCreditPurchaseConfirmationAsync(vendor, pack);

        var sent = Assert.Single(sender.TextMessages);
        Assert.Equal("+33612345678", sent.Phone);
        Assert.Equal("Vous avez acheté le pack Découverte. Vous disposez maintenant de 15 commandes.", sent.Message);
    }

    [Fact]
    public async Task SendLowCreditAlert_ShouldSendExpectedMessage()
    {
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(sender);
        var vendor = CreateVendor("Vendeur Test", "+33612345678", credits: 3);

        await service.SendLowCreditAlertAsync(vendor);

        var sent = Assert.Single(sender.TextMessages);
        Assert.Equal("Il vous reste 3 commandes. Rechargez dès maintenant.", sent.Message);
    }

    [Fact]
    public async Task SendNoCreditAlert_ShouldSendExpectedMessage()
    {
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(sender);
        var vendor = CreateVendor("Vendeur Test", "+33612345678", credits: 0);

        await service.SendNoCreditAlertAsync(vendor);

        var sent = Assert.Single(sender.TextMessages);
        Assert.Equal("Vous n'avez plus de crédits. Achetez un pack pour continuer.", sent.Message);
    }

    [Fact]
    public async Task SendAlert_WithoutPhone_ShouldSkip()
    {
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(sender);
        var vendor = CreateVendor("Vendeur Test", null, credits: 0);

        await service.SendNoCreditAlertAsync(vendor);

        Assert.Empty(sender.TextMessages);
    }

    [Fact]
    public async Task SendRiderAssigned_ShouldNotifyClientVendorAndRider()
    {
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(sender);
        var order = new Order("Client Test", "+33611112222", "+33612345678", "Commande", 10m);
        var rider = new User("Rider Test", "hash", UserRole.Rider, "+33698765432");
        var orderCode = order.Id.ToString("N")[..8].ToUpperInvariant();

        await service.SendRiderAssignedAsync(order, rider);

        // Client + vendeur + livreur
        Assert.Equal(3, sender.TextMessages.Count);
        Assert.Contains(sender.TextMessages, m => m.Phone == "+33611112222" && m.Message.Contains($"#{orderCode}"));
        Assert.Contains(sender.TextMessages, m => m.Phone == "+33612345678" && m.Message.Contains("Rider Test"));
        Assert.Contains(sender.TextMessages, m => m.Phone == "+33698765432" && m.Message.StartsWith("✅"));
    }

    [Fact]
    public async Task SendBatchAssigned_ShouldNotifyEachClientOnce_AndVendorOnce()
    {
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(sender);
        var rider = new User("Rider Test", "hash", UserRole.Rider, "+33698765432");
        var orders = new List<Order>
        {
            new("Client A", "+33611110001", "+33612345678", "C1", 10m),
            new("Client B", "+33611110002", "+33612345678", "C2", 20m)
        };

        await service.SendBatchAssignedAsync(rider, orders);

        // 2 clients + 1 vendeur + 1 livreur
        Assert.Equal(4, sender.TextMessages.Count);
        Assert.Single(sender.TextMessages, m => m.Phone == "+33611110001");
        Assert.Single(sender.TextMessages, m => m.Phone == "+33611110002");
        Assert.Single(sender.TextMessages, m => m.Phone == "+33612345678" && m.Message.Contains("lot de 2 commandes"));
        Assert.Contains(sender.TextMessages, m => m.Phone == "+33698765432" && m.Message.Contains("Tournée acceptée"));
    }

    [Fact]
    public async Task SendBatchOffer_WithSingleOrder_ShouldUseSimpleText()
    {
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(sender);

        await service.SendBatchOfferAsync("+33698765432", 1, "ABCD1234");

        var sent = Assert.Single(sender.TextMessages);
        Assert.Contains("ACCEPTE ABCD1234", sent.Message);
        Assert.DoesNotContain("groupée", sent.Message);
    }

    private static User CreateVendor(string name, string? phone, int credits)
    {
        var vendor = new User(name, "hash", UserRole.Vendor, phone);
        if (credits > 0)
            vendor.AddCredits(credits);
        return vendor;
    }

    private sealed class RecordingWhatsAppSender : IWhatsAppSender
    {
        public List<(string Phone, string Message)> TextMessages { get; } = new();
        public List<(string Phone, string Template, Dictionary<string, string> Variables)> TemplateMessages { get; } = new();

        public Task SendTemplateAsync(string toPhoneNumber, string templateName, Dictionary<string, string> variables)
        {
            TemplateMessages.Add((toPhoneNumber, templateName, variables));
            return Task.CompletedTask;
        }

        public Task SendTextMessageAsync(string toPhoneNumber, string message)
        {
            TextMessages.Add((toPhoneNumber, message));
            return Task.CompletedTask;
        }
    }
}
