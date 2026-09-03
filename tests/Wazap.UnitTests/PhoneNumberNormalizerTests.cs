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
}
