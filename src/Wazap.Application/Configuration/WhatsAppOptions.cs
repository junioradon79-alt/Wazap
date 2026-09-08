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

        // Offre de lot groupé. Template approuvé « rider_batch_offer » : {{1}} = nombre de
        // commandes, {{2}} = code de l'offre — le livreur répond « ACCEPTE <code> » (ou
        // clique, si une variante à bouton des métadonnées du template est activée).
        public string TemplateRiderBatchOffer { get; set; } = "rider_batch_offer";

        // Notification client/vendeur après acceptation d'un livreur (variables alignées
        // sur les corps Meta approuvés) :
        //   client : « Bonjour, votre livreur {{2}} a accepté votre commande #{{1}} » → 1 = id court, 2 = nom livreur.
        //   vendeur : « Le livreur {{1}} a accepté la commande #{{3}} de {{2}} » → 1 = nom livreur, 2 = client, 3 = id court.
        // Vides = envoi en texte.
        public string TemplateRiderAssignedClient { get; set; } = "rider_assigned_client";
        public string TemplateRiderAssignedVendor { get; set; } = "rider_assigned_vendor";

        // Code de livraison remis au client (variables : 1 = id court, 2 = code à 4 chiffres).
        // Message DÉDIÉ (et non un ajout au message d'assignation) pour que le code parvienne
        // au client aussi bien en mode texte qu'une fois les templates approuvés par Meta.
        // Vide = envoi en texte.
        public string TemplateDeliveryCode { get; set; } = "";

        // Templates de crédits (vide = envoi en texte). `credit_purchase` : 2 variables
        // (pack, commandes). `low_credit` : 1 variable (crédits restants) ;
        // `no_credit` : aucune variable.
        public string TemplateCreditPurchase { get; set; } = "credit_purchase";
        public string TemplateLowCredit { get; set; } = "low_credit";
        public string TemplateNoCredit { get; set; } = "no_credit";

        // Onboarding vendeur séquencé (J+1 / J+3 / J+7) — variables par étape :
        //   J+1 : 1 = nom du vendeur
        //   J+3 : 1 = nom, 2 = nb de courses livrées (« 0 » sinon)
        //   J+7 : 1 = nom, 2 = code parrainage, 3 = crédits restants
        // Vide = étape non envoyée (le worker réessaie) tant que le template n'est pas approuvé.
        public string TemplateVendorOnboardingDay1 { get; set; } = "";
        public string TemplateVendorOnboardingDay3 { get; set; } = "";
        public string TemplateVendorOnboardingDay7 { get; set; } = "";

        // Templates de prospection (catégorie Marketing) — variables :
        //   prospect_approach : 1 = nom commerce, 2 = commercial, 3 = lien démo/vente
        //   prospect_followup : 1 = nom commerce, 2 = commercial
        //   prospect_offer    : 1 = nom commerce
        //   rider_recruit     : 1 = prénom, 2 = lien WhatsApp inscription
        //   rider_company     : 1 = nom entreprise, 2 = lien WhatsApp partenariat
        // Vides = envoi texte best-effort (le bot / l'outil de campagne les ignore).
        public string TemplateProspectApproach { get; set; } = "";
        public string TemplateProspectFollowup { get; set; } = "";
        public string TemplateProspectOffer { get; set; } = "";
        public string TemplateRiderRecruit { get; set; } = "";
        public string TemplateRiderCompany { get; set; } = "";
    }
}
