using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wazap.Application.Abstractions;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

public class VerifiedPaymentWebhookTests
{
    private const string Secret = "isolated-geniuspay-webhook-secret";

    private static HttpRequestMessage Event(Guid paymentId, decimal? amount, bool validSignature = true, string? currency = "XOF", string status = "completed")
    {
        var payload = JsonSerializer.Serialize(new
        {
            data = new { reference = "VERIFIED-" + paymentId, amount, currency, status, metadata = new { wazap_transaction_id = paymentId } }
        });
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhook/geniuspay")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Webhook-Timestamp", timestamp);
        request.Headers.Add("X-Webhook-Event", "payment.success");
        request.Headers.Add("X-Webhook-Signature", validSignature
            ? GeniusPaySignatureVerifier.ComputeHmacSha256(timestamp + "." + payload, Secret)
            : Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        return request;
    }

    [Fact]
    public async Task SignedWebhook_CompletesExactCheckoutAndNotifiesOnlyOnce()
    {
        var sender = new RecordingWhatsAppSender();
        using var factory = new WazapAppFactory(new Dictionary<string, string?> { ["GeniusPay:WebhookSecret"] = Secret });
        using var app = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IWhatsAppSender>(sender)));
        using var client = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = new Order("Client", "+2250700000701", "+2250700000702", "Colis", 25000, deliveryFee: 1500);
        var older = new OrderPayment(order.Id, 26683);
        older.SetSplitQuote(24818, 1500, 365, "Shared");
        var newer = new OrderPayment(order.Id, 27000);
        db.AddRange(order, older, newer);
        await db.SaveChangesAsync();

        using var forged = Event(older.Id, older.Amount, validSignature: false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(forged)).StatusCode);
        using var missingAmount = Event(older.Id, null);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(missingAmount)).StatusCode);
        using var incorrectAmount = Event(older.Id, 1);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(incorrectAmount)).StatusCode);
        Assert.Equal(TransactionStatus.Pending, (await db.OrderPayments.AsNoTracking().SingleAsync(p => p.Id == older.Id)).Status);

        using var incorrectCurrency = Event(older.Id, older.Amount, currency: "USD");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(incorrectCurrency)).StatusCode);
        using var missingCurrency = Event(older.Id, older.Amount, currency: null);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(missingCurrency)).StatusCode);
        using var mismatchedEvent = Event(older.Id, older.Amount, status: "failed");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(mismatchedEvent)).StatusCode);
        Assert.Equal(TransactionStatus.Pending, (await db.OrderPayments.AsNoTracking().SingleAsync(p => p.Id == older.Id)).Status);

        using var verified = Event(older.Id, older.Amount);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(verified)).StatusCode);
        var completed = await db.OrderPayments.AsNoTracking().SingleAsync(p => p.Id == older.Id);
        Assert.Equal(TransactionStatus.Completed, completed.Status);
        Assert.Equal(24818, completed.VendorPayoutDue);
        Assert.Equal(1500, completed.RiderPayoutDue);
        Assert.Equal(TransactionStatus.Pending, (await db.OrderPayments.AsNoTracking().SingleAsync(p => p.Id == newer.Id)).Status);
        var count = sender.TextMessages.Count;
        Assert.True(count > 0);
        using var duplicate = Event(older.Id, older.Amount);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(duplicate)).StatusCode);
        Assert.Equal(count, sender.TextMessages.Count);
    }
}
