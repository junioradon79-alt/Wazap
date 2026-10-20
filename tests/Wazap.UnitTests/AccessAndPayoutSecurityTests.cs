using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wazap.API.Services;
using Wazap.Application.Helpers;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Xunit;

namespace Wazap.UnitTests;

public class AccessAndPayoutSecurityTests
{
    [Fact]
    public async Task AccessLink_IsSingleUse_AndIsNotARefreshToken()
    {
        using var factory = new WazapAppFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = new User("magic-" + Guid.NewGuid().ToString("N"), "unusable-password-hash", UserRole.Vendor, "+2250700000001");
        db.Users.Add(user); await db.SaveChangesAsync();
        var link = await PasswordlessAccess.IssueAsync(db, user);
        var token = link.Split("#access=")[1];
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = token })).StatusCode);
        var result = await client.PostAsJsonAsync("/api/auth/access-link", new { token });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/access-link", new { token })).StatusCode);
        var ordinaryRefresh = JsonDocument.Parse(await result.Content.ReadAsStringAsync()).RootElement.GetProperty("refreshToken").GetString();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/access-link", new { token = ordinaryRefresh })).StatusCode);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task AccessLink_RejectsRevokedExpiredAndMfa(bool revoke, bool mfa)
    {
        using var factory = new WazapAppFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = new User("magic-" + Guid.NewGuid().ToString("N"), "hash", UserRole.Rider);
        db.Users.Add(user); await db.SaveChangesAsync();
        const string token = "test-opaque-token";
        db.RefreshTokens.Add(new RefreshToken(user.Id, PasswordlessAccess.Hash(token),
            revoke || mfa ? DateTime.UtcNow.AddMinutes(15) : DateTime.UtcNow.AddMinutes(-1), user.SecurityStamp));
        if (revoke) user.RevokeSessions();
        if (mfa) user.EnableTwoFactor(Totp.GenerateSecret());
        await db.SaveChangesAsync();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/access-link", new { token })).StatusCode);
    }

    [Theory]
    [InlineData("start-delivery")]
    [InlineData("scan-handover")]
    public async Task PublicTracking_CannotActAsRider(string route)
    {
        using var factory = new WazapAppFactory(); using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/client/orders/{Guid.NewGuid()}/{route}", new { })).StatusCode);
    }

    [Fact]
    public void ReceiptCapability_IsOrderBoundExpiringAndNotSerialized()
    {
        var a = new Order("A", "+2250700000001", "+2250500000001", "Test", 5000);
        var b = new Order("B", "+2250700000002", "+2250500000001", "Test", 5000);
        a.EnsureDeliveryCode(); b.EnsureDeliveryCode();
        var token = a.CreateReceiptToken();
        Assert.True(a.VerifyReceiptToken(token)); Assert.False(b.VerifyReceiptToken(token));
        Assert.False(a.VerifyReceiptToken(a.CreateReceiptToken(DateTimeOffset.UtcNow.AddMinutes(-1))));
        Assert.False(a.VerifyReceiptToken(token + "x")); Assert.False(a.VerifyReceiptToken(a.DeliveryCode));
        a.RenewRecipientAccess();
        Assert.False(a.VerifyReceiptToken(token));
        var json = JsonSerializer.Serialize(a);
        Assert.DoesNotContain("ReceiptSecret", json); Assert.DoesNotContain("DeliveryCode\"", json);
    }

    [Fact]
    public void Payout_RequiresExactAmountRealEvidenceAndSingleConfirmation()
    {
        var payout = new PaymentPayout(Guid.NewGuid(), "Vendor", Guid.NewGuid(), "+2250700000001", 4900);
        Assert.Throws<ArgumentException>(() => payout.Confirm(Guid.NewGuid(), "GeniusPay", "real-reference", "receipt", 4800));
        Assert.Throws<ArgumentException>(() => payout.Confirm(Guid.NewGuid(), "GeniusPay", "DISB-fabricated", "receipt", 4900));
        Assert.Throws<ArgumentException>(() => payout.Confirm(Guid.NewGuid(), "GeniusPay", "real-reference", "", 4900));
        payout.Confirm(Guid.NewGuid(), "GeniusPay", "real-reference", "Justificatif du prestataire contrôlé", 4900);
        Assert.NotNull(payout.ConfirmedAtUtc);
        Assert.Throws<InvalidOperationException>(() => payout.Confirm(Guid.NewGuid(), "GeniusPay", "another", "receipt", 4900));
    }

    [Fact]
    public async Task PayoutRegister_RequiresAdministrator()
    {
        using var factory = new WazapAppFactory(); using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/payouts")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/admin/payouts/{Guid.NewGuid()}/confirm", new { })).StatusCode);
    }

    [Fact]
    public async Task PayoutConfirmation_RejectsVendorWrongAmountAndDuplicate()
    {
        using var factory = new WazapAppFactory(); using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var admin = new User("payout-admin", "hash", UserRole.Admin);
        var vendor = new User("payout-vendor", "hash", UserRole.Vendor, "+2250700000001");
        var order = new Order("Client", "+2250500000001", vendor.PhoneNumber!, "Colis", 5000);
        var payment = new OrderPayment(order.Id, 5000); payment.Complete("sandbox-checkout", 0);
        var payout = new PaymentPayout(payment.Id, "Vendor", vendor.Id, vendor.PhoneNumber, 5000);
        db.AddRange(admin, vendor, order, payment, payout); await db.SaveChangesAsync();
        var tokens = scope.ServiceProvider.GetRequiredService<Wazap.Application.Abstractions.IJwtTokenGenerator>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Generate(vendor));
        var route = $"/api/admin/payouts/{payout.Id}/confirm";
        var evidence = new { provider = "Sandbox", reference = "sandbox-transfer", evidence = "Justificatif de test", amount = 5000 };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(route, evidence)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Generate(admin));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(route, new { provider = "Sandbox", reference = "sandbox-transfer", evidence = "Justificatif", amount = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(route, evidence)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(route, evidence)).StatusCode);
    }

    [Theory]
    [InlineData(null, "XOF", "expected")]
    [InlineData(5000, "USD", "expected")]
    [InlineData(5000, "XOF", "other")]
    public void Reconciliation_RejectsIncompleteOrMismatchedProviderProof(int? amount, string currency, string reference)
        => Assert.False(new Wazap.Application.Abstractions.PaymentStatusResult("completed", amount, currency, reference).Confirms(5000, "expected"));
}
