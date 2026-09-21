using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;

namespace Wazap.Infrastructure.Services;

/// <summary>
/// Téléchargement des médias entrants (photos d'identité livreur, preuve de livraison) reçus via YCloud.
/// Supporte à la fois les URLs directes YCloud et les identifiants media_id Meta/YCloud.
/// </summary>
public sealed class YCloudMediaDownloader : IWhatsAppMediaDownloader
{
    private const long MaxBytes = 10 * 1024 * 1024; // 10 Mo

    private readonly HttpClient _httpClient;
    private readonly YCloudOptions _options;
    private readonly ILogger<YCloudMediaDownloader> _logger;

    public YCloudMediaDownloader(
        HttpClient httpClient,
        YCloudOptions options,
        ILogger<YCloudMediaDownloader> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<(byte[] Content, string FileName)?> TryDownloadAsync(
        string? url, string? mediaId, string? mimeType, CancellationToken ct = default)
    {
        // 1) Si on a une URL directe
        if (!string.IsNullOrWhiteSpace(url))
        {
            return await DownloadFromUrlAsync(url, mimeType, ct);
        }

        // 2) Si on a un mediaId, résolution via YCloud media endpoint
        if (!string.IsNullOrWhiteSpace(mediaId))
        {
            var mediaEndpoint = $"{_options.BaseUrl.TrimEnd('/')}/media/{mediaId}";
            return await DownloadFromUrlAsync(mediaEndpoint, mimeType, ct, withApiKey: true);
        }

        return null;
    }

    private async Task<(byte[] Content, string FileName)?> DownloadFromUrlAsync(
        string targetUrl, string? mimeType, CancellationToken ct, bool withApiKey = false)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
            if (withApiKey && !string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                request.Headers.Add("X-API-Key", _options.ApiKey);
            }

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Échec téléchargement média YCloud ({StatusCode}) sur {Url}", response.StatusCode, targetUrl);
                return null;
            }

            var actualMime = response.Content.Headers.ContentType?.MediaType ?? mimeType ?? "image/jpeg";
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);

            if (bytes.Length > MaxBytes)
            {
                _logger.LogWarning("Média YCloud rejeté : taille excessive ({Length} octets > max 10 Mo)", bytes.Length);
                return null;
            }

            var extension = DeduceExtension(actualMime);
            var fileName = $"ycloud_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}.{extension}";

            return (bytes, fileName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Exception lors du téléchargement du média YCloud");
            return null;
        }
    }

    private static string DeduceExtension(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "image/png" => "png",
        "image/webp" => "webp",
        "image/gif" => "gif",
        "application/pdf" => "pdf",
        _ => "jpg"
    };
}
