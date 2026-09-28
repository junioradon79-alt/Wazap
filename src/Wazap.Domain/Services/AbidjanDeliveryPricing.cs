using System.Text.RegularExpressions;

namespace Wazap.Domain.Services;

/// <summary>
/// Moteur de tarification officiel de livraison WAZAP pour le Grand Abidjan.
/// Règle inviolable : PLANCHER MINIMUM STRICT DE 1 000 FCFA NET AU LIVREUR (0% commission plateforme).
/// </summary>
public static class AbidjanDeliveryPricing
{
    /// <summary>Plancher tarifaire minimum inviolable garanti au livreur partenaire dès le 1er mètre.</summary>
    public const decimal MinimumFee = 1000m;

    /// <summary>Palier 1 : Intra-commune (0 à 4 km).</summary>
    public const decimal Tier1IntraCommune = 1000m;

    /// <summary>Palier 2 : Communes limitrophes / Même rive (4 à 8 km).</summary>
    public const decimal Tier2Adjacent = 1500m;

    /// <summary>Palier 3 : Traversée inter-rives / Longue distance (8 à 16 km).</summary>
    public const decimal Tier3CrossLagoon = 2000m;

    /// <summary>Palier 4 : Périphérie / Grand Abidjan (> 16 km, Bingerville, Songon, etc.).</summary>
    public const decimal Tier4Periphery = 2500m;

    private static readonly HashSet<string> CommunesPeripheriques = new(StringComparer.OrdinalIgnoreCase)
    {
        "Songon", "Grand-Bassam", "Bassam", "Dabou", "Anyama"
    };

    private static readonly HashSet<(string, string)> PairesLimitrophes = new(new CommunePairComparer())
    {
        ("Plateau", "Treichville"),
        ("Plateau", "Adjamé"),
        ("Plateau", "Adjame"),
        ("Plateau", "Cocody"),
        ("Plateau", "Attécoubé"),
        ("Plateau", "Attecoube"),
        ("Cocody", "Adjamé"),
        ("Cocody", "Adjame"),
        ("Cocody", "Bingerville"),
        ("Adjamé", "Adjame"),
        ("Adjamé", "Attécoubé"),
        ("Adjame", "Attecoube"),
        ("Adjamé", "Abobo"),
        ("Adjame", "Abobo"),
        ("Attécoubé", "Yopougon"),
        ("Attecoube", "Yopougon"),
        ("Marcory", "Koumassi"),
        ("Marcory", "Treichville"),
        ("Marcory", "Port-Bouët"),
        ("Marcory", "Port-Bouet"),
        ("Koumassi", "Port-Bouët"),
        ("Koumassi", "Port-Bouet"),
        ("Treichville", "Koumassi"),
    };

    /// <summary>
    /// Calcule le tarif de livraison recommandé entre deux communes d'Abidjan.
    /// Garantit en toute circonstance un montant supérieur ou égal au plancher de 1 000 FCFA.
    /// </summary>
    public static decimal CalculateFee(string? originZone, string? destinationZone)
    {
        var origin = NormalizeZone(originZone);
        var dest = NormalizeZone(destinationZone);

        // Si l'une des zones est inconnue ou identique : plancher garanti
        if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(dest))
            return MinimumFee;

        if (string.Equals(origin, dest, StringComparison.OrdinalIgnoreCase))
            return Tier1IntraCommune;

        // Périphérie éloignée
        if (CommunesPeripheriques.Contains(origin) || CommunesPeripheriques.Contains(dest))
            return Tier4Periphery;

        // Communes limitrophes
        if (PairesLimitrophes.Contains((origin, dest)))
            return Tier2Adjacent;

        // Traversée ou intercommune éloignée (ex : Yopougon <-> Cocody, Abobo <-> Marcory)
        return Tier3CrossLagoon;
    }

    /// <summary>
    /// Calcule le tarif de livraison recommandé à partir de la distance kilométrique réelle.
    /// </summary>
    public static decimal CalculateFeeByDistance(double distanceKm)
    {
        if (distanceKm <= 4.0)
            return Tier1IntraCommune;

        if (distanceKm <= 8.0)
            return Tier2Adjacent;

        if (distanceKm <= 16.0)
            return Tier3CrossLagoon;

        return Tier4Periphery;
    }

    /// <summary>
    /// Applique le plancher minimum inviolable de 1 000 FCFA.
    /// </summary>
    public static decimal EnforceFloor(decimal fee)
    {
        return Math.Max(MinimumFee, fee);
    }

    private static string NormalizeZone(string? zone)
    {
        if (string.IsNullOrWhiteSpace(zone))
            return string.Empty;

        var clean = zone.Trim();
        if (clean.Contains("Cocody", StringComparison.OrdinalIgnoreCase) || clean.Contains("Angré", StringComparison.OrdinalIgnoreCase) || clean.Contains("Riviera", StringComparison.OrdinalIgnoreCase) || clean.Contains("Plateaux", StringComparison.OrdinalIgnoreCase))
            return "Cocody";
        if (clean.Contains("Yopougon", StringComparison.OrdinalIgnoreCase) || clean.Contains("Niangon", StringComparison.OrdinalIgnoreCase) || clean.Contains("Maroc", StringComparison.OrdinalIgnoreCase) || clean.Contains("Siporex", StringComparison.OrdinalIgnoreCase))
            return "Yopougon";
        if (clean.Contains("Marcory", StringComparison.OrdinalIgnoreCase) || clean.Contains("Biétry", StringComparison.OrdinalIgnoreCase) || clean.Contains("Zone 4", StringComparison.OrdinalIgnoreCase))
            return "Marcory";
        if (clean.Contains("Koumassi", StringComparison.OrdinalIgnoreCase))
            return "Koumassi";
        if (clean.Contains("Plateau", StringComparison.OrdinalIgnoreCase))
            return "Plateau";
        if (clean.Contains("Treichville", StringComparison.OrdinalIgnoreCase))
            return "Treichville";
        if (clean.Contains("Adjamé", StringComparison.OrdinalIgnoreCase) || clean.Contains("Adjame", StringComparison.OrdinalIgnoreCase))
            return "Adjamé";
        if (clean.Contains("Abobo", StringComparison.OrdinalIgnoreCase))
            return "Abobo";
        if (clean.Contains("Port-Bouët", StringComparison.OrdinalIgnoreCase) || clean.Contains("Port Bouet", StringComparison.OrdinalIgnoreCase))
            return "Port-Bouët";
        if (clean.Contains("Attécoubé", StringComparison.OrdinalIgnoreCase) || clean.Contains("Attecoube", StringComparison.OrdinalIgnoreCase))
            return "Attécoubé";
        if (clean.Contains("Bingerville", StringComparison.OrdinalIgnoreCase))
            return "Bingerville";
        if (clean.Contains("Songon", StringComparison.OrdinalIgnoreCase))
            return "Songon";

        return clean;
    }

    private sealed class CommunePairComparer : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) x, (string, string) y)
        {
            var (x1, x2) = x;
            var (y1, y2) = y;
            return (string.Equals(x1, y1, StringComparison.OrdinalIgnoreCase) && string.Equals(x2, y2, StringComparison.OrdinalIgnoreCase))
                || (string.Equals(x1, y2, StringComparison.OrdinalIgnoreCase) && string.Equals(x2, y1, StringComparison.OrdinalIgnoreCase));
        }

        public int GetHashCode((string, string) obj)
        {
            var h1 = StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1);
            var h2 = StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2);
            return h1 ^ h2;
        }
    }
}
