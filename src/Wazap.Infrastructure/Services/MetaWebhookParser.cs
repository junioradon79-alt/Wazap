using System.Text.Json;

namespace Wazap.Infrastructure.Services;

/// <summary>
/// Événement WhatsApp entrant normalisé, quelle que soit la source (passerelle WhatChimp ou
/// API Meta WhatsApp Cloud). Produit par <see cref="MetaWebhookParser"/> à partir du payload
/// Cloud API, et consommé par le routage métier tel quel.
/// </summary>
public sealed record MetaWebhookEvent(
    string? From,
    string? Text,
    double? Latitude,
    double? Longitude,
    string? ButtonId,
    string? ButtonTitle,
    string? MediaUrl,
    string? MediaId,
    string? MimeType);

/// <summary>
/// Extraction des champs d'un payload webhook Meta WhatsApp Cloud :
/// <c>entry[0].changes[0].value.messages[0]</c>, avec gestion des types text / image /
/// bouton / interactif / localisation, et normalisation du <c>from</c> en E.164 avec « + ».
/// Retourne <c>null</c> si ce n'est pas un payload Cloud API (le routeur essaie alors le
/// format WhatChimp).
/// </summary>
public static class MetaWebhookParser
{
    public static bool IsMetaPayload(JsonElement raw)
        => raw.ValueKind == JsonValueKind.Object
            && raw.TryGetProperty("object", out var objectProp)
            && objectProp.ValueKind == JsonValueKind.String
            && string.Equals(objectProp.GetString(), "whatsapp_business_account", StringComparison.OrdinalIgnoreCase);

    public static MetaWebhookEvent? TryParse(JsonElement raw)
    {
        if (!IsMetaPayload(raw))
            return null;

        var value = EntryValue(raw);
        if (value is not { ValueKind: JsonValueKind.Object } obj)
            return null;

        var message = FirstMessage(obj);
        if (message is { ValueKind: JsonValueKind.Object } messageObj)
        {
            var from = Str(messageObj, "from");
            var type = Str(messageObj, "type");

            string? text = null;
            string? buttonId = null;
            string? buttonTitle = null;
            string? mediaUrl = null;
            string? mediaId = null;
            string? mimeType = null;
            double? latitude = null;
            double? longitude = null;

            switch (type)
            {
                case "text":
                    text = Str(Find(messageObj, "text"), "body");
                    break;
                case "button":
                    buttonTitle = Str(Find(messageObj, "button"), "text");
                    buttonId = Str(Find(messageObj, "button"), "payload");
                    break;
                case "interactive":
                {
                    var interactive = Find(messageObj, "interactive");
                    var reply = Find(interactive, "button_reply") ?? Find(interactive, "nfm_reply");
                    buttonId = Str(reply, "id");
                    buttonTitle = Str(reply, "title");
                    break;
                }
                case "location":
                {
                    var location = Find(messageObj, "location");
                    latitude = Dbl(location, "latitude");
                    longitude = Dbl(location, "longitude");
                    break;
                }
                case "image":
                case "video":
                case "document":
                case "audio":
                {
                    var media = Find(messageObj, type);
                    mediaId = Str(media, "id");
                    mimeType = Str(media, "mime_type");
                    break;
                }
            }

            var phone = from is null
                ? null
                : "+" + new string(from.Where(char.IsDigit).ToArray());

            return new MetaWebhookEvent(phone, text, latitude, longitude, buttonId, buttonTitle,
                mediaUrl, mediaId, mimeType);
        }

        return null;
    }
/// <summary>Remonte <c>entry[0].changes[0].value</c> (ou null si absent).</summary>
    private static JsonElement? EntryValue(JsonElement raw)
    {
        if (!raw.TryGetProperty("entry", out var entry) || entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() == 0)
            return null;

        var first = entry[0];
        if (!first.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array || changes.GetArrayLength() == 0)
            return null;

        var change = changes[0];
        return change.ValueKind == JsonValueKind.Object && change.TryGetProperty("value", out var value)
            ? value
            : null;
    }

    /// <summary>Le premier message entrant (les éléments du tableau sont les plus récents).</summary>
    private static JsonElement? FirstMessage(JsonElement value)
    {
        if (!value.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array || messages.GetArrayLength() == 0)
            return null;

        return messages[messages.GetArrayLength() - 1];
    }

    // ---- Lecture tolérante du payload (camelCase ET snake_case) ----

    private static JsonElement? Find(JsonElement? node, string name)
    {
        if (!node.HasValue || node.Value.ValueKind != JsonValueKind.Object)
            return null;

        var target = NormalizeKey(name);
        foreach (var prop in node.Value.EnumerateObject())
        {
            if (NormalizeKey(prop.Name) == target)
                return prop.Value;
        }

        return null;
    }

    private static string NormalizeKey(string name)
        => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string? Str(JsonElement? node, string name)
    {
        var value = Find(node, name);
        if (value is { ValueKind: JsonValueKind.String } found)
            return found.GetString();
        return null;
    }

    private static double? Dbl(JsonElement? node, string name)
    {
        var value = Find(node, name);
        if (value is null)
            return null;

        return value.Value.ValueKind switch
        {
            JsonValueKind.Number => value.Value.GetDouble(),
            JsonValueKind.String when double.TryParse(
                value.Value.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var d) => d,
            _ => null
        };
    }
}