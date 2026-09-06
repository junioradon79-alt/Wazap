namespace Wazap.Application.Configuration;

/// <summary>
/// Séquence d'onboarding vendeur (section « VendorOnboarding ») : messages planifiés
/// J+1 / J+3 / J+7. Nécessite des templates WhatsApp approuvés par Meta pour les
/// envois hors fenêtre 24 h (voir <see cref="WhatsAppOptions.TemplateVendorOnboardingDay1"/>).
/// </summary>
public sealed class VendorOnboardingOptions
{
    public const string SectionName = "VendorOnboarding";

    /// <summary>Active le worker de planification (à activer quand les templates sont approuvés).</summary>
    public bool Enabled { get; set; }
}
