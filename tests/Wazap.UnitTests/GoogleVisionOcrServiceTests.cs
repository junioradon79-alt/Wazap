using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

public class GoogleVisionOcrServiceTests
{
    [Fact]
    public void ExtractIvorianIdentityFields_PermisDeConduire_Reel_IbrahimNiagare()
    {
        // Texte brut typique renvoyé par Google Cloud Vision sur le permis téléversé par l'utilisateur
        var rawText = """
            [A|B|C|D|E]
            RÉPUBLIQUE DE CÔTE D'IVOIRE
            MINISTÈRE DES TRANSPORTS
            PERMIS DE CONDUIRE
            1. Nom
            NIAGARE
            2. Prénoms
            IBRAHIM
            3. Date et lieu de naissance
            20-02-1998 Abobo
            4. Date et lieu de délivrance
            07-01-2021 Abidjan
            5. Numéro du permis de conduire
            NIAG01-21-24209886I
            6. Restriction(s)
            89673358
            """;

        var result = GoogleVisionOcrService.ExtractIvorianIdentityFields(rawText);

        Assert.True(result.Success);
        Assert.Equal("Permis de Conduire", result.DocumentType);
        Assert.Equal("NIAGARE IBRAHIM", result.FullName);
        Assert.Equal("NIAG01-21-24209886I", result.IdNumber);
    }

    [Fact]
    public void ExtractIvorianIdentityFields_PermisDeConduire_SurUneSeuleLigne()
    {
        var rawText = """
            RÉPUBLIQUE DE CÔTE D'IVOIRE
            PERMIS DE CONDUIRE
            1. NOM : KOFFI
            2. PRENOMS : JEAN BAPTISTE
            5. NUMERO DU PERMIS : CI-2022-998877
            """;

        var result = GoogleVisionOcrService.ExtractIvorianIdentityFields(rawText);

        Assert.True(result.Success);
        Assert.Equal("Permis de Conduire", result.DocumentType);
        Assert.Equal("KOFFI JEAN BAPTISTE", result.FullName);
        Assert.Equal("CI-2022-998877", result.IdNumber);
    }

    [Fact]
    public void ExtractIvorianIdentityFields_CniBiometriqueONECI()
    {
        var rawText = """
            RÉPUBLIQUE DE CÔTE D'IVOIRE
            CARTE NATIONALE D'IDENTITÉ
            ONECI
            Nom / Surname
            KOUASSI
            Prénoms / Given names
            YAO MICHEL
            N° CNI / ID No
            CI0012345678
            Date d'expiration / Expiry date
            12.05.2030
            """;

        var result = GoogleVisionOcrService.ExtractIvorianIdentityFields(rawText);

        Assert.True(result.Success);
        Assert.Equal("CNI", result.DocumentType);
        Assert.Equal("KOUASSI YAO MICHEL", result.FullName);
        Assert.Equal("CI0012345678", result.IdNumber);
    }
}
