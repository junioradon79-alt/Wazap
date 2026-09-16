using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory.Infrastructure.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Propagation du jeton d'annulation depuis les WORKERS jusqu'aux ports sortants (P2 / C-14,
/// dernier temps).
///
/// Enjeu : à l'arrêt du service, un worker qui attend une passerelle WhatsApp lente retient
/// l'arrêt. Le délai HTTP est désormais borné (30 s), mais le jeton doit aussi descendre
/// jusqu'à l'appel sortant pour interrompre au plus tôt — sinon la propagation ajoutée ne sert
/// à rien et une refonte ultérieure la perdrait sans bruit.
/// </summary>
public class CancellationPropagationTests
{
    /// <summary>Fixture minimale : un vendeur géolocalisé crédité, un livreur éligible, une course.</summary>
    private static async Task<(ApplicationDbContext Context, DeliveryOfferService Offers,
        RecordingWhatsAppSender Sender, Guid OrderId)> BuildBroadcastFixtureAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("cancel-propagation-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var context = new ApplicationDbContext(options);

        var vendor = new User("vendeur", "hash", UserRole.Vendor, "+2250700000700");
        vendor.SetZone("Cocody");
        vendor.AddCredits(5);
        vendor.UpdateLocation(5.3599, -4.0083);
        context.Users.Add(vendor);

        var rider = new User("livreur", "hash", UserRole.Rider, "+2250700000701");
        rider.SetAvailability(true);
        rider.UpdateLocation(5.3600, -4.0084);
        context.Users.Add(rider);

        var order = new Order("Client", "+2250700000702", vendor.PhoneNumber!, "1 colis", 3000m);
        order.LinkVendor(vendor.Id);
        order.ConfirmByVendor();
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var sender = new RecordingWhatsAppSender();
        var orchestrator = new WhatsAppOrchestrationService(sender, new WhatsAppOptions(),
            NullLogger<WhatsAppOrchestrationService>.Instance);

        var offers = new DeliveryOfferService(context, sender, new WhatsAppOptions(),
            new GeoOptions { MaxDistanceKm = 15 }, new GroupingOptions(), new ClientOptions(),
            orchestrator, new RiderSecurityOptions(), new RiderReputationOptions(),
            new ClientPaymentOptions(), new RiderPriorityOptions(),
            NullLogger<DeliveryOfferService>.Instance);

        return (context, offers, sender, order.Id);
    }

    [Fact]
    public async Task Diffusion_PropageLeJetonDUneArretJusquAuxEnvois()
    {
        var (context, offers, sender, orderId) = await BuildBroadcastFixtureAsync();
        using var _ = context;

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Le worker de diffusion passe son `stoppingToken` : le jeton doit atteindre l'envoi.
        await offers.BroadcastAsync(orderId, cts.Token);

        Assert.NotNull(sender.LastToken);
        Assert.True(sender.LastToken!.Value.IsCancellationRequested,
            "Le jeton d'arrêt n'est pas parvenu jusqu'au port d'envoi WhatsApp.");
    }

    [Fact]
    public async Task Diffusion_SansJeton_ResteAppelableCommeAvant()
    {
        var (context, offers, sender, orderId) = await BuildBroadcastFixtureAsync();
        using var _ = context;

        // Les appelants qui n'ont pas de jeton (webhook, tests, outils) ne doivent pas changer.
        var result = await offers.BroadcastAsync(orderId);

        Assert.Equal(1, result.OffersCreated);
        Assert.NotNull(sender.LastToken);
        Assert.False(sender.LastToken!.Value.CanBeCanceled);
    }

    /// <summary>
    /// Les méthodes d'envoi sollicitées par les workers doivent accepter un jeton : sans lui,
    /// un arrêt ne peut pas interrompre l'appel sortant en cours.
    /// </summary>
    [Theory]
    [InlineData(nameof(WhatsAppOrchestrationService.SendRiderOfferAsync))]
    [InlineData(nameof(WhatsAppOrchestrationService.SendBatchOfferAsync))]
    [InlineData(nameof(WhatsAppOrchestrationService.TrySendVendorOnboardingAsync))]
    [InlineData(nameof(WhatsAppOrchestrationService.SendCreditPurchaseConfirmationAsync))]
    [InlineData(nameof(WhatsAppOrchestrationService.SendRiderPriorityPurchaseConfirmationAsync))]
    public void EnvoisDeclenchesParLesWorkers_AcceptentUnJeton(string methodName)
    {
        var method = typeof(WhatsAppOrchestrationService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == methodName);

        Assert.Contains(typeof(CancellationToken), method.GetParameters().Select(p => p.ParameterType));
    }
}
