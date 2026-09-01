using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;

namespace Wazap.API.Services
{
    /// <summary>
    /// Géocodage d'adresses via Nominatim (OpenStreetMap, gratuit).
    /// </summary>
    public sealed class NominatimGeocodingService : IGeocodingService
    {
        private readonly HttpClient _httpClient;
        private readonly string _userAgent;
        private readonly ILogger<NominatimGeocodingService> _logger;

        public NominatimGeocodingService(HttpClient httpClient, GeoOptions geo, ILogger<NominatimGeocodingService> logger)
        {
            _httpClient = httpClient;
            _userAgent = geo.NominatimUserAgent;
            _logger = logger;
        }

        public async Task<(double Latitude, double Longitude)?> GeocodeAsync(string address, CancellationToken cancellationToken = default)
        {
            var url = $"https://nominatim.openstreetmap.org/search?format=json&limit=1&q={Uri.EscapeDataString(address)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var results = JsonSerializer.Deserialize<List<NominatimResult>>(json);

            if (results is null || results.Count == 0 || results[0].Lat is null || results[0].Lon is null)
                return null;

            var latitude = double.Parse(results[0].Lat!, CultureInfo.InvariantCulture);
            var longitude = double.Parse(results[0].Lon!, CultureInfo.InvariantCulture);

            _logger.LogInformation("Adresse géocodée : {Address} → {Lat}, {Lon}", address, latitude, longitude);
            return (latitude, longitude);
        }

        private sealed class NominatimResult
        {
            [JsonPropertyName("lat")]
            public string? Lat { get; set; }

            [JsonPropertyName("lon")]
            public string? Lon { get; set; }
        }
    }
}
