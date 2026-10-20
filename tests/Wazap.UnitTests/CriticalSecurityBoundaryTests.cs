using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

public class CriticalSecurityBoundaryTests
{
    private const string Secret = "test-device-secret-at-least-32-bytes-long";

    private static WazapAppFactory GatewayFactory() => new(new Dictionary<string, string?>
    {
        ["Gateway:Devices:test-device"] = Secret
    });

    private static HttpRequestMessage Signed(string body, string? nonce = null, long? timestamp = null)
    {
        const string path = "/api/gateway/whatsapp/process";
        nonce ??= Guid.NewGuid().ToString("N");
        var seconds = (timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var canonical = $"POST\n{path}\n{seconds}\n{nonce}\n{hash}";
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(canonical)));
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Gateway-Device", "test-device");
        request.Headers.Add("X-Gateway-Timestamp", seconds);
        request.Headers.Add("X-Gateway-Nonce", nonce);
        request.Headers.Add("X-Gateway-Signature", signature);
        return request;
    }

    [Theory]
    [InlineData("process")]
    [InlineData("upload-photo")]
    public async Task Gateway_AnonymousRequest_IsRejectedBeforeBusinessLogic(string action)
    {
        using var factory = GatewayFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync($"/api/gateway/whatsapp/{action}", new { sender = "+2250701020304", text = "DISPO" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Gateway_ValidSignatureIsAccepted_ButReplayedNonceIsRejected()
    {
        using var factory = GatewayFactory();
        using var client = factory.CreateClient();
        var nonce = Guid.NewGuid().ToString("N");
        // Corps vide : l'authentification passe puis le contrôleur refuse les données métier.
        using var first = Signed("{}", nonce);
        using var replay = Signed("{}", nonce);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(first)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(replay)).StatusCode);
    }

    [Fact]
    public async Task Gateway_ModifiedBodyAndExpiredSignatureAreRejected()
    {
        using var factory = GatewayFactory();
        using var client = factory.CreateClient();
        using var modified = Signed("{}");
        modified.Content = new StringContent("{\"text\":\"DISPO\"}", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(modified)).StatusCode);
        using var expired = Signed("{}", timestamp: DateTimeOffset.UtcNow.AddMinutes(-6).ToUnixTimeSeconds());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(expired)).StatusCode);
    }

    [Fact]
    public async Task PublicClient_CannotConfirmPayment_EvenForAnExistingOrder()
    {
        using var factory = new WazapAppFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = new Order("Client", "+2250700000800", "+2250700000801", "Colis", 5000);
        var payment = new OrderPayment(order.Id, 5000);
        db.AddRange(order, payment);
        await db.SaveChangesAsync();
        var response = await client.PostAsJsonAsync($"/api/client/orders/{order.Id}/complete-split", new { paymentReference = "FORGED" });
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        var stored = await db.OrderPayments.AsNoTracking().SingleAsync(p => p.Id == payment.Id);
        Assert.Equal(TransactionStatus.Pending, stored.Status);
    }

    [Fact]
    public async Task AdminCookie_PasswordAloneCannotBypassMfa()
    {
        using var factory = new WazapAppFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var username = "mfa-" + Guid.NewGuid().ToString("N");
        var user = new User(username, new PasswordHasher().Hash("TestPassword123!"), UserRole.Admin);
        user.EnableTwoFactor(Wazap.Application.Helpers.Totp.GenerateSecret());
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var response = await client.PostAsJsonAsync("/api/auth/ui/login", new { username, password = "TestPassword123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var verified = await client.PostAsJsonAsync("/api/auth/ui/login-2fa", new
        {
            username, password = "TestPassword123!", code = Wazap.Application.Helpers.Totp.CurrentCode(user.TwoFactorSecret!)
        });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.Contains(verified.Headers.GetValues("Set-Cookie"), c => c.StartsWith("wazap.admin="));
    }

    [Fact]
    public async Task AdminCookie_IsRejectedAfterPasswordChange()
    {
        using var factory = new WazapAppFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var username = "cookie-" + Guid.NewGuid().ToString("N");
        var user = new User(username, new PasswordHasher().Hash("TestPassword123!"), UserRole.Admin);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var login = await client.PostAsJsonAsync("/api/auth/ui/login", new { username, password = "TestPassword123!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith("wazap.admin="));
        user.ChangePassword(new PasswordHasher().Hash("NewPassword123!"));
        await db.SaveChangesAsync();
        var response = await client.GetAsync("/health/details");
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("wazap.admin=;"));
    }
}
