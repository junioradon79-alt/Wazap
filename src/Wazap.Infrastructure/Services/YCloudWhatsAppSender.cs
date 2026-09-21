using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Exceptions;
using Wazap.Application.Helpers;

namespace Wazap.Infrastructure.Services;

/// <summary>
/// Envoi via l'API officielle YCloud (Tier-1 Meta Business Solution Provider).
/// Endpoint : POST https://api.ycloud.com/v2/whatsapp/messages/sendDirectly
/// Authentification : en-tête X-API-Key.
/// </summary>
public sealed class YCloudWhatsAppSender : IWhatsAppSender
{
    private readonly HttpClient _httpClient;
    private readonly YCloudOptions _options;
    private readonly IvoryCoastNumberingOptions _ciNumbering;
    private readonly ILogger<YCloudWhatsAppSender> _logger;
    private readonly IWhatsAppMessageLogService? _messageLogService;

    public YCloudWhatsAppSender(
        HttpClient httpClient,
        YCloudOptions options,
        IvoryCoastNumberingOptions ciNumbering,
        ILogger<YCloudWhatsAppSender> logger,
        IWhatsAppMessageLogService? messageLogService = null)
    {
        _httpClient = httpClient;
        _options = options;
        _ciNumbering = ciNumbering;
        _logger = logger;
        _messageLogService = messageLogService;
    }

    /// <summary>
    /// Numéro de destination au format international E.164 (avec le « + »),
    /// en appliquant la conversion 8 → 10 chiffres (plan ARTCI) si nécessaire.
    /// </summary>
    private string PrepareRecipient(string toPhoneNumber)
    {
        var converted = PhoneNumberNormalizer.ConvertOldCiToCurrent(toPhoneNumber, _ciNumbering) ?? toPhoneNumber;
        var digits = PhoneNumberNormalizer.DigitsOnly(converted);
        return digits.StartsWith("+") ? digits : $"+{digits}";
    }

    private string PrepareSender()
    {
        var digits = PhoneNumberNormalizer.DigitsOnly(_options.PhoneNumber);
        return digits.StartsWith("+") ? digits : $"+{digits}";
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
            from = PrepareSender(),
            to = recipient,
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = _options.LanguageCode },
                components = new object[] { new { type = "body", parameters } }
            }
        };

        await SendAsync(payload, $"template {templateName} vers {recipient}", recipient, templateName, null, ct);
    }

    public async Task SendTextMessageAsync(string toPhoneNumber, string message, CancellationToken ct = default)
    {
        var recipient = PrepareRecipient(toPhoneNumber);

        var payload = new
        {
            from = PrepareSender(),
            to = recipient,
            type = "text",
            text = new { body = message }
        };

        await SendAsync(payload, $"message texte vers {recipient}", recipient, null, message, ct);
    }

    private async Task SendAsync(
        object payload,
        string context,
        string recipientPhone,
        string? templateName,
        string? messageText,
        CancellationToken ct)
    {
        try
        {
            var baseEndpoint = _options.BaseUrl.TrimEnd('/');
            var url = baseEndpoint.EndsWith("messages/sendDirectly", StringComparison.OrdinalIgnoreCase)
                ? baseEndpoint
                : $"{baseEndpoint}/messages/sendDirectly";

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("X-API-Key", _options.ApiKey);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Envoi YCloud réussi : {Context}. Réponse : {Content}", context, content);

                if (_messageLogService != null)
                {
                    var messageId = ExtractYCloudMessageId(content);
                    await _messageLogService.LogOutboundAsync(
                        recipientPhone,
                        templateName,
                        messageText,
                        provider: "YCloud",
                        success: true,
                        providerMessageId: messageId,
                        ct: ct);
                }

                return;
            }

            var (errorCode, errorMessage, isPermanent) = ParseYCloudError(content);
            _logger.LogError(
                "Échec d'envoi YCloud ({StatusCode}) sur {Context} : code={Code}, message={Message}",
                response.StatusCode, context, errorCode, errorMessage);

            if (_messageLogService != null)
            {
                await _messageLogService.LogOutboundAsync(
                    recipientPhone,
                    templateName,
                    messageText,
                    provider: "YCloud",
                    success: false,
                    errorCode: errorCode,
                    errorMessage: errorMessage,
                    ct: ct);
            }

            throw new WhatsAppSendException(
                $"YCloud a refusé l'envoi ({context}) : code={errorCode}, message={errorMessage}",
                isPermanent: isPermanent);
        }
        catch (WhatsAppSendException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Erreur réseau/technique lors de l'appel YCloud pour {Context}", context);

            if (_messageLogService != null)
            {
                await _messageLogService.LogOutboundAsync(
                    recipientPhone,
                    templateName,
                    messageText,
                    provider: "YCloud",
                    success: false,
                    errorMessage: ex.Message,
                    ct: ct);
            }

            throw;
        }
    }

    private static int ParseVariableIndex(string key)
    {
        if (int.TryParse(key, out var directIndex)) return directIndex;
        var digits = new string(key.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var parsed) ? parsed : int.MaxValue;
    }

    internal static string? ExtractYCloudMessageId(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("id", out var idProp))
                return idProp.GetString();
            if (doc.RootElement.TryGetProperty("messages", out var messagesProp)
                && messagesProp.ValueKind == JsonValueKind.Array
                && messagesProp.GetArrayLength() > 0
                && messagesProp[0].TryGetProperty("id", out var innerId))
                return innerId.GetString();
        }
        catch { }
        return null;
    }

    internal static (int? Code, string Message, bool IsPermanent) ParseYCloudError(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errorProp))
            {
                int? intCode = null;
                string? strCode = null;

                if (errorProp.TryGetProperty("code", out var codeProp))
                {
                    if (codeProp.ValueKind == JsonValueKind.Number && codeProp.TryGetInt32(out var c))
                        intCode = c;
                    else if (codeProp.ValueKind == JsonValueKind.String)
                        strCode = codeProp.GetString();
                }

                var msg = errorProp.TryGetProperty("message", out var msgProp) ? msgProp.GetString() : "Erreur inconnue";
                var isPermanent = IsPermanentError(intCode, strCode, msg);
                return (intCode, strCode != null ? $"[{strCode}] {msg}" : (msg ?? "Erreur inconnue"), isPermanent);
            }

            if (root.TryGetProperty("message", out var directMsg))
            {
                var intCode = root.TryGetProperty("code", out var directCode) && directCode.TryGetInt32(out var c) ? c : (int?)null;
                var msg = directMsg.GetString() ?? "Erreur";
                return (intCode, msg, IsPermanentError(intCode, null, msg));
            }
        }
        catch { }

        return (null, responseJson, false);
    }

    private static bool IsPermanentError(int? code, string? strCode, string? message)
    {
        if (code is 400 or 401 or 403 or 404 or 100 or 131026)
            return true;

        if (!string.IsNullOrWhiteSpace(strCode))
        {
            var upper = strCode.ToUpperInvariant();
            if (upper.Contains("NOT_VALID") || upper.Contains("INVALID") ||
                upper.Contains("NOT_FOUND") || upper.Contains("UNAUTHORIZED") ||
                upper.Contains("FORBIDDEN") || upper.Contains("TEMPLATE"))
                return true;
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            var lower = message.ToLowerInvariant();
            if (lower.Contains("invalid") || lower.Contains("not exist") || lower.Contains("not found"))
                return true;
        }

        return false;
    }
}
