using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Garde-fou sur l'endpoint le plus sensible du produit : <c>POST /api/webhook/whatsapp</c>.
/// <para>
/// Son corps de requête pilote TOUT (créer une course au nom d'un vendeur, accepter une offre,
/// clôturer une livraison, déclarer un sinistre) et il était auparavant accessible sans aucune
/// authentification : la signature HMAC n'était vérifiée que si l'en-tête était présent, donc
/// l'omettre suffisait à contourner le contrôle. Ces tests démarrent l'application RÉELLE
/// (pipeline complet : middlewares, autorisation, routage) pour verrouiller le comportement.
/// </para>
/// </summary>
public class WebhookAuthenticationTests
{
    private const string VerifyToken = "jeton-de-verification-de-test";
    private const string AppSecret = "secret-app-meta-de-test";

    private static Dictionary<string, string?> AuthConfiguration() => new()
    {
        ["Meta:Enabled"] = "false",
        ["Meta:WebhookVerifyToken"] = VerifyToken,
        ["Meta:WebhookAppSecret"] = AppSecret
    };

    private static StringContent JsonBody(string json)
        => new(json, Encoding.UTF8, "application/json");

    private static string Sign(string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSecret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }

    [Fact]
    public async Task Post_SansAucunePreuve_EstRefuse()
    {
        using var factory = new WazapAppFactory(AuthConfiguration());
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/webhook/whatsapp", JsonBody("{}"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_AvecJetonPartageIncorrect_EstRefuse()
    {
        using var factory = new WazapAppFactory(AuthConfiguration());
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/webhook/whatsapp?token=mauvais-jeton", JsonBody("{}"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_AvecJetonPartageValide_EstAccepte()
    {
        using var factory = new WazapAppFactory(AuthConfiguration());
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/webhook/whatsapp?token={Uri.EscapeDataString(VerifyToken)}", JsonBody("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_AvecJetonPartageEnEntete_EstAccepte()
    {
        using var factory = new WazapAppFactory(AuthConfiguration());
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhook/whatsapp")
        {
            Content = JsonBody("{}")
        };
        request.Headers.Add("X-Webhook-Token", VerifyToken);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_AvecSignatureMetaValide_EstAccepte()
    {
        using var factory = new WazapAppFactory(AuthConfiguration());
        using var client = factory.CreateClient();

        const string body = "{\"object\":\"whatsapp_business_account\",\"entry\":[]}";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhook/whatsapp")
        {
            Content = JsonBody(body)
        };
        request.Headers.TryAddWithoutValidation("X-Hub-Signature-256", Sign(body));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_AvecSignatureMetaInvalide_EstRefuse()
    {
        using var factory = new WazapAppFactory(AuthConfiguration());
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhook/whatsapp")
        {
            Content = JsonBody("{\"entry\":[]}")
        };
        request.Headers.TryAddWithoutValidation("X-Hub-Signature-256", Sign("{\"autre\":\"corps\"}"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_SigneMaisSecretNonConfigure_Repond503()
    {
        // Fail closed : mieux vaut refuser que d'accepter sans pouvoir vérifier.
        var configuration = AuthConfiguration();
        configuration["Meta:WebhookAppSecret"] = string.Empty;

        using var factory = new WazapAppFactory(configuration);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhook/whatsapp")
        {
            Content = JsonBody("{}")
        };
        request.Headers.TryAddWithoutValidation("X-Hub-Signature-256", "sha256=peu-importe");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Get_ChallengeMeta_AvecBonJeton_RenvoieLeChallenge()
    {
        using var factory = new WazapAppFactory(AuthConfiguration());
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/webhook/whatsapp?hub.mode=subscribe&hub.verify_token={Uri.EscapeDataString(VerifyToken)}&hub.challenge=abc123");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("abc123", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_ChallengeMeta_AvecMauvaisJeton_EstRefuse()
    {
        using var factory = new WazapAppFactory(AuthConfiguration());
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/webhook/whatsapp?hub.mode=subscribe&hub.verify_token=faux&hub.challenge=abc123");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
