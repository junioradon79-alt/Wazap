using Microsoft.Extensions.Logging.Abstractions;
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
    private static WhatsAppOrchestrationService CreateService(RecordingWhatsAppSender sender, WhatsAppOptions? options = null)
        => new(sender, options ?? new WhatsAppOptions(), NullLogger<WhatsAppOrchestrationService>.Instance);
    [Fact]
    public async Task SendCreditPurchaseConfirmation_WithApprovedTemplate_SendsTemplate()
    {
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(sender); // défaut : credit_purchase (approuvé)
        var vendor = CreateVendor("Vendeur Test", "+33612345678", credits: 15);
        var pack = new PackConfiguration { Name = "Découverte", Price = 2500m, Credits = 15 };

        await service.SendCreditPurchaseConfirmationAsync(vendor, pack);

        var sent = Assert.Single(sender.TemplateMessages);
        Assert.Equal("credit_purchase", sent.Template);
        // Corps Meta : « Bonjour, votre pack {{1}} est actif. Vous disposez maintenant de {{2}} commandes. »
        Assert.Equal("Découverte", sent.Variables["1"]);
        Assert.Equal("15", sent.Variables["2"]);
        Assert.Empty(sender.TextMessages);
    }

    [Fact]
    public async Task SendCreditPurchaseConfirmation_WhenTemplateEmpty_FallsBackToText()
    {
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(sender, new WhatsAppOptions { TemplateCreditPurchase = "" });
        var vendor = CreateVendor("Vendeur Test", "+33612345678", credits: 15);
        var pack = new PackConfiguration { Name = "Découverte", Price = 2500m, Credits = 15 };

        await service.SendCreditPurchaseConfirmationAsync(vendor, pack);

        var sent = Assert.Single(sender.TextMessages);
        Assert.Equal("+33612345678", sent.Phone);
        Assert.Equal("Vous avez acheté le pack Découverte. Vous disposez maintenant de 15 commandes.", sent.Message);
    }

    [Fact]
    public async Task SendLowCreditAlert_WithApprovedTemplate_SendsTemplate()
    {
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(sender);
        var vendor = CreateVendor("Vendeur Test", "+33612345678", credits: 3);

        await service.SendLowCreditAlertAsync(vendor);

        var sent = Assert.Single(sender.TemplateMessages);
        Assert.Equal("low_credit", sent.Template);
        Assert.Equal("3", Assert.Single(sent.Variables).Value);
        Assert.Empty(sender.TextMessages);
    }

    [Fact]
    public async Task SendNoCreditAlert_WithApprovedTemplate_SendsTemplateWithoutVariables()
    {
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(sender);
        var vendor = CreateVendor("Vendeur Test", "+33612345678", credits: 0);

        await service.SendNoCreditAlertAsync(vendor);

        var sent = Assert.Single(sender.TemplateMessages);
        Assert.Equal("no_credit", sent.Template);
        Assert.Empty(sent.Variables);
        Assert.Empty(sender.TextMessages);
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
    public async Task SendRiderAssigned_WithApprovedTemplates_SendsMappedVariables()
    {
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(sender); // défauts : rider_assigned_client / rider_assigned_vendor (approuvés)
        var order = new Order("Client Test", "+33611112222", "+33612345678", "Commande", 10m);
        var rider = new User("Rider Test", "hash", UserRole.Rider, "+33698765432");
        var orderCode = order.Id.ToString("N")[..8].ToUpperInvariant();

        await service.SendRiderAssignedAsync(order, rider);

        // Client — corps Meta : « Bonjour, votre livreur {{2}} a accepté votre commande #{{1}} »
        var client = Assert.Single(sender.TemplateMessages, m => m.Template == "rider_assigned_client");
        Assert.Equal("+33611112222", client.Phone);
        Assert.Equal(orderCode, client.Variables["1"]);
        Assert.Equal("Rider Test", client.Variables["2"]);

        // Vendeur — corps Meta : « Le livreur {{1}} a accepté la commande #{{3}} de {{2}} »
        var vendor = Assert.Single(sender.TemplateMessages, m => m.Template == "rider_assigned_vendor");
        Assert.Equal("+33612345678", vendor.Phone);
        Assert.Equal("Rider Test", vendor.Variables["1"]);
        Assert.Equal("Client Test", vendor.Variables["2"]);
        Assert.Equal(orderCode, vendor.Variables["3"]);

        // Livreur : toujours en texte (résumé + liens Maps).
        Assert.Contains(sender.TextMessages, m => m.Phone == "+33698765432" && m.Message.StartsWith("✅"));
    }

    [Fact]
    public async Task SendRiderAssigned_WhenTemplatesEmpty_FallsBackToText()
    {
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(sender, new WhatsAppOptions
        {
            TemplateRiderAssignedClient = "",
            TemplateRiderAssignedVendor = ""
        });
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
    public async Task SendBatchAssigned_WithApprovedTemplates_NotifiesClientsAndVendorOnce()
    {
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(sender); // défauts : rider_assigned_client / rider_assigned_vendor (approuvés)
        var rider = new User("Rider Test", "hash", UserRole.Rider, "+33698765432");
        var orders = new List<Order>
        {
            new("Client A", "+33611110001", "+33612345678", "C1", 10m),
            new("Client B", "+33611110002", "+33612345678", "C2", 20m)
        };

        await service.SendBatchAssignedAsync(rider, orders);

        // 2 clients (template) + 1 vendeur (template) + 1 livreur (texte)
        Assert.Equal(2, sender.TemplateMessages.Count(m => m.Template == "rider_assigned_client"));
        Assert.Single(sender.TemplateMessages, m => m.Template == "rider_assigned_vendor");
        Assert.Contains(sender.TextMessages, m => m.Phone == "+33698765432" && m.Message.Contains("Tournée acceptée"));

        // Variables alignées sur le corps Meta client (« votre livreur {{2}} … commande #{{1}} »).
        var firstCode = orders[0].Id.ToString("N")[..8].ToUpperInvariant();
        var clientA = Assert.Single(sender.TemplateMessages, m => m.Phone == "+33611110001");
        Assert.Equal(firstCode, clientA.Variables["1"]);
        Assert.Equal("Rider Test", clientA.Variables["2"]);

        // Récap vendeur : « Le livreur {{1}} a accepté la commande #{{3}} de {{2}} ».
        var vendor = Assert.Single(sender.TemplateMessages, m => m.Template == "rider_assigned_vendor");
        Assert.Equal("Rider Test", vendor.Variables["1"]);
        Assert.Equal("Client A", vendor.Variables["2"]);
        Assert.Equal(firstCode, vendor.Variables["3"]);
    }

    [Fact]
    public async Task SendBatchAssigned_WhenTemplatesEmpty_FallsBackToText()
    {
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(sender, new WhatsAppOptions
        {
            TemplateRiderAssignedClient = "",
            TemplateRiderAssignedVendor = ""
        });
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
    public async Task SendBatchOffer_WithApprovedTemplate_SendsCountAndOfferCode()
    {
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(sender); // défaut : rider_batch_offer (approuvé)

        await service.SendBatchOfferAsync("+33698765432", 3, "ABCD1234");

        var sent = Assert.Single(sender.TemplateMessages);
        Assert.Equal("rider_batch_offer", sent.Template);
        Assert.Empty(sender.TextMessages);
        // Corps Meta : « Livraison disponible : {{1}} commandes … Répondez ACCEPTE {{2}} pour accepter. »
        Assert.Equal(2, sent.Variables.Count);
        Assert.Equal("3", sent.Variables["1"]);
        Assert.Equal("ABCD1234", sent.Variables["2"]);
    }

    [Fact]
    public async Task SendBatchOffer_WithSingleOrder_ShouldUseSimpleText()
    {
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(sender, new WhatsAppOptions { TemplateRiderBatchOffer = "" });

        await service.SendBatchOfferAsync("+33698765432", 1, "ABCD1234");

        var sent = Assert.Single(sender.TextMessages);
        Assert.Contains("ACCEPTE ABCD1234", sent.Message);
        Assert.DoesNotContain("groupée", sent.Message);
        Assert.Empty(sender.TemplateMessages);
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
