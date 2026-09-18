using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Exceptions;
using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Couverture du fournisseur Meta WhatsApp Cloud : format du payload Graph (variables
/// ordonnées par indice, numéro E.164 sans « + ») et traduction des refus Meta en
/// <see cref="WhatsAppSendException"/> (codes permanents → outbox sans nouvelle tentative).
/// </summary>
public class MetaCloudApiWhatsAppSenderTests
{
    private static MetaApiOptions Options() => new()
    {
        Enabled = true,
        ApiToken = "jeton-permanent",
        PhoneNumberId = "735886129615120",
        ApiVersion = "v21.0",
        GraphUrl = "https://graph.facebook.com/",
        LanguageCode = "fr"
    };

    private static MetaCloudApiWhatsAppSender CreateSender(FakeHttpMessageHandler handler)
        => new(new HttpClient(handler), Options(), new IvoryCoastNumberingOptions(),
            NullLogger<MetaCloudApiWhatsAppSender>.Instance);

    [Fact]
    public async Task SendTemplateAsync_PostsGraphEndpoint_WithBearerAndOrderedVariables()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"messages":[{"id":"wamid.test"}]}""", Encoding.UTF8, "application/json")
            };
        });

        var service = CreateSender(handler);
        var variables = new Dictionary<string, string>
        {
            ["3"] = "lien",      // ordre volontairement inversé : seul l'indice compte.
            ["1"] = "Chez Thalia",
            ["2"] = "L'équipe WAZAP"
        };

        await service.SendTemplateAsync("+2250700000000", "prospect_approach_v2", variables);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("https://graph.facebook.com/v21.0/735886129615120/messages", captured.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer jeton-permanent", captured.Headers.Authorization!.ToString());

        var body = JsonDocument.Parse(await captured.Content!.ReadAsStringAsync());
        Assert.Equal("whatsapp", body.RootElement.GetProperty("messaging_product").GetString());
        Assert.Equal("2250700000000", body.RootElement.GetProperty("to").GetString()); // E.164 sans « + »
        Assert.Equal("template", body.RootElement.GetProperty("type").GetString());
        Assert.Equal("prospect_approach_v2", body.RootElement.GetProperty("template").GetProperty("name").GetString());

        var parameters = body.RootElement.GetProperty("template").GetProperty("components")[0].GetProperty("parameters");
        Assert.Equal(3, parameters.GetArrayLength());
        Assert.Equal("Chez Thalia", parameters[0].GetProperty("text").GetString());
        Assert.Equal("L'équipe WAZAP", parameters[1].GetProperty("text").GetString());
        Assert.Equal("lien", parameters[2].GetProperty("text").GetString());
    }

    [Fact]
    public async Task SendTextMessageAsync_UsesTextBody_WithoutPlusInRecipient()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"messages":[{"id":"wamid.text"}]}""", Encoding.UTF8, "application/json")
            };
        });

        var service = CreateSender(handler);
        await service.SendTextMessageAsync("+2250747000000", "Bonjour");

        var body = JsonDocument.Parse(await captured!.Content!.ReadAsStringAsync());
        Assert.Equal("text", body.RootElement.GetProperty("type").GetString());
        Assert.Equal("2250747000000", body.RootElement.GetProperty("to").GetString());
        Assert.Equal("Bonjour", body.RootElement.GetProperty("text").GetProperty("body").GetString());
        Assert.False(body.RootElement.GetProperty("text").GetProperty("preview_url").GetBoolean());
    }
[Fact]
    public async Task MetaRefusal_ThrowsWhatsAppSendException_AsPermanentWhenLocked()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"error":{"message":"Business account has been locked.","type":"OAuthException","code":131031}}""",
                Encoding.UTF8,
                "application/json")
        });

        var service = CreateSender(handler);

        var ex = await Assert.ThrowsAsync<WhatsAppSendException>(() =>
            service.SendTemplateAsync("+2250700000000", "prospect_approach_v2", new Dictionary<string, string> { ["1"] = "x" }));

        Assert.True(ex.IsPermanent);
        Assert.Contains("131031", ex.Message);
        Assert.Contains("Business account has been locked", ex.Message);
    }

    [Fact]
    public void ExtractMetaMessageId_ExtractsWamidCorrectly()
    {
        var json = """{"messaging_product":"whatsapp","contacts":[{"input":"2250700000000","wa_id":"2250700000000"}],"messages":[{"id":"wamid.HBgLMjI1MDcwMDAwMDAwFQIAEhggQ0RDRkRCMkY1MTQ0OTZERTFFOTI5REQyOUVDQzg2OTcA"}]}""";
        var id = MetaCloudApiWhatsAppSender.ExtractMetaMessageId(json);
        Assert.Equal("wamid.HBgLMjI1MDcwMDAwMDAwFQIAEhggQ0RDRkRCMkY1MTQ0OTZERTFFOTI5REQyOUVDQzg2OTcA", id);
    }

    [Fact]
    public async Task SendTemplateAsync_WhenMessageLogServiceConfigured_LogsOutboundSuccess()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"messages":[{"id":"wamid.12345"}]}""", Encoding.UTF8, "application/json")
        });

        var dbName = Guid.NewGuid().ToString();
        using var context = TestInfra.NewContext(dbName);
        var scopeFactory = new ServiceScopeFactoryStub(context);
        var logService = new WhatsAppMessageLogService(scopeFactory, NullLogger<WhatsAppMessageLogService>.Instance);

        var sender = new MetaCloudApiWhatsAppSender(
            new HttpClient(handler),
            Options(),
            new IvoryCoastNumberingOptions(),
            NullLogger<MetaCloudApiWhatsAppSender>.Instance,
            logService);

        await sender.SendTemplateAsync("+2250700000000", "order_confirm", new Dictionary<string, string> { ["1"] = "1234" });

        var log = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleOrDefaultAsync(context.WhatsAppMessageLogs);
        Assert.NotNull(log);
        Assert.Equal("order_confirm", log.TemplateName);
        Assert.Equal("Sent", log.Status);
        Assert.Equal("wamid.12345", log.ProviderMessageId);
        Assert.Equal(2.27m, log.EstimatedCostFcfa);
    }

    /// <summary>Handler HTTP minimaliste propre à ce fichier de tests.</summary>
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