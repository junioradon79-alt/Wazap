using Wazap.Application.Abstractions;
using Wazap.Application.Dtos;

namespace Wazap.Application.Services;

public sealed class WhatsAppNotificationService
{
    private readonly IWhatsAppSender _whatsAppSender;

    public WhatsAppNotificationService(IWhatsAppSender whatsAppSender)
    {
        _whatsAppSender = whatsAppSender;
    }

    public async Task SendOrderCreatedNotificationAsync(OrderCreatedNotification notification)
    {
        var vendorTemplateData = new Dictionary<string, string>
        {
            { "order_id", notification.OrderId.ToString().Substring(0, 8) },
            { "client_name", notification.ClientName },
            { "description", notification.Description },
            { "amount", notification.Amount.ToString("F2") }
        };

        var clientTemplateData = new Dictionary<string, string>
        {
            { "order_id", notification.OrderId.ToString().Substring(0, 8) },
            { "vendor_name", "Vendeur" },
            { "estimated_time", "15-30 minutes" }
        };

        await Task.WhenAll(
            _whatsAppSender.SendTemplateAsync(notification.VendorWhatsAppNumber, "order_confirmation", vendorTemplateData),
            _whatsAppSender.SendTemplateAsync(notification.ClientWhatsAppNumber, "order_received", clientTemplateData));
    }
}
