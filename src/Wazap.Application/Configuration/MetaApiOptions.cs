namespace Wazap.Application.Configuration;

/// <summary>
/// Connexion au compte WhatsApp Business dédié via l'API Meta WhatsApp Cloud (Graph).
/// Section de configuration « Meta » (mêmes règles de secret que les autres sections :
/// le jeton permanent et l'App Secret vont dans la config d'environnement, jamais en dur).
/// </summary>
public sealed class MetaApiOptions
{
    public const string SectionName = "Meta";

    /// <summary>
    /// Bascule d'infrastructure : tant que <c>false</c>, le produit continue d'utiliser la
    /// passerelle WhatChimp (implémentations existantes) ; à <c>true</c>, toutes les
    /// notifications WhatsApp passent par l'API Meta WhatsApp Cloud + le webhook Meta.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Jeton d'accès permanent du compte système (WABA).</summary>
    public string ApiToken { get; set; } = "";

    /// <summary>Identifiant du numéro dédié (Phone Number ID du WABA).</summary>
    public string PhoneNumberId { get; set; } = "";

    /// <summary>Version de l'API Graph (défaut : la plus récente utilisée par l'outil de campagne).</summary>
    public string ApiVersion { get; set; } = "v21.0";

    /// <summary>Base de l'API Graph.</summary>
    public string GraphUrl { get; set; } = "https://graph.facebook.com/";

    /// <summary>Langue des templates (correspond à la langue des corps approuvés).</summary>
    public string LanguageCode { get; set; } = "fr";

    /// <summary>Token de vérification du webhook entrant (GET hub.verify_token).</summary>
    public string WebhookVerifyToken { get; set; } = "";

    /// <summary>App Secret de l'app Meta : vérification HMAC-SHA256 des notifications reçues.</summary>
    public string WebhookAppSecret { get; set; } = "";
}