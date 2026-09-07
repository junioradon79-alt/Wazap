using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wazap.Application.Abstractions;

namespace Wazap.Infrastructure.Services;

/// <summary>
/// Téléchargement des médias WhatsApp entrants via WhatChimp. Deux formes acceptées,
/// car la passerelle n'expose pas un format de payload média unique et documenté :
/// <list type="bullet">
/// <item>URL directe (lien parfois signé/temporaire) — repli authentifié avec le token
/// API si la passerelle l'exige ;</item>
/// <item>identifiant de média (<c>media_id</c>, style Cloud API) : <c>GET media/{id}</c>
/// renvoie soit l'URL réelle (JSON), soit le binaire lui-même.</item>
/// </list>
/// Le contrôleur extrait des candidats du payload ; ici, on essaie dans l'ordre et on
/// journalise clairement les échecs plutôt que de perdre le média en silence.
/// </summary>
public sealed class WhatChimpMediaDownloader : IWhatsAppMediaDownloader
{
    /// <summary>WhatsApp plafonne déjà les images (~16 Mo) ; 10 Mo suffisent pour une pièce d'identité.</summary>
    private const long MaxBytes = 10 * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly string _apiToken;
    private readonly string _phoneNumberId;
    private readonly string _baseUrl;
    private readonly ILogger<WhatChimpMediaDownloader> _logger;

    public WhatChimpMediaDownloader(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<WhatChimpMediaDownloader> logger)
    {
        _httpClient = httpClient;
        _apiToken = config["WhatChimp:ApiToken"] ?? throw new ArgumentNullException("WhatChimp:ApiToken");
        _phoneNumberId = config["WhatChimp:PhoneNumberId"] ?? throw new ArgumentNullException("WhatChimp:PhoneNumberId");
        _baseUrl = config["WhatChimp:BaseUrl"] ?? "https://app.whatchimp.com/api/v1/whatsapp/";
        _logger = logger;
    }

    public async Task<(byte[] Content, string FileName)?> TryDownloadAsync(
        string? url, string? mediaId, string? mimeType, CancellationToken ct = default)
    {
        // 1) URL directe fournie par la passerelle (le cas le plus courant).
        if (!string.IsNullOrWhiteSpace(url))
        {
            var direct = await TryGetAsync(url, authFallback: true, ct);
            if (direct is not null)
                return (direct, BuildFileName(mimeType, url));
        }

        // 2) Identifiant de média : résolution style Cloud API (JSON { url } ou binaire).
        if (!string.IsNullOrWhiteSpace(mediaId))
        {
            var infoUrl = $"{_baseUrl}media/{Uri.EscapeDataString(mediaId)}"
                + $"?apiToken={Uri.EscapeDataString(_apiToken)}"
                + $"&phone_number_id={Uri.EscapeDataString(_phoneNumberId)}";

            var info = await TryGetAsync(infoUrl, authFallback: false, ct);
            if (info is not null)
            {
                var resolved = TryExtractUrl(info);
                if (resolved is not null)
                {
                    var payload = await TryGetAsync(resolved, authFallback: true, ct);
                    if (payload is not null)
                        return (payload, BuildFileName(mimeType, resolved));
                }
                else if (!LooksLikeJson(info))
                {
                    // Le binaire a été servi directement : c'est le média.
                    return (info, BuildFileName(mimeType, null));
                }
                else
                {
                    _logger.LogWarning(
                        "Média WhatsApp {MediaId} : la résolution a renvoyé un JSON sans URL exploitable.",
                        mediaId);
                }
            }
        }

        return null;
    }

    /// <summary>GET tolérant : journalise les échecs (y compris repli authentifié) et retourne null.</summary>
    private async Task<byte[]?> TryGetAsync(string url, bool authFallback, CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.IsSuccessStatusCode)
                return await ReadCappedAsync(response.Content, ct);

            if (authFallback && !url.Contains("apiToken=", StringComparison.Ordinal))
            {
                var separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
                using var retry = await _httpClient.GetAsync(
                    $"{url}{separator}apiToken={Uri.EscapeDataString(_apiToken)}",
                    HttpCompletionOption.ResponseHeadersRead, ct);
                if (retry.IsSuccessStatusCode)
                    return await ReadCappedAsync(retry.Content, ct);

                _logger.LogWarning(
                    "Média WhatsApp non téléchargeable ({Status} puis {RetryStatus}) : {Url}",
                    (int)response.StatusCode, (int)retry.StatusCode, Sanitize(url));
                return null;
            }

            _logger.LogWarning("Média WhatsApp non téléchargeable ({Status}) : {Url}",
                (int)response.StatusCode, Sanitize(url));
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Média WhatsApp : téléchargement en échec : {Url}", Sanitize(url));
            return null;
        }
    }

    /// <summary>Lecture bornée : au-delà du plafond, on abandonne (jamais de mémoire saturée).</summary>
    private static async Task<byte[]?> ReadCappedAsync(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength is > MaxBytes)
            return null;

        await using var stream = await content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > MaxBytes)
                return null;
            await memory.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return memory.Length == 0 ? null : memory.ToArray();
    }

    /// <summary>URL d'un média résolu dans une réponse JSON ({ "url": … } style Cloud API).</summary>
    private static string? TryExtractUrl(byte[] content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var name in new[] { "url", "link", "media_url", "mediaUrl" })
            {
                if (root.TryGetProperty(name, out var found)
                    && found.ValueKind == JsonValueKind.String
                    && found.GetString() is { } value
                    && value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return value;
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool LooksLikeJson(byte[] content)
    {
        var start = 0;
        while (start < content.Length && char.IsWhiteSpace((char)content[start]))
            start++;
        return start < content.Length && (content[start] == '{' || content[start] == '[');
    }

    /// <summary>Nom de fichier : extension déduite du type MIME, sinon de l'URL, sinon JPG.</summary>
    private static string BuildFileName(string? mimeType, string? url)
    {
        var extension = mimeType?.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "application/pdf" => ".pdf",
            _ => null
        };

        if (extension is null
            && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && !string.IsNullOrWhiteSpace(Path.GetExtension(uri.AbsolutePath)))
            extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();

        if (extension is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".pdf"))
            extension = ".jpg";

        return $"whatsapp-{DateTime.UtcNow:yyyyMMddHHmmss}{extension}";
    }

    /// <summary>URL journalisable : la query est retirée (elle peut porter le token API).</summary>
    private static string Sanitize(string url)
    {
        var index = url.IndexOf('?', StringComparison.Ordinal);
        return index < 0 ? url : url[..index] + "?…";
    }
}
