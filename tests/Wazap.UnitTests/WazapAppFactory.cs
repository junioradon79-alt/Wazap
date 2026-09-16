using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wazap.Infrastructure.Data;

namespace Wazap.UnitTests;

/// <summary>
/// Démarre l'application RÉELLE (le vrai <c>Program.cs</c>) dans un serveur de test, afin de
/// pouvoir vérifier le graphe d'injection de dépendances de production.
/// <para>
/// Motif : jusqu'ici les contrôleurs étaient reconstruits à la main dans les tests
/// (<c>WebhookHarness</c>), ce qui ne pouvait pas détecter un service OUBLIÉ dans
/// <c>Program.cs</c>. Ce type d'oubli a mis le webhook WhatsApp hors service en production
/// (toute requête entrante répondait 409). Ici le conteneur est celui de production.
/// </para>
/// <para>
/// Seuls deux écarts par rapport à la production, tous deux sans effet sur le graphe testé :
/// la base PostgreSQL est remplacée par une base InMemory (aucun accès réseau), et les workers
/// de fond sont retirés (ils interrogeraient la base et tourneraient sans fin). Aucun service
/// applicatif n'est retiré : un service manquant doit faire ÉCHOUER les tests.
/// </para>
/// </summary>
internal sealed class WazapAppFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Configuration hermétique : aucune valeur réelle (ni base, ni jeton WhatsApp, ni clé JWT
    /// de production). Plusieurs services jettent à la construction si une clé est absente
    /// (<c>WhatChimpService</c> exige un jeton non nul, Program.cs exige une clé JWT) : sans ces
    /// valeurs, le test échouerait pour une raison de CONFIG et non de DÉPENDANCE manquante,
    /// ce qui masquerait le verdict recherché.
    /// </summary>
    private static readonly Dictionary<string, string?> TestConfiguration = new()
    {
        // Chaîne inutilisée (la base InMemory la remplace) mais présente pour éviter toute surprise.
        ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=wazap-di-tests",
        // Assez longue pour le HMAC-SHA256 attendu par JwtBearer.
        ["Jwt:Key"] = new string('k', 64),
        // Pas de compte administrateur ni de données de démonstration pendant les tests.
        ["SeedAdmin:Username"] = string.Empty,
        ["SeedAdmin:Password"] = string.Empty,
        ["DemoData:Enabled"] = "false",
        ["Trial:Enabled"] = "false",
        // WhatChimpService / WhatChimpMediaDownloader exigent un jeton non nul à la construction.
        ["WhatChimp:ApiToken"] = "test-api-token",
        ["WhatChimp:PhoneNumberId"] = "000000000000000",
        ["WhatChimp:BaseUrl"] = "https://example.invalid/"
    };

    private readonly Dictionary<string, string?> _configuration;
    private readonly string _environment;

    /// <param name="extraConfiguration">
    /// Réglages ajoutés (ou remplacés) pour un test donné — par exemple les secrets de webhook.
    /// Sans cela, un test qui a besoin d'une valeur de configuration devait improviser son propre
    /// hôte, donc ne vérifiait plus le <c>Program.cs</c> réel.
    /// </param>
    /// <param name="environment">
    /// Environnement d'hébergement vu par <c>Program.cs</c> (<c>app.Environment.IsProduction()</c>).
    /// « Testing » par défaut : les tests ordinaires ne doivent pas dépendre des règles propres à la
    /// production (journaux JSON, paiement simulé interdit…). Les tests qui vérifient justement un
    /// comportement RÉSERVÉ à la production (ex. B-17 : <c>/metrics</c> fermé) passent
    /// « Production » explicitement, avec la configuration qui satisfait les contrôles de démarrage.
    /// </param>
    public WazapAppFactory(IDictionary<string, string?>? extraConfiguration = null, string environment = "Testing")
    {
        _environment = environment;
        _configuration = new Dictionary<string, string?>(TestConfiguration);
        if (extraConfiguration is null)
            return;

        foreach (var (key, value) in extraConfiguration)
            _configuration[key] = value;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Environnement demandé : ni « Development » (qui chargerait les user-secrets locaux,
        // donc la vraie base et la vraie clé JWT) ni, par défaut, « Production » (logs JSON et
        // règles de démarrage propres à la production).
        builder.UseEnvironment(_environment);

        // Valeurs injectées AUSSI dans la configuration de l'hôte (UseSetting), en plus des
        // sources de configuration ci-dessous : elles sont ainsi visibles à la fois pendant la
        // construction de Program.cs (lectures de builder.Configuration) et par les services
        // résolus. Sans cela, un service qui lit la configuration pourrait jeter et masquer le
        // vrai verdict du test (dépendance manquante).
        foreach (var (key, value) in _configuration)
            builder.UseSetting(key, value);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(_configuration);
        });

        builder.ConfigureServices(services =>
        {
            // 1) Base InMemory à la place de PostgreSQL.
            //    On retire TOUTES les inscriptions qui configurent le contexte : les options
            //    GÉNÉRIQUES, leur configuration (c'est elle qui portait le `UseNpgsql` d'origine)
            //    et le contexte lui-même. Ne retirer que `DbContextOptions<ApplicationDbContext>`
            //    laissait la configuration Npgsql en place : les deux fournisseurs cohabitaient
            //    alors dans le même fournisseur de services interne et la PREMIÈRE utilisation
            //    réelle du DbContext levait « Only a single database provider can be registered ».
            //    Ce défaut était latent : aucun test n'utilisait vraiment la base avant celui de
            //    révocation de session.
            foreach (var descriptor in services.Where(d =>
                         d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                         || d.ServiceType == typeof(DbContextOptions)
                         || d.ServiceType == typeof(ApplicationDbContext)
                         || (d.ServiceType.IsGenericType
                             && d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)))
                         .ToList())
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<ApplicationDbContext>(o =>
                o.UseInMemoryDatabase("di-resolution-tests"));

            // 2) Retrait des workers de fond (BackgroundService de Wazap.API.Services).
            //    Ciblé par type : on ne touche pas aux autres IHostedService du framework
            //    (dont GenericWebHostService, sans lequel le serveur ne démarrerait pas).
            foreach (var worker in services
                         .Where(d => d.ServiceType == typeof(IHostedService)
                                     && d.ImplementationType is { } type
                                     && type.Namespace == "Wazap.API.Services"
                                     && typeof(BackgroundService).IsAssignableFrom(type))
                         .ToList())
            {
                services.Remove(worker);
            }
        });
    }
}
