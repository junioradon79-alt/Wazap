using Wazap.Application.Configuration;

namespace Wazap.Application.Helpers;

public static class PhoneNumberNormalizer
{
    private const string DefaultCountryCode = "33"; // France (configurable via paramètre)

    public static string? Normalize(string? phoneNumber, string countryCode = DefaultCountryCode)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return null;

        // Ne garder que les chiffres et le '+'
        var cleaned = new string(phoneNumber.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (cleaned.Length == 0)
            return null;

        // Déjà au format international
        if (cleaned.StartsWith("+"))
            return cleaned;

        // Préfixe international « 00 »
        if (cleaned.StartsWith("00"))
            return "+" + cleaned[2..];

        // Numéro national commençant par 0 → on remplace le 0 par l'indicatif
        if (cleaned.StartsWith("0"))
            return "+" + countryCode + cleaned[1..];

        // Déjà international mais sans le '+' (ex : 33612345678)
        if (cleaned.StartsWith(countryCode))
            return "+" + cleaned;

        // Numéro national sans le 0 initial
        return "+" + countryCode + cleaned;
    }

    /// <summary>Ne conserve que les chiffres d'un numéro (comparaisons insensibles au format).</summary>
    public static string DigitsOnly(string? phoneNumber)
        => phoneNumber is null
            ? string.Empty
            : new string(phoneNumber.Where(char.IsDigit).ToArray());

    /// <summary>
    /// Détermine si deux numéros correspondent à la MÊME ligne WhatsApp.
    /// Gère la numérotation ivoirienne pré-2021 (+225 + 8 chiffres) vs post-2021
    /// (+225 + 10 chiffres : préfixe opérateur de 2 chiffres + ancien numéro de 8).
    /// </summary>
    public static bool SameSubscriber(string? a, string? b)
    {
        var da = DigitsOnly(a);
        var db = DigitsOnly(b);

        if (da.Length == 0 || db.Length == 0)
            return false;

        if (da == db)
            return true;

        // Côte d'Ivoire : ancien = 225 + 8 chiffres (11), nouveau = 225 + 10 chiffres (13).
        // Le nouveau numéro = préfixe (2 chiffres) + ancien (8 chiffres) → mêmes 8 derniers.
        if (da.StartsWith("225") && db.StartsWith("225")
            && da.Length is 11 or 13 && db.Length is 11 or 13)
        {
            return da[^8..] == db[^8..];
        }

        return false;
    }

    /// <summary>
    /// Convertit un numéro ivoirien au format HISTORIQUE (+225 + 8 chiffres) vers le format
    /// COURANT (+225 + 10 chiffres), via la table pilotée par <paramref name="options"/>
    /// (<see cref="IvoryCoastNumberingOptions.OldToNewPrefixMap"/>).
    /// </summary>
    /// <remarks>
    /// Squelette prêt pour la table officielle ARTCI : tant que la conversion n'est pas activée
    /// (<c>Enabled = false</c>) ou qu'aucun préfixe ne correspond, cette méthode retourne
    /// <c>null</c> → les appelants conservent la valeur d'origine (aucun changement de
    /// comportement, aucun risque).
    /// </remarks>
    public static string? ConvertOldCiToCurrent(string? phoneNumber, IvoryCoastNumberingOptions options)
        => options is { Enabled: true }
            ? ConvertOldCiToCurrent(phoneNumber, options.OldToNewPrefixMap)
            : null;

    /// <summary>
    /// Conversion 8 → 10 chiffres pilotée par une table (ancien préfixe → nouveau préfixe).
    /// </summary>
    /// <param name="phoneNumber">Numéro au format historique (+225 + 8 chiffres), avec ou sans « + ».</param>
    /// <param name="oldToNewPrefixMap">
    /// Table officielle ARTCI : clé = ancien préfixe national (chiffres uniquement), valeur =
    /// nouveau préfixe de 2 chiffres à préfixer aux 8 chiffres conservés. Le préfixe le plus
    /// long connu gagne (ex. « 08 » prime sur « 0 »). Entrées invalides ignorées.
    /// </param>
    /// <returns>
    /// Numéro canonique converti (ex. « +2250708323366 »), la valeur inchangée si le numéro est
    /// déjà au format courant, ou <c>null</c> quand la conversion est impossible ou incertaine
    /// (numéro non ivoirien, préfixe inconnu, map absente/vide).
    /// </returns>
    public static string? ConvertOldCiToCurrent(string? phoneNumber, IReadOnlyDictionary<string, string>? oldToNewPrefixMap)
    {
        var digits = DigitsOnly(phoneNumber);

        // Déjà au format courant (+225 + 10 chiffres) → retour canonique, aucune conversion.
        if (digits.Length == 13 && digits.StartsWith("225", StringComparison.Ordinal))
            return "+" + digits;

        // Seul l'ancien format ivoirien (+225 + 8 chiffres) est convertible.
        if (digits.Length != 11 || !digits.StartsWith("225", StringComparison.Ordinal))
            return null;

        // Les 8 chiffres d'origine sont conservés tels quels ; seul le préfixe change.
        var national = digits[3..];
        var newPrefix = BestNewPrefix(national, oldToNewPrefixMap);
        if (newPrefix is null)
            return null;

        return "+225" + newPrefix + national;
    }

    /// <summary>Retrouve le nouveau préfixe (2 chiffres) associé au plus long ancien préfixe connu.</summary>
    private static string? BestNewPrefix(string nationalNumber, IReadOnlyDictionary<string, string>? oldToNewPrefixMap)
    {
        if (oldToNewPrefixMap is null || oldToNewPrefixMap.Count == 0)
            return null;

        string? best = null;
        var bestLength = -1;

        foreach (var (oldPrefix, newPrefix) in oldToNewPrefixMap)
        {
            // Règles invalides ignorées : ancien préfixe chiffres uniquement, nouveau préfixe = 2 chiffres.
            if (string.IsNullOrEmpty(oldPrefix) || oldPrefix.Any(c => !char.IsDigit(c)))
                continue;
            if (string.IsNullOrEmpty(newPrefix) || newPrefix.Length != 2 || newPrefix.Any(c => !char.IsDigit(c)))
                continue;
            if (nationalNumber.StartsWith(oldPrefix, StringComparison.Ordinal) && oldPrefix.Length > bestLength)
            {
                best = newPrefix;
                bestLength = oldPrefix.Length;
            }
        }

        return best;
    }
}
