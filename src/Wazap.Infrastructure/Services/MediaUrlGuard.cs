using System.Net;

namespace Wazap.Infrastructure.Services;

/// <summary>
/// Garde-fou des URL de médias WhatsApp : l'URL provient du corps d'un webhook, donc
/// d'une donnée NON fiable. Sans contrôle, le serveur émettait un GET vers l'hôte de
/// l'attaquant <b>en y joignant son jeton d'envoi WhatsApp</b> (en-tête Bearer côté Meta,
/// query <c>apiToken</c> côté WhatChimp) : le jeton était volé, et l'appel servait aussi
/// de sonde SSRF vers le réseau interne (169.254.169.254, localhost…).
/// </summary>
public static class MediaUrlGuard
{
    /// <summary>Domaines légitimes des médias de l'API Meta WhatsApp Cloud.</summary>
    private static readonly string[] TrustedMediaDomains =
    [
        "facebook.com",
        "fbcdn.net",
        "fbsbx.com",
        "whatsapp.net",
        "instagram.com"
    ];

    /// <summary>Domaines légitimes de la passerelle historique WhatChimp.</summary>
    private static readonly string[] TrustedWhatChimpDomains =
    [
        "whatchimp.com",
        "whatchimp.co"
    ];

    /// <summary>URL Meta de téléchargement d'un média (https, domaine Meta officiel) ?</summary>
    public static bool IsTrustedMetaMediaUrl(string? url)
        => IsTrustedUrl(url, TrustedMediaDomains);

    /// <summary>URL de la passerelle WhatChimp (seule destination où le jeton peut être joint) ?</summary>
    public static bool IsTrustedWhatChimpUrl(string? url)
        => IsTrustedUrl(url, TrustedWhatChimpDomains);

    private static bool IsTrustedUrl(string? url, string[] domains)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        // HTTPS uniquement : une URL http enverrait le jeton en clair.
        if (uri.Scheme != Uri.UriSchemeHttps)
            return false;

        return IsTrustedHost(uri.Host, domains) && !IsPrivateOrLoopbackHost(uri.Host);
    }

    /// <summary>L'hôte appartient-il à l'un des domaines (ou à l'un de leurs sous-domaines) ?</summary>
    public static bool IsTrustedHost(string? host, string[] domains)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        foreach (var domain in domains)
        {
            if (host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Hôte local, privé ou non résolu par un nom de domaine public : à ne jamais appeler
    /// (protection contre le SSRF vers le réseau interne et les métadonnées cloud).
    /// </summary>
    public static bool IsPrivateOrLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return true;

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!IPAddress.TryParse(host.Trim('[', ']'), out var ip))
            return false;

        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var octets = ip.GetAddressBytes();
            return octets[0] switch
            {
                0 or 10 or 127 => true,
                169 when octets[1] == 254 => true,   // link-local / métadonnées cloud
                172 when octets[1] is >= 16 and <= 31 => true,
                192 when octets[1] == 168 => true,
                _ => false
            };
        }

        // IPv6 : boucle locale (déjà couverte), lien-local (fe80::/10) et unique-local (fc00::/7).
        var bytes = ip.GetAddressBytes();
        return (bytes[0] & 0xFE) == 0xFC || (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80);
    }
}
