using Wazap.Application.Dtos;
using Wazap.Application.Services;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Pondération du matching par la réputation (« PreferHigherRatedRiders ») : les livreurs bien
/// notés (assez d'avis) sont proposés avant les autres, la distance servant de départage.
/// </summary>
public sealed class DeliveryOfferReputationTests
{
    private static NearestRiderDto Rider(Guid id, double distanceKm)
        => new(id, distanceKm);

    private static Dictionary<Guid, double?> Scores(params (Guid RiderId, double? Score)[] entries)
        => new(entries.ToDictionary(e => e.RiderId, e => e.Score));

    [Fact]
    public void HigherRatedRider_ComesFirst_EvenIfFarther()
    {
        var wellRated = Guid.NewGuid();
        var neutral = Guid.NewGuid();
        var scores = Scores((wellRated, 4.8), (neutral, null));

        var cmp = DeliveryOfferService.CompareWithReputation(
            Rider(wellRated, 12.0), Rider(neutral, 0.5), scores);

        Assert.True(cmp < 0);
    }

    [Fact]
    public void BothRated_HighestAverageFirst_ThenDistance()
    {
        var top = Guid.NewGuid();
        var lower = Guid.NewGuid();
        var scores = Scores((top, 4.5), (lower, 4.0));

        Assert.True(DeliveryOfferService.CompareWithReputation(
            Rider(lower, 1.0), Rider(top, 20.0), scores) > 0);
    }

    [Fact]
    public void EqualAverage_ClosestRiderFirst()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var scores = Scores((a, 4.2), (b, 4.2));

        var cmp = DeliveryOfferService.CompareWithReputation(
            Rider(a, 5.0), Rider(b, 2.0), scores);

        Assert.True(cmp > 0);
    }

    [Fact]
    public void BothNeutral_ClosestRiderFirst()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var scores = Scores((a, null), (b, null));

        var cmp = DeliveryOfferService.CompareWithReputation(
            Rider(a, 7.0), Rider(b, 1.5), scores);

        Assert.True(cmp > 0);
    }
}