using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Wazap.Domain.Entities;
using Wazap.Infrastructure.Data;

namespace Wazap.API.Middleware;

/// <summary>Authentifie un appareil enrôlé et réserve son nonce en base avant tout effet métier.</summary>
public sealed class GatewayAuthenticationMiddleware(RequestDelegate next)
{
    private const long MaxBodyBytes = 8 * 1024 * 1024;

    public async Task InvokeAsync(HttpContext http, IConfiguration configuration, ApplicationDbContext db)
    {
        if (!HttpMethods.IsPost(http.Request.Method))
        {
            await next(http);
            return;
        }

        var device = http.Request.Headers["X-Gateway-Device"].ToString();
        var timestamp = http.Request.Headers["X-Gateway-Timestamp"].ToString();
        var nonce = http.Request.Headers["X-Gateway-Nonce"].ToString();
        var signature = http.Request.Headers["X-Gateway-Signature"].ToString();
        if (device.Length is < 1 or > 64 || device.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')
            || !Guid.TryParseExact(nonce, "N", out _)
            || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || Math.Abs((decimal)DateTimeOffset.UtcNow.ToUnixTimeSeconds() - seconds) > 300)
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // Retirer une entrée révoque immédiatement l'appareil. Aucun secret n'est embarqué dans l'APK.
        var secret = configuration[$"Gateway:Devices:{device}"];
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        if (http.Request.ContentLength > MaxBodyBytes)
        {
            http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        http.Request.EnableBuffering(bufferThreshold: 64 * 1024, bufferLimit: MaxBodyBytes);
        byte[] bodyHash;
        try
        {
            bodyHash = await SHA256.HashDataAsync(http.Request.Body, http.RequestAborted);
            http.Request.Body.Position = 0;
        }
        catch (IOException)
        {
            http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        var canonical = $"{http.Request.Method}\n{http.Request.Path}\n{timestamp}\n{nonce}\n{Convert.ToHexString(bodyHash).ToLowerInvariant()}";
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(canonical));
        byte[] supplied;
        try { supplied = Convert.FromHexString(signature); }
        catch (FormatException) { supplied = []; }
        if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var messageId = $"gateway:{device}:{nonce}";
        if (await db.ProcessedWebhookMessages.AnyAsync(m => m.Id == messageId, http.RequestAborted))
        {
            http.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }
        var marker = new ProcessedWebhookMessage(messageId);
        db.ProcessedWebhookMessages.Add(marker);
        try { await db.SaveChangesAsync(http.RequestAborted); }
        catch (DbUpdateException)
        {
            db.Entry(marker).State = EntityState.Detached;
            if (!await db.ProcessedWebhookMessages.AnyAsync(m => m.Id == messageId, http.RequestAborted))
                throw;
            http.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }
        await next(http);
    }
}
