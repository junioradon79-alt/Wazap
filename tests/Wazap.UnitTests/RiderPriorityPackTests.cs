using Wazap.Application.Dtos;
using Wazap.Application.Services;
using Wazap.Application.Validators;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Pack prioritaire LIVREUR (« pack prioritaire » de la politique commerciale) : WAZAP vend la
/// <b>visibilité</b> (être proposé en premier dans son rayon), jamais une attribution garantie.
/// Couvre le droit de tirage (<see cref="User.GrantPriority"/>), le cycle de vie de l'achat
/// (<see cref="RiderPriorityPurchase"/>) et le tri du matching
/// (<see cref="RiderMatchingService.ApplyPriorityOrdering"/>), plafond d'équité compris.
/// </summary>
public sealed class RiderPriorityPackTests
{
    private static User Rider() => new("rider-prio", "hash", UserRole.Rider, "+2250700000000");

    private static NearestRiderDto Candidate(Guid id, double distanceKm, DateTime? priorityUntilUtc = null)
        => new(id, distanceKm, priorityUntilUtc);

    // --- Droit de tirage (User) ---

    [Fact]
    public void GrantPriority_WithoutPreviousPriority_StartsFromNow()
    {
        var rider = Rider();

        // Instant de référence capturé UNE fois : la fenêtre était calculée autour de DEUX
        // appels séparés à UtcNow, ce qui rendait l'assertion sensible à une machine chargée
        // ou à un ajustement d'horloge (± 14 min de tolérance implicite).
        var before = DateTime.UtcNow;
        rider.GrantPriority(7);
        var after = DateTime.UtcNow;

        Assert.NotNull(rider.PriorityUntilUtc);
        Assert.InRange(rider.PriorityUntilUtc!.Value, before.AddDays(7), after.AddDays(7));
    }

    [Fact]
    public void GrantPriority_WithActivePriority_ExtendsWithoutLosingPaidDays()
    {
        var rider = Rider();
        rider.GrantPriority(7);
        var afterFirst = rider.PriorityUntilUtc;

        var before = DateTime.UtcNow;
        rider.GrantPriority(30);
        var after = DateTime.UtcNow;

        // Prolongation à partir de l'échéance en cours, jamais à partir de maintenant —
        // bornes exactes, sans fenêtre de tolérance.
        Assert.InRange(rider.PriorityUntilUtc!.Value, afterFirst!.Value.AddDays(30), afterFirst.Value.AddDays(30));
        Assert.True(rider.PriorityUntilUtc.Value >= before.AddDays(30), "les jours payés ne doivent pas être perdus");
    }

