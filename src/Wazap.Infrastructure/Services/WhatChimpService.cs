using System.Text;
using Wazap.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Wazap.Infrastructure.Services;

public class WhatChimpService : IWhatsAppSender
{
    private readonly HttpClient _httpClient;
    private readonly string _apiToken;
    private readonly string _phoneNumberId;
    private readonly string _baseUrl;
    private readonly ILogger<WhatChimpService> _logger;

    public WhatChimpService(HttpClient httpClient, IConfiguration config, ILogger<WhatChimpService> logger)
    {
        _httpClient = httpClient;
        _apiToken = config["WhatChimp:ApiToken"] ?? throw new ArgumentNullException("WhatChimp:ApiToken");
        _phoneNumberId = config["WhatChimp:PhoneNumberId"] ?? throw new ArgumentNullException("WhatChimp:PhoneNumberId");
        _baseUrl = config["WhatChimp:BaseUrl"] ?? "https://app.whatchimp.com/api/v1/whatsapp/";
        _logger = logger;
    }

    public async Task SendTemplateAsync(string toPhoneNumber, string templateName, Dictionary<string, string> variables)
    {
        try
        {
            var sb = new StringBuilder(_baseUrl)
                .Append("send?apiToken=").Append(Uri.EscapeDataString(_apiToken))
                .Append("&phone_number_id=").Append(Uri.EscapeDataString(_phoneNumberId))
                .Append("&phone_number=").Append(Uri.EscapeDataString(toPhoneNumber))
                .Append("&message_type=template&template_name=").Append(Uri.EscapeDataString(templateName));

            int index = 1;
            foreach (var variable in variables)
            {
                sb.Append("&variable").Append(index++)
                  .Append('=').Append(Uri.EscapeDataString(variable.Value));
            }

            var response = await _httpClient.GetAsync(sb.ToString());
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            _logger.LogInformation($"Template {templateName} envoyé à {toPhoneNumber}. Réponse : {content}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Erreur lors de l'envoi du template {templateName} à {toPhoneNumber}");
            throw;
        }
    }

    public async Task SendTextMessageAsync(string toPhoneNumber, string message)
    {
        try
        {
            var sb = new StringBuilder(_baseUrl)
                .Append("send?apiToken=").Append(Uri.EscapeDataString(_apiToken))
                .Append("&phone_number_id=").Append(Uri.EscapeDataString(_phoneNumberId))
                .Append("&phone_number=").Append(Uri.EscapeDataString(toPhoneNumber))
                .Append("&message_type=text&message=").Append(Uri.EscapeDataString(message));

            var response = await _httpClient.GetAsync(sb.ToString());
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            _logger.LogInformation($"Message texte envoyé à {toPhoneNumber}. Réponse : {content}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Erreur lors de l'envoi du message à {toPhoneNumber}");
            throw;
        }
    }
}
