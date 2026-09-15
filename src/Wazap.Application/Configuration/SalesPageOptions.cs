namespace Wazap.Application.Configuration;

/// <summary>
/// Options de la page de vente / acquisition de leads (section « SalesPage »).
/// </summary>
public sealed class SalesPageOptions
{
    public const string SectionName = "SalesPage";

    /// <summary>
    /// Numéro WhatsApp du commerce (format international sans « + », ex. 2250708091011)
    /// utilisé pour le bouton « Discuter sur WhatsApp ». Vide = CTA masqué (recontact par l'équipe).
    /// </summary>
    public string? WhatsAppNumber { get; set; }

    /// <summary>
    /// URL publique de base (ex. <c>https://wazap.ci</c>) utilisée pour construire les liens
    /// absolus — QR codes des flyers notamment. Sans elle, l'URL était bâtie depuis l'en-tête
    /// <c>Host</c> de la requête, donc depuis une donnée que l'appelant contrôle : un QR code
    /// pouvait pointer vers le domaine d'un tiers (hameçonnage).
    /// </summary>
    public string? PublicBaseUrl { get; set; }
}
