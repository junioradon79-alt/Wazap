namespace Wazap.Application.Configuration
{
    /// <summary>
    /// Configuration de l'agrégateur de paiement GeniusPay (section « GeniusPay »).
    /// Clés API et webhook secret à stocker dans les user-secrets.
    /// </summary>
    public sealed class GeniusPayOptions
    {
        public const string SectionName = "GeniusPay";

        public bool Enabled { get; set; }
        public string BaseUrl { get; set; } = "https://geniuspay.ci/api/v1/merchant";
        public string ApiKey { get; set; } = string.Empty;
        public string ApiSecret { get; set; } = string.Empty;
        public string WebhookSecret { get; set; } = string.Empty;
        public string SuccessUrl { get; set; } = string.Empty;
        public string ErrorUrl { get; set; } = string.Empty;
    }
}
