namespace Wazap.Application.Helpers;

/// <summary>
/// Analyse des commandes WhatsApp du livreur.
/// </summary>
public static class RiderCommandParser
{
    private const string ClientCodeKeyword = "CODE";

    /// <summary>
    /// Sépare les arguments de « LIVRE » en code de course et code client :
    /// « A1B2C3D4 CODE 1234 » → (« A1B2C3D4 », « 1234 »), « CODE 1234 » → (« », « 1234 »),
    /// « TOUT » → (« TOUT », null).
    /// </summary>
    /// <remarks>
    /// La séparation est sans ambiguïté : un code de course est hexadécimal, or la lettre
    /// « O » de CODE n'appartient pas à l'alphabet hexadécimal — le mot-clé ne peut donc
    /// jamais apparaître à l'intérieur d'un code de course.
    /// </remarks>
    public static (string OrderCode, string? ClientCode) SplitDeliveryCommand(string? arguments)
    {
        var rest = (arguments ?? string.Empty).Trim();

        var keyword = rest.IndexOf(ClientCodeKeyword, StringComparison.OrdinalIgnoreCase);
        if (keyword < 0)
            return (rest, null);

        var clientCode = rest[(keyword + ClientCodeKeyword.Length)..].Trim();
        var orderCode = rest[..keyword].Trim();

        return (orderCode, clientCode);
    }
}
