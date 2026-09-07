using System.Text;
using System.Text.Json;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Exceptions;
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

            // L'indice provient de la CLÉ (« 1 », « 2 »…), jamais de l'ordre d'énumération
            // du dictionnaire : celui-ci n'est pas garanti par .NET, et une variable
            // déplacée enverrait le nom du client à la place du code de commande.
            foreach (var variable in variables.OrderBy(v => ParseVariableIndex(v.Key)))
            {
                sb.Append("&variable").Append(ParseVariableIndex(variable.Key))
                  .Append('=').Append(Uri.EscapeDataString(variable.Value));
            }

            var response = await _httpClient.GetAsync(sb.ToString());
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            EnsureGatewayAccepted(content, $"template {templateName} vers {recipient}");
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
            EnsureGatewayAccepted(content, $"message texte vers {recipient}");
            _logger.LogInformation($"Message texte envoyé à {recipient}. Réponse : {content}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Erreur lors de l'envoi du message à {toPhoneNumber}");
            throw;
        }
    }

    /// <summary>Indice d'une variable de template, tiré de sa clé (« 1 », « 2 »…).</summary>
    private static int ParseVariableIndex(string key)
        => int.TryParse(key, out var index) && index > 0
            ? index
            : throw new ArgumentException($"Clé de variable de template invalide : « {key} » (attendu : 1, 2, 3…).");

    /// <summary>
    /// WhatChimp répond <c>HTTP 200</c> même quand l'envoi échoue, en plaçant le verdict
    /// dans le corps (<c>{"status":"0","message":"…"}</c>). Sans cette vérification, un
    /// template refusé par Meta était compté comme envoyé : l'outbox marquait le message
    /// « Sent » et personne n'apprenait que le destinataire n'avait rien reçu.
    /// </summary>
    internal static void EnsureGatewayAccepted(string? content, string context)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        string? status = null;
        string? gatewayMessage = null;

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return;

            if (document.RootElement.TryGetProperty("status", out var statusElement))
            {
                status = statusElement.ValueKind switch
                {
                    JsonValueKind.String => statusElement.GetString(),
                    JsonValueKind.Number => statusElement.GetRawText(),
                    JsonValueKind.False => "0",
                    JsonValueKind.True => "1",
                    _ => null
                };
            }

            if (document.RootElement.TryGetProperty("message", out var messageElement)
                && messageElement.ValueKind == JsonValueKind.String)
            {
                gatewayMessage = messageElement.GetString();
            }
        }
        catch (JsonException)
        {
            // Corps non JSON : on ne peut rien conclure, on laisse passer plutôt que de
            // faire échouer des envois qui fonctionnent.
            return;
        }

        // Prudence délibérée : seul un « status » explicitement négatif est un échec.
        // Une réponse sans « status » reste considérée comme un succès.
        if (status is not "0")
            return;

        var reason = string.IsNullOrWhiteSpace(gatewayMessage) ? content : gatewayMessage;
        throw new WhatsAppSendException($"WhatChimp a refusé l'envoi ({context}) : {reason}", IsPermanent(reason));
    }

    /// <summary>Refus qu'un nouvel essai ne peut pas lever (fenêtre 24 h, template, variables).</summary>
    private static bool IsPermanent(string reason)
    {
        var text = reason.ToLowerInvariant();
        return text.Contains("24 hour") || text.Contains("24 hours")
            || text.Contains("template")
            || text.Contains("parameter") || text.Contains("variable");
    }
}
