using System.Globalization;
using System.Text.Json;

namespace Wazap.Application.Helpers;

/// <summary>
/// Lecture TOLÉRANTE d'un payload JSON : accepte camelCase ET snake_case, les nombres comme les
/// chaînes, et ne lève jamais sur une forme inattendue.
/// <para>
/// La passerelle WhatsApp n'a pas de forme de payload unique et documentée, et deux exemplaires
/// de cette logique coexistaient (routeur du webhook et analyseur Meta) : une correction dans
/// l'un ne profitait pas à l'autre. Une seule implémentation, testée, sert désormais les deux.
/// </para>
/// </summary>
public static class JsonPayloadReader
{
    /// <summary>Premier champ dont le nom correspond, à la casse et aux séparateurs près.</summary>
    public static JsonElement? Find(JsonElement? node, string name)
    {
        if (node is not { ValueKind: JsonValueKind.Object } obj)
            return null;

        var target = NormalizeKey(name);
        foreach (var property in obj.EnumerateObject())
        {
            if (NormalizeKey(property.Name) == target)
                return property.Value;
        }

        return null;
    }

    /// <summary>Valeur texte d'un champ (null si absent ou d'un autre type).</summary>
    public static string? Str(JsonElement? node, string name)
        => Find(node, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    /// <summary>Valeur numérique d'un champ, acceptée en nombre comme en chaîne.</summary>
    public static double? Dbl(JsonElement? node, string name)
    {
        var value = Find(node, name);
        if (value is null)
            return null;

        return value.Value.ValueKind switch
        {
            JsonValueKind.Number => value.Value.GetDouble(),
            JsonValueKind.String when double.TryParse(
                value.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    /// <summary>Clé de comparaison : lettres et chiffres uniquement, en minuscules.</summary>
    public static string NormalizeKey(string name)
        => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
