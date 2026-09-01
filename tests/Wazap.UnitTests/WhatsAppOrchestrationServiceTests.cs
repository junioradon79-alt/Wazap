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
