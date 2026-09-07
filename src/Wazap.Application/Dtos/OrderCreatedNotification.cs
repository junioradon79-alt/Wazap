namespace Wazap.Application.Dtos;

public sealed record OrderCreatedNotification(
    Guid OrderId,
    string ClientName,
    string ClientWhatsAppNumber,
    string VendorWhatsAppNumber,
    string Description,
    decimal Amount,
    // Nom de la boutique, affiché au client (« {vendeur} a bien reçu votre commande »).
    // Optionnel et EN DERNIER : les payloads outbox sérialisés avant son ajout restent
    // désérialisables (valeur nulle → libellé de repli).
    string? VendorName = null);
