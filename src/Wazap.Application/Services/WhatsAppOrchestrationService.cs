using System.Globalization;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
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
        private readonly WhatsAppOptions _whatsAppOptions;

        public WhatsAppOrchestrationService(IWhatsAppSender whatsAppSender, WhatsAppOptions whatsAppOptions)
        {
            _whatsAppSender = whatsAppSender;
            _whatsAppOptions = whatsAppOptions;
        }

        public async Task SendOrderCreatedNotificationAsync(OrderCreatedNotification notification)
        {
            // Template order_confirm (vendeur) : variables 1=client, 2=description, 3=montant.
            var vendorTemplateData = new Dictionary<string, string>
            {
                ["1"] = notification.ClientName,
                ["2"] = notification.Description,
                ["3"] = notification.Amount.ToString("0.##", CultureInfo.InvariantCulture)
            };

            // Template order_received (client) : variables 1=id court, 2=vendeur, 3=délai.
            var clientTemplateData = new Dictionary<string, string>
            {
                ["1"] = notification.OrderId.ToString("N")[..8].ToUpperInvariant(),
                ["2"] = "Vendeur",
                ["3"] = "15-30 minutes"
            };

            await Task.WhenAll(
                _whatsAppSender.SendTemplateAsync(notification.VendorWhatsAppNumber, _whatsAppOptions.TemplateOrderConfirm, vendorTemplateData),
                _whatsAppSender.SendTemplateAsync(notification.ClientWhatsAppNumber, _whatsAppOptions.TemplateOrderReceived, clientTemplateData));
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
