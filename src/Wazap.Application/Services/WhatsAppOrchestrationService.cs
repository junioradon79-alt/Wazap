using Wazap.Application.Abstractions;
using Wazap.Application.Dtos;
using Wazap.Domain.Configuration;
using Wazap.Domain.Entities;

namespace Wazap.Application.Services
{
    /// <summary>
    /// Orchestration des notifications WhatsApp : commandes (outbox) + événements de crédits
    /// (confirmation d'achat, alerte crédits bas, alerte crédits épuisés).
    /// </summary>
    public sealed class WhatsAppOrchestrationService
    {
        private readonly IWhatsAppSender _whatsAppSender;

        public WhatsAppOrchestrationService(IWhatsAppSender whatsAppSender)
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

        /// <summary>
        /// Confirmation d'achat de pack :
        /// « Vous avez acheté le pack {pack.Name}. Vous disposez maintenant de {vendor.Credits} commandes. »
        /// </summary>
        public async Task SendCreditPurchaseConfirmationAsync(User vendor, PackConfiguration pack)
        {
            await SendTextAsync(vendor,
                $"Vous avez acheté le pack {pack.Name}. Vous disposez maintenant de {vendor.Credits} commandes.");
        }

        /// <summary>
        /// Alerte de crédits bas (≤ 5) :
        /// « Il vous reste {vendor.Credits} commandes. Rechargez dès maintenant. »
        /// </summary>
        public async Task SendLowCreditAlertAsync(User vendor)
        {
            await SendTextAsync(vendor,
                $"Il vous reste {vendor.Credits} commandes. Rechargez dès maintenant.");
        }

        /// <summary>
        /// Alerte crédits épuisés :
        /// « Vous n'avez plus de crédits. Achetez un pack pour continuer. »
        /// </summary>
        public async Task SendNoCreditAlertAsync(User vendor)
        {
            await SendTextAsync(vendor,
                "Vous n'avez plus de crédits. Achetez un pack pour continuer.");
        }

        private async Task SendTextAsync(User vendor, string message)
        {
            if (string.IsNullOrWhiteSpace(vendor.PhoneNumber))
                return;

            await _whatsAppSender.SendTextMessageAsync(vendor.PhoneNumber, message);
        }
    }
}
