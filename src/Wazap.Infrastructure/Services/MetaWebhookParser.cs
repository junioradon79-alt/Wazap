using System.Text.Json;
using Wazap.Application.Helpers;

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
/// <para>
/// La lecture des champs (tolérante : camelCase comme snake_case, nombres comme chaînes) est
/// mutualisée dans <see cref="JsonPayloadReader"/> : le routeur du webhook et cet analyseur en
/// avaient chacun une copie, si bien qu'une correction dans l'une ne profitait pas à l'autre.
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
        var from = JsonPayloadReader.Str(messageObj, "from");
        var type = JsonPayloadReader.Str(messageObj, "type");
        var messageId = JsonPayloadReader.Str(messageObj, "id");

        string? text = null;
        string? buttonId = null;
        string? buttonTitle = null;
        string? mediaId = null;
        string? mimeType = null;
        double? latitude = null;
        double? longitude = null;

        switch (type)
        {
            case "text":
                text = JsonPayloadReader.Str(JsonPayloadReader.Find(messageObj, "text"), "body");
                break;
            case "button":
                buttonTitle = JsonPayloadReader.Str(JsonPayloadReader.Find(messageObj, "button"), "text");
                buttonId = JsonPayloadReader.Str(JsonPayloadReader.Find(messageObj, "button"), "payload");
                break;
            case "interactive":
            {
                var interactive = JsonPayloadReader.Find(messageObj, "interactive");
                var reply = JsonPayloadReader.Find(interactive, "button_reply")
                            ?? JsonPayloadReader.Find(interactive, "nfm_reply");
                buttonId = JsonPayloadReader.Str(reply, "id");
                buttonTitle = JsonPayloadReader.Str(reply, "title");
                break;
            }
            case "location":
            {
                var location = JsonPayloadReader.Find(messageObj, "location");
                latitude = JsonPayloadReader.Dbl(location, "latitude");
                longitude = JsonPayloadReader.Dbl(location, "longitude");
                break;
            }
            case "image":
            case "video":
            case "document":
            case "audio":
            {
                var media = JsonPayloadReader.Find(messageObj, type);
                mediaId = JsonPayloadReader.Str(media, "id");
                mimeType = JsonPayloadReader.Str(media, "mime_type");
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
            null /* mediaUrl : Meta ne fournit qu'un identifiant de média */, mediaId, mimeType, messageId);
    }
}
