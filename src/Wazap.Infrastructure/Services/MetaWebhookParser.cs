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
    string? MimeType,
    string? MessageId = null);

/// <summary>
/// Extraction des champs d'un payload webhook Meta WhatsApp Cloud :
/// <c>entry[].changes[].value.messages[]</c>, avec gestion des types text / image /
/// bouton / interactif / localisation, et normalisation du <c>from</c> en E.164 avec « + ».
/// <para>
/// <see cref="ParseAll"/> parcourt TOUTES les entrées, TOUS les changements et TOUS les
/// messages : un payload peut en contenir plusieurs (conversation active, envois groupés),
/// et l'ancienne version n'en lisait qu'UN SEUL — les autres étaient perdus sans trace ni
/// réessai. <see cref="MetaWebhookEvent.MessageId"/> porte l'identifiant unique du message
/// (<c>messages[].id</c>), indispensable à la déduplication des reprises de Meta.
/// </para>
/// </summary>
public static class MetaWebhookParser
{
    public static bool IsMetaPayload(JsonElement raw)
        => raw.ValueKind == JsonValueKind.Object
            && raw.TryGetProperty("object", out var objectProp)
            && objectProp.ValueKind == JsonValueKind.String
            && string.Equals(objectProp.GetString(), "whatsapp_business_account", StringComparison.OrdinalIgnoreCase);

    /// <summary>Dernier événement du payload (compatibilité) — <c>null</c> si aucun message exploitable.</summary>
    public static MetaWebhookEvent? TryParse(JsonElement raw)
    {
        var all = ParseAll(raw);
        return all.Count == 0 ? null : all[^1];
    }

    /// <summary>
    /// TOUS les messages d'un payload Cloud API, dans l'ordre du document (entry → changes →
    /// messages). Liste vide si ce n'est pas un payload Cloud API ou s'il ne contient aucun
    /// message (accusés de réception, mises à jour de statut de livraison…).
    /// </summary>
    public static IReadOnlyList<MetaWebhookEvent> ParseAll(JsonElement raw)
    {
        var events = new List<MetaWebhookEvent>();
        if (!IsMetaPayload(raw))
            return events;

        if (!raw.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return events;

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("changes", out var changes)
                || changes.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var change in changes.EnumerateArray())
            {
                if (change.ValueKind != JsonValueKind.Object
                    || !change.TryGetProperty("value", out var value)
                    || value.ValueKind != JsonValueKind.Object)
                    continue;

                if (!value.TryGetProperty("messages", out var messages)
                    || messages.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var message in messages.EnumerateArray())
                {
                    if (message.ValueKind != JsonValueKind.Object)
                        continue;

                    var parsed = ParseMessage(message);
                    if (parsed is not null)
                        events.Add(parsed);
                }
            }
        }

        return events;
    }

    private static MetaWebhookEvent? ParseMessage(JsonElement messageObj)
    {
        var from = Str(messageObj, "from");
        var type = Str(messageObj, "type");
        var messageId = Str(messageObj, "id");

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
            default:
                // Type non pris en charge (sticker, contacts, réaction…) : rien d'exploitable
                // pour le routage, mais le message reste identifié pour la déduplication.
                break;
        }

        // Un message sans expéditeur n'est pas exploitable par le routage métier.
        if (from is null && text is null && latitude is null && mediaId is null && buttonId is null)
            return null;

        var phone = from is null
            ? null
            : "+" + new string(from.Where(char.IsDigit).ToArray());

        return new MetaWebhookEvent(phone, text, latitude, longitude, buttonId, buttonTitle,
            mediaUrl, mediaId, mimeType, messageId);
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