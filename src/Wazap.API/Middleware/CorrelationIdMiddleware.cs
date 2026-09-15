namespace Wazap.API.Middleware;

/// <summary>
/// Identifiant de corrélation par requête.
/// <para>
/// Sans lui, il était impossible de relier une erreur signalée par un utilisateur (« j'ai eu
/// une erreur en validant ma commande ») à la ligne de log correspondante : les logs ne
/// portaient ni identifiant de requête, ni chemin. L'identifiant est repris de l'en-tête
/// <c>X-Correlation-Id</c> s'il est fourni par un proxy/outil, sinon généré, puis renvoyé dans
/// la réponse et poussé dans la portée de journalisation de la requête.
/// </para>
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();

        // Borné et nettoyé : un en-tête client arbitraire ne doit ni polluer les logs ni
        // permettre d'y injecter des sauts de ligne.
        correlationId = string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")
            : new string(correlationId.Where(c => !char.IsControl(c)).Take(64).ToArray());

        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["correlationId"] = correlationId,
            ["path"] = context.Request.Path.Value ?? string.Empty,
            ["method"] = context.Request.Method
        }))
        {
            await _next(context);
        }
    }
}
