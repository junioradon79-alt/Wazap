namespace Wazap.Application.Configuration;

/// <summary>
/// Configuration de l'intégration YCloud (partenaire officiel Tier-1 Meta WhatsApp Business).
/// </summary>
public sealed class YCloudOptions
{
    public const string SectionName = "YCloud";

    /// <summary>Active l'envoi et la réception via l'API YCloud.</summary>
    public bool Enabled { get; set; }

    /// <summary>Clé d'API YCloud (fournie dans Developers > API Keys sur le portail YCloud).</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Numéro d'expéditeur officiel (ex. "+2250787119520").</summary>
    public string PhoneNumber { get; set; } = "";

    /// <summary>URL de base de l'API YCloud.</summary>
    public string BaseUrl { get; set; } = "https://api.ycloud.com/v2/whatsapp/";

    /// <summary>Secret optionnel de signature du webhook YCloud.</summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>Langue par défaut des templates WhatsApp.</summary>
    public string LanguageCode { get; set; } = "fr";
}