    [Fact]
    public void GrantPriority_WithNonPositiveDays_Throws()
    {
        var rider = Rider();

        Assert.Throws<ArgumentOutOfRangeException>(() => rider.GrantPriority(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rider.GrantPriority(-1));
    }

    [Fact]
    public void HasActivePriority_FalseWhenNeverPurchased()
    {
        var rider = Rider();

        Assert.False(rider.HasActivePriority(DateTime.UtcNow));
        Assert.Equal(0, rider.RemainingPriorityDays(DateTime.UtcNow));
    }

    [Fact]
    public void HasActivePriority_TrueWithinWindow_FalseAfterExpiry()
    {
        var rider = Rider();
        rider.GrantPriority(7);
        var until = rider.PriorityUntilUtc!.Value;

        Assert.True(rider.HasActivePriority(until.AddMinutes(-1)));
        Assert.False(rider.HasActivePriority(until));
        Assert.False(rider.HasActivePriority(until.AddMinutes(1)));
    }

    [Fact]
    public void RemainingPriorityDays_RoundsUpToWholeDays()
    {
        var rider = Rider();
        rider.GrantPriority(7);

        Assert.Equal(7, rider.RemainingPriorityDays(DateTime.UtcNow));
        Assert.Equal(0, rider.RemainingPriorityDays(rider.PriorityUntilUtc!.Value));
    }

    // --- Cycle de vie de l'achat (RiderPriorityPurchase) ---

    [Fact]
    public void Purchase_WithoutReference_GetsProvisionalReference_ExcludedFromReconciliation()
    {
        var purchase = new RiderPriorityPurchase(Guid.NewGuid(), 1000m, 7, null, "Priorite Livreur 7 jours");

        Assert.StartsWith(RiderPriorityPurchase.PendingReferencePrefix, purchase.TransactionReference);
        Assert.Equal(TransactionStatus.Pending, purchase.Status);
        Assert.Null(purchase.CompletedAt);
    }

    [Fact]
    public void Purchase_Complete_SetsCompletedStatusAndTimestamp()
    {
        var purchase = new RiderPriorityPurchase(Guid.NewGuid(), 3000m, 30, "RDRP-PENDING-x", "Pack 30");
        purchase.SetTransactionReference("GP-123456");

        purchase.Complete("GP-123456");

        Assert.Equal(TransactionStatus.Completed, purchase.Status);
        Assert.Equal("GP-123456", purchase.TransactionReference);
        Assert.NotNull(purchase.CompletedAt);
    }

    [Fact]
    public void Purchase_CompleteOnFailed_Throws()
    {
        var purchase = new RiderPriorityPurchase(Guid.NewGuid(), 1000m, 7);
        purchase.MarkFailed();

        Assert.Throws<InvalidOperationException>(() => purchase.Complete("GP-1"));
    }

    [Fact]
    public void Purchase_MarkFailedOnCompleted_Throws()
    {
        var purchase = new RiderPriorityPurchase(Guid.NewGuid(), 1000m, 7);
        purchase.Complete("GP-1");

        Assert.Throws<InvalidOperationException>(() => purchase.MarkFailed());
    }

    [Fact]
    public void Purchase_InvalidAmountOrDuration_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RiderPriorityPurchase(Guid.NewGuid(), 0m, 7));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RiderPriorityPurchase(Guid.NewGuid(), 1000m, 0));
    }

    // --- Tri du matching (pack prioritaire) ---

    [Fact]
    public void PriorityOrdering_ActiveRiderComesFirst_EvenIfFarther()
    {
        var now = DateTime.UtcNow;
        var subscribed = Guid.NewGuid();
        var closest = Guid.NewGuid();
        var ordered = new List<NearestRiderDto>
        {
            Candidate(subscribed, 12.0, now.AddDays(3)),
            Candidate(closest, 0.4)
        };

        var result = RiderMatchingService.ApplyPriorityOrdering(ordered, now, maxPriorityRidersPerWave: 2);

        Assert.Equal(subscribed, result[0].RiderUserId);
        Assert.Equal(closest, result[1].RiderUserId);
    }

    [Fact]
    public void PriorityOrdering_ExpiredPriorityDoesNotBoost()
    {
        var now = DateTime.UtcNow;
        var expired = Guid.NewGuid();
        var closest = Guid.NewGuid();
        var ordered = new List<NearestRiderDto>
        {
            Candidate(closest, 0.4),
            Candidate(expired, 12.0, now.AddMinutes(-1))
        };

        var result = RiderMatchingService.ApplyPriorityOrdering(ordered, now, maxPriorityRidersPerWave: 2);

        Assert.Equal(closest, result[0].RiderUserId);
        Assert.Equal(expired, result[1].RiderUserId);
    }

    [Fact]
    public void PriorityOrdering_NoPriority_KeepsOrderUntouched()
    {
        var now = DateTime.UtcNow;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var ordered = new List<NearestRiderDto> { Candidate(a, 0.4), Candidate(b, 5.0) };

        var result = RiderMatchingService.ApplyPriorityOrdering(ordered, now, maxPriorityRidersPerWave: 2);

        Assert.Equal(new[] { a, b }, result.Select(r => r.RiderUserId));
    }

    [Fact]
    public void PriorityOrdering_CapLimitsBoostedPlaces_Fairness()
    {
        var now = DateTime.UtcNow;
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var unfunded = Guid.NewGuid();
        var third = Guid.NewGuid();
        // Tri de base déjà appliqué : le non-prioritaire est devant les 3 prioritaires.
        var ordered = new List<NearestRiderDto>
        {
            Candidate(unfunded, 0.5),
            Candidate(first, 8.0, now.AddDays(5)),
            Candidate(second, 9.0, now.AddDays(5)),
            Candidate(third, 10.0, now.AddDays(5))
        };

        var result = RiderMatchingService.ApplyPriorityOrdering(ordered, now, maxPriorityRidersPerWave: 2);

        // Les 2 places réservées vont aux prioritaires les mieux classés ; le 3e reste à son rang.
        Assert.Equal(new[] { first, second, unfunded, third }, result.Select(r => r.RiderUserId));
    }

    [Fact]
    public void PriorityOrdering_NoCap_KeepsGeographicOrder()
    {
        var now = DateTime.UtcNow;
        var closest = Guid.NewGuid();
        var subscribed = Guid.NewGuid();
        var ordered = new List<NearestRiderDto>
        {
            Candidate(closest, 0.4),
            Candidate(subscribed, 12.0, now.AddDays(3))
        };

        var result = RiderMatchingService.ApplyPriorityOrdering(ordered, now, maxPriorityRidersPerWave: 0);

        Assert.Equal(new[] { closest, subscribed }, result.Select(r => r.RiderUserId));
    }

    [Fact]
    public void PriorityOrdering_NeverAddsOrRemovesCandidates()
    {
        var now = DateTime.UtcNow;
        var ordered = new List<NearestRiderDto>
        {
            Candidate(Guid.NewGuid(), 0.4),
            Candidate(Guid.NewGuid(), 2.0, now.AddDays(2)),
            Candidate(Guid.NewGuid(), 3.0),
            Candidate(Guid.NewGuid(), 4.0, now.AddDays(1))
        };

        var result = RiderMatchingService.ApplyPriorityOrdering(ordered, now, maxPriorityRidersPerWave: 5);

        Assert.Equal(ordered.Count, result.Count);
        Assert.Equal(
            ordered.Select(r => r.RiderUserId).OrderBy(id => id),
            result.Select(r => r.RiderUserId).OrderBy(id => id));
    }

    [Fact]
    public void IsPriorityActive_ReflectsExpiryWindow()
    {
        var now = DateTime.UtcNow;

        Assert.True(RiderMatchingService.IsPriorityActive(
            Candidate(Guid.NewGuid(), 1.0, now.AddDays(1)), now));
        Assert.False(RiderMatchingService.IsPriorityActive(
            Candidate(Guid.NewGuid(), 1.0, now), now));
        Assert.False(RiderMatchingService.IsPriorityActive(
            Candidate(Guid.NewGuid(), 1.0), now));
    }

    // --- Validation de la demande d'achat ---

    [Fact]
    public void Validator_AcceptsWellFormedRequest()
    {
        var validator = new BuyRiderPriorityRequestValidator();

        var result = validator.Validate(new BuyRiderPriorityRequest
        {
            RiderId = Guid.NewGuid(),
            PackName = "Priorite Livreur 7 jours"
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_RejectsEmptyRiderIdAndPackName()
    {
        var validator = new BuyRiderPriorityRequestValidator();

        var result = validator.Validate(new BuyRiderPriorityRequest
        {
            RiderId = Guid.Empty,
            PackName = string.Empty
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(BuyRiderPriorityRequest.RiderId));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(BuyRiderPriorityRequest.PackName));
    }
}