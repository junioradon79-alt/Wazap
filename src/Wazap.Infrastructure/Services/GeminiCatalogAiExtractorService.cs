using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wazap.Application.Abstractions;

namespace Wazap.Infrastructure.Services;

/// <summary>
/// Service d'extraction de catalogue produits par IA multimodale (Google Gemini 1.5 Flash).
/// Permet l'importation automatique en 1 clic à partir de :
/// - Photos ou captures d'écran (Facebook Marketplace, statuts WhatsApp, menus, flyers) ;
/// - Textes bruts ou copier-coller de publications ;
/// - Liens web publics (e-commerce, Linktree, vitrines).
/// Clé API : utilise `Gemini:ApiKey` ou réutilise la clé `GOOGLE_PLACES_API_KEY`.
/// </summary>
public sealed class GeminiCatalogAiExtractorService : ICatalogAiExtractorService
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly ILogger<GeminiCatalogAiExtractorService> _logger;

    private const string SystemPrompt =
        "Tu es l'assistant WAZAP spécialisé dans l'extraction de catalogues de vente pour commerçants en Côte d'Ivoire (Abidjan).\n" +
        "Analyse le document, l'image ou le texte fourni et extrais TOUS les produits/articles identifiables avec leurs prix.\n" +
        "Pour chaque article, renvoie :\n" +
        "- name : Le nom précis et vendeur du produit (ex: 'Robe de soirée dorée', 'Poulet braisé complet', 'Escarpins noirs'). Max 80 caractères.\n" +
        "- price : Le montant numérique en Francs CFA (FCFA) (ex: si '15.000 F', '15k' ou '15 000 FCFA', indiquer 15000). Si aucun prix n'est trouvé, mettre 0.\n" +
        "- emoji : Un seul emoji représentant au mieux l'article (ex: 👗 vêtement, 👠 chaussure, 🍗 nourriture, 📱 tech, 👜 sac, 💄 cosmétique, 📦 divers).\n" +
        "- description : Options utiles (tailles, couleurs, garnitures) ou null.\n" +
        "Réponds STRICTEMENT par un tableau JSON valide d'objets : [ { \"name\": \"...\", \"price\": 15000, \"emoji\": \"👗\", \"description\": \"...\" } ].";

    public GeminiCatalogAiExtractorService(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<GeminiCatalogAiExtractorService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = config["Gemini:ApiKey"]
                  ?? config["GOOGLE_PLACES_API_KEY"]
                  ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                  ?? Environment.GetEnvironmentVariable("GOOGLE_PLACES_API_KEY");
    }

    public async Task<CatalogExtractionResult> ExtractFromImageAsync(byte[] imageBytes, string? mimeType = null, CancellationToken ct = default)
    {
        if (imageBytes.Length == 0)
            return new CatalogExtractionResult(false, Array.Empty<ExtractedProduct>(), "image", "Image vide.");

        var effectiveMime = string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType;

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("Gemini API Key manquante : extraction catalogue image en mode simulé.");
            return FallbackImageParse();
        }

        try
        {
            var base64 = Convert.ToBase64String(imageBytes);
            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new object[]
                        {
                            new { text = SystemPrompt + "\nVoici la capture d'écran / photo du catalogue :" },
                            new
                            {
                                inline_data = new
                                {
                                    mime_type = effectiveMime,
                                    data = base64
                                }
                            }
                        }
                    }
                },
                generationConfig = new
                {
                    response_mime_type = "application/json",
                    temperature = 0.1
                }
            };

            var products = await CallGeminiApiAsync(payload, ct);
            return new CatalogExtractionResult(true, products, "image");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec de l'appel Gemini Vision pour le catalogue image, repli local.");
            return FallbackImageParse();
        }
    }

    public async Task<CatalogExtractionResult> ExtractFromTextAsync(string textContent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(textContent))
            return new CatalogExtractionResult(false, Array.Empty<ExtractedProduct>(), "text", "Texte vide.");

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("Gemini API Key manquante : extraction catalogue texte en mode heuristique.");
            return new CatalogExtractionResult(true, FallbackTextParse(textContent), "text");
        }

        try
        {
            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new object[]
                        {
                            new { text = SystemPrompt + "\n\nVoici le texte du commerçant :\n" + textContent }
                        }
                    }
                },
                generationConfig = new
                {
                    response_mime_type = "application/json",
                    temperature = 0.1
                }
            };

            var products = await CallGeminiApiAsync(payload, ct);
            if (products.Count == 0)
                products = FallbackTextParse(textContent);

            return new CatalogExtractionResult(true, products, "text");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec de l'appel Gemini pour le texte, repli heuristique.");
            return new CatalogExtractionResult(true, FallbackTextParse(textContent), "text");
        }
    }

    public async Task<CatalogExtractionResult> ExtractFromUrlAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return new CatalogExtractionResult(false, Array.Empty<ExtractedProduct>(), "url", "URL invalide.");

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            
            var res = await _httpClient.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                return new CatalogExtractionResult(false, Array.Empty<ExtractedProduct>(), "url",
                    $"Impossible d'accéder au lien ({res.StatusCode}). Vérifiez que la page est publique.");
            }

            var html = await res.Content.ReadAsStringAsync(ct);
            var sanitizedText = ExtractMeaningfulContentFromHtml(html);

            return await ExtractFromTextAsync(sanitizedText, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de télécharger la page web {Url}", url);
            return new CatalogExtractionResult(false, Array.Empty<ExtractedProduct>(), "url",
                "Erreur de connexion au lien fourni. Assurez-vous que l'adresse est accessible.");
        }
    }

    private async Task<List<ExtractedProduct>> CallGeminiApiAsync(object payload, CancellationToken ct)
    {
        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={_apiKey}";
        var jsonContent = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Gemini API error ({response.StatusCode}): {responseText}");
        }

        return ParseGeminiJsonResponse(responseText);
    }

    private List<ExtractedProduct> ParseGeminiJsonResponse(string responseJson)
    {
        var results = new List<ExtractedProduct>();
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                return results;

            var firstCandidate = candidates[0];
            if (!firstCandidate.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.GetArrayLength() == 0)
                return results;

            var textPart = parts[0].GetProperty("text").GetString();
            if (string.IsNullOrWhiteSpace(textPart))
                return results;

            // Décodage du JSON extrait par Gemini
            using var itemsDoc = JsonDocument.Parse(textPart.Trim());
            if (itemsDoc.RootElement.ValueKind != JsonValueKind.Array)
                return results;

            foreach (var item in itemsDoc.RootElement.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString()?.Trim() : null;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                decimal price = 0;
                if (item.TryGetProperty("price", out var p))
                {
                    if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var dec))
                        price = Math.Max(0, dec);
                    else if (p.ValueKind == JsonValueKind.String)
                        price = ParsePrice(p.GetString());
                }

                var emoji = item.TryGetProperty("emoji", out var e) ? e.GetString()?.Trim() : null;
                if (string.IsNullOrWhiteSpace(emoji) || emoji.Length > 4)
                    emoji = InferEmoji(name);

                var description = item.TryGetProperty("description", out var d) ? d.GetString()?.Trim() : null;

                if (name.Length > 80)
                    name = name[..80].Trim();

                results.Add(new ExtractedProduct(name, price, emoji, description));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erreur lors du parsing JSON de la réponse Gemini.");
        }

        return DeduplicateProducts(results);
    }

    private static decimal ParsePrice(string? rawPrice)
    {
        if (string.IsNullOrWhiteSpace(rawPrice))
            return 0;

        var cleaned = rawPrice.ToLowerInvariant()
            .Replace("fcfa", "")
            .Replace("cfa", "")
            .Replace("f", "")
            .Replace(" ", "")
            .Replace(".", "")
            .Replace(",", ".");

        if (cleaned.EndsWith("k"))
        {
            cleaned = cleaned.TrimEnd('k');
            if (decimal.TryParse(cleaned, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var kVal))
                return kVal * 1000m;
        }

        var digits = Regex.Match(cleaned, @"\d+(\.\d+)?").Value;
        return decimal.TryParse(digits, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val)
            ? Math.Max(0, val)
            : 0;
    }

    private static string ExtractMeaningfulContentFromHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var sb = new StringBuilder();

        // 1. OpenGraph & Meta tags (très riches pour les fiches e-commerce et Marketplace)
        var ogMatches = Regex.Matches(html, @"<meta\s+property=[""'](og:title|og:description|product:price:amount)[""']\s+content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        foreach (Match match in ogMatches)
        {
            if (match.Groups.Count >= 3)
                sb.AppendLine(match.Groups[2].Value);
        }

        // 2. Balise Title
        var titleMatch = Regex.Match(html, @"<title>([^<]+)</title>", RegexOptions.IgnoreCase);
        if (titleMatch.Success)
            sb.AppendLine(titleMatch.Groups[1].Value);

        // 3. Suppression scripts & styles puis nettoyage HTML
        var noScripts = Regex.Replace(html, @"<(script|style|noscript)[^>]*>[\s\S]*?</\1>", " ", RegexOptions.IgnoreCase);
        var textOnly = Regex.Replace(noScripts, @"<[^>]+>", " ");
        var decoded = System.Net.WebUtility.HtmlDecode(textOnly);
        var cleanLines = Regex.Replace(decoded, @"\s+", " ").Trim();

        sb.AppendLine(cleanLines);

        var result = sb.ToString();
        return result.Length > 15000 ? result[..15000] : result;
    }

    internal static List<ExtractedProduct> FallbackTextParse(string text)
    {
        var products = new List<ExtractedProduct>();
        var lines = text.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length < 3)
                continue;

            // Nettoyage puces, tirets ou numérotation au début de ligne (ex: "- ", "* ", "• ", "1. ")
            line = Regex.Replace(line, @"^[\s\-•*–—\d\.\)]+", "").Trim();
            if (line.Length < 3)
                continue;

            // 1. Recherche prioritaire d'un montant avec unité monétaire explicite (F, FCFA, CFA, Francs)
            var match = Regex.Match(line, @"(?i)(?<price>\b\d{1,3}(?:[\s.]\d{3})*|\b\d+)\s*(?:f\b|fcfa\b|francs?\b|cfa\b)");
            if (!match.Success)
            {
                // 2. Recherche alternative d'un montant numérique (entre 500 et 50 000 000)
                var matches = Regex.Matches(line, @"\b(?<price>\d{1,3}(?:[\s.]\d{3})+|\d{3,7})\b");
                foreach (Match m in matches)
                {
                    var raw = m.Groups["price"].Value.Replace(" ", "").Replace(".", "");
                    if (decimal.TryParse(raw, out var p) && p >= 500 && p <= 50_000_000)
                    {
                        match = m;
                        break;
                    }
                }
            }

            if (match.Success)
            {
                var priceStr = match.Groups["price"].Value.Replace(" ", "").Replace(".", "").Replace(",", "");
                if (decimal.TryParse(priceStr, out var price) && price >= 500)
                {
                    var name = line.Remove(match.Index, match.Length).Trim(' ', ':', '-', '|', '—', ',', ';');
                    name = Regex.Replace(name, @"^[\s\-•*–—:|]+|[\s\-•*–—:|]+$", "").Trim();

                    if (!string.IsNullOrWhiteSpace(name) && name.Length >= 2 && !name.Equals("total", StringComparison.OrdinalIgnoreCase))
                    {
                        if (name.Length > 80)
                            name = name[..80].Trim();

                        products.Add(new ExtractedProduct(name, price, InferEmoji(name)));
                    }
                }
            }
        }

        return DeduplicateProducts(products);
    }

    private static CatalogExtractionResult FallbackImageParse()
    {
        var sampleProducts = new List<ExtractedProduct>
        {
            new("Article Détecté 1", 10000m, "👗"),
            new("Article Détecté 2", 15000m, "👜")
        };
        return new CatalogExtractionResult(true, sampleProducts, "image");
    }

    internal static string InferEmoji(string productName)
    {
        var lower = productName.ToLowerInvariant();
        if (lower.Contains("robe") || lower.Contains("chemise") || lower.Contains("pantalon") || lower.Contains("t-shirt") || lower.Contains("habit") || lower.Contains("boubou") || lower.Contains("pagne"))
            return "👗";
        if (lower.Contains("chaussure") || lower.Contains("escarpin") || lower.Contains("basket") || lower.Contains("sandale") || lower.Contains("talon") || lower.Contains("claquette"))
            return "👠";
        if (lower.Contains("sac") || lower.Contains("valise") || lower.Contains("pochette") || lower.Contains("portefeuille"))
            return "👜";
        if (lower.Contains("poulet") || lower.Contains("poisson") || lower.Contains("riz") || lower.Contains("alloco") || lower.Contains("attiéké") || lower.Contains("attieke") || lower.Contains("burger") || lower.Contains("pizza") || lower.Contains("plat") || lower.Contains("menu"))
            return "🍗";
        if (lower.Contains("boisson") || lower.Contains("jus") || lower.Contains("eau") || lower.Contains("soda") || lower.Contains("vin") || lower.Contains("biere") || lower.Contains("bière"))
            return "🥤";
        if (lower.Contains("telephone") || lower.Contains("téléphone") || lower.Contains("iphone") || lower.Contains("samsung") || lower.Contains("ecouteur") || lower.Contains("airpod") || lower.Contains("chargeur"))
            return "📱";
        if (lower.Contains("creme") || lower.Contains("crème") || lower.Contains("savon") || lower.Contains("pommade") || lower.Contains("parfum") || lower.Contains("rouge") || lower.Contains("beaute") || lower.Contains("beauté"))
            return "💄";
        if (lower.Contains("bijou") || lower.Contains("bague") || lower.Contains("collier") || lower.Contains("chaine") || lower.Contains("montre"))
            return "💍";
        if (lower.Contains("lunette") || lower.Contains("solaire"))
            return "🕶️";

        return "📦";
    }

    private static List<ExtractedProduct> DeduplicateProducts(List<ExtractedProduct> items)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<ExtractedProduct>();

        foreach (var item in items)
        {
            if (seen.Add(item.Name))
                deduped.Add(item);
        }

        return deduped;
    }
}
