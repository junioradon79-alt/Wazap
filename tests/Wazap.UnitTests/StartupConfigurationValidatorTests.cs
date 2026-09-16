using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Wazap.API.Configuration;
using Wazap.Application.Configuration;
using Wazap.Domain.Configuration;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Contrôle de démarrage : il REFUSE le démarrage quand la configuration est incohérente
/// (l'application ne sert alors aucun trafic). Un faux positif bloque donc un déploiement.
///
/// Deux garanties sont vérifiées ici :
///  ① une configuration saine ne produit AUCUN problème (pas de faux positif) ;
///  ② les en-têtes de proxy (B-16) ne peuvent pas être crus sans liste de proxies déclarés : sans
///     cette liste, n'importe quel client choisirait son adresse — donc son compartiment de
///     limitation de débit (les 5 politiques sont partitionnées par IP) et échapperait au
///     verrouillage anti force-brute de la connexion.
/// </summary>
public class StartupConfigurationValidatorTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Wazap.API";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>Configuration saine : chaîne de connexion, clé JWT de 32 caractères, paiement actif.</summary>
    private static IConfiguration Healthy(params (string Key, string? Value)[] extra)
    {
        // Les valeurs passées en extra ÉCRASENT les valeurs saines (un dictionnaire, et non une
        // liste : la source en mémoire refuse les clés dupliquées).
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=wazap",
            ["Jwt:Key"] = new string('k', 40),
            ["GeniusPay:Enabled"] = "true",
        };
        foreach (var (key, value) in extra)
            values[key] = value;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static IReadOnlyList<string> Problems(
        IConfiguration configuration,
        bool production = false,
        bool geniusPayEnabled = true)
        => StartupConfigurationValidator.FindProblems(
            configuration,
            new Env(production ? Environments.Production : Environments.Development),
            new GeoOptions(),
            new ClientPaymentOptions(),
            new RiderReputationOptions(),
            new RetentionOptions(),
            new GeniusPayOptions { Enabled = geniusPayEnabled },
            [],
            []);

    [Fact]
    public void ConfigurationSaine_NeProduitAucunProbleme()
    {
        // Garde-fou contre le faux positif : c'est ce contrôle qui, en échouant, empêcherait
        // un déploiement par ailleurs correct.
        Assert.Empty(Problems(Healthy()));
    }

    [Fact]
    public void ClesIndispensables_Absentes_OuTropCourtes_SontSignalees()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new List<KeyValuePair<string, string?>>
        {
            new("ConnectionStrings:DefaultConnection", ""),
            new("Jwt:Key", "trop-court"),
        }).Build();

        var problems = Problems(config);

        Assert.Contains(problems, p => p.Contains("DefaultConnection"));
        Assert.Contains(problems, p => p.Contains("Jwt:Key"));
    }

    // ------------------------------------------------------- B-16 : en-têtes de proxy

    [Fact]
    public void EntetesDeProxy_SansListeDeProxies_SontRefuses()
    {
        // Croire X-Forwarded-For sans liste = laisser le client écrire sa propre adresse.
        var problems = Problems(Healthy(("Networking:TrustForwardedHeaders", "true")));

        Assert.Contains(problems, p => p.Contains("Networking:KnownProxies"));
    }

    [Fact]
    public void EntetesDeProxy_AvecAdressesValides_NeProduisentAucunProbleme()
    {
        var problems = Problems(Healthy(
            ("Networking:TrustForwardedHeaders", "true"),
            ("Networking:KnownProxies:0", "10.0.0.1"),
            ("Networking:KnownProxies:1", "192.168.1.10")));

        Assert.Empty(problems);
    }

    [Fact]
    public void EntetesDeProxy_AvecEntreeNonIp_EstSignale()
    {
        var problems = Problems(Healthy(
            ("Networking:TrustForwardedHeaders", "true"),
            ("Networking:KnownProxies:0", "proxy.example.com")));

        Assert.Contains(problems, p => p.Contains("adresses IP valides"));
    }

    [Fact]
    public void EntetesDeProxy_Desactives_NExigentAucuneListe()
    {
        // Comportement par défaut : hébergement IIS in-process, l'adresse du client est déjà
        // celle de la connexion — aucun réglage à fournir.
        Assert.Empty(Problems(Healthy(("Networking:TrustForwardedHeaders", "false"))));
    }

    // ------------------------------------------------------- Paiement simulé en production

    [Fact]
    public void PaiementSimule_EnProduction_EstRefuseParDefaut()
    {
        var problems = Problems(Healthy(("GeniusPay:Enabled", "false")),
            production: true, geniusPayEnabled: false);

        Assert.Contains(problems, p => p.Contains("paiement SIMULÉ"));
    }

    [Fact]
    public void PaiementSimule_EnProduction_EstAccepteSiAssumeExplicitement()
    {
        var problems = Problems(
            Healthy(("GeniusPay:Enabled", "false"), ("Payments:AllowSimulatedPayments", "true")),
            production: true,
            geniusPayEnabled: false);

        Assert.Empty(problems);
    }
}
