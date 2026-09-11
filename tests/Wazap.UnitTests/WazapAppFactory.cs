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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Environnement dédié : ni « Development » (qui chargerait les user-secrets locaux,
        // donc la vraie base et la vraie clé JWT) ni « Production » (logs JSON).
        builder.UseEnvironment("Testing");

        // Valeurs injectées AUSSI dans la configuration de l'hôte (UseSetting), en plus des
        // sources de configuration ci-dessous : elles sont ainsi visibles à la fois pendant la
        // construction de Program.cs (lectures de builder.Configuration) et par les services
        // résolus. Sans cela, un service qui lit la configuration pourrait jeter et masquer le
        // vrai verdict du test (dépendance manquante).
        foreach (var (key, value) in TestConfiguration)
            builder.UseSetting(key, value);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(TestConfiguration);
        });

        builder.ConfigureServices(services =>
        {
            // 1) Base InMemory à la place de PostgreSQL.
            //    On retire TOUTES les options de contexte enregistrées (et pas un seul élément) :
            //    une seconde inscription ajouterait un doublon que « Single » refuserait, avec un
            //    message trompeur alors qu'aucune dépendance ne manque réellement.
            var dbOptions = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>))
                .ToList();
            foreach (var option in dbOptions)
                services.Remove(option);

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
