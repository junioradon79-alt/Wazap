namespace Wazap.API.Middleware;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Protège les routes <c>/api/v1</c> (monté via <c>UseWhen</c>) par une clé API
/// (en-tête <c>X-Api-Key</c>) validée contre <see cref="Wazap.Application.Configuration.PublicApiOptions.Keys"/>.
/// La comparaison est faite en temps constant (et sur des empreintes SHA-256, pour ne pas
/// révéler la longueur de la clé par le temps de réponse).
/// </summary>
public sealed class PublicApiKeyMiddleware
{
    private readonly RequestDelegate _next;

    public PublicApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, Wazap.Application.Configuration.PublicApiOptions options)
    {
        if (options.Keys.Count == 0)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { error = "API publique non configurée (PublicApi:Keys)." });
            return;
        }

        var key = context.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(key) || !IsKnownKey(key, options.Keys))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Clé API invalide ou absente. En-tête requis : X-Api-Key." });
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Comparaison à temps constant : on parcourt TOUTES les clés configurées sans sortie
    /// anticipée, afin que le temps de réponse ne révèle pas le préfixe correct d'une clé.
    /// </summary>
    private static bool IsKnownKey(string provided, IReadOnlyList<string> configuredKeys)
    {
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        var match = false;

        foreach (var configured in configuredKeys)
        {
            var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
            match |= CryptographicOperations.FixedTimeEquals(providedHash, configuredHash);
        }

        return match;
    }
}
