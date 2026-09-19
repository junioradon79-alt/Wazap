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
/// Envoi de messages WhatsApp via la passerelle WAHA (WhatsApp HTTP API - devlikeapro/waha).
/// Avantages majeurs :
/// <list type="bullet">
/// <item>Aucune restriction de fenêtre de 24h ;</item>
/// <item>Aucune approbation de template Meta requise (conversion automatique des templates en messages naturels) ;</item>
/// <item>Compatibilité totale avec l'écosystème WAZAP et la déduplication des messages.</item>
/// </list>
/// </summary>
public sealed class WahaWhatsAppSender : IWhatsAppSender
{
    private readonly HttpClient _httpClient;
    private readonly WahaOptions _options;
    private readonly IvoryCoastNumberingOptions _ciNumbering;
    private readonly ILogger<WahaWhatsAppSender> _logger;
    private readonly IWhatsAppMessageLogService? _messageLogService;

    public WahaWhatsAppSender(
        HttpClient httpClient,
        WahaOptions options,
        IvoryCoastNumberingOptions ciNumbering,
        ILogger<WahaWhatsAppSender> logger,
        IWhatsAppMessageLogService? messageLogService = null)
    {
        _httpClient = httpClient;
        _options = options;
        _ciNumbering = ciNumbering;
        _logger = logger;
        _messageLogService = messageLogService;
    }

    /// <summary>
    /// Numéro de destination au format chatId attendu par WAHA (&lt;chiffres&gt;@c.us),
    /// avec conversion 8 → 10 chiffres pour la Côte d'Ivoire si activée.
    /// </summary>
    internal string PrepareChatId(string toPhoneNumber)
    {
        var digits = PrepareRecipientDigits(toPhoneNumber);
        return $"{digits}@c.us";
    }

    /// <summary>
    /// Chiffres uniquement pour la journalisation et l'identification.
    /// </summary>
    internal string PrepareRecipientDigits(string toPhoneNumber)
    {
        var converted = PhoneNumberNormalizer.ConvertOldCiToCurrent(toPhoneNumber, _ciNumbering) ?? toPhoneNumber;
        return PhoneNumberNormalizer.DigitsOnly(converted);
    }

    public async Task SendTextMessageAsync(string toPhoneNumber, string message, CancellationToken ct = default)
    {
        var recipientDigits = PrepareRecipientDigits(toPhoneNumber);
        var chatId = PrepareChatId(toPhoneNumber);

        var payload = new
        {
            chatId = chatId,
            text = message,
            session = _options.SessionName
        };

        await SendAsync(payload, "/api/sendText", $"message texte vers {chatId}", recipientDigits, null, message, ct);
    }

    public async Task SendTemplateAsync(string toPhoneNumber, string templateName, Dictionary<string, string> variables, CancellationToken ct = default)
    {
        var recipientDigits = PrepareRecipientDigits(toPhoneNumber);
        var chatId = PrepareChatId(toPhoneNumber);
        var messageText = FormatTemplateMessage(templateName, variables);

        // Si le template bénéficie de boutons cliquables (offres livreurs, confirmation vendeur)
        var buttonPayload = TryBuildInteractiveButtonPayload(chatId, templateName, messageText, variables);
        if (buttonPayload != null)
        {
            try
            {
                await SendAsync(buttonPayload, "/api/sendButtons", $"boutons {templateName} vers {chatId}", recipientDigits, templateName, messageText, ct);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Échec de l'envoi des boutons interactifs pour {Template}, repli en texte.", templateName);
            }
        }

        var payload = new
        {
            chatId = chatId,
            text = messageText,
            session = _options.SessionName
        };

        await SendAsync(payload, "/api/sendText", $"template {templateName} vers {chatId}", recipientDigits, templateName, messageText, ct);
    }

    private object? TryBuildInteractiveButtonPayload(string chatId, string templateName, string messageText, Dictionary<string, string> variables)
    {
        string V(string key, string fallback = "") => variables.TryGetValue(key, out var val) ? val : fallback;

