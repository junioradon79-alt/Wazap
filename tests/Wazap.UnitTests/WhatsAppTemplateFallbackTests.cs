using Microsoft.Extensions.Logging.Abstractions;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Dtos;
using Wazap.Application.Exceptions;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Repli texte quand un template est DÉFINITIVEMENT refusé par Meta (non approuvé, en
/// cours d'examen, suspendu). Sans ce repli, le chemin critique — création de commande et
/// diffusion des courses — dépend entièrement de l'approbation Meta.
/// </summary>
public class WhatsAppTemplateFallbackTests
{
    private static WhatsAppOrchestrationService CreateService(IWhatsAppSender sender, WhatsAppOptions? options = null)
        => new(sender, options ?? new WhatsAppOptions(), NullLogger<WhatsAppOrchestrationService>.Instance);

    [Fact]
    public async Task RiderOffer_WhenTemplateRefused_FallsBackToText()
    {
        var sender = new RefusingSender(permanent: true);

        await CreateService(sender).SendRiderOfferAsync("+2250700000000", "A1B2C3D4");

        var (phone, message) = Assert.Single(sender.TextMessages);
        Assert.Equal("+2250700000000", phone);
        // Le livreur doit pouvoir accepter : le code de l'offre est indispensable.
        Assert.Contains("ACCEPTE A1B2C3D4", message);
    }

    [Fact]
    public async Task RiderOffer_WhenTemplateAccepted_SendsNoText()
    {
        var sender = new RecordingWhatsAppSender();

        await CreateService(sender).SendRiderOfferAsync("+2250700000000", "A1B2C3D4");

        Assert.Single(sender.TemplateMessages);
        Assert.Empty(sender.TextMessages);
    }

    [Fact]
    public async Task TransientRefusal_IsNotSwallowed()
    {
        // Une panne passagère doit remonter pour que l'outbox réessaie : la rattraper en
        // texte transformerait un incident temporaire en message dégradé définitif.
        var sender = new RefusingSender(permanent: false);

        await Assert.ThrowsAsync<WhatsAppSendException>(
            () => CreateService(sender).SendRiderOfferAsync("+2250700000000", "A1B2C3D4"));

        Assert.Empty(sender.TextMessages);
    }

    [Fact]
    public async Task OrderCreated_WhenTemplatesRefused_NotifiesVendorAndClientInText()
    {
        var sender = new RefusingSender(permanent: true);

        await CreateService(sender).SendOrderCreatedNotificationAsync(new OrderCreatedNotification(
            Guid.NewGuid(), "Awa Koné", "+2250700000001", "+2250700000002",
            "2 pagnes wax", 15000m, "Chez Awa"));

        Assert.Equal(2, sender.TextMessages.Count);

        var vendorText = sender.TextMessages.Single(m => m.Phone == "+2250700000002").Message;
        var clientText = sender.TextMessages.Single(m => m.Phone == "+2250700000001").Message;

        // Le vendeur doit savoir quoi répondre : le webhook comprend « Confirmer »/« Refuser ».
        Assert.Contains("Confirmer", vendorText);
        Assert.Contains("Awa Koné", vendorText);
        // Le client voit le nom de la boutique, plus le mot « Vendeur ».
        Assert.Contains("Chez Awa", clientText);
    }

    [Fact]
    public async Task ClientTrackingLink_WhenTemplateAccepted_SendsTemplateWithVariables()
    {
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(sender);

        await service.SendClientTrackingLinkAsync("+2250700000003", "CMD12345", "Boutique Wax", "https://wazap.ci/suivi/123");

        var template = Assert.Single(sender.TemplateMessages);
        Assert.Equal("+2250700000003", template.Phone);
        Assert.Equal("client_tracking_link", template.Template);
        Assert.Equal("Boutique Wax", template.Variables["1"]);
        Assert.Equal("CMD12345", template.Variables["2"]);
        Assert.Equal("https://wazap.ci/suivi/123", template.Variables["3"]);
        Assert.Empty(sender.TextMessages);
    }

    [Fact]
    public async Task ClientTrackingLink_WhenTemplateRefused_FallsBackToText()
    {
        var sender = new RefusingSender(permanent: true);
        var service = CreateService(sender);

        await service.SendClientTrackingLinkAsync("+2250700000003", "CMD12345", "Boutique Wax", "https://wazap.ci/suivi/123");

        var text = Assert.Single(sender.TextMessages);
        Assert.Equal("+2250700000003", text.Phone);
        Assert.Contains("CMD12345", text.Message);
        Assert.Contains("https://wazap.ci/suivi/123", text.Message);
    }

    [Fact]
    public async Task OrderDelivered_WhenTemplateAccepted_SendsTemplateWithVariables()
    {
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(sender);
        var order = new Order("Client", "+2250700000003", "+2250700000002", "Colis", 5000m);

        await service.SendDeliveredNotificationAsync(order);

        var template = Assert.Single(sender.TemplateMessages);
        Assert.Equal("+2250700000003", template.Phone);
        Assert.Equal("order_delivered", template.Template);
        Assert.Equal(order.Id.ToString("N")[..8].ToUpperInvariant(), template.Variables["1"]);
        Assert.Empty(sender.TextMessages);
    }

    [Fact]
    public async Task OrderDelivered_WhenTemplateRefused_FallsBackToText()
    {
        var sender = new RefusingSender(permanent: true);
        var service = CreateService(sender);
        var order = new Order("Client", "+2250700000003", "+2250700000002", "Colis", 5000m);

        await service.SendDeliveredNotificationAsync(order);

        var text = Assert.Single(sender.TextMessages);
        Assert.Equal("+2250700000003", text.Phone);
        Assert.Contains("livré", text.Message);
        Assert.Contains("NOTE", text.Message);
    }

    [Fact]
    public async Task DeliveryCode_WhenTemplateAccepted_SendsTemplateWithCode()
    {
        var sender = new RecordingWhatsAppSender();
        var service = CreateService(sender);
        var order = new Order("Client", "+2250700000003", "+2250700000002", "Colis", 5000m);
        order.EnsureDeliveryCode();

        await service.SendDeliveryCodeAsync(order);

        var template = Assert.Single(sender.TemplateMessages);
        Assert.Equal("+2250700000003", template.Phone);
        Assert.Equal("delivery_code", template.Template);
        Assert.Equal(order.DeliveryCode, template.Variables["1"]);
        Assert.Empty(sender.TextMessages);
    }

    [Fact]
    public async Task DeliveryCode_WhenTemplateRefused_FallsBackToText()
    {
        var sender = new RefusingSender(permanent: true);
        var service = CreateService(sender);
        var order = new Order("Client", "+2250700000003", "+2250700000002", "Colis", 5000m);
        order.EnsureDeliveryCode();

        await service.SendDeliveryCodeAsync(order);

        var text = Assert.Single(sender.TextMessages);
        Assert.Equal("+2250700000003", text.Phone);
        Assert.Contains(order.DeliveryCode!, text.Message);
    }

    /// <summary>Passerelle qui refuse tout envoi de template, mais accepte le texte.</summary>
    private sealed class RefusingSender : IWhatsAppSender
    {
        private readonly bool _permanent;

        public RefusingSender(bool permanent) => _permanent = permanent;

        public List<(string Phone, string Message)> TextMessages { get; } = new();

        public Task SendTemplateAsync(string toPhoneNumber, string templateName, Dictionary<string, string> variables, CancellationToken ct = default)
            => throw new WhatsAppSendException($"WhatChimp a refusé l'envoi ({templateName}).", _permanent);

        public Task SendTextMessageAsync(string toPhoneNumber, string message, CancellationToken ct = default)
        {
            TextMessages.Add((toPhoneNumber, message));
            return Task.CompletedTask;
        }
    }
}
