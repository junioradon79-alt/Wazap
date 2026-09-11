namespace Wazap.Domain.Enums;

/// <summary>
/// Étapes du brouillon de commande construit par le bot WhatsApp conversationnel avec
/// un client (article → commerce → adresse). La commande n'est créée qu'à la fin.
/// </summary>
public enum ClientOrderDraftStage
{
    /// <summary>En attente de l'article/commande du client.</summary>
    AwaitingItems = 1,

        /// <summary>En attente du nom (ou du numéro) du commerce.</summary>
    AwaitingVendor = 2,

    /// <summary>Plusieurs commerces correspondent → le client choisit un numéro.</summary>
    AwaitingVendorChoice = 3,

    /// <summary>
    /// Commerce retenu possède un catalogue produits → le client choisit un ou plusieurs
    /// produits par numéro. Si le commerce n’a pas de catalogue, on passe directement à l’adresse.
    /// </summary>
    AwaitingProductChoice = 7,

    /// <summary>En attente du quartier/adresse de livraison (ou confirmation du panier).</summary>
    AwaitingAddress = 4,

    /// <summary>Commande créée (la conversation est terminée).</summary>
    Completed = 5,

    /// <summary>Conversation annulée par le client ou abandonnée.</summary>
    Cancelled = 6
}