        return templateName switch
        {
            "rider_offer_v2" or "rider_offer" => new
            {
                chatId = chatId,
                text = messageText,
                buttons = new object[]
                {
                    new { id = $"ACCEPTE {V("4")}", text = $"✅ Accepter ({V("3")} F)" },
                    new { id = $"REFUSE {V("4")}", text = "❌ Refuser" }
                },
                session = _options.SessionName
            },
            "order_confirm" => new
            {
                chatId = chatId,
                text = messageText,
                buttons = new object[]
                {
                    new { id = "CONFIRMER", text = "✅ Confirmer" },
                    new { id = "REFUSER", text = "❌ Refuser" }
                },
                session = _options.SessionName
            },
            "rider_batch_offer" => new
            {
                chatId = chatId,
                text = messageText,
                buttons = new object[]
                {
                    new { id = $"ACCEPTE {V("2")}", text = $"✅ Accepter le lot ({V("1")})" },
                    new { id = $"REFUSE {V("2")}", text = "❌ Refuser" }
                },
                session = _options.SessionName
            },
            _ => null
        };
    }

    private async Task SendAsync(
        object payload,
        string endpoint,
        string context,
        string recipientPhone,
        string? templateName,
        string? messageText,
        CancellationToken ct)
    {
        try
        {
            var url = $"{_options.BaseUrl.TrimEnd('/')}{endpoint}";

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                request.Headers.Add("X-Api-Key", _options.ApiKey);
            }

            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Passerelle WAHA : {Context}. Réponse : {Content}", context, content);

                if (_messageLogService != null)
                {
                    var messageId = ExtractWahaMessageId(content);
                    await _messageLogService.LogOutboundAsync(
                        recipientPhone,
                        templateName,
                        messageText,
                        provider: "WAHA",
                        success: true,
                        providerMessageId: messageId,
                        ct: ct);
                }

                return;
            }

            var statusCode = (int)response.StatusCode;
            _logger.LogWarning("Passerelle WAHA : refus HTTP {StatusCode} ({Context}) : {Content}", statusCode, context, content);

            if (_messageLogService != null)
            {
                await _messageLogService.LogOutboundAsync(
                    recipientPhone,
                    templateName,
                    messageText,
                    provider: "WAHA",
                    success: false,
                    errorCode: statusCode,
                    errorMessage: content,
                    ct: ct);
            }

            throw new WhatsAppSendException(
                $"WAHA a refusé l'envoi ({context}) : HTTP {statusCode} {content}",
                isPermanent: statusCode is 400 or 401 or 403 or 404);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Passerelle WAHA : erreur réseau ({Context}).", context);

            if (_messageLogService != null)
            {
                await _messageLogService.LogOutboundAsync(
                    recipientPhone,
                    templateName,
                    messageText,
                    provider: "WAHA",
                    success: false,
                    errorMessage: ex.Message,
                    ct: ct);
            }

