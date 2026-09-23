namespace Wazap.Application.Abstractions;

/// <summary>
/// Article extrait automatiquement par l'IA à partir d'une photo, d'un lien web ou d'un texte.
/// </summary>
public sealed record ExtractedProduct(
    string Name,
    decimal Price,
    string? Emoji = null,
    string? Description = null);

/// <summary>
/// Résultat de l'extraction de catalogue par IA.
/// </summary>
public sealed record CatalogExtractionResult(
    bool Success,
    IReadOnlyList<ExtractedProduct> Products,
    string? SourceType = null,
    string? Error = null);

/// <summary>
/// Service d'extraction et de structuration intelligente de catalogue produits (Google Gemini).
/// Permet l'import en 1 clic d'articles depuis :
/// 1. Une photo ou capture d'écran (Marketplace, menu, flyer, statut WhatsApp) ;
/// 2. Un texte brut ou description de publication Facebook ;
/// 3. Une URL publique (site web, e-shop, Linktree, catalogue ouvert).
/// </summary>
public interface ICatalogAiExtractorService
{
    Task<CatalogExtractionResult> ExtractFromImageAsync(byte[] imageBytes, string? mimeType = null, CancellationToken ct = default);
    Task<CatalogExtractionResult> ExtractFromTextAsync(string textContent, CancellationToken ct = default);
    Task<CatalogExtractionResult> ExtractFromUrlAsync(string url, CancellationToken ct = default);
}
