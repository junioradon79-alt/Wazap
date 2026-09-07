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
                ["2"] = string.IsNullOrWhiteSpace(notification.VendorName)
                    ? "Votre vendeur"
                    : notification.VendorName,
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
            await SendAlertAsync(vendor, _whatsAppOptions.TemplateCreditPurchase,
                $"Vous avez acheté le pack {pack.Name}. Vous disposez maintenant de {vendor.Credits} commandes.",
                new Dictionary<string, string>
                {
                    ["1"] = pack.Name,
                    ["2"] = vendor.Credits.ToString()
                });
        }

        /// <summary>
        /// Alerte de crédits bas (≤ 5) :
        /// « Il vous reste {vendor.Credits} commandes. Rechargez dès maintenant. »
        /// </summary>
        public async Task SendLowCreditAlertAsync(User vendor)
        {
            await SendAlertAsync(vendor, _whatsAppOptions.TemplateLowCredit,
                $"Il vous reste {vendor.Credits} commandes. Rechargez dès maintenant.",
                new Dictionary<string, string>
                {
                    ["1"] = vendor.Credits.ToString()
                });
        }

        /// <summary>
        /// Alerte crédits épuisés :
        /// « Vous n'avez plus de crédits. Achetez un pack pour continuer. »
        /// </summary>
        public async Task SendNoCreditAlertAsync(User vendor)
        {
            await SendAlertAsync(vendor, _whatsAppOptions.TemplateNoCredit,
                "Vous n'avez plus de crédits. Achetez un pack pour continuer.",
                new Dictionary<string, string>());
        }

        /// <summary>
        /// Envoie un message texte à un utilisateur (réponses du webhook aux commandes).
        /// </summary>
        public async Task SendTextAsync(User user, string message)
        {
            if (string.IsNullOrWhiteSpace(user.PhoneNumber))
                return;

            await _whatsAppSender.SendTextMessageAsync(user.PhoneNumber, message);
        }

        /// <summary>
        /// Propose une livraison groupée à un livreur (template si configuré, sinon texte).
        /// </summary>
        public async Task SendBatchOfferAsync(string riderPhoneNumber, int orderCount, string offerCode)
        {
            if (string.IsNullOrWhiteSpace(riderPhoneNumber))
                return;

            if (!string.IsNullOrWhiteSpace(_whatsAppOptions.TemplateRiderBatchOffer))
            {
                await _whatsAppSender.SendTemplateAsync(riderPhoneNumber, _whatsAppOptions.TemplateRiderBatchOffer,
                    new Dictionary<string, string>
                    {
                        ["1"] = orderCount.ToString(),
                        ["2"] = offerCode
                    });
                return;
            }

            var message = orderCount > 1
                ? $"🛵 Livraison groupée : {orderCount} commandes à récupérer chez le vendeur. Répondez ACCEPTE {offerCode}."
                : $"🛵 Livraison disponible. Répondez ACCEPTE {offerCode}.";
            await _whatsAppSender.SendTextMessageAsync(riderPhoneNumber, message);
        }

        /// <summary>
        /// Notifie le client, le vendeur et le livreur après l'acceptation d'une course simple.
        /// <paramref name="vendorPickupLink"/> / <paramref name="dropoffLink"/> = liens Google Maps
        /// (retrait chez le vendeur / livraison chez le client) quand les coordonnées existent.
        /// </summary>
        public async Task SendRiderAssignedAsync(
            Order order,
            User rider,
            string? vendorPickupLink = null,
            string? dropoffLink = null,
            string? riderProfileLine = null)
        {
            var orderCode = order.Id.ToString("N")[..8].ToUpperInvariant();
            var riderName = rider.Username;

            // Client : votre livreur arrive.
            // Numérotation alignée sur le template Meta « Bonjour, votre livreur {{1}} a
            // accepté votre commande #{{2}} » — les variables DOIVENT y apparaître dans
            // l'ordre du texte, sous peine de rejet à la revue.
            await SendStatusAsync(order.ClientWhatsAppNumber, _whatsAppOptions.TemplateRiderAssignedClient,
                $"🛵 {riderName} a accepté votre commande #{orderCode}. Livraison en route !",
                new Dictionary<string, string>
                {
                    ["1"] = riderName,
                    ["2"] = orderCode
                });

            // Vendeur : préparez le colis
            var vendorText = $"🛵 {riderName} a accepté la commande #{orderCode} de {order.ClientName}. Il arrive pour récupérer le colis."
                + (string.IsNullOrWhiteSpace(riderProfileLine) ? string.Empty : $"\n{riderProfileLine}");
            // Template Meta « Le livreur {{1}} a accepté la commande #{{2}} de {{3}} ».
            await SendStatusAsync(order.VendorWhatsAppNumber, _whatsAppOptions.TemplateRiderAssignedVendor,
                vendorText,
                new Dictionary<string, string>
                {
                    ["1"] = riderName,
                    ["2"] = orderCode,
                    ["3"] = order.ClientName
                });

            // Livreur : confirmation de course + détails + liens cartographiques
            var riderText = $"✅ Course acceptée #{orderCode}.\n" +
                            $"📦 À livrer : {order.Description}\n" +
                            "📍 Retrait chez le vendeur.";

            if (!string.IsNullOrWhiteSpace(vendorPickupLink))
                riderText += $"\n🗺️ Retrait : {vendorPickupLink}";

            if (!string.IsNullOrWhiteSpace(dropoffLink))
                riderText += $"\n🗺️ Client : {dropoffLink}";

            if (!string.IsNullOrWhiteSpace(order.DeliveryCode))
                riderText += $"\n🔐 À la remise : demandez son code au client, puis envoyez"
                          + $" LIVRE {orderCode} CODE <4 chiffres>.";

            await SendTextAsync(rider, riderText);

            // Code de livraison au client, en message séparé (preuve de remise).
            await SendDeliveryCodeAsync(order);
        }

        /// <summary>
        /// Transmet au client le code à 4 chiffres qu'il devra donner au livreur à la
        /// remise du colis. Sans effet si la commande n'a pas de code.
        /// </summary>
        public async Task SendDeliveryCodeAsync(Order order)
        {
            if (string.IsNullOrWhiteSpace(order.DeliveryCode))
                return;

            var orderCode = order.Id.ToString("N")[..8].ToUpperInvariant();

            await SendStatusAsync(order.ClientWhatsAppNumber, _whatsAppOptions.TemplateDeliveryCode,
                $"🔐 Votre code de livraison pour la commande #{orderCode} : {order.DeliveryCode}\n" +
                "Donnez-le au livreur UNIQUEMENT quand vous avez le colis en main.",
                new Dictionary<string, string>
                {
                    ["1"] = orderCode,
                    ["2"] = order.DeliveryCode
                });
        }

        /// <summary>
        /// Notifie chaque client, le vendeur (une seule fois) et le livreur après
        /// l'acceptation d'un lot de livraison groupée. Liens Google Maps ajoutés
        /// quand les coordonnées (client / vendeur) existent.
        /// </summary>
        public async Task SendBatchAssignedAsync(User rider, IReadOnlyList<Order> orders,
            string? vendorPickupLink = null, string? riderProfileLine = null)
        {
            if (orders.Count == 0)
                return;

            var riderName = rider.Username;

            // Chaque client est notifié individuellement
            foreach (var order in orders)
            {
                var orderCode = order.Id.ToString("N")[..8].ToUpperInvariant();
                await SendStatusAsync(order.ClientWhatsAppNumber, _whatsAppOptions.TemplateRiderAssignedClient,
                    $"🛵 {riderName} a accepté votre commande #{orderCode}. Livraison en route !",
                    new Dictionary<string, string>
                    {
                        ["1"] = riderName,
                        ["2"] = orderCode
                    });

                // Code de livraison propre à chaque client de la tournée.
                await SendDeliveryCodeAsync(order);
            }

            // Le vendeur reçoit un seul récapitulatif pour le lot
            var first = orders[0];
            var firstCode = first.Id.ToString("N")[..8].ToUpperInvariant();
            var batchVendorText = $"🛵 {riderName} a accepté le lot de {orders.Count} commandes (dont #{firstCode} de {first.ClientName}). Il arrive pour récupérer les colis."
                + (string.IsNullOrWhiteSpace(riderProfileLine) ? string.Empty : $"\n{riderProfileLine}");
            await SendStatusAsync(first.VendorWhatsAppNumber, _whatsAppOptions.TemplateRiderAssignedVendor,
                batchVendorText,
                new Dictionary<string, string>
                {
                    ["1"] = riderName,
                    ["2"] = firstCode,
                    ["3"] = first.ClientName
                });

            // Livreur : confirmation de la tournée groupée + liste détaillée par client
            var details = string.Join("\n", orders.Select(o =>
            {
                var line = $"  • #{o.Id.ToString("N")[..8].ToUpperInvariant()} — {o.ClientName} : {o.Description}";
                var link = ClientMapLink(o);
                return link is null ? line : $"{line}\n     🗺️ {link}";
            }));

            var withCode = orders.Any(o => !string.IsNullOrWhiteSpace(o.DeliveryCode));
            var tourText = $"✅ Tournée acceptée : {orders.Count} commandes à récupérer chez le vendeur.\n" +
                           $"📋 Livraisons :\n{details}\n" +
                           (withCode
                               ? "🔄 Après CHAQUE livraison : demandez son code au client et envoyez LIVRE <code> CODE <4 chiffres>."
                               : "🔄 Envoyez LIVRE <code> après CHAQUE livraison effectuée.");

            if (!string.IsNullOrWhiteSpace(vendorPickupLink))
                tourText += $"\n📍 Retrait : {vendorPickupLink}";

            await SendTextAsync(rider, tourText);
        }

        private static string? ClientMapLink(Order order)
        {
            if (order.ClientLatitude is not { } lat || order.ClientLongitude is not { } lng)
                return null;

            return $"https://www.google.com/maps/search/?api=1&query={lat.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)},{lng.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}";
        }

        /// <summary>
        /// Envoie au client le lien de la page de suivi (après acceptation du vendeur).
        /// </summary>
        public async Task SendClientTrackingLinkAsync(string clientPhone, string orderCode, string vendorName, string trackingUrl)
        {
            await SendStatusAsync(clientPhone, string.Empty,
                $"✅ {vendorName} a accepté votre commande #{orderCode} !\n" +
                $"Confirmez votre adresse pour lancer la livraison : {trackingUrl}",
                new Dictionary<string, string>());
        }

        /// <summary>
        /// Informe le client que sa livraison est lancée (un livreur est recherché).
        /// </summary>
        public async Task SendDispatchStartedAsync(string clientPhone, string orderCode)
        {
            await SendStatusAsync(clientPhone, string.Empty,
                $"🚀 Livraison #{orderCode} lancée ! Un livreur proche est contacté, il arrive bientôt.",
                new Dictionary<string, string>());
        }

        /// <summary>
        /// Notifie le vendeur que le client a validé ses coordonnées et que la recherche
        /// des livreurs est lancée.
        /// </summary>
        public async Task SendVendorDispatchStartedAsync(string vendorPhone, string orderCode, string? clientAddress)
        {
            await SendStatusAsync(vendorPhone, string.Empty,
                $"✅ Coordonnées du client reçues pour la commande #{orderCode}" +
                (string.IsNullOrWhiteSpace(clientAddress) ? string.Empty : $" ({clientAddress})") +
                ". Recherche d'un livreur lancée !",
                new Dictionary<string, string>());
        }

        /// <summary>
        /// Envoie une étape de l'onboarding vendeur (J+1/J+3/J+7) via template WhatsApp.
        /// Retourne false si le template n'est pas encore configuré/approuvé (l'étape
        /// reste programmée et sera réessayée plus tard).
        /// </summary>
        public async Task<bool> TrySendVendorOnboardingAsync(User vendor, int stage, string deliveredStats)
        {
            if (string.IsNullOrWhiteSpace(vendor.PhoneNumber))
                return false;

            var template = stage switch
            {
                1 => _whatsAppOptions.TemplateVendorOnboardingDay1,
                2 => _whatsAppOptions.TemplateVendorOnboardingDay3,
                3 => _whatsAppOptions.TemplateVendorOnboardingDay7,
                _ => null
            };

            if (string.IsNullOrWhiteSpace(template))
                return false;

            var variables = stage switch
            {
                1 => new Dictionary<string, string> { ["1"] = vendor.Username },
                2 => new Dictionary<string, string> { ["1"] = vendor.Username, ["2"] = deliveredStats },
                _ => new Dictionary<string, string>
                {
                    ["1"] = vendor.Username,
                    ["2"] = vendor.ReferralCode ?? vendor.Username,
                    ["3"] = vendor.Credits.ToString()
                }
            };

            await _whatsAppSender.SendTemplateAsync(vendor.PhoneNumber, template, variables);
            return true;
        }

        /// <summary>
        /// Notifie le client que sa livraison a été effectuée (texte, best-effort).
        /// </summary>
        public async Task SendDeliveredNotificationAsync(Order order)
        {
            var orderCode = order.Id.ToString("N")[..8].ToUpperInvariant();
            await SendStatusAsync(order.ClientWhatsAppNumber, string.Empty,
                $"✅ Votre colis #{orderCode} a été livré. Merci d'avoir choisi WAZAP !",
                new Dictionary<string, string>());
        }

        /// <summary>
        /// Notifie le vendeur qu'aucun livreur n'a accepté sa commande (timeout global) —
        /// la commande est annulée, le vendeur peut renvoyer « LIVRAISON » pour réessayer.
        /// </summary>
        public async Task SendNoRiderFoundAsync(string vendorPhone, string orderCode)
        {
            if (string.IsNullOrWhiteSpace(vendorPhone))
                return;

            const string message =
                "😕 Aucun livreur n'a accepté votre commande. Elle a été annulée (aucun crédit débité). " +
                "Renvoyez LIVRAISON pour réessayer dans quelques minutes.";
            await _whatsAppSender.SendTextMessageAsync(vendorPhone, $"{message} #{orderCode}");
        }

        /// <summary>
        /// Envoie un template si un nom est configuré (templates approuvés), sinon un texte.
        /// </summary>
        private async Task SendStatusAsync(
            string? phoneNumber,
            string templateName,
            string textMessage,
            Dictionary<string, string> variables)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
                return;

            if (!string.IsNullOrWhiteSpace(templateName))
            {
                await _whatsAppSender.SendTemplateAsync(phoneNumber, templateName, variables);
                return;
            }

            await _whatsAppSender.SendTextMessageAsync(phoneNumber, textMessage);
        }

        /// <summary>
        /// Envoie un template si un nom est configuré (templates approuvés), sinon un texte.
        /// </summary>
        private async Task SendAlertAsync(
            User vendor,
            string templateName,
            string textMessage,
            Dictionary<string, string> variables)
        {
            if (string.IsNullOrWhiteSpace(vendor.PhoneNumber))
                return;

            if (!string.IsNullOrWhiteSpace(templateName))
            {
                await _whatsAppSender.SendTemplateAsync(vendor.PhoneNumber, templateName, variables);
                return;
            }

            await _whatsAppSender.SendTextMessageAsync(vendor.PhoneNumber, textMessage);
        }
    }
}
