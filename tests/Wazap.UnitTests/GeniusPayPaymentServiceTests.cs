using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

public class GeniusPayPaymentServiceTests
{
    private static GeniusPayOptions Options() => new()
    {
        BaseUrl = "https://geniuspay.ci/api/v1/merchant",
        ApiKey = "pk_test",
        ApiSecret = "sk_test"
    };

    [Fact]
    public async Task RequestPaymentAsync_ShouldSendHeadersAndReturnCheckoutUrl()
    {
        string? apiKey = null;
        string? apiSecret = null;
        string? body = null;

        var handler = new FakeHttpMessageHandler(request =>
        {
            apiKey = request.Headers.GetValues("X-API-Key").First();
            apiSecret = request.Headers.GetValues("X-API-Secret").First();
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"success":true,"data":{"id":"GP-123","checkout_url":"https://checkout.geniuspay.ci/abc"}}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var service = new GeniusPayPaymentService(new HttpClient(handler), Options(), NullLogger<GeniusPayPaymentService>.Instance);

        var result = await service.RequestPaymentAsync(Guid.NewGuid(), "Découverte", 2500m, "tx-1");

        Assert.True(result.Success);
        Assert.Equal("GP-123", result.TransactionReference);
        Assert.Equal("https://checkout.geniuspay.ci/abc", result.PaymentLink);
        Assert.Equal("pk_test", apiKey);
        Assert.Equal("sk_test", apiSecret);
        Assert.Contains("wazap_transaction_id", body!);
        Assert.Contains("tx-1", body!);
        Assert.EndsWith("/payments", handler.LastRequestUri);
    }

    [Fact]
    public async Task RequestPaymentAsync_OnApiError_ShouldReturnFailure()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var service = new GeniusPayPaymentService(new HttpClient(handler), Options(), NullLogger<GeniusPayPaymentService>.Instance);

        var result = await service.RequestPaymentAsync(Guid.NewGuid(), "Petit", 5000m, "tx-2");

        Assert.False(result.Success);
        Assert.Null(result.PaymentLink);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task CheckPaymentStatusAsync_ShouldReturnStatusAndAmount()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"success":true,"data":{"id":"GP-123","status":"completed","amount":5000}}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var service = new GeniusPayPaymentService(new HttpClient(handler), Options(), NullLogger<GeniusPayPaymentService>.Instance);

        var status = await service.CheckPaymentStatusAsync("GP-123");

        Assert.NotNull(status);
        Assert.Equal("completed", status.Status);
        Assert.Equal(5000m, status.Amount);
        Assert.Contains("/payments/GP-123", handler.LastRequestUri);
    }

    [Fact]
    public async Task CheckPaymentStatusAsync_OnApiError_ShouldReturnNull()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = new GeniusPayPaymentService(new HttpClient(handler), Options(), NullLogger<GeniusPayPaymentService>.Instance);

        var status = await service.CheckPaymentStatusAsync("GP-404");

        Assert.Null(status);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public string LastRequestUri { get; private set; } = string.Empty;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString() ?? string.Empty;
            return Task.FromResult(_responder(request));
        }
    }
}
