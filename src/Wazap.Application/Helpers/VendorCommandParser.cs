using System.Globalization;
using System.Text.RegularExpressions;

namespace Wazap.Application.Helpers;

/// <summary>
/// Analyse des commandes texte du vendeur (WhatsApp), extraite du contrôleur webhook (P2 / C-13).
///
/// Ces deux analyseurs étaient des méthodes statiques privées du contrôleur : ni testables
/// directement, ni réutilisables par un autre point d'entrée — alors que le numéro de téléphone
/// du client est aussi extrait par la conversion d'un lead côté équipe, et que le format du
/// produit doit rester strictement le même que celui du menu du bot client.
/// </summary>
public static class VendorCommandParser
{
    /// <summary>
    /// « &lt;nom&gt; | &lt;prix&gt; [| &lt;emoji&gt;] » : le nom peut contenir des espaces,
    /// le prix tolère « 2 500 » ou « 2500 FCFA ». L'emoji reste optionnel.
    /// </summary>
    public static bool TryParseProductCommand(string payload, out string name, out decimal price, out string? emoji)
    {
        name = string.Empty;
        price = 0m;
        emoji = null;

        var parts = payload.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts[0].Length < 2)
            return false;

        var rawPrice = parts[1]
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("FCFA", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (!decimal.TryParse(rawPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out price) || price < 0)
            return false;

        name = parts[0];
        if (parts.Length >= 3 && parts[2].Length > 0)
            emoji = parts[2];

        return true;
    }

    /// <summary>
    /// Numéro de téléphone du client cité dans un texte libre (ex : « … tél 0708091011 »).
    /// Renvoie la forme E.164 ivoirienne, ou <c>null</c> si aucun numéro plausible n'est présent.
    /// </summary>
    public static string? TryExtractClientPhone(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Candidats : +225XXXXXXXXXX (nouveau), +225XXXXXXXX (ancien),
        // 0XXXXXXXXX (nouveau 10) / 0XXXXXXXX (ancien 8).
        var match = Regex.Match(text, @"(?:\+?\s?225[\s.-]?)?(?:0[157]\d{8}|0\d{7})");
        if (!match.Success)
            return null;

        var digits = new string(match.Value.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("225") && digits.Length is 11 or 13)
            return "+" + digits;

        // Numéro national ivoirien (8 ou 10 chiffres commençant par 0) : on conserve le 0
        // après l'indicatif (E.164 CI : +225 07 08 … → 2250708…).
        if (digits.Length is 8 or 10 && digits.StartsWith("0"))
            return "+225" + digits;

        return null;
    }
}
