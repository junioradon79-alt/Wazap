using System.Security.Cryptography;
using System.Text;

namespace Wazap.API.Middleware;

/// <summary>
/// Vérification des notifications entrantes de l'API Meta WhatsApp Cloud (endpoint
/// <c>/api/webhook/whatsapp</c>). Meta signe chaque POST avec <c>X-Hub-Signature-256</c> :
/// HMAC-SHA256 du corps brut avec l'App Secret de l'app Meta.
/// <para>
/// Règles :
/// <list type="bullet">
/// <item>en-tête absent (mode WhatChimp legacy) → le middleware n'intervient pas ; la
/// vérification reste au contrôleur (token de query) ;</item>
/// <item>en-tête présent mais secret non configuré → <c>503</c> (fail closed : jamais d'acceptation
/// sans vérification) ;</item>
/// <item>signature invalide → <c>403</c> avant même d'atteindre le contrôleur.</item>
/// </list>
/// </para>
/// </summary>
public sealed class MetaWebhookSignatureMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;

    public MetaWebhookSignatureMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var signature = context.Request.Headers["X-Hub-Signature-256"].FirstOrDefault();

        if (context.Request.Method == HttpMethods.Post
            && context.Request.Path.StartsWithSegments("/api/webhook/whatsapp")
            && !string.IsNullOrWhiteSpace(signature))
        {
            var secret = _configuration["Meta:WebhookAppSecret"];
            if (string.IsNullOrWhiteSpace(secret))
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsync("Webhook Meta non configuré (Meta:WebhookAppSecret).");
                return;
            }

            // Le corps brut doit être mis en mémoire pour calculer le HMAC, puis relu par le binder.
            context.Request.EnableBuffering();
            string body;
            using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true))
            {
                body = await reader.ReadToEndAsync();
            }
            context.Request.Body.Position = 0;

            if (!VerifySignature(signature, body, secret))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Signature HMAC invalide.");
                return;
            }
        }

        await _next(context);
    }

    /// <summary>Vérifie <c>sha256=hexdigest</c> contre le HMAC-SHA256(body, secret), en temps constant.</summary>
    internal static bool VerifySignature(string headerValue, string body, string secret)
    {
        var expected = headerValue;
        if (expected.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            expected = expected["sha256=".Length..];

        if (string.IsNullOrWhiteSpace(expected))
            return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

        return computed.Length == expected.Length
            && CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(computed),
                Encoding.ASCII.GetBytes(expected));
    }
}