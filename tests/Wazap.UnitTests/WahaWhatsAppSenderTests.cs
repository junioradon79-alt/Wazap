using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Exceptions;
using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

public class WahaWhatsAppSenderTests
{
    private static WahaOptions CreateOptions() => new()
    {
        Enabled = true,
        BaseUrl = "http://localhost:3000",
        ApiKey = "secret-waha-key",
        SessionName = "default"
    };

    private static WahaWhatsAppSender CreateSender(FakeHttpMessageHandler handler)
        => new(
            new HttpClient(handler),
            CreateOptions(),
            new IvoryCoastNumberingOptions(),
            NullLogger<WahaWhatsAppSender>.Instance);

    [Fact]
    public async Task SendTextMessageAsync_PostsSendTextEndpoint_WithApiKeyAndChatId()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"false_2250700000000@c.us_12345"}""", Encoding.UTF8, "application/json")
            };
        });

        var sender = CreateSender(handler);
        await sender.SendTextMessageAsync("+2250700000000", "Hello WAHA !");

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("http://localhost:3000/api/sendText", captured.RequestUri!.AbsoluteUri);
        Assert.True(captured.Headers.Contains("X-Api-Key"));
        Assert.Equal("secret-waha-key", captured.Headers.GetValues("X-Api-Key").First());

        var body = await captured.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("2250700000000@c.us", doc.RootElement.GetProperty("chatId").GetString());
        Assert.Equal("Hello WAHA !", doc.RootElement.GetProperty("text").GetString());
        Assert.Equal("default", doc.RootElement.GetProperty("session").GetString());
    }

    [Fact]
    public async Task SendTemplateAsync_FormatsTemplate_AndPostsToWaha()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"false_2250700000000@c.us_67890"}""", Encoding.UTF8, "application/json")
            };
        });

        var sender = CreateSender(handler);
        var variables = new Dictionary<string, string>
        {
            ["1"] = "Marcory",
            ["2"] = "Plateau",
            ["3"] = "1500",
            ["4"] = "OFF-42"
        };

        await sender.SendTemplateAsync("+2250700000000", "rider_offer_v2", variables);

        Assert.NotNull(captured);
        var body = await captured!.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("text").GetString();

        Assert.Contains("Marcory", text);
        Assert.Contains("Plateau", text);
        Assert.Contains("1500 F", text);
        Assert.Contains("ACCEPTE OFF-42", text);
    }

    [Fact]
    public async Task SendTextMessageAsync_HttpError_ThrowsWhatsAppSendException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":"Session closed"}""", Encoding.UTF8, "application/json")
        });

        var sender = CreateSender(handler);

        var ex = await Assert.ThrowsAsync<WhatsAppSendException>(() =>
            sender.SendTextMessageAsync("+2250700000000", "Message"));

        Assert.True(ex.IsPermanent);
        Assert.Contains("Session closed", ex.Message);
    }

    [Fact]
    public void ExtractWahaMessageId_ExtractsStringOrSerialized()
    {
        var jsonDirect = """{"id": "msg-123"}""";
        Assert.Equal("msg-123", WahaWhatsAppSender.ExtractWahaMessageId(jsonDirect));

        var jsonObject = """{"id": {"_serialized": "msg-serialized-456"}}""";
        Assert.Equal("msg-serialized-456", WahaWhatsAppSender.ExtractWahaMessageId(jsonObject));

        var jsonInvalid = """{"other": 123}""";
        Assert.Null(WahaWhatsAppSender.ExtractWahaMessageId(jsonInvalid));
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
