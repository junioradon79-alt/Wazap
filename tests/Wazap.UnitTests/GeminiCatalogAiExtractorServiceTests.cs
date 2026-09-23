using Microsoft.Extensions.Logging.Abstractions;
using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

public class GeminiCatalogAiExtractorServiceTests
{
    [Fact]
    public void FallbackTextParse_ExtractsMultipleProductsAndPrices()
    {
        var rawText = @"
✨ NOUVEAUX ARRIVAGES CHEZ BOUTIQUE AWA :
- Robe de soirée dorée : 15 000 FCFA
- Escarpins noirs vernis : 18.000 F
- Sac à main cuir croco : 25 000 CFA
- Poulet braisé pimenté - 6000 F
- iPhone 13 Pro 128Go : 350.000 FCFA
- Lunettes de soleil chic : 8000 FCFA
Livraison partout à Abidjan !";

        var products = GeminiCatalogAiExtractorService.FallbackTextParse(rawText);

        Assert.NotEmpty(products);
        Assert.True(products.Count >= 5);

        var robe = Assert.Single(products, p => p.Name.Contains("Robe"));
        Assert.Equal(15000m, robe.Price);
        Assert.Equal("👗", robe.Emoji);

        var escarpin = Assert.Single(products, p => p.Name.Contains("Escarpins"));
        Assert.Equal(18000m, escarpin.Price);
        Assert.Equal("👠", escarpin.Emoji);

        var sac = Assert.Single(products, p => p.Name.Contains("Sac"));
        Assert.Equal(25000m, sac.Price);
        Assert.Equal("👜", sac.Emoji);

        var poulet = Assert.Single(products, p => p.Name.Contains("Poulet"));
        Assert.Equal(6000m, poulet.Price);
        Assert.Equal("🍗", poulet.Emoji);

        var lunettes = Assert.Single(products, p => p.Name.Contains("Lunettes"));
        Assert.Equal(8000m, lunettes.Price);
        Assert.Equal("🕶️", lunettes.Emoji);
    }

    [Theory]
    [InlineData("Robe fleurie", "👗")]
    [InlineData("Pagne kita tissé", "👗")]
    [InlineData("Escarpins pointus", "👠")]
    [InlineData("Basket de sport", "👠")]
    [InlineData("Sac en bandoulière", "👜")]
    [InlineData("Poulet DG spécial", "🍗")]
    [InlineData("Jus d'ananas naturel", "🥤")]
    [InlineData("iPhone 14 Neuf", "📱")]
    [InlineData("Pommade éclaircissante", "💄")]
    [InlineData("Collier argent 925", "💍")]
    [InlineData("Article divers", "📦")]
    public void InferEmoji_AssignsCorrectCategoryEmoji(string productName, string expectedEmoji)
    {
        var emoji = GeminiCatalogAiExtractorService.InferEmoji(productName);
        Assert.Equal(expectedEmoji, emoji);
    }

    [Fact]
    public async Task ExtractFromTextAsync_WithoutApiKey_SucceedsViaHeuristicFallback()
    {
        var service = new GeminiCatalogAiExtractorService(new HttpClient(), new ConfigStub(), NullLogger<GeminiCatalogAiExtractorService>.Instance);
        var result = await service.ExtractFromTextAsync("Robe rouge 12000 F\nSac cuir 20000 FCFA");

        Assert.True(result.Success);
        Assert.Equal(2, result.Products.Count);
        Assert.Contains(result.Products, p => p.Name.Contains("Robe") && p.Price == 12000m);
        Assert.Contains(result.Products, p => p.Name.Contains("Sac") && p.Price == 20000m);
    }
}
