using Wazap.Domain.Services;
using Xunit;

namespace Wazap.UnitTests;

public class GeoDistanceTests
{
    [Fact]
    public void SamePoint_ShouldReturnZero()
    {
        var distance = GeoDistance.DistanceKm(48.8566, 2.3522, 48.8566, 2.3522);
        Assert.Equal(0, distance, 5);
    }

    [Fact]
    public void OneDegreeLongitude_AtEquator_ShouldBeAbout111Km()
    {
        var distance = GeoDistance.DistanceKm(0, 0, 0, 1);
        Assert.InRange(distance, 110.5, 111.5);
    }

    [Fact]
    public void ParisToLyon_ShouldBeAbout392Km()
    {
        var distance = GeoDistance.DistanceKm(48.8566, 2.3522, 45.7640, 4.8357);
        Assert.InRange(distance, 385, 400);
    }

    [Fact]
    public void NorthToSouthPole_ShouldBeAbout20015Km()
    {
        var distance = GeoDistance.DistanceKm(90, 0, -90, 0);
        Assert.InRange(distance, 19950, 20040);
    }
}
