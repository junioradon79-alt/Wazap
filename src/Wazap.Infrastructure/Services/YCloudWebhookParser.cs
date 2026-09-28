using System.Text.Json;
using Wazap.Application.Helpers;

namespace Wazap.Infrastructure.Services;

/// <summary>
/// Analyseur des webhooks entrants émis par la passerelle YCloud (Tier-1 Meta Business Solution Provider).
/// Normalise les messages entrants en <see cref="MetaWebhookEvent"/> pour être consommés
/// directement par le routeur existant sans modifier la logique métier.
/// </summary>
public static class YCloudWebhookParser
{
    /// <summary>
    /// Vérifie si le payload JSON brut provient de YCloud.
    /// YCloud envoie un objet contenant "type" commençant par "whatsapp." ou un objet "whatsappInboundMessage".
    /// </summary>
    public static bool IsYCloudPayload(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Object)
            return false;

        if (raw.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
        {
            var type = typeProp.GetString();
            if (type != null && type.StartsWith("whatsapp.", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return raw.TryGetProperty("whatsappInboundMessage", out _);
    }

    /// <summary>
    /// Retourne le premier événement normalisé depuis le payload YCloud, ou <c>null</c>.
    /// </summary>
    public static MetaWebhookEvent? TryParse(JsonElement raw)
    {
        var all = ParseAll(raw);
        return all.Count == 0 ? null : all[0];
    }

    /// <summary>
    /// Extrait tous les messages actionnables d'un webhook YCloud.
    /// Retourne une liste vide pour les accusés de réception ou mises à jour de statut (whatsapp.message.updated).
    /// </summary>
    public static IReadOnlyList<MetaWebhookEvent> ParseAll(JsonElement raw)
    {
        var events = new List<MetaWebhookEvent>();
        if (!IsYCloudPayload(raw))
            return events;

        // Événement d'inbound message officiel YCloud
        if (raw.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
        {
            var eventType = typeProp.GetString();
            // Si c'est un autre type d'événement (ex: statut de livraison, mise à jour template), on ignore
            if (!string.Equals(eventType, "whatsapp.inbound_message.received", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(eventType)
                && !eventType.Contains("inbound", StringComparison.OrdinalIgnoreCase))
            {
                return events;
            }
        }

        // Le conteneur principal de message entrant YCloud
        if (!raw.TryGetProperty("whatsappInboundMessage", out var inbound) || inbound.ValueKind != JsonValueKind.Object)
            return events;

        if (!inbound.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
            return events;

        foreach (var message in messages.EnumerateArray())
        {
            if (message.ValueKind != JsonValueKind.Object)
                continue;

            var parsed = MetaWebhookParser.ParseMessage(message);
            if (parsed is not null)
                events.Add(parsed);
        }

        return events;
    }
}
