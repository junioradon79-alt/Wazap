using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Exceptions;
using Wazap.Application.Helpers;

namespace Wazap.Infrastructure.Services;

/// <summary>
/// Envoi via l'API Meta WhatsApp Cloud (Graph), en remplacement de la passerelle WhatChimp.
/// Particularités par rapport à WhatChimp :
/// <list type="bullet">
/// <item>le template est délivré par Meta directement à N'IMPORTE QUEL numéro (pas de
/// fenêtre 24 h, pas de subscriber préalable) ;</item>
/// <item>les variables passent dans le corps JSON (<c>components[].body.parameters[]</c>)
/// dans l'ordre de leurs indices, et non dans la query string ;</item>
/// <item>un échec renvoie un <c>HTTP</c> non-2xx avec <c>error.code</c> — le code est
/// journalisé et traduit en <see cref="WhatsAppSendException"/> (codes permanents
/// identifiés pour que l'outbox ne se relance pas sur une cause indélébile).</item>
/// </list>
/// </summary>
public sealed class MetaCloudApiWhatsAppSender : IWhatsAppSender
{
    private readonly HttpClient _httpClient;
    private readonly MetaApiOptions _options;
    private readonly IvoryCoastNumberingOptions _ciNumbering;
    private readonly ILogger<MetaCloudApiWhatsAppSender> _logger;

    public MetaCloudApiWhatsAppSender(
        HttpClient httpClient,
        MetaApiOptions options,
        IvoryCoastNumberingOptions ciNumbering,
        ILogger<MetaCloudApiWhatsAppSender> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _ciNumbering = ciNumbering;
        _logger = logger;
    }

    /// <summary>
    /// Numéro de destination au format attendu par Meta (E.164 sans « + »), en appliquant la
    /// conversion 8 → 10 chiffres (plan ARTCI) si elle est activée.
    /// </summary>
    private string PrepareRecipient(string toPhoneNumber)
    {
        var converted = PhoneNumberNormalizer.ConvertOldCiToCurrent(toPhoneNumber, _ciNumbering) ?? toPhoneNumber;
        return PhoneNumberNormalizer.DigitsOnly(converted);
    }

    public async Task SendTemplateAsync(string toPhoneNumber, string templateName, Dictionary<string, string> variables, CancellationToken ct = default)
    {
        var recipient = PrepareRecipient(toPhoneNumber);

        var parameters = variables
            .OrderBy(v => ParseVariableIndex(v.Key))
            .Select(v => (object)new { type = "text", text = v.Value })
            .ToArray();

        var payload = new
        {
            messaging_product = "whatsapp",
            to = recipient,
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = _options.LanguageCode },
                components = new object[] { new { type = "body", parameters } }
            }
        };

        await SendAsync(payload, $"template {templateName} vers {recipient}", ct);
    }

    public async Task SendTextMessageAsync(string toPhoneNumber, string message, CancellationToken ct = default)
    {
        var recipient = PrepareRecipient(toPhoneNumber);

        var payload = new
        {
            messaging_product = "whatsapp",
            to = recipient,
            type = "text",
            text = new { preview_url = false, body = message }
        };

        await SendAsync(payload, $"message texte vers {recipient}", ct);
    }

    private async Task SendAsync(object payload, string context, CancellationToken ct)
    {
        try
        {
            var url = $"{_options.GraphUrl.TrimEnd('/')}/{_options.ApiVersion}/{_options.PhoneNumberId}/messages";

            // Pas de « using » volontaire : les tests et l'inspection de la réponse
            // accèdent encore à la requête après l'envoi ; le GC s'en charge.
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Compte Meta Cloud API : {Context}. Réponse : {Content}", context, content);
                return;
            }

            // HTTP non-2xx = refus Meta : extraire code + message (body Graph { error: {…} }).
            var (code, reason) = ParseGraphError(content, (int)response.StatusCode);
            _logger.LogWarning("Compte Meta Cloud API : refus {Code} ({Context}) : {Reason}", code, context, reason);
            throw new WhatsAppSendException(
                $"Meta a refusé l'envoi ({context}) : [{code}] {reason}",
                IsPermanent(code, reason));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Compte Meta Cloud API : erreur réseau ({Context}).", context);
            throw;
        }
    }

    /// <summary>
    /// Extrait <c>error.code</c> et <c>error.message</c> d'un corps Graph API d'échec.
    /// Retourne le code HTTP si le corps n'est pas le JSON d'erreur attendu.
    /// </summary>
    internal static (int Code, string Message) ParseGraphError(string? content, int httpStatus)
    {
        if (!string.IsNullOrWhiteSpace(content))
        {
            try
            {
                using var document = JsonDocument.Parse(content);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("error", out var error)
                    && error.ValueKind == JsonValueKind.Object)
                {
                    var code = error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number
                        ? c.GetInt32()
                        : httpStatus;
                    var message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                        ? m.GetString() ?? string.Empty
                        : content;
                    return (code, message);
                }
            }
            catch (JsonException)
            {
                // Corps non JSON : on retombe sur le statut HTTP.
            }
        }

        return (httpStatus, content ?? string.Empty);
    }

    /// <summary>
    /// Refus qu'un nouvel essai ne peut pas lever : codes de verrouillage / autorisation /
    /// politique Meta, et message mentionnant la « fenêtre 24 h » (comportement historique).
    /// </summary>
    internal static bool IsPermanent(int code, string reason)
    {
        if (code is 131026 or 131029 or 131030 or 131031 or 131032 or 131042 or 132000 or 132001 or 132012)
            return true;

        var text = reason.ToLowerInvariant();
        return text.Contains("24 hour") || text.Contains("24 hours")
            || text.Contains("locked") || text.Contains("permanently blocked");
    }

    /// <summary>Indice d'une variable de template, tiré de sa clé (« 1 », « 2 »…).</summary>
    private static int ParseVariableIndex(string key)
        => int.TryParse(key, out var index) && index > 0
            ? index
            : throw new ArgumentException($"Clé de variable de template invalide : « {key} » (attendu : 1, 2, 3…).");
}