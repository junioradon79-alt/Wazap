namespace Wazap.Application.Services;

/// <summary>
/// Moteur de classification et de calcul de coût unitaire des messages WhatsApp pour la Côte d'Ivoire.
/// Source tarifaire : Région « Rest of Africa » de Meta.
/// - Utility / Authentication : 0,0040 USD ≈ 2,27 FCFA (taux 568,53 XOF/USD).
/// - Marketing : 0,0225 USD ≈ 12,79 FCFA.
/// - Service (texte libre dans fenêtre 24h) : 0 FCFA avant le 01/10/2026, puis 2,27 FCFA.
/// - Échec (Failed) ou Entrant (Inbound) : 0 FCFA.
/// </summary>
public static class WhatsAppCostCalculator
{
    public const decimal UtilityCostFcfa = 2.27m;
    public const decimal AuthenticationCostFcfa = 2.27m;
    public const decimal MarketingCostFcfa = 12.79m;
    public const decimal ServiceCostBeforeOct2026Fcfa = 0m;
    public const decimal ServiceCostAfterOct2026Fcfa = 2.27m;

    private static readonly DateTime ServiceFeeEffectiveDateUtc = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Détermine la catégorie Meta d'un message selon le nom du template ou sa nature.
    /// </summary>
    public static string DetermineCategory(string? templateName)
    {
        if (string.IsNullOrWhiteSpace(templateName))
            return "service";

        var lower = templateName.Trim().ToLowerInvariant();

        if (lower.StartsWith("prospect_") ||
            lower.StartsWith("rider_recruit") ||
            lower.StartsWith("rider_company") ||
            lower == "rider_offer_v2")
        {
            return "marketing";
        }

        if (lower == "delivery_code" || lower.Contains("auth"))
        {
            return "authentication";
        }

        return "utility";
    }

    /// <summary>
    /// Calcule le coût unitaire estimé en FCFA.
    /// </summary>
    public static decimal CalculateEstimatedCost(
        string category,
        string direction,
        string status,
        DateTime? timestampUtc = null)
    {
        // Les messages entrants ou les messages non livrés ne sont pas facturés par Meta.
        if (string.Equals(direction, "Inbound", StringComparison.OrdinalIgnoreCase))
            return 0m;

        if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase))
            return 0m;

        var normalizedCategory = category.Trim().ToLowerInvariant();

        switch (normalizedCategory)
        {
            case "marketing":
                return MarketingCostFcfa;

            case "utility":
                return UtilityCostFcfa;

            case "authentication":
                return AuthenticationCostFcfa;

            case "service":
                var date = timestampUtc ?? DateTime.UtcNow;
                return date >= ServiceFeeEffectiveDateUtc
                    ? ServiceCostAfterOct2026Fcfa
                    : ServiceCostBeforeOct2026Fcfa;

            default:
                return UtilityCostFcfa;
        }
    }
}
