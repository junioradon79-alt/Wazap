using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Garde-fou contre la régression la plus coûteuse possible : un service INJECTÉ mais jamais
/// enregistré dans <c>Program.cs</c>.
/// <para>
/// Contexte : <c>ClientOrderBotService</c> était injecté dans <c>WebhookWhatsAppController</c>
/// sans être enregistré. En production, le conteneur DI ne pouvait donc pas construire le
/// contrôleur et TOUT message WhatsApp entrant répondait 409 — vendeurs, livreurs, notes
/// client et bot prospects étaient hors service. Les tests existants ne l'ont pas vu parce
/// qu'ils instanciaient le contrôleur à la main, sans passer par le conteneur.
/// </para>
/// <para>
/// Ce test démarre l'application réelle et construit CHAQUE contrôleur découvert par MVC avec
/// l'activateur de contrôleurs de production (<see cref="IControllerActivator"/>), c'est-à-dire
/// la mécanique exacte utilisée pour servir une requête. Tout service non résoluble fait échouer
/// le test, en nommant le contrôleur fautif.
/// </para>
/// </summary>
public class DiControllerResolutionTests
{
    [Fact]
    public void ChaqueControleur_EstConstructibleParLeConteneurDeProduction()
    {
        using var factory = new WazapAppFactory();
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider;

        var controllerTypes = DiscoverControllers(provider);
        Assert.NotEmpty(controllerTypes);

        // L'activateur RÉEL de MVC (celui qui sert les requêtes HTTP).
        var activator = provider.GetRequiredService<IControllerActivator>();

        var failures = new List<string>();
        foreach (var controllerType in controllerTypes)
        {
            try
            {
                var actionDescriptor = new ControllerActionDescriptor
                {
                    ControllerTypeInfo = controllerType.GetTypeInfo()
                };
                var context = new ControllerContext(new ActionContext(
                    new DefaultHttpContext { RequestServices = provider },
                    new RouteData(),
                    actionDescriptor));

                var instance = activator.Create(context);
                Assert.NotNull(instance);
            }
            catch (Exception ex)
            {
                failures.Add($"{controllerType.FullName} : {ex.GetType().Name} — {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0,
            "Contrôleur(s) non constructible(s) par le conteneur DI de production "
            + "(service manquant dans Program.cs ?) :\n - " + string.Join("\n - ", failures));
    }

    /// <summary>Contrôleurs découverts par MVC dans l'application réelle.</summary>
    private static IReadOnlyList<Type> DiscoverControllers(IServiceProvider provider)
    {
        var partManager = provider.GetRequiredService<ApplicationPartManager>();
        var feature = new ControllerFeature();
        partManager.PopulateFeature(feature);
        return feature.Controllers.Select(c => c.AsType()).ToList();
    }
}
