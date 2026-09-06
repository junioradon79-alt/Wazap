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
}
