using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;

namespace Wazap.Infrastructure.Services
{
    /// <summary>
    /// Client de l'API marchande GeniusPay (initiation d'un paiement → checkout_url).
    /// Doc : https://geniuspay.ci/docs/api
    /// </summary>
    public sealed class GeniusPayPaymentService : IPaymentService
    {
        private readonly HttpClient _httpClient;
        private readonly GeniusPayOptions _options;
        private readonly ILogger<GeniusPayPaymentService> _logger;

        public GeniusPayPaymentService(
            HttpClient httpClient,
            GeniusPayOptions options,
            ILogger<GeniusPayPaymentService> logger)
        {
            _httpClient = httpClient;
            _options = options;
            _logger = logger;
        }

        public async Task<PaymentResult> RequestPaymentAsync(
            Guid vendorId,
            string packName,
            decimal amount,
            string reference)
        {
            var body = new GeniusPayPaymentRequest
            {
                Amount = amount,
                Currency = "XOF",
                Description = packName,
                SuccessUrl = _options.SuccessUrl,
                ErrorUrl = _options.ErrorUrl,
                Metadata = new Dictionary<string, string>
                {
                    ["wazap_transaction_id"] = reference,
                    ["vendor_id"] = vendorId.ToString()
                }
            };

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_options.BaseUrl.TrimEnd('/')}/payments")
            {
                Content = JsonContent.Create(body)
            };

            request.Headers.TryAddWithoutValidation("X-API-Key", _options.ApiKey);
            request.Headers.TryAddWithoutValidation("X-API-Secret", _options.ApiSecret);

            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GeniusPay a retourné {Status} : {Content}",
                    (int)response.StatusCode, Truncate(content));
                return new PaymentResult(false, null, null, $"Erreur GeniusPay ({(int)response.StatusCode}).");
            }

            var result = JsonSerializer.Deserialize<GeniusPayInitiateResponse>(content);
            if (result?.Data?.CheckoutUrl is null)
            {
                _logger.LogWarning("Réponse GeniusPay inattendue : {Content}", Truncate(content));
                return new PaymentResult(false, null, null, "Réponse GeniusPay invalide.");
            }

            _logger.LogInformation("Paiement GeniusPay initié : id={Id}, url={Url}",
                result.Data.Id, result.Data.CheckoutUrl);

            return new PaymentResult(true, result.Data.Id, result.Data.CheckoutUrl, null);
        }

        private static string Truncate(string value, int max = 500)
            => value.Length <= max ? value : value[..max];

        private sealed class GeniusPayPaymentRequest
        {
            public decimal Amount { get; set; }
            public string Currency { get; set; } = "XOF";
            public string? Description { get; set; }
            public string? SuccessUrl { get; set; }
            public string? ErrorUrl { get; set; }
            public Dictionary<string, string>? Metadata { get; set; }
        }

        private sealed class GeniusPayInitiateResponse
        {
            public bool Success { get; set; }

            [JsonPropertyName("data")]
            public GeniusPayInitiateData? Data { get; set; }
        }

        private sealed class GeniusPayInitiateData
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("checkout_url")]
            public string? CheckoutUrl { get; set; }

            [JsonPropertyName("payment_url")]
            public string? PaymentUrl { get; set; }
        }
    }
}