            throw;
        }
    }

    /// <summary>
    /// Convertit un nom de template Meta et ses variables en message textuel naturel pour WhatsApp.
    /// </summary>
    internal static string FormatTemplateMessage(string templateName, Dictionary<string, string> variables)
    {
        string V(string key, string fallback = "") =>
            variables.TryGetValue(key, out var val) ? val : fallback;

        return templateName switch
        {
            "order_confirm" =>
                $"🛎️ Nouvelle commande de {V("1", "Client")} : {V("2", "Colis")} — {V("3", "0")} F.\nRépondez Confirmer ou Refuser.",

            "order_received" =>
                $"✅ {V("2", "Votre vendeur")} a bien reçu votre commande #{V("1", "")}. Livraison estimée : {V("3", "15-30 minutes")}.",

            "rider_offer_v2" or "rider_offer" =>
                $"📦 Nouvelle course disponible !\nDépart : {V("1", "Abidjan")}\nArrivée : {V("2", "Abidjan")}\nGain : {V("3", "0")} F\nPour accepter, répondez : ACCEPTE {V("4", "")}",

            "rider_batch_offer" =>
                $"📦 Lot groupé disponible ({V("1", "plusieurs")} livraisons) !\nPour accepter, répondez : ACCEPTE {V("2", "")}",

            "rider_assigned_client" =>
                $"🛵 Votre livreur {V("2", "WAZAP")} a accepté votre commande #{V("1", "")} et arrive pour la récupérer.",

            "rider_assigned_vendor" =>
                $"🛵 Le livreur {V("1", "WAZAP")} a accepté la commande #{V("3", "")} de {V("2", "Client")}. Préparez le colis.",

            "delivery_code" =>
                variables.ContainsKey("2")
                    ? $"🔐 Votre code secret pour la commande #{V("1")} : *{V("2")}*.\nNe le donnez au livreur qu'une fois votre colis en main !"
                    : $"🔐 Votre code secret de livraison : *{V("1")}*.\nNe le donnez au livreur qu'une fois votre colis en main !",

            "client_tracking_link" =>
                $"📍 Suivez votre livraison en direct de {V("1", "votre vendeur")} (commande #{V("2", "")}) :\n{V("3", "")}",

            "order_delivered" or "order_delivred" =>
                $"🎉 Votre commande #{V("1", "")} a été livrée avec succès ! Merci d'avoir utilisé WAZAP.",

            "credit_purchase" =>
                $"✅ Rechargement réussi ! Pack {V("1", "")} activé ({V("2", "")} courses ajoutées à votre solde).",

            "low_credit" =>
                $"⚠️ Attention : il ne vous reste que {V("1", "1")} crédit(s) WAZAP. Rechargez votre compte pour continuer à livrer sans interruption.",

            "no_credit" =>
                "❌ Votre solde de crédits WAZAP est épuisé. Rechargez votre compte pour publier de nouvelles livraisons.",

            "rider_priority_purchase" =>
                $"⭐ Pack Prioritaire {V("1", "")} activé pour {V("2", "")} jours ! Vous êtes proposé en priorité aux commerçants jusqu'au {V("3", "")}.",

            "vendor_onboarding_day1" =>
                $"Bienvenue sur WAZAP {V("1", "")} ! Frais de service WAZAP offerts sur vos 15 premières courses (0 FCFA de commission de mise en relation). Prêt à lancer votre première livraison ?",

            "vendor_onboarding_day3" =>
                $"Bonjour {V("1", "")}, vous avez déjà réalisé {V("2", "0")} livraison(s) avec WAZAP ! Avez-vous besoin d'assistance pour vos prochaines courses ?",

            "vendor_onboarding_day7" =>
                $"Bonjour {V("1", "")}, boostez vos ventes ! Partagez votre code parrainage {V("2", "")} à d'autres commerçants. Solde actuel : {V("3", "0")} crédits.",

            "prospect_approach_v2" or "prospect_approach" =>
                $"Bonjour {V("1", "")}, ici {V("2", "l'équipe")} de WAZAP. Découvrez notre solution de livraison instantanée à Abidjan : {V("3", "")}",

            "prospect_followup_v2" or "prospect_followup" =>
                $"Bonjour {V("1", "")}, avez-vous pu découvrir notre offre de livraison WAZAP ? Je reste disponible pour échanger. Cordialement, {V("2", "l'équipe")}.",

            "prospect_offer_v2" or "prospect_offer" =>
                $"Offre spéciale WAZAP pour {V("1", "")} : Frais de service WAZAP offerts sur vos 15 premières courses (0 FCFA de commission de mise en relation) !",

            "rider_recruit_v2" or "rider_recruit" =>
                $"Bonjour {V("1", "")}, rejoignez la communauté des livreurs WAZAP à Abidjan et augmentez vos revenus quotidiens ! Inscription : {V("2", "")}",

            "rider_company_v2" or "rider_company" =>
                $"Bonjour {V("1", "")}, WAZAP propose aux flottes de livreurs des courses régulières et rentables à Abidjan. Partenariat : {V("2", "")}",

            _ => FallbackFormat(templateName, variables)
        };
    }

    private static string FallbackFormat(string templateName, Dictionary<string, string> variables)
    {
        if (variables.Count == 0)
            return $"[{templateName}]";

        var orderedValues = variables
            .OrderBy(v => int.TryParse(v.Key, out var idx) ? idx : 999)
            .Select(v => v.Value);

        return string.Join(" ", orderedValues);
    }

    /// <summary>
    /// Extrait l'identifiant unique du message depuis la réponse JSON de WAHA.
    /// Exemples de réponses WAHA :
    /// {"id": "false_2250701020304@c.us_3EB0C34B9C496A000000", ...}
    /// ou {"id": {"_serialized": "false_225..."}, ...}
    /// </summary>
    internal static string? ExtractWahaMessageId(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            if (doc.RootElement.TryGetProperty("id", out var idProp))
            {
                if (idProp.ValueKind == JsonValueKind.String)
                {
                    return idProp.GetString();
                }

                if (idProp.ValueKind == JsonValueKind.Object &&
                    idProp.TryGetProperty("_serialized", out var serialized) &&
                    serialized.ValueKind == JsonValueKind.String)
                {
                    return serialized.GetString();
                }
            }
        }
        catch
        {
            // Erreur de parsing tolérée
        }

        return null;
    }
}
