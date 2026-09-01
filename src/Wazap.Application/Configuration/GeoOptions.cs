namespace Wazap.Application.Configuration
{
    /// <summary>
    /// Configuration du module de géolocalisation (section « Geo »).
    /// </summary>
    public sealed class GeoOptions
    {
        public const string SectionName = "Geo";

        public double MaxDistanceKm { get; set; } = 15;
        public int LocationFreshnessMinutes { get; set; } = 5;
        public int ExclusivitySeconds { get; set; } = 30;
        public int GlobalTimeoutMinutes { get; set; } = 5;
        public int LocationRetentionHours { get; set; } = 24;
        public string NominatimUserAgent { get; set; } = "wazap-app";
    }
}
