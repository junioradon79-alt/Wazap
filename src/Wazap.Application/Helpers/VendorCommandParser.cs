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

        // Candidats avec ou sans séparateurs : +225 XXXXXXXXXX, 07 08 09 10 11, 07-08-09-10-11, 0708091011...
        var match = Regex.Match(text, @"(?:\+?\s?225[\s.-]?)?(?:0[157](?:[\s.-]?\d){8}|0(?:[\s.-]?\d){7})");
        if (!match.Success)
            match = Regex.Match(text, @"(?:\+?\s?225[\s.-]?)?(?:0[157]\d{8}|0\d{7})");

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

    private static readonly (string Zone, string[] Aliases)[] CommunesAbidjan = new[]
    {
        ("Cocody", new[] { "Cocody", "Angré", "Angre", "Riviera", "Deux-Plateaux", "Deux Plateaux", "2 Plateaux", "Danga", "Anono" }),
        ("Yopougon", new[] { "Yopougon", "Niangon", "Toit Rouge", "Maroc", "Siporex", "Gesco", "Sideci", "Kouté", "Koute" }),
        ("Plateau", new[] { "Plateau" }),
        ("Marcory", new[] { "Marcory", "Zone 4", "Zone 3", "Biétry", "Bietry", "Anoumabo" }),
        ("Koumassi", new[] { "Koumassi", "Remblais", "Campement", "Prodomo" }),
        ("Treichville", new[] { "Treichville", "Arras", "Belleville" }),
        ("Adjamé", new[] { "Adjamé", "Adjame", "220 Logements", "Williamsville" }),
        ("Abobo", new[] { "Abobo", "Gagnoa gare", "Sogefiha", "PK18", "Avocatier" }),
        ("Port-Bouët", new[] { "Port-Bouët", "Port-Bouet", "Port Bouet", "Vridi", "Gonzagueville" }),
        ("Attécoubé", new[] { "Attécoubé", "Attecoube", "Locodjro" }),
        ("Bingerville", new[] { "Bingerville", "Feh Kessé" }),
        ("Songon", new[] { "Songon" })
    };

    /// <summary>
    /// Extraction intelligente des données de commande depuis un texte libre (ex: message WhatsApp du client).
    /// Détecte automatiquement : nom, téléphone ivoirien, article, prix en FCFA, commune et adresse.
    /// </summary>
    public static ParsedOrderInfo ParseFreeTextOrder(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new ParsedOrderInfo(null, null, null, 0m, 1500m, null, null);

        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        string? clientPhone = TryExtractClientPhone(text);
        string? clientName = null;
        string? description = null;
        decimal amount = 0m;
        string? address = null;
        string? detectedZone = null;

        // 1. Détection du montant (ex: « 25 000 FCFA », « 15000 F », « 35k »)
        var priceMatch = Regex.Match(text, @"(?i)(?:prix|montant|total|somme)?\s*[:=]?\s*([0-9]{1,3}(?:[\s.][0-9]{3})+|[0-9]{3,7})\s*(?:f\b|fcfa\b|cfa\b|francs?\b|xof\b)");
        if (priceMatch.Success)
        {
            var rawNum = priceMatch.Groups[1].Value.Replace(" ", "").Replace(".", "");
            if (decimal.TryParse(rawNum, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedAmt))
                amount = parsedAmt;
        }
        else
        {
            var kMatch = Regex.Match(text, @"(?i)\b([0-9]{1,3})\s*k\b");
            if (kMatch.Success && decimal.TryParse(kMatch.Groups[1].Value, out var kVal))
                amount = kVal * 1000m;
        }

        // 2. Détection de la Commune & Zone
        foreach (var (zone, aliases) in CommunesAbidjan)
        {
            foreach (var alias in aliases)
            {
                if (Regex.IsMatch(text, $@"(?i)\b{Regex.Escape(alias)}\b"))
                {
                    detectedZone = zone;
                    break;
                }
            }
            if (detectedZone is not null) break;
        }

        // 3. Détection par lignes étiquetées (ex : « Nom : ... », « Adresse : ... », « Article : ... »)
        var usedLines = new HashSet<int>();

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            // Nom
            var nameMatch = Regex.Match(line, @"^(?i)(?:nom|client|destinataire|pour|mme|mr|m\.)\s*[:=\-]?\s*([a-zA-ZÀ-ÿ\s'-]{2,50})$");
            if (nameMatch.Success && clientName is null)
            {
                clientName = nameMatch.Groups[1].Value.Trim();
                usedLines.Add(i);
                continue;
            }

            // Adresse
            var addrMatch = Regex.Match(line, @"^(?i)(?:adresse|lieu|livraison(?:\s*à)?|vers|destination)\s*[:=\-]?\s*(.+)$");
            if (addrMatch.Success && address is null)
            {
                address = addrMatch.Groups[1].Value.Trim();
                usedLines.Add(i);
                continue;
            }

            // Article / Description
            var descMatch = Regex.Match(line, @"^(?i)(?:article|produit|commande|colis)\s*[:=\-]?\s*(.+)$");
            if (descMatch.Success && description is null)
            {
                description = descMatch.Groups[1].Value.Trim();
                usedLines.Add(i);
                continue;
            }
        }

        // 4. Si non trouvé par étiquettes, extraction heuristique en texte conversationnel
        if (clientName is null)
        {
            var namePattern = Regex.Match(text, @"(?i)(?:je m'appelle|mon nom c'est|moi c'est|nom\s*:?)\s*([a-zA-ZÀ-ÿ\s'-]{2,30})");
            if (namePattern.Success)
                clientName = namePattern.Groups[1].Value.Trim();
        }

        if (address is null && detectedZone is not null)
        {
            // Cherche la ligne ou phrase contenant la commune
            var addrLine = lines.FirstOrDefault(l => Regex.IsMatch(l, $@"(?i)\b{detectedZone}\b"));
            if (addrLine is not null)
            {
                // Nettoie les mots introductifs
                address = Regex.Replace(addrLine, @"^(?i)(?:je suis à|livraison à|vers|chez moi à|à)\s*", "").Trim();
            }
            else
            {
                address = detectedZone;
            }
        }

        // Description résiduelle si non étiquetée
        if (description is null)
        {
            var unused = lines.Where((l, idx) => !usedLines.Contains(idx)
                && TryExtractClientPhone(l) is null
                && (address is null || !l.Contains(address))
                && (clientName is null || !l.Contains(clientName))).ToList();

            if (unused.Count > 0)
                description = unused[0].Trim();
        }

        return new ParsedOrderInfo(
            ClientName: clientName,
            ClientPhone: clientPhone,
            Description: description,
            Amount: amount,
            DeliveryFee: 1500m,
            Address: address,
            Zone: detectedZone);
    }
}

/// <summary>Résultat d'extraction intelligente d'une commande client en texte libre.</summary>
public sealed record ParsedOrderInfo(
    string? ClientName,
    string? ClientPhone,
    string? Description,
    decimal Amount,
    decimal DeliveryFee,
    string? Address,
    string? Zone);

