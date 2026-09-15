using System.Security.Cryptography;
using System.Text;
using Wazap.Application.Configuration;

namespace Wazap.API.Middleware;

/// <summary>
/// Authentifie les notifications ENTRANTES adressées à <c>/api/webhook/whatsapp</c>.
/// <para>
/// Deux preuves acceptées, dans cet ordre :
/// <list type="number">
/// <item><b>Signature Meta</b> — l'API WhatsApp Cloud signe chaque POST avec
/// <c>X-Hub-Signature-256</c> : HMAC-SHA256 du corps brut calculé avec l'App Secret.
/// En-tête présent mais secret non configuré → <c>503</c> ; signature invalide → <c>403</c>.</item>
/// <item><b>Jeton partagé</b> — l'ancienne passerelle (WhatChimp) ne signe pas : elle est
/// acceptée si elle présente le jeton de configuration (<c>Meta:WebhookVerifyToken</c>, repli
/// <c>WhatChimp:WebhookToken</c>) en query <c>?token=</c> ou en en-tête <c>X-Webhook-Token</c>.</item>
/// </list>
/// </para>
/// <para>
/// Sans aucune des deux, la requête est <b>refusée</b> (<c>403</c>). Ce refus est essentiel :
/// le corps du webhook pilote tout le produit (créer une course au nom d'un vendeur, accepter
/// une offre, clôturer une livraison, déclarer un sinistre). Un POST anonyme sur cet endpoint
/// est donc une usurpation d'identité complète, pas une simple fuite d'information.
/// L'échappatoire <c>WebhookSecurity:RequireAuthentication=false</c> existe pour le
/// développement local et est signalée bruyamment au démarrage.
/// </para>
/// </summary>
public sealed class MetaWebhookSignatureMiddleware
{
    /// <summary>En-tête de signature de l'API WhatsApp Cloud (HMAC-SHA256 du corps brut).</summary>
    public const string SignatureHeader = "X-Hub-Signature-256";

    /// <summary>En-tête de jeton partagé accepté pour les passerelles qui ne signent pas.</summary>
    public const string SharedTokenHeader = "X-Webhook-Token";

    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly WebhookSecurityOptions _options;
    private readonly ILogger<MetaWebhookSignatureMiddleware> _logger;

    public MetaWebhookSignatureMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        WebhookSecurityOptions options,
        ILogger<MetaWebhookSignatureMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _options = options;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Method != HttpMethods.Post
            || !context.Request.Path.StartsWithSegments("/api/webhook/whatsapp"))
        {
            await _next(context);
            return;
        }

        var signature = context.Request.Headers[SignatureHeader].FirstOrDefault();

        // 1) Signature Meta : la preuve la plus forte, vérifiée sur le corps brut.
        if (!string.IsNullOrWhiteSpace(signature))
        {
            var secret = _configuration["Meta:WebhookAppSecret"];
            if (string.IsNullOrWhiteSpace(secret))
            {
                await RejectAsync(context, StatusCodes.Status503ServiceUnavailable,
                    "Webhook Meta non configuré (Meta:WebhookAppSecret).");
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
                await RejectAsync(context, StatusCodes.Status403Forbidden, "Signature HMAC invalide.");
                return;
            }

            await _next(context);
            return;
        }

        // 2) Aucune signature : accepté uniquement si l'authentification est explicitement désactivée.
        if (!_options.RequireAuthentication)
        {
            _logger.LogWarning(
                "POST non signé accepté sur /api/webhook/whatsapp : "
                + "WebhookSecurity:RequireAuthentication=false (à n'utiliser qu'en développement local).");
            await _next(context);
            return;
        }

        // 3) Jeton partagé de la passerelle historique (query ?token= ou en-tête X-Webhook-Token).
        var sharedToken = _configuration["Meta:WebhookVerifyToken"] ?? _configuration["WhatChimp:WebhookToken"];
        if (string.IsNullOrWhiteSpace(sharedToken))
        {
            _logger.LogError(
                "POST sans signature refusé : aucun jeton partagé configuré "
                + "(Meta:WebhookVerifyToken / WhatChimp:WebhookToken).");
            await RejectAsync(context, StatusCodes.Status503ServiceUnavailable,
                "Webhook non configuré (jeton de vérification absent).");
            return;
        }

        var provided = context.Request.Query["token"].FirstOrDefault()
                       ?? context.Request.Headers[SharedTokenHeader].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(provided)
            && Wazap.Application.Helpers.SecurityHelper.FixedTimeEquals(provided, sharedToken))
        {
            await _next(context);
            return;
        }

        _logger.LogWarning(
            "POST refusé sur /api/webhook/whatsapp : ni signature {Header} valide, ni jeton partagé correct.",
            SignatureHeader);
        await RejectAsync(context, StatusCodes.Status403Forbidden,
            "Requête non authentifiée : signature Meta ou jeton de webhook requis.");
    }

    private static async Task RejectAsync(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync(message);
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
                Encoding.ASCII.GetBytes(expected.ToLowerInvariant()));
    }
}
