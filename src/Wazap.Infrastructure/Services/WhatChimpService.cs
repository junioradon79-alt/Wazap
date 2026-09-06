using System.Text;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Helpers;
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
    private readonly IvoryCoastNumberingOptions _ciNumbering;

    public WhatChimpService(HttpClient httpClient, IConfiguration config, ILogger<WhatChimpService> logger,
        IvoryCoastNumberingOptions ciNumbering)
    {
        _httpClient = httpClient;
        _apiToken = config["WhatChimp:ApiToken"] ?? throw new ArgumentNullException("WhatChimp:ApiToken");
        _phoneNumberId = config["WhatChimp:PhoneNumberId"] ?? throw new ArgumentNullException("WhatChimp:PhoneNumberId");
        _baseUrl = config["WhatChimp:BaseUrl"] ?? "https://app.whatchimp.com/api/v1/whatsapp/";
        _logger = logger;
        _ciNumbering = ciNumbering;
    }

    /// <summary>
    /// Numéro de destination prêt pour l'envoi : si la conversion 8 → 10 chiffres (plan ARTCI) est
    /// activée et que le numéro stocké est un ancien format ivoirien (+225 + 8 chiffres), on l'envoie
    /// sous sa forme actuelle (+225 + 10 chiffres) pour rester joignable. Sinon : valeur inchangée.
    /// </summary>
    private string PrepareRecipient(string toPhoneNumber)
        => PhoneNumberNormalizer.ConvertOldCiToCurrent(toPhoneNumber, _ciNumbering) ?? toPhoneNumber;

    public async Task SendTemplateAsync(string toPhoneNumber, string templateName, Dictionary<string, string> variables)
    {
        try
        {
            var recipient = PrepareRecipient(toPhoneNumber);
            var sb = new StringBuilder(_baseUrl)
                .Append("send?apiToken=").Append(Uri.EscapeDataString(_apiToken))
                .Append("&phone_number_id=").Append(Uri.EscapeDataString(_phoneNumberId))
                .Append("&phone_number=").Append(Uri.EscapeDataString(recipient))
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
            _logger.LogInformation($"Template {templateName} envoyé à {recipient}. Réponse : {content}");
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
            var recipient = PrepareRecipient(toPhoneNumber);
            var sb = new StringBuilder(_baseUrl)
                .Append("send?apiToken=").Append(Uri.EscapeDataString(_apiToken))
                .Append("&phone_number_id=").Append(Uri.EscapeDataString(_phoneNumberId))
                .Append("&phone_number=").Append(Uri.EscapeDataString(recipient))
                .Append("&message_type=text&message=").Append(Uri.EscapeDataString(message));

            var response = await _httpClient.GetAsync(sb.ToString());
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            _logger.LogInformation($"Message texte envoyé à {recipient}. Réponse : {content}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Erreur lors de l'envoi du message à {toPhoneNumber}");
            throw;
        }
    }
}
