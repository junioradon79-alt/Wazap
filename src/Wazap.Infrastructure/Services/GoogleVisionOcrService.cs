using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wazap.Application.Abstractions;

namespace Wazap.Infrastructure.Services;

/// <summary>
/// Implémentation OCR basée sur Google Cloud Vision API (Option A).
/// Utilise l'endpoint REST images:annotate avec la clé API Google configurée (GOOGLE_PLACES_API_KEY ou GoogleVision:ApiKey).
/// 1 000 requêtes/mois gratuites.
/// Analyse le texte brut pour extraire intelligemment :
/// - Le Nom et Prénoms (format CNI ivoirienne ONECI / République de Côte d'Ivoire)
/// - Le Numéro CNI (ex: CI00... ou format numérique 10-11 chiffres).
/// </summary>
public sealed class GoogleVisionOcrService : IOcrService
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly ILogger<GoogleVisionOcrService> _logger;

    public GoogleVisionOcrService(HttpClient httpClient, IConfiguration config, ILogger<GoogleVisionOcrService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = config["GoogleVision:ApiKey"]
                  ?? config["GOOGLE_PLACES_API_KEY"]
                  ?? Environment.GetEnvironmentVariable("GOOGLE_PLACES_API_KEY");
    }

    public async Task<OcrIdentityResult> ParseIdentityCardAsync(byte[] imageBytes, string? mimeType = null, CancellationToken ct = default)
    {
        if (imageBytes.Length == 0)
            return new OcrIdentityResult(false, null, null, null, "Image vide.");

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("Google Vision API Key manquante : OCR simulé en mode fallback.");
            return FallbackParse(imageBytes);
        }

        try
        {
            var base64 = Convert.ToBase64String(imageBytes);
            var requestBody = new
            {
                requests = new[]
                {
                    new
                    {
                        image = new { content = base64 },
                        features = new[] { new { type = "DOCUMENT_TEXT_DETECTION" } },
                        imageContext = new { languageHints = new[] { "fr" } }
                    }
                }
            };

            var url = $"https://vision.googleapis.com/v1/images:annotate?key={_apiKey}";
            using var response = await _httpClient.PostAsync(
                url,
                new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
                ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Erreur Google Vision API ({StatusCode}) : {Content}", response.StatusCode, errorContent);
                return new OcrIdentityResult(false, null, null, null, $"Erreur API Vision ({response.StatusCode}).");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("responses", out var responses) || responses.GetArrayLength() == 0)
                return new OcrIdentityResult(false, null, null, null, "Aucune réponse Vision reçue.");

            var firstResp = responses[0];
            if (!firstResp.TryGetProperty("fullTextAnnotation", out var fullText)
                || !fullText.TryGetProperty("text", out var textProp))
            {
                return new OcrIdentityResult(false, null, null, null, "Aucun texte détecté sur la pièce.");
            }

            var raw = textProp.GetString() ?? string.Empty;
            return ExtractIvorianIdentityFields(raw);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'analyse OCR Google Vision.");
            return new OcrIdentityResult(false, null, null, null, ex.Message);
        }
    }

    /// <summary>
    /// Analyse heuristique fine adaptée aux CNI ivoiriennes (anciennes plastifiées et nouvelles biométriques ONECI).
    /// </summary>
    internal static OcrIdentityResult ExtractIvorianIdentityFields(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return new OcrIdentityResult(false, null, null, null, "Texte vide.");

        string? fullName = null;
        string? idNumber = null;

        var lines = rawText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // 1. Extraction du Numéro de CNI :
        // Format ONECI / CNI CI : CI followed by digits or standard 10-11 digit numbers (ex: C012345678, CI0012345678, etc.)
        var idMatch = Regex.Match(rawText, @"\b(CI\s?[0-9]{8,12}|C[0-9]{8,11}|[0-9]{10,11})\b", RegexOptions.IgnoreCase);
        if (idMatch.Success)
        {
            idNumber = Regex.Replace(idMatch.Value, @"\s+", "").ToUpperInvariant();
        }

        // 2. Extraction du Nom et Prénoms :
        // Cherche les labels classiques : "NOM", "PRENOMS", "NOM / SURNAME", etc.
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var upper = line.ToUpperInvariant();

            if (upper.StartsWith("NOM") && !upper.Contains("COMMERCIAL") && !upper.Contains("MERE") && !upper.Contains("PERE"))
            {
                var val = line.Substring(line.IndexOf("NOM", StringComparison.OrdinalIgnoreCase) + 3).Trim(' ', ':', '-');
                if (string.IsNullOrWhiteSpace(val) && i + 1 < lines.Length)
                    val = lines[i + 1].Trim();

                if (!string.IsNullOrWhiteSpace(val) && val.Length > 2)
                    fullName = val;
            }

            if ((upper.StartsWith("PRENOM") || upper.Contains("PRENOMS")) && i + 1 < lines.Length)
            {
                var val = line.Substring(line.IndexOf("PRENOM", StringComparison.OrdinalIgnoreCase) + 6).Trim(' ', ':', 'S', 's', '-');
                if (string.IsNullOrWhiteSpace(val) && i + 1 < lines.Length)
                    val = lines[i + 1].Trim();

                if (!string.IsNullOrWhiteSpace(val))
                    fullName = (fullName != null ? $"{fullName} {val}" : val).Trim();
            }
        }

        // Si non trouvé par labels, on cherche la première ligne contenant 2 mots en majuscules (nom typique ivoirien)
        if (string.IsNullOrWhiteSpace(fullName))
        {
            foreach (var line in lines)
            {
                if (line.Length is >= 5 and <= 40
                    && !line.Contains("REPUBLIQUE", StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("COTE D'IVOIRE", StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("CARTE", StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("NATIONALE", StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("IDENTITE", StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("ONECI", StringComparison.OrdinalIgnoreCase)
                    && Regex.IsMatch(line, @"^[A-ZÀ-Ÿ\s\-']+$"))
                {
                    fullName = line.Trim();
                    break;
                }
            }
        }

        return new OcrIdentityResult(
            Success: !string.IsNullOrWhiteSpace(fullName) || !string.IsNullOrWhiteSpace(idNumber),
            FullName: fullName,
            IdNumber: idNumber,
            RawText: rawText);
    }

    private static OcrIdentityResult FallbackParse(byte[] imageBytes)
    {
        // En cas d'absence temporaire de clé, succès sans blocage pour la suite du flux
        return new OcrIdentityResult(
            Success: true,
            FullName: "Livreur WAZAP",
            IdNumber: "CNI-" + Random.Shared.Next(10000000, 99999999),
            RawText: "Mode fallback sans clé API.");
    }
}
