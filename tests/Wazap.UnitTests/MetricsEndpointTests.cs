using System.Net;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// P1 / B-17 — <c>/metrics</c> exposait la profondeur de la file d'échecs, l'uptime et l'état de
/// la base à quiconque connaissait l'URL, tant que <c>Monitoring:MetricsToken</c> n'était pas
/// renseigné. Or cette clé n'est pas committée : en production, l'endpoint était donc OUVERT par
/// défaut, et un simple avertissement au démarrage ne fermait rien.
/// <para>
/// Ces tests portent sur l'application RÉELLE (le vrai <c>Program.cs</c>) et sur les deux
/// environnements qui comptent : « Production » (fail closed, route absente) et hors production
/// (endpoint ouvert, pour le développement). Un test qui se contenterait de lire la configuration
/// ne prouverait pas que la ROUTE n'est pas montée : c'est bien la réponse HTTP qui est vérifiée.
/// </para>
/// </summary>
public class MetricsEndpointTests
{
    private const string Token = "jeton-de-supervision-de-test";

    /// <summary>Configuration minimale rendant un démarrage « Production » possible.</summary>
    private static Dictionary<string, string?> Production(IDictionary<string, string?>? extra = null)
    {
        // Le contrôle de démarrage refuse le paiement SIMULÉ en production (il accorde des crédits
        // sans encaissement) : on l'autorise explicitement, sinon l'hôte ne démarre pas et le test
        // échouerait pour une raison sans rapport avec /metrics.
        var config = new Dictionary<string, string?> { ["Payments:AllowSimulatedPayments"] = "true" };
        if (extra is null)
            return config;

        foreach (var (key, value) in extra)
            config[key] = value;

        return config;
    }

    [Fact]
    public async Task HorsProduction_SansJeton_LendpointResteOuvert()
    {
        using var factory = new WazapAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/metrics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("wazap_up 1", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Production_SansJeton_LendpointNestPasMonte()
    {
        using var factory = new WazapAppFactory(Production(), "Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/metrics");

        // 404 et non 401 : la route n'existe pas, il n'y a rien à découvrir ni à forcer.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("wazap_up", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task JetonRenseigne_HorsProduction_LeJetonEstExige()
    {
        using var factory = new WazapAppFactory(
            new Dictionary<string, string?> { ["Monitoring:MetricsToken"] = Token });
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/metrics")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/metrics?token=faux")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/metrics?token={Token}")).StatusCode);
    }

    [Fact]
    public async Task JetonRenseigne_LeJetonPeutVenirDeLEnTete()
    {
        using var factory = new WazapAppFactory(
            new Dictionary<string, string?> { ["Monitoring:MetricsToken"] = Token });
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        request.Headers.Add("X-Metrics-Token", Token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("wazap_up 1", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Production_AvecJeton_LendpointEstActifEtProtege()
    {
        using var factory = new WazapAppFactory(
            Production(new Dictionary<string, string?> { ["Monitoring:MetricsToken"] = Token }),
            "Production");
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/metrics")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/metrics?token={Token}")).StatusCode);
    }

    /// <summary>
    /// Garde-fou : fermer <c>/metrics</c> ne doit pas fermer la supervision. <c>/health</c> (sonde
    /// d'uptime) et <c>/health/details</c> (détail réservé aux administrateurs) doivent continuer à
    /// répondre — une « correction de sécurité » qui coupe la supervision serait pire que le mal.
    /// </summary>
    [Fact]
    public async Task Production_LesSondesDeSanteRestentDisponibles()
    {
        using var factory = new WazapAppFactory(Production(), "Production");
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/details")).StatusCode);
    }
}
