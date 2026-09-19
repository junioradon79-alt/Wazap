using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;

namespace Wazap.Infrastructure.Services;

/// <summary>
/// Téléchargement des médias WhatsApp entrants reçus via la passerelle WAHA
/// (pièces d'identité livreurs « Garantie Colis Sûr », photos de preuve de livraison).
/// </summary>
public sealed class WahaMediaDownloader : IWhatsAppMediaDownloader
{
    private const long MaxBytes = 10 * 1024 * 1024; // 10 Mo max

    private readonly HttpClient _httpClient;
    private readonly WahaOptions _options;
    private readonly ILogger<WahaMediaDownloader> _logger;

    public WahaMediaDownloader(
        HttpClient httpClient,
        WahaOptions options,
        ILogger<WahaMediaDownloader> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<(byte[] Content, string FileName)?> TryDownloadAsync(
        string? url, string? mediaId, string? mimeType, CancellationToken ct = default)
    {
        // 1) Si une URL directe est fournie (ex: URL retournée dans le webhook WAHA)
        var targetUrl = url;

        // Si l'URL est relative, on la préfixe avec l'URL de base WAHA
        if (!string.IsNullOrWhiteSpace(targetUrl) && targetUrl.StartsWith("/"))
        {
            targetUrl = $"{_options.BaseUrl.TrimEnd('/')}{targetUrl}";
        }

        // 2) Si pas d'URL mais un mediaId fourni
        if (string.IsNullOrWhiteSpace(targetUrl) && !string.IsNullOrWhiteSpace(mediaId))
        {
            targetUrl = $"{_options.BaseUrl.TrimEnd('/')}/api/files/{_options.SessionName}/{Uri.EscapeDataString(mediaId)}";
        }

        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(targetUrl));
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                request.Headers.Add("X-Api-Key", _options.ApiKey);
            }

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Téléchargement média WAHA échoué : HTTP {StatusCode} pour {Url}",
                    (int)response.StatusCode, targetUrl);
                return null;
            }

            using var content = response.Content;
            if (content.Headers.ContentLength is > MaxBytes)
            {
                _logger.LogWarning("Média WAHA trop volumineux ({Size} octets) : rejeté.", content.Headers.ContentLength);
                return null;
            }

            await using var stream = await content.ReadAsStreamAsync(ct);
            using var memory = new MemoryStream();
            var buffer = new byte[81920];
            long total = 0;
            int read;

            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > MaxBytes)
                {
                    _logger.LogWarning("Média WAHA a dépassé le plafond de 10 Mo en cours de lecture : rejeté.");
                    return null;
                }
                await memory.WriteAsync(buffer.AsMemory(0, read), ct);
            }

            if (memory.Length == 0) return null;

            var fileName = BuildFileName(mimeType, targetUrl);
            return (memory.ToArray(), fileName);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Erreur réseau lors du téléchargement du média WAHA depuis {Url}.", targetUrl);
            return null;
        }
    }

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
        {
            extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        }

        if (extension is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".pdf"))
        {
            extension = ".jpg";
        }

        return $"whatsapp-{DateTime.UtcNow:yyyyMMddHHmmss}{extension}";
    }
}
