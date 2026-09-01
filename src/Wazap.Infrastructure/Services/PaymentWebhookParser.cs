using System.Text.Json;

namespace Wazap.Infrastructure.Services
{
    /// <summary>
    /// Informations extraites d'un webhook GeniusPay.
    /// </summary>
    public sealed record PaymentWebhookInfo(
        Guid? WazapTransactionId,
        string? Reference,
        decimal? Amount,
        string? Status);

    /// <summary>
    /// Parse le payload d'un webhook GeniusPay de façon tolérante à la casse.
    /// <c>data.metadata.wazap_transaction_id</c> → identifiant interne WAZAP.
    /// </summary>
    public static class PaymentWebhookParser
    {
        public static PaymentWebhookInfo? Parse(JsonElement root)
        {
            var data = Find(root, "data");
            if (data is null || data.Value.ValueKind != JsonValueKind.Object)
                return null;

            var status = Str(data, "status");
            var reference = Str(data, "reference") ?? Str(data, "id");
            var amount = Dbl(data, "amount");

            Guid? wazapTransactionId = null;
            var metadata = Find(data, "metadata");
            var metaValue = Str(metadata, "wazap_transaction_id");
            if (Guid.TryParse(metaValue, out var parsed))
                wazapTransactionId = parsed;

            return new PaymentWebhookInfo(wazapTransactionId, reference, amount, status);
        }

        private static JsonElement? Find(JsonElement? node, string name)
        {
            if (!node.HasValue || node.Value.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var prop in node.Value.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    return prop.Value;
            }
            return null;
        }

        private static string? Str(JsonElement? node, string name)
        {
            var v = Find(node, name);
            return v is { ValueKind: JsonValueKind.String } ? v.Value.GetString() : null;
        }

        private static decimal? Dbl(JsonElement? node, string name)
        {
            var v = Find(node, name);
            if (v is null) return null;

            return v.Value.ValueKind switch
            {
                JsonValueKind.Number when v.Value.TryGetDecimal(out var d) => d,
                JsonValueKind.String when decimal.TryParse(
                    v.Value.GetString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var d) => d,
                _ => null
            };
        }
    }
}
