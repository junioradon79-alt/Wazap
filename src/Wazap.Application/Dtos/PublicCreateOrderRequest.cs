namespace Wazap.Application.Dtos;

/// <summary>
/// Création de commande via l'API publique v1. Le partenaire identifie le vendeur par son
/// numéro WhatsApp (numéro E.164). Aucun JWT requis (authentification par clé API X-Api-Key).
/// </summary>
public sealed record PublicCreateOrderRequest(
    string VendorWhatsAppNumber,
    string Description,
    decimal Amount,
    string ClientName,
    string ClientWhatsAppNumber,
    string? ClientAddress = null);