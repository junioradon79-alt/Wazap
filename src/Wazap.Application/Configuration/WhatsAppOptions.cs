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

        // Templates de crédits (vide = envoi en texte). À renseigner quand les templates
        // sont créés et approuvés par Meta.
        public string TemplateCreditPurchase { get; set; } = "";
        public string TemplateLowCredit { get; set; } = "";
        public string TemplateNoCredit { get; set; } = "";
    }
}
