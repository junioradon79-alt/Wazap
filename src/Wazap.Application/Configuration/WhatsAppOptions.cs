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
    }
}
