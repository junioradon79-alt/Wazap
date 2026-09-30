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
                  ?? config["GooglePlaces:ApiKey"]
                  ?? config["GOOGLE_PLACES_API_KEY"]
                  ?? config["Gemini:ApiKey"]
                  ?? config["GEMINI_API_KEY"]
                  ?? Environment.GetEnvironmentVariable("GOOGLE_VISION_API_KEY")
                  ?? Environment.GetEnvironmentVariable("GOOGLE_PLACES_API_KEY")
                  ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
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
    /// Analyse heuristique fine adaptée aux pièces d'identité ivoiriennes :
    /// - CNI (anciennes plastifiées et nouvelles biométriques ONECI)
    /// - Permis de Conduire (Ministère des Transports / Quipux Afrique / DGTTC)
    /// - Passeport
    /// </summary>
    internal static OcrIdentityResult ExtractIvorianIdentityFields(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return new OcrIdentityResult(false, null, null, null, "Texte vide.");

        string? lastName = null;
        string? firstNames = null;
        string? idNumber = null;
        string documentType = "CNI";

        var upperRaw = rawText.ToUpperInvariant();

        // 0. Qualification du Type de Document :
        if (upperRaw.Contains("PERMIS") || upperRaw.Contains("CONDUIRE") || upperRaw.Contains("MINISTERE DES TRANSPORTS") || upperRaw.Contains("QUIPUX"))
        {
            documentType = "Permis de Conduire";
        }
        else if (upperRaw.Contains("PASSEPORT") || upperRaw.Contains("PASSPORT"))
        {
            documentType = "Passeport";
        }

        var lines = rawText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // 1. Extraction du Numéro de la pièce :
        if (documentType == "Permis de Conduire")
        {
            // Format Permis Ivoirien Quipux/Ministère : ex "NIAG01-21-24209886I" ou avec label "5. Numéro du permis..."
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (Regex.IsMatch(line, @"(?:NUM[EÉ]RO\s*(?:DU)?\s*PERMIS|PERMIS\s*(?:DE\s*CONDUIRE)?\s*N[°O\.]?|N[°O\.]\s*DU\s*PERMIS)", RegexOptions.IgnoreCase))
                {
                    // Regarde sur la même ligne après le label
                    var mSame = Regex.Match(line, @"[A-Z0-9\-]{6,25}$", RegexOptions.IgnoreCase);
                    if (mSame.Success && !mSame.Value.Contains("CONDUIRE", StringComparison.OrdinalIgnoreCase))
                    {
                        idNumber = mSame.Value.Trim(' ', ':', '-').ToUpperInvariant();
                        break;
                    }
                    // Ou sur la ligne suivante
                    if (i + 1 < lines.Length)
                    {
                        var nextLine = lines[i + 1].Trim();
                        if (Regex.IsMatch(nextLine, @"^[A-Z0-9\-]{6,25}$", RegexOptions.IgnoreCase))
                        {
                            idNumber = nextLine.ToUpperInvariant();
                            break;
                        }
                    }
                }
            }

            // Regex globale Permis de Conduire CI (ex: NIAG01-21-24209886I)
            if (string.IsNullOrWhiteSpace(idNumber))
            {
                var permisPattern = Regex.Match(rawText, @"\b([A-Z]{2,6}\d{2}-\d{2}-\d{6,10}[A-Z0-9]?)\b", RegexOptions.IgnoreCase);
                if (permisPattern.Success)
                {
                    idNumber = permisPattern.Groups[1].Value.ToUpperInvariant();
                }
            }
        }

        if (string.IsNullOrWhiteSpace(idNumber))
        {
            // Format ONECI / CNI CI : ex: "CI0012345678", "C0123456789", 8-12 chiffres
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (Regex.IsMatch(line, @"(?:N[°O\.]?\s*CNI|ID\s*N[O°\.]?|NUM[EÉ]RO\s*IDENTIT[EÉ])", RegexOptions.IgnoreCase))
                {
                    var mSame = Regex.Match(line, @"\b(CI\s?[0-9]{8,12}|C[0-9]{8,11}|[0-9]{8,12})\b", RegexOptions.IgnoreCase);
                    if (mSame.Success)
                    {
                        idNumber = Regex.Replace(mSame.Value, @"\s+", "").ToUpperInvariant();
                        break;
                    }
                    if (i + 1 < lines.Length)
                    {
                        var mNext = Regex.Match(lines[i + 1], @"\b(CI\s?[0-9]{8,12}|C[0-9]{8,11}|[0-9]{8,12})\b", RegexOptions.IgnoreCase);
                        if (mNext.Success)
                        {
                            idNumber = Regex.Replace(mNext.Value, @"\s+", "").ToUpperInvariant();
                            break;
                        }
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(idNumber))
        {
            var idMatch = Regex.Match(rawText, @"\b(CI\s?[0-9]{8,12}|C[0-9]{8,11}|PC\s?[0-9]{6,10}|[0-9]{8,12})\b", RegexOptions.IgnoreCase);
            if (idMatch.Success)
            {
                idNumber = Regex.Replace(idMatch.Value, @"\s+", "").ToUpperInvariant();
            }
        }

        // 2. Extraction du Nom et Prénoms :
        // Détecte les labels structurés avec ou sans numérotation ("1. Nom", "Nom / Surname", "2. Prénoms", etc.)
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // NOM
            if (Regex.IsMatch(line, @"^\s*(?:\d+[\.\-\)]\s*)?NOM\b", RegexOptions.IgnoreCase)
                && !line.Contains("COMMERCIAL", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("MERE", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("PERE", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("JEUNE", StringComparison.OrdinalIgnoreCase))
            {
                // Regarde après le label sur la même ligne
                var val = Regex.Replace(line, @"^\s*(?:\d+[\.\-\)]\s*)?NOM(?:\s*[\/:]\s*(?:SURNAME|NAME))?\s*[:\-\.]?\s*", "", RegexOptions.IgnoreCase).Trim();
                if (string.IsNullOrWhiteSpace(val) && i + 1 < lines.Length)
                {
                    var next = lines[i + 1].Trim();
                    if (!Regex.IsMatch(next, @"^\s*\d+[\.\-\)]") && !next.Contains("PRENOM", StringComparison.OrdinalIgnoreCase))
                    {
                        val = next;
                    }
                }

                if (!string.IsNullOrWhiteSpace(val) && val.Length >= 2)
                    lastName = val;
            }

            // PRÉNOMS
            if (Regex.IsMatch(line, @"^\s*(?:\d+[\.\-\)]\s*)?PR[EÉ]NOMS?\b", RegexOptions.IgnoreCase))
            {
                var val = Regex.Replace(line, @"^\s*(?:\d+[\.\-\)]\s*)?PR[EÉ]NOMS?(?:\s*[\/:]\s*(?:GIVEN\s*NAMES?|FORENAMES?))?\s*[:\-\.]?\s*", "", RegexOptions.IgnoreCase).Trim();
                if (string.IsNullOrWhiteSpace(val) && i + 1 < lines.Length)
                {
                    var next = lines[i + 1].Trim();
                    if (!Regex.IsMatch(next, @"^\s*\d+[\.\-\)]") && !next.Contains("DATE", StringComparison.OrdinalIgnoreCase))
                    {
                        val = next;
                    }
                }

                if (!string.IsNullOrWhiteSpace(val) && val.Length >= 2)
                    firstNames = val;
            }
        }

        string? fullName = null;
        if (!string.IsNullOrWhiteSpace(lastName) && !string.IsNullOrWhiteSpace(firstNames))
            fullName = $"{lastName} {firstNames}".Trim();
        else if (!string.IsNullOrWhiteSpace(lastName))
            fullName = lastName;
        else if (!string.IsNullOrWhiteSpace(firstNames))
            fullName = firstNames;

        // Fallback heuristique si non trouvé par labels
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
                    && !line.Contains("PERMIS", StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("CONDUIRE", StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("MINISTERE", StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("TRANSPORTS", StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("CATEGORIE", StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("QUIPUX", StringComparison.OrdinalIgnoreCase)
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
            RawText: rawText,
            DocumentType: documentType);
    }

    private static OcrIdentityResult FallbackParse(byte[] imageBytes)
    {
        return new OcrIdentityResult(
            Success: false,
            FullName: null,
            IdNumber: null,
            RawText: null,
            Error: "Clé API Google Vision non configurée sur le serveur. Veuillez renseigner le nom et le numéro manuellement ou configurer la clé API.");
    }
}
