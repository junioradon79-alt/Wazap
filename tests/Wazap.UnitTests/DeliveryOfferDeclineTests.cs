using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Xunit;

namespace Wazap.UnitTests;

public class DeliveryOfferDeclineTests
{
    private static (ApplicationDbContext Context, DeliveryOfferService Service) CreateHarness()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("offer-decline-" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var context = new ApplicationDbContext(options);
        var sender = new RecordingWhatsAppSender();
        var whatsAppOptions = new WhatsAppOptions();
        var orchestrator = new WhatsAppOrchestrationService(sender, whatsAppOptions, NullLogger<WhatsAppOrchestrationService>.Instance);

        var service = new DeliveryOfferService(
            context,
            sender,
            whatsAppOptions,
            new GeoOptions(),
            new GroupingOptions(),
            new ClientOptions(),
            orchestrator,
            new RiderSecurityOptions(),
            new RiderReputationOptions(),
            new ClientPaymentOptions(),
            new RiderPriorityOptions(),
            NullLogger<DeliveryOfferService>.Instance);

        return (context, service);
    }

    [Fact]
    public async Task DeclineOfferAsync_WhenValid_TransitionsToDeclined()
    {
        var (context, service) = CreateHarness();
        var riderId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var offer = new DeliveryOffer(orderId, riderId, 1);
        context.DeliveryOffers.Add(offer);
        await context.SaveChangesAsync();

        var success = await service.DeclineOfferAsync(offer.Id, riderId);

        Assert.True(success);
        var updated = await context.DeliveryOffers.FindAsync(offer.Id);
        Assert.NotNull(updated);
        Assert.Equal(DeliveryOfferStatus.Declined, updated.Status);
        Assert.NotNull(updated.RespondedAt);
    }

    [Fact]
    public async Task DeclineOfferAsync_WhenOfferNotFound_ReturnsFalse()
    {
        var (_, service) = CreateHarness();

        var success = await service.DeclineOfferAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(success);
    }

    [Fact]
    public async Task DeclineOfferAsync_WhenWrongRider_ReturnsFalseAndLeavesPending()
    {
        var (context, service) = CreateHarness();
        var riderId = Guid.NewGuid();
        var otherRiderId = Guid.NewGuid();
        var offer = new DeliveryOffer(Guid.NewGuid(), riderId, 1);
        context.DeliveryOffers.Add(offer);
        await context.SaveChangesAsync();

        var success = await service.DeclineOfferAsync(offer.Id, otherRiderId);

        Assert.False(success);
        var updated = await context.DeliveryOffers.FindAsync(offer.Id);
        Assert.NotNull(updated);
        Assert.Equal(DeliveryOfferStatus.Pending, updated.Status);
        Assert.Null(updated.RespondedAt);
    }

    [Fact]
    public async Task DeclineOfferAsync_WhenAlreadyAcceptedOrExpired_ReturnsFalse()
    {
        var (context, service) = CreateHarness();
        var riderId = Guid.NewGuid();
        var offer = new DeliveryOffer(Guid.NewGuid(), riderId, 1);
        offer.Accept();
        context.DeliveryOffers.Add(offer);
        await context.SaveChangesAsync();

        var success = await service.DeclineOfferAsync(offer.Id, riderId);

        Assert.False(success);
        var updated = await context.DeliveryOffers.FindAsync(offer.Id);
        Assert.NotNull(updated);
        Assert.Equal(DeliveryOfferStatus.Accepted, updated.Status);
    }
}
