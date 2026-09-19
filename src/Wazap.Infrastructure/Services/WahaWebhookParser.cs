using System.Text.Json;
using Wazap.Application.Helpers;

namespace Wazap.Infrastructure.Services;

/// <summary>
/// Analyseur des webhooks entrants émis par la passerelle WAHA (WhatsApp HTTP API).
/// Normalise les événements entrants en <see cref="MetaWebhookEvent"/> pour être consommés
/// directement par le routage existant sans modifier la logique métier.
/// </summary>
public static class WahaWebhookParser
{
    /// <summary>
    /// Vérifie si le payload JSON brut provient de WAHA.
    /// WAHA envoie un objet contenant "event", "session" et "payload".
    /// </summary>
    public static bool IsWahaPayload(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Object)
            return false;

        return raw.TryGetProperty("event", out var eventProp)
               && eventProp.ValueKind == JsonValueKind.String
               && raw.TryGetProperty("session", out _)
               && raw.TryGetProperty("payload", out var payloadProp)
               && payloadProp.ValueKind == JsonValueKind.Object;
    }

    /// <summary>
    /// Retourne l'événement normalisé depuis le payload WAHA, ou <c>null</c> s'il s'agit
    /// d'un message sortant (fromMe), d'un message de groupe (@g.us) ou d'un événement sans message.
    /// </summary>
    public static MetaWebhookEvent? TryParse(JsonElement raw)
    {
        var all = ParseAll(raw);
        return all.Count == 0 ? null : all[0];
    }

    /// <summary>
    /// Extrait tous les événements actionnables du webhook WAHA.
    /// </summary>
    public static IReadOnlyList<MetaWebhookEvent> ParseAll(JsonElement raw)
    {
        var events = new List<MetaWebhookEvent>();
        if (!IsWahaPayload(raw))
            return events;

        var eventType = JsonPayloadReader.Str(raw, "event");
        // WAHA émet "message", "message.any", "message.ack", etc.
        // Seuls les messages entrants réels ("message" ou "message.any") nous intéressent pour le routage.
        if (!string.Equals(eventType, "message", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(eventType, "message.any", StringComparison.OrdinalIgnoreCase))
        {
            return events;
        }

        var payload = JsonPayloadReader.Find(raw, "payload");
        if (payload is null)
            return events;

        var parsed = ParsePayload(payload.Value);
        if (parsed is not null)
        {
            events.Add(parsed);
        }

        return events;
    }

    private static MetaWebhookEvent? ParsePayload(JsonElement payload)
    {
        // 1) Ignorer les messages sortants émis par nous-mêmes (fromMe: true)
        if (payload.TryGetProperty("fromMe", out var fromMeProp)
            && fromMeProp.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        // 2) Récupération de l'expéditeur (ex: "2250701020304@c.us")
        var from = JsonPayloadReader.Str(payload, "from");
        if (string.IsNullOrWhiteSpace(from))
            return null;

        // Ignorer les messages de groupe WhatsApp (se terminant par @g.us ou @temp)
        if (from.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase)
            || from.EndsWith("@temp", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Normalisation au format E.164 (+225...)
        var digits = new string(from.Split('@')[0].Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(digits))
            return null;

        var normalizedPhone = "+" + digits;

        // 3) Identifiant unique du message (utilisé pour la déduplication)
        var messageId = ExtractId(payload);

        // 4) Texte du message
        var text = JsonPayloadReader.Str(payload, "body");

        // 5) Localisation GPS
        double? latitude = null;
        double? longitude = null;
        var location = JsonPayloadReader.Find(payload, "location");
        if (location is not null)
        {
            latitude = JsonPayloadReader.Dbl(location, "latitude");
            longitude = JsonPayloadReader.Dbl(location, "longitude");
        }

        // 6) Boutons ou réponses interactives
        var buttonId = JsonPayloadReader.Str(payload, "selectedButtonId")
                       ?? JsonPayloadReader.Str(payload, "buttonId");
        var buttonTitle = JsonPayloadReader.Str(payload, "selectedDisplayText")
                          ?? JsonPayloadReader.Str(payload, "buttonTitle");

        // 7) Médias (photo CNI, preuve de livraison, audio...)
        string? mediaUrl = null;
        string? mediaId = null;
        string? mimeType = null;

        var media = JsonPayloadReader.Find(payload, "media");
        if (media is not null)
        {
            mediaUrl = JsonPayloadReader.Str(media, "url");
            mediaId = JsonPayloadReader.Str(media, "id");
            mimeType = JsonPayloadReader.Str(media, "mimetype") ?? JsonPayloadReader.Str(media, "mime_type");
        }

        mediaUrl ??= JsonPayloadReader.Str(payload, "mediaUrl");

        // Si le message n'a aucun contenu exploitable
        if (text is null && latitude is null && mediaUrl is null && mediaId is null && buttonId is null)
        {
            return null;
        }

        return new MetaWebhookEvent(
            From: normalizedPhone,
            Text: text,
            Latitude: latitude,
            Longitude: longitude,
            ButtonId: buttonId,
            ButtonTitle: buttonTitle,
            MediaUrl: mediaUrl,
            MediaId: mediaId,
            MimeType: mimeType,
            MessageId: messageId);
    }

    private static string? ExtractId(JsonElement payload)
    {
        if (payload.TryGetProperty("id", out var idProp))
        {
            if (idProp.ValueKind == JsonValueKind.String)
                return idProp.GetString();

            if (idProp.ValueKind == JsonValueKind.Object
                && idProp.TryGetProperty("_serialized", out var serialized)
                && serialized.ValueKind == JsonValueKind.String)
            {
                return serialized.GetString();
            }
        }

        return null;
    }
}
