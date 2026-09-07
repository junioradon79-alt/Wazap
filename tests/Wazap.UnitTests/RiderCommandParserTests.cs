using Wazap.Application.Helpers;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Découpage de « LIVRE &lt;code course&gt; CODE &lt;4 chiffres&gt; » (preuve de livraison).
/// </summary>
public class RiderCommandParserTests
{
    [Theory]
    // Course + code client
    [InlineData("A1B2C3D4 CODE 1234", "A1B2C3D4", "1234")]
    // Une seule course en cours : le code de course est facultatif
    [InlineData("CODE 1234", "", "1234")]
    // Casse et espaces libres (saisie mobile)
    [InlineData("a1b2c3d4 code 1234", "a1b2c3d4", "1234")]
    [InlineData("  A1B2C3D4   CODE   1234  ", "A1B2C3D4", "1234")]
    public void SplitDeliveryCommand_ExtractsClientCode(string input, string orderCode, string clientCode)
    {
        var (order, client) = RiderCommandParser.SplitDeliveryCommand(input);

        Assert.Equal(orderCode, order);
        Assert.Equal(clientCode, client);
    }

    [Theory]
    // Flux historique, sans preuve de livraison
    [InlineData("A1B2C3D4", "A1B2C3D4")]
    [InlineData("TOUT", "TOUT")]
    [InlineData("", "")]
    public void SplitDeliveryCommand_WithoutKeyword_LeavesCommandIntact(string input, string orderCode)
    {
        var (order, client) = RiderCommandParser.SplitDeliveryCommand(input);

        Assert.Equal(orderCode, order);
        Assert.Null(client);
    }

    [Fact]
    public void SplitDeliveryCommand_WithNull_IsEmpty()
    {
        var (order, client) = RiderCommandParser.SplitDeliveryCommand(null);

        Assert.Equal(string.Empty, order);
        Assert.Null(client);
    }

    [Fact]
    public void SplitDeliveryCommand_KeywordNeverCollidesWithAnOrderCode()
    {
        // Un code de course est hexadécimal : le « O » de CODE ne peut pas y figurer.
        // Les codes ci-dessous sont les plus proches du mot-clé et doivent rester intacts.
        foreach (var hexLike in new[] { "C0DE", "DECAF0DE", "C0DEC0DE" })
        {
            var (order, client) = RiderCommandParser.SplitDeliveryCommand(hexLike);

            Assert.Equal(hexLike, order);
            Assert.Null(client);
        }
    }

    [Fact]
    public void SplitDeliveryCommand_MissingClientCode_YieldsEmptyCode()
    {
        // « LIVRE A1B2C3D4 CODE » sans chiffres : refusé plus loin comme code erroné,
        // jamais confondu avec une clôture sans preuve.
        var (order, client) = RiderCommandParser.SplitDeliveryCommand("A1B2C3D4 CODE");

        Assert.Equal("A1B2C3D4", order);
        Assert.Equal(string.Empty, client);
    }
}
