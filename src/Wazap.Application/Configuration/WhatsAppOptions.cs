namespace Wazap.Application.Configuration
{
    /// <summary>
    /// Noms des templates WhatsApp WhatChimp utilisés par le flux de commande (section « WhatChimp »).
    /// </summary>
    public sealed class WhatsAppOptions
    {
        public const string SectionName = "WhatChimp";

        public string TemplateOrderConfirm { get; set; } = "order_confirm";
        public string TemplateOrderReceived { get; set; } = "order_received";
        public string TemplateRiderOffer { get; set; } = "rider_offer";

        // Offre de lot groupé (variables : 1 = nombre de commandes, 2 = code court).
        // Vide = envoi en texte.
        public string TemplateRiderBatchOffer { get; set; } = "";

        // Notification client/vendeur après acceptation d'un livreur (variables :
        // client : 1 = id court, 2 = nom livreur ; vendeur : 1 = nom livreur, 2 = client, 3 = id court).
        // Vides = envoi en texte.
        public string TemplateRiderAssignedClient { get; set; } = "";
        public string TemplateRiderAssignedVendor { get; set; } = "";

        // Templates de crédits (vide = envoi en texte). À renseigner quand les templates
        // sont créés et approuvés par Meta.
        public string TemplateCreditPurchase { get; set; } = "";
        public string TemplateLowCredit { get; set; } = "";
        public string TemplateNoCredit { get; set; } = "";
    }
}
