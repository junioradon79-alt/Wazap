namespace Wazap.API.Middleware;

/// <summary>
/// Protège les routes <c>/api/v1</c> (monté via <c>UseWhen</c>) par une clé API
/// (en-tête <c>X-Api-Key</c>) validée contre <see cref="Wazap.Application.Configuration.PublicApiOptions.Keys"/>.
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
        if (string.IsNullOrEmpty(key) || !options.Keys.Contains(key, StringComparer.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Clé API invalide ou absente. En-tête requis : X-Api-Key." });
            return;
        }

        await _next(context);
    }
}
