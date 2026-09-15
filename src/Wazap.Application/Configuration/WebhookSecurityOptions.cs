namespace Wazap.Application.Configuration;

/// <summary>
/// Sécurité des webhooks ENTRANTS : exiger une preuve d'authenticité avant de router un
/// message vers le produit (commandes, offres, statuts livreur, sinistres).
/// <para>
/// Sans cette exigence, l'endpoint <c>POST /api/webhook/whatsapp</c> est un point d'entrée
/// anonyme : n'importe qui connaissant l'URL peut créer des courses, accepter des offres
/// ou clôturer des livraisons au nom d'un vendeur ou d'un livreur (usurpation complète).
/// </para>
/// <para>
/// Deux preuves acceptées : la signature HMAC Meta (<c>X-Hub-Signature-256</c>, calculée sur
/// le corps brut avec l'App Secret) ou, pour l'ancienne passerelle qui ne signe pas, le jeton
/// partagé de configuration (<c>Meta:WebhookVerifyToken</c> / <c>WhatChimp:WebhookToken</c>)
/// transmis en query <c>?token=</c> ou en en-tête <c>X-Webhook-Token</c>.
/// </para>
/// </summary>
public sealed class WebhookSecurityOptions
{
    public const string SectionName = "WebhookSecurity";

    /// <summary>
    /// Exige une preuve d'authenticité sur chaque POST entrant. Valeur par défaut : <c>true</c>
    /// (fail closed). La passer <c>false</c> n'est légitime qu'en développement local, jamais
    /// sur une URL publique : le démarrage le signale bruyamment.
    /// </summary>
    public bool RequireAuthentication { get; set; } = true;
}
