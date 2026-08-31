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
}
