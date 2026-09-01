namespace Wazap.Domain.Services;

/// <summary>
/// Calculs de distance géographique (formule de Haversine, à vol d'oiseau).
/// </summary>
public static class GeoDistance
{
    private const double EarthRadiusKm = 6371.0;

    /// <summary>
    /// Distance en kilomètres entre deux points GPS.
    /// </summary>
    public static double DistanceKm(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2)
    {
        var dLat = ToRadians(latitude2 - latitude1);
        var dLon = ToRadians(longitude2 - longitude1);
        var lat1 = ToRadians(latitude1);
        var lat2 = ToRadians(latitude2);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1) * Math.Cos(lat2)
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusKm * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
