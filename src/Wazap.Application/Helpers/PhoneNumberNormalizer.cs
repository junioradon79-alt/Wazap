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
}
