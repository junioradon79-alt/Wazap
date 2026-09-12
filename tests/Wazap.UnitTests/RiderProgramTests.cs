using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Services;
using Wazap.Application.Configuration;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Programme « Ambassadeur WAZAP » : suivi des 3 conditions (enrôlement certifié,
/// livraisons, filleuls validés) et notifications de franchissement de seuil.
/// </summary>
public sealed class RiderProgramTests
{
    private static RiderProgramOptions Options(
        int deliveries = 3, int referrals = 2, int minFilleul = 2,
        double minRating = 0, bool enabled = true)
        => new()
        {
            Enabled = enabled,
            DeliveriesTarget = deliveries,
            ReferralsTarget = referrals,
            MinFilleulDeliveries = minFilleul,
            MinAverageRating = minRating,
            RewardLabel = "1 smartphone (type Redmi 15C)"
        };

    private static RiderProgramService CreateService(ApplicationDbContext ctx, RiderProgramOptions options,
        RecordingWhatsAppSender? sender = null)
        => new(ctx, options, sender ?? new RecordingWhatsAppSender(),
            NullLogger<RiderProgramService>.Instance);

    private static User NewRider(ApplicationDbContext ctx, string username, string phone, Guid? referrerId = null)
    {
        var rider = new User(username, "hash", UserRole.Rider, phone);
        if (referrerId is { } id)
            rider.SetReferral(id);
        ctx.Users.Add(rider);
        return rider;
    }

    private static void Certify(ApplicationDbContext ctx, Guid riderId)
    {
        var identity = new RiderIdentity(riderId, "Nom Complet");
        identity.Verify("Nom Complet", null, null, null);
        ctx.RiderIdentities.Add(identity);
    }

    private static void AddDeliveredOrder(ApplicationDbContext ctx, Guid riderId, string clientPhone = "+2250708091010")
    {
        var order = new Order("Client", clientPhone, "+2250700000001", "1 colis", 1000m);
        order.ConfirmByVendor();
        order.AwaitRiderAcceptance();
        order.AssignRider("+2250700000002");
        order.LinkRider(riderId);
        order.MarkReadyForPickup();
        order.MarkPickedUp();
        order.MarkInTransit();
        order.MarkDelivered();
        ctx.Orders.Add(order);
    }

    // ------------------------------------------------------------ Défauts de configuration

    [Fact]
    public void Options_Defaults_MatchThePublishedOffer()
    {
        var options = new RiderProgramOptions();

        Assert.True(options.Enabled);
        Assert.Equal(250, options.DeliveriesTarget);
        Assert.Equal(5, options.ReferralsTarget);
        Assert.Equal(25, options.MinFilleulDeliveries);
        Assert.Equal(0, options.MinAverageRating);
        Assert.Equal("RiderProgram", RiderProgramOptions.SectionName);
    }

    // ------------------------------------------------------------ Calcul de progression

    [Fact]
    public async Task BuildProgress_CountsDeliveriesAndValidatedFilleuls()
    {
        var ctx = TestInfra.NewContext(nameof(BuildProgress_CountsDeliveriesAndValidatedFilleuls));
        var referrer = NewRider(ctx, "parrain", "+2250700000002");
        for (var i = 0; i < 3; i++) AddDeliveredOrder(ctx, referrer.Id);

        // 2 filleuls avec 2 livraisons chacun (>= minimum) → validés.
        for (var f = 0; f < 2; f++)
        {
            var filleul = NewRider(ctx, $"filleul{f}", $"+22507000001{f}", referrer.Id);
            AddDeliveredOrder(ctx, filleul.Id);
            AddDeliveredOrder(ctx, filleul.Id);
        }

        await ctx.SaveChangesAsync();
        var service = CreateService(ctx, Options());

        var progress = await service.BuildProgressAsync(referrer.Id);

        Assert.NotNull(progress);
        Assert.Equal(3, progress!.Deliveries);
        Assert.Equal(2, progress.ValidatedReferrals);
        Assert.True(progress.DeliveriesMet);
        Assert.True(progress.ReferralsMet);
    }

    [Fact]
    public async Task BuildProgress_IgnoresInactiveFilleuls()
    {
        var ctx = TestInfra.NewContext(nameof(BuildProgress_IgnoresInactiveFilleuls));
        var referrer = NewRider(ctx, "parrain", "+2250700000002");

        // Filleul avec seulement 1 livraison (< min de 2) → NON validé.
        var faible = NewRider(ctx, "faible", "+2250700000011", referrer.Id);
        AddDeliveredOrder(ctx, faible.Id);

        await ctx.SaveChangesAsync();
        var service = CreateService(ctx, Options(minFilleul: 2));

        var progress = await service.BuildProgressAsync(referrer.Id);

        Assert.NotNull(progress);
        Assert.Equal(0, progress!.ValidatedReferrals);
        Assert.False(progress.ReferralsMet);
    }

    [Fact]
    public async Task BuildProgress_NotCertified_DoesNotUnlockReward()
    {
        var ctx = TestInfra.NewContext(nameof(BuildProgress_NotCertified_DoesNotUnlockReward));
        var referrer = NewRider(ctx, "parrain", "+2250700000002");
        for (var i = 0; i < 3; i++) AddDeliveredOrder(ctx, referrer.Id);
        for (var f = 0; f < 2; f++)
        {
            var filleul = NewRider(ctx, $"filleul{f}", $"+22507000001{f}", referrer.Id);
            AddDeliveredOrder(ctx, filleul.Id);
            AddDeliveredOrder(ctx, filleul.Id);
        }
        await ctx.SaveChangesAsync();
        var service = CreateService(ctx, Options());

        var progress = await service.BuildProgressAsync(referrer.Id);

        Assert.NotNull(progress);
        Assert.False(progress!.EnrolledMet);       // condition 1 : certification requise
        Assert.Equal(2, progress.ConditionsMet);   // livraisons + filleuls
        Assert.False(progress.RewardUnlocked);
    }

    [Fact]
    public async Task BuildProgress_AllConditionsMet_UnlocksReward()
    {
        var ctx = TestInfra.NewContext(nameof(BuildProgress_AllConditionsMet_UnlocksReward));
        var referrer = NewRider(ctx, "parrain", "+2250700000002");
        for (var i = 0; i < 3; i++) AddDeliveredOrder(ctx, referrer.Id);
        for (var f = 0; f < 2; f++)
        {
            var filleul = NewRider(ctx, $"filleul{f}", $"+22507000001{f}", referrer.Id);
            AddDeliveredOrder(ctx, filleul.Id);
            AddDeliveredOrder(ctx, filleul.Id);
        }
        Certify(ctx, referrer.Id);
        await ctx.SaveChangesAsync();
        var service = CreateService(ctx, Options());

        var progress = await service.BuildProgressAsync(referrer.Id);

        Assert.NotNull(progress);
        Assert.Equal(3, progress!.ConditionsMet);
        Assert.True(progress.RewardUnlocked);
    }

    [Fact]
    public async Task BuildProgress_RatingGate_BlocksReward_WhenTooLow()
    {
        var ctx = TestInfra.NewContext(nameof(BuildProgress_RatingGate_BlocksReward_WhenTooLow));
        var referrer = NewRider(ctx, "parrain", "+2250700000002");
        for (var i = 0; i < 3; i++) AddDeliveredOrder(ctx, referrer.Id);
        for (var f = 0; f < 2; f++)
        {
            var filleul = NewRider(ctx, $"filleul{f}", $"+22507000001{f}", referrer.Id);
            AddDeliveredOrder(ctx, filleul.Id);
            AddDeliveredOrder(ctx, filleul.Id);
        }
        Certify(ctx, referrer.Id);

        var ratedOrder = new Order("Client", "+2250708091011", "+2250700000001", "colis", 1000m);
        ctx.Orders.Add(ratedOrder);
        ctx.RiderRatings.Add(new RiderRating(ratedOrder.Id, referrer.Id, "+2250708091011", 2));
        await ctx.SaveChangesAsync();

        var service = CreateService(ctx, Options(minRating: 4.5));

        var progress = await service.BuildProgressAsync(referrer.Id);

        Assert.NotNull(progress);
        Assert.False(progress!.RatingMet);
        Assert.False(progress.RewardUnlocked);
    }

    [Fact]
    public async Task BuildProgress_ReturnsNull_WhenDisabledOrUnknown()
    {
        var ctx = TestInfra.NewContext(nameof(BuildProgress_ReturnsNull_WhenDisabledOrUnknown));
        var rider = NewRider(ctx, "rider", "+2250700000002");
        await ctx.SaveChangesAsync();

        var disabled = CreateService(ctx, Options(enabled: false));
        Assert.Null(await disabled.BuildProgressAsync(rider.Id));

        var enabled = CreateService(ctx, Options());
        Assert.Null(await enabled.BuildProgressAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task BuildProgressText_MentionsTargetsAndReward()
    {
        var ctx = TestInfra.NewContext(nameof(BuildProgressText_MentionsTargetsAndReward));
        var rider = NewRider(ctx, "rider", "+2250700000002");
        AddDeliveredOrder(ctx, rider.Id);
        await ctx.SaveChangesAsync();
        var service = CreateService(ctx, Options());

        var progress = await service.BuildProgressAsync(rider.Id);
        var text = RiderProgramService.BuildProgressText(progress!);

        Assert.Contains("Programme Ambassadeur", text);
        Assert.Contains("1/3", text);
        Assert.Contains("0/2", text);
        Assert.Contains("1 smartphone", text);
    }

    // ------------------------------------------------------------ Notifications

    [Fact]
    public async Task NotifyMilestones_NotifiesRider_OnDeliveryTarget()
    {
        var ctx = TestInfra.NewContext(nameof(NotifyMilestones_NotifiesRider_OnDeliveryTarget));
        var rider = NewRider(ctx, "rider", "+2250700000002");
        for (var i = 0; i < 3; i++) AddDeliveredOrder(ctx, rider.Id);
        await ctx.SaveChangesAsync();

        var sender = new RecordingWhatsAppSender();
        var service = CreateService(ctx, Options(), sender);

        await service.NotifyMilestonesAsync(rider.Id);

        Assert.Contains(sender.TextMessages, m => m.Phone == "+2250700000002" && m.Message.Contains("3 livraisons atteintes"));
    }

    [Fact]
    public async Task NotifyMilestones_NotifiesReferrer_WhenFilleulBecomesValidated()
    {
        var ctx = TestInfra.NewContext(nameof(NotifyMilestones_NotifiesReferrer_WhenFilleulBecomesValidated));
        var referrer = NewRider(ctx, "parrain", "+2250700000002");
        var filleul = NewRider(ctx, "filleul", "+2250700000011", referrer.Id);

        // Le filleul atteint exactement l'activité minimale (2 livraisons).
        AddDeliveredOrder(ctx, filleul.Id);
        AddDeliveredOrder(ctx, filleul.Id);
        await ctx.SaveChangesAsync();

        var sender = new RecordingWhatsAppSender();
        var service = CreateService(ctx, Options(referrals: 1, minFilleul: 2), sender);

        await service.NotifyMilestonesAsync(filleul.Id);

        Assert.Contains(sender.TextMessages, m => m.Phone == "+2250700000002");
    }

    [Fact]
    public async Task NotifyMilestones_DoesNothing_WhenDisabled()
    {
        var ctx = TestInfra.NewContext(nameof(NotifyMilestones_DoesNothing_WhenDisabled));
        var rider = NewRider(ctx, "rider", "+2250700000002");
        for (var i = 0; i < 3; i++) AddDeliveredOrder(ctx, rider.Id);
        await ctx.SaveChangesAsync();

        var sender = new RecordingWhatsAppSender();
        var service = CreateService(ctx, Options(enabled: false), sender);

        await service.NotifyMilestonesAsync(rider.Id);

        Assert.Empty(sender.TextMessages);
    }
}


