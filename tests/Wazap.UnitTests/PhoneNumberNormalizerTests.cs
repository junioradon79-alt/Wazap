using Wazap.Application.Configuration;
using Wazap.Application.Helpers;
using Xunit;

namespace Wazap.UnitTests;

public class PhoneNumberNormalizerTests
{
    [Theory]
    [InlineData("+33612345678", "+33612345678")]
    [InlineData("0033612345678", "+33612345678")]
    [InlineData("0612345678", "+33612345678")]
    [InlineData("33612345678", "+33612345678")]
    [InlineData("+33 6 12 34 56 78", "+33612345678")]
    [InlineData("06.12.34.56.78", "+33612345678")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalize_ShouldReturnE164(string? input, string? expected)
    {
        Assert.Equal(expected, PhoneNumberNormalizer.Normalize(input));
    }

    [Fact]
    public void SameSubscriber_IvoryCoastOldVsNew_ShouldMatch()
    {
        // Même ligne : ancien format (8 chiffres) vs nouveau format (10 chiffres, préfixe + ancien).
        Assert.True(PhoneNumberNormalizer.SameSubscriber("+22508323366", "+2250708323366"));
        Assert.True(PhoneNumberNormalizer.SameSubscriber("+2250708323366", "22508323366"));
        Assert.True(PhoneNumberNormalizer.SameSubscriber("22508323366", "2250708323366"));
    }

    [Fact]
    public void SameSubscriber_DifferentLines_ShouldNotMatch()
    {
        Assert.False(PhoneNumberNormalizer.SameSubscriber("+2250708323366", "+2250508123456"));
        Assert.False(PhoneNumberNormalizer.SameSubscriber("+33612345678", "+33699887766"));
        Assert.False(PhoneNumberNormalizer.SameSubscriber("+33612345678", "+22508323366"));
        Assert.False(PhoneNumberNormalizer.SameSubscriber(null, "+22508323366"));
        Assert.False(PhoneNumberNormalizer.SameSubscriber("", "+22508323366"));
    }

    // --- Conversion 8 → 10 chiffres (squelette piloté par la table ARTCI) ---

    [Fact]
    public void ConvertOldCiToCurrent_RealExample_ShouldConvert()
    {
        // Cas réel validé en prod : ancien « 08323366 » → nouveau « 07 » + « 08323366 » = « 0708323366 ».
        var map = new Dictionary<string, string> { ["08"] = "07" };
        Assert.Equal("+2250708323366", PhoneNumberNormalizer.ConvertOldCiToCurrent("+22508323366", map));
    }

    [Theory]
    [InlineData("22508323366", "+2250708323366")]       // sans « + »
    [InlineData("+225 08 32 33 66", "+2250708323366")]  // espaces
    public void ConvertOldCiToCurrent_FormatVariants_ShouldConvert(string input, string expected)
    {
        var map = new Dictionary<string, string> { ["08"] = "07" };
        Assert.Equal(expected, PhoneNumberNormalizer.ConvertOldCiToCurrent(input, map));
    }

    [Theory]
    [InlineData("+2250708323366")]
    [InlineData("2250708323366")]
    public void ConvertOldCiToCurrent_AlreadyCurrentFormat_ShouldReturnCanonical(string input)
    {
        // Déjà au format courant (10 chiffres) → aucune conversion, retour canonique E.164.
        Assert.Equal("+2250708323366", PhoneNumberNormalizer.ConvertOldCiToCurrent(input, new Dictionary<string, string>()));
        Assert.Equal("+2250708323366", PhoneNumberNormalizer.ConvertOldCiToCurrent(input, (IReadOnlyDictionary<string, string>?)null));
    }

    [Theory]
    [InlineData("+22509091800")] // préfixe « 09 » absent de la table → ne PAS deviner
    [InlineData("+33612345678")] // non ivoirien
    [InlineData("08323366")]     // national seul (sans +225) → trop incertain
    public void ConvertOldCiToCurrent_UnknownOrNonCi_ShouldReturnNull(string input)
    {
        var map = new Dictionary<string, string> { ["08"] = "07" };
        Assert.Null(PhoneNumberNormalizer.ConvertOldCiToCurrent(input, map));
    }

    [Fact]
    public void ConvertOldCiToCurrent_EmptyOrNullMap_ShouldReturnNull()
    {
        Assert.Null(PhoneNumberNormalizer.ConvertOldCiToCurrent("+22508323366", new Dictionary<string, string>()));
        Assert.Null(PhoneNumberNormalizer.ConvertOldCiToCurrent("+22508323366", (IReadOnlyDictionary<string, string>?)null));
    }

    [Fact]
    public void ConvertOldCiToCurrent_LongestKnownPrefix_Wins()
    {
        var map = new Dictionary<string, string>
        {
            ["0"] = "99", // règle générique (démonstration)
            ["08"] = "07" // plus spécifique → doit gagner
        };
        Assert.Equal("+2250708323366", PhoneNumberNormalizer.ConvertOldCiToCurrent("+22508323366", map));
    }

    [Fact]
    public void ConvertOldCiToCurrent_InvalidRules_AreIgnored()
    {
        var map = new Dictionary<string, string>
        {
            ["09"] = "1",   // nouveau préfixe invalide (1 chiffre) → ignoré
            ["07x"] = "07", // ancien préfixe non numérique → ignoré
            [""] = "07"     // clé vide → ignorée
        };
        Assert.Null(PhoneNumberNormalizer.ConvertOldCiToCurrent("+22509091800", map));
    }

    [Fact]
    public void ConvertOldCiToCurrent_ResultStillMatchesSameSubscriber()
    {
        var map = new Dictionary<string, string> { ["08"] = "07" };
        var converted = PhoneNumberNormalizer.ConvertOldCiToCurrent("+22508323366", map);
        Assert.NotNull(converted);
        // Cohérence avec le matching existant : ancien et nouveau restent « même ligne ».
        Assert.True(PhoneNumberNormalizer.SameSubscriber("+22508323366", converted));
    }

    [Fact]
    public void ConvertOldCiToCurrent_OptionsDisabled_ShouldReturnNull()
    {
        var options = new IvoryCoastNumberingOptions
        {
            Enabled = false,
            OldToNewPrefixMap = new Dictionary<string, string> { ["08"] = "07" }
        };
        Assert.Null(PhoneNumberNormalizer.ConvertOldCiToCurrent("+22508323366", options));
    }

    [Fact]
    public void ConvertOldCiToCurrent_OptionsEnabled_ShouldConvert()
    {
        var options = new IvoryCoastNumberingOptions
        {
            Enabled = true,
            OldToNewPrefixMap = new Dictionary<string, string> { ["08"] = "07" }
        };
        Assert.Equal("+2250708323366", PhoneNumberNormalizer.ConvertOldCiToCurrent("+22508323366", options));
    }

    // --- Table ARTCI par défaut (plan 2021) ---

    [Fact]
    public void DefaultOptions_AreDisabled_ShouldReturnNull()
    {
        // Sécurité : la conversion reste DESACTIVEE par défaut (validation officielle en cours).
        var options = new IvoryCoastNumberingOptions();
        Assert.False(options.Enabled);
        Assert.True(options.OldToNewPrefixMap.Count > 0);
        Assert.Null(PhoneNumberNormalizer.ConvertOldCiToCurrent("+22508323366", options));
    }

    [Theory]
    [InlineData("+22508323366", "+2250708323366")]   // Orange 08 → 07 (exemple réel validé en prod)
    [InlineData("+22547639363", "+2250747639363")]   // Orange 47 → 07 (prospect réel 0747639363)
    [InlineData("+22587870768", "+2250787870768")]   // Orange 87 → 07 (prospect réel 0787870768)
    [InlineData("+22555901010", "+2250555901010")]   // MTN 55 → 05 (prospect réel 0555901010)
    [InlineData("+22542331142", "+2250142331142")]   // Moov/ex-Atlantique 42 → 01
    [InlineData("+22540647584", "+2250140647584")]   // Moov/ex-Atlantique 40 → 01
    public void ConvertOldCiToCurrent_DefaultTable_RealSamples_PerOperator(string input, string expected)
    {
        var options = new IvoryCoastNumberingOptions { Enabled = true };
        Assert.Equal(expected, PhoneNumberNormalizer.ConvertOldCiToCurrent(input, options));
    }

    [Theory]
    [InlineData("+22522472404")] // fixe Cocody (ancien 2x) → pas de WhatsApp, aucune conversion
    [InlineData("+22522415538")] // fixe (ancien 2x)
    public void ConvertOldCiToCurrent_DefaultTable_FixedLines_ShouldNotConvert(string input)
    {
        var options = new IvoryCoastNumberingOptions { Enabled = true };
        Assert.Null(PhoneNumberNormalizer.ConvertOldCiToCurrent(input, options));
    }

    [Fact]
    public void DefaultTable_KeysAndValues_AreWellFormed()
    {
        // Garde-fou de saisie : clés = 2 chiffres, valeurs = préfixes mobiles officiels 01/05/07.
        var options = new IvoryCoastNumberingOptions();
        Assert.All(options.OldToNewPrefixMap, entry =>
        {
            Assert.Equal(2, entry.Key.Length);
            Assert.True(entry.Key.All(char.IsDigit), $"Clé invalide : {entry.Key}");
            Assert.Contains(entry.Value, new[] { "01", "05", "07" });
        });
    }

    [Fact]
    public void DefaultTable_MatchesOfficialArteiPlan2021()
    {
        // PLAN OFFICIEL ARTCI (réforme du 31/01/2021) — anciens préfixes mobiles (2 chiffres) par opérateur,
        // et nouveau préfixe de 2 chiffres à préfixer aux 8 chiffres conservés.
        // Sources :
        //   • communiqué ARTCI « Passage de 8 à 10 chiffres à compter du 31 janvier 2021 » (artci.ci, 11/08/2020) ;
        //   • plan national de numérotation (NNP) — liste des anciens préfixes mobiles par opérateur ;
        //   • recoupement avec les wa_id réels observés en prod (5 échantillons, testés ci-dessus).
        // Nouvelles numérotations : mobiles 01 (Moov) / 05 (MTN) / 07 (Orange) ; fixes 21/25/27 (hors table, pas de WhatsApp).
        var expected = new Dictionary<string, string>
        {
            // Orange – Côte d'Ivoire → 07 (15 préfixes historiques)
            ["07"] = "07", ["08"] = "07", ["09"] = "07",
            ["47"] = "07", ["48"] = "07", ["49"] = "07",
            ["57"] = "07", ["58"] = "07", ["59"] = "07",
            ["77"] = "07", ["78"] = "07",
            ["87"] = "07", ["88"] = "07", ["89"] = "07", ["98"] = "07",

            // MTN – Côte d'Ivoire → 05 (11 préfixes historiques ; 04/05/06 hérités d'Oricel/Warid/Comium fermés)
            ["04"] = "05", ["05"] = "05", ["06"] = "05",
            ["44"] = "05", ["45"] = "05", ["46"] = "05",
            ["55"] = "05", ["56"] = "05",
            ["84"] = "05", ["85"] = "05", ["86"] = "05",

            // Moov Africa CI (ex-Atlantique Telecom) → 01 (5 préfixes historiques)
            ["01"] = "01", ["02"] = "01", ["03"] = "01",
            ["40"] = "01", ["42"] = "01"
        };

        var actual = new IvoryCoastNumberingOptions().OldToNewPrefixMap;
        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(
            expected.OrderBy(e => e.Key).Select(e => $"{e.Key}={e.Value}"),
            actual.OrderBy(e => e.Key).Select(e => $"{e.Key}={e.Value}"));
    }

    [Theory]
    [InlineData("41")] // non attribué mobile (ni MTN, ni Orange, ni Moov selon le NNP)
    [InlineData("43")] // non attribué
    [InlineData("50")] // Warid (fermé avant 2021 — lignes migrées, préfixe obsolète)
    [InlineData("60")] // Oricel (fermé)
    [InlineData("66")] // Comium (fermé)
    [InlineData("67")] // Comium (fermé)
    [InlineData("69")] // Aircomm (fermé)
    public void DefaultTable_UnassignedOrClosedPrefixes_ShouldNeverConvert(string oldPrefix)
    {
        // Règle de sûreté : ne JAMAIS convertir un préfixe absent du plan officiel → null (aucune devinette).
        var options = new IvoryCoastNumberingOptions { Enabled = true };
        Assert.Null(PhoneNumberNormalizer.ConvertOldCiToCurrent("+225" + oldPrefix + "123456", options));
    }
}
