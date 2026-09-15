using Microsoft.AspNetCore.Http;
using System.Threading.RateLimiting;

namespace Wazap.API;

/// <summary>
/// Clés de partition des limites de débit.
/// <para>
/// Une politique de limiteur NON partitionnée partage un unique compteur entre TOUS les
/// appelants : dix tentatives de connexion depuis une seule IP suffisaient à faire répondre
/// <c>429</c> à l'ensemble des utilisateurs, et un afflux sur le webhook bloquait les
/// notifications WhatsApp légitimes (Meta recevant 429 perd l'événement). Chaque politique
/// est donc partitionnée : par clé d'API quand il y en a une, sinon par adresse IP.
/// </para>
/// </summary>
public static class RateLimitPartitions
{
    /// <summary>Partition par clé d'API (repli : adresse IP si l'en-tête est absent).</summary>
    public static string ByApiKey(HttpContext context)
        => context.Request.Headers["X-Api-Key"].FirstOrDefault() is { Length: > 0 } key
            ? "key:" + key
            : ByClient(context);

    /// <summary>
    /// Partition par adresse IP de l'appelant. <c>inconnu</c> regroupe les requêtes dont
    /// l'adresse n'est pas résolue (elles restent limitées, mais partagent un compteur).
    /// </summary>
    public static string ByClient(HttpContext context)
        => "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "inconnu");

    /// <summary>Configuration d'une fenêtre fixe (une instance par partition).</summary>
    public static FixedWindowRateLimiterOptions FixedWindow(int permitLimit, TimeSpan window)
        => new()
        {
            PermitLimit = Math.Max(1, permitLimit),
            Window = window,
            QueueLimit = 0
        };
}
