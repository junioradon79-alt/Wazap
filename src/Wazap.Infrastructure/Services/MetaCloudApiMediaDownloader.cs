using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;

namespace Wazap.Infrastructure.Services;

/// <summary>
/// Téléchargement des médias WhatsApp entrants reçus via l'API Meta WhatsApp Cloud.
/// Meta ne fournit jamais l'URL directement dans le webhook : il envoie un <c>media_id</c>.
/// Résolution en 2 étapes, toutes deux authentifiées par le jeton Bearer :
/// <list type="bullet">
/// <item><c>GET /{version}/{media_id}</c> → JSON <c>{ "url": …, "mime_type": … }</c> ;</item>
/// <item>puis téléchargement binaire de l'URL retournée (URL signée, requiert l'en-tête Bearer).</item>
/// </list>
/// Comportement conservé : retourne <c>null</c> si irrécupérable (l'appelant répond alors une
/// erreur explicite au livreur). Lecture bornée 10 Mo, nom de fichier déduit du MIME,
/// URLs journalisées SANS query.
/// </summary>
public sealed class MetaCloudApiMediaDownloader : IWhatsAppMediaDownloader
{
    private const long MaxBytes = 10 * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly MetaApiOptions _options;
    private readonly ILogger<MetaCloudApiMediaDownloader> _logger;

    public MetaCloudApiMediaDownloader(
        HttpClient httpClient,
        MetaApiOptions options,
        ILogger<MetaCloudApiMediaDownloader> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<(byte[] Content, string FileName)?> TryDownloadAsync(
        string? url, string? mediaId, string? mimeType, CancellationToken ct = default)
    {
        // 1) Résolution d'un identifiant de média (le format réel des webhooks Meta Cloud API).
        if (!string.IsNullOrWhiteSpace(mediaId))
        {
            var info = await TryGetAsync(MediaInfoUrl(mediaId).ToString(), ct);
            if (info is null)
                return null;

            var (resolvedUrl, resolvedMime) = TryParseMediaInfo(info);
            if (resolvedUrl is null)
                return null;

            // L'URL signée est renvoyée par Meta, mais on ne lui fait pas aveuglément
            // confiance : elle est appelée AVEC le jeton Bearer d'envoi.
            if (!MediaUrlGuard.IsTrustedMetaMediaUrl(resolvedUrl))
            {
                _logger.LogWarning(
                    "Média Meta ignoré : l'URL résolue ne pointe pas vers un domaine Meta officiel ({Host}).",
                    SafeHost(resolvedUrl));
                return null;
            }

            var payload = await TryGetAsync(resolvedUrl, ct);
            if (payload is null)
                return null;

            return (payload, BuildFileName(resolvedMime ?? mimeType, resolvedUrl));
        }

        // 2) URL directe : elle vient du corps du webhook, donc d'un tiers non fiable.
        //    Sans contrôle d'hôte, ce chemin transformait le serveur en proxy authentifié
        //    vers l'hôte de l'attaquant (vol du jeton) et en sonde du réseau interne (SSRF).
        if (!string.IsNullOrWhiteSpace(url))
        {
            if (!MediaUrlGuard.IsTrustedMetaMediaUrl(url))
            {
                _logger.LogWarning(
                    "Média Meta ignoré : URL non fiable dans le payload ({Host}).",
                    SafeHost(url));
                return null;
            }

            var payload = await TryGetAsync(url, ct);
            if (payload is not null)
                return (payload, BuildFileName(mimeType, url));
        }

        return null;
    }

    /// <summary>Hôte seul, pour journaliser sans exposer la query (qui peut porter une signature).</summary>
    private static string SafeHost(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "hôte invalide";

    private Uri MediaInfoUrl(string mediaId)
        => new Uri($"{_options.GraphUrl.TrimEnd('/')}/{_options.ApiVersion}/{Uri.EscapeDataString(mediaId)}");

    /// <summary>GET authentifié Bearer, lecture bornée ; retourne <c>null</c> hors plafond ou en échec.</summary>
    private async Task<byte[]?> TryGetAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            using var content = response.Content;
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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>Extrait l'URL signée et le MIME d'une réponse <c>GET /{media_id}</c> (JSON Graph).</summary>
    private static (string? Url, string? MimeType) TryParseMediaInfo(byte[] content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (null, null);

            string? url = null;
            string? mime = null;

            if (root.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String)
                url = urlProp.GetString();
            if (root.TryGetProperty("mime_type", out var mimeProp) && mimeProp.ValueKind == JsonValueKind.String)
                mime = mimeProp.GetString();

            return (url, mime);
        }
        catch (JsonException)
        {
            return (null, null);
        }
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
}