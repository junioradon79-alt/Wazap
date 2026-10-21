using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Tests de non-rgression des routes racines et liens courts marketing de production.
/// Vrifie l'absence de conflit d'endpoints (AmbiguousMatchException / HTTP 500)
/// et le respect des redirections 1-tap vers l'application et WhatsApp officiel.
/// </summary>
public class RootRedirectionTests
{
    [Fact]
    public async Task GetRoot_RedirigeVersAppSansErreur500()
    {
        using var factory = new WazapAppFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/app", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/tiktok")]
    [InlineData("/15")]
    public async Task GetTikTokEt15_RedirigentVersNumeroWhatsAppOfficiel(string path)
    {
        using var factory = new WazapAppFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.OriginalString;
        Assert.NotNull(location);
        Assert.StartsWith("https://wa.me/2250544051972", location);
    }

    [Theory]
    [InlineData("/livreurs", "/app/livreurs")]
    [InlineData("/vente", "/app/vente")]
    [InlineData("/parrainage", "/app/parrainage")]
    [InlineData("/devenir-livreur", "/app/livreurs")]
    public async Task GetMarketingShortLinks_RedirigentVersPagesApp(string path, string expectedDestination)
    {
        using var factory = new WazapAppFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expectedDestination, response.Headers.Location?.OriginalString);
    }
}
