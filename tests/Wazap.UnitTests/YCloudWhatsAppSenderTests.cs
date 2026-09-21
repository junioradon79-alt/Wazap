using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.Application.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Application.Dtos;
using Wazap.Application.Exceptions;
using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Couverture du connecteur officiel YCloud WhatsApp API :
/// validation de l'authentification (X-API-Key), format E.164 (+225...), ordonnancement des variables,
/// gestion des erreurs (permanentes vs réessayables) et audit log outbox.
/// </summary>
public class YCloudWhatsAppSenderTests
{
    private static YCloudOptions Options() => new()
    {
        Enabled = true,
        ApiKey = "ycloud_api_key_test_12345",
        PhoneNumber = "2250787119520",
        BaseUrl = "https://api.ycloud.com/v2/whatsapp/",
        LanguageCode = "fr"
    };

    private static YCloudWhatsAppSender CreateSender(
        FakeHttpMessageHandler handler,
        IWhatsAppMessageLogService? logService = null)
        => new(
            new HttpClient(handler),
            Options(),
            new IvoryCoastNumberingOptions(),
            NullLogger<YCloudWhatsAppSender>.Instance,
            logService);

    [Fact]
    public async Task SendTemplateAsync_PostsToSendDirectly_WithXApiKeyAndOrderedVariables()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"ycloud_msg_98765","status":"accepted"}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var logService = new FakeMessageLogService();
        var service = CreateSender(handler, logService);

        var variables = new Dictionary<string, string>
        {
            ["2"] = "4 500 FCFA",
            ["1"] = "Kouassi",
            ["3"] = "https://wazap.ci/track/123"
        };

        await service.SendTemplateAsync("+2250787119520", "order_notification", variables);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("https://api.ycloud.com/v2/whatsapp/messages/sendDirectly", captured.RequestUri!.AbsoluteUri);
        Assert.Equal("ycloud_api_key_test_12345", captured.Headers.GetValues("X-API-Key").FirstOrDefault());

        var json = JsonDocument.Parse(await captured.Content!.ReadAsStringAsync());
        var root = json.RootElement;

        Assert.Equal("+2250787119520", root.GetProperty("from").GetString());
        Assert.Equal("+2250787119520", root.GetProperty("to").GetString());
        Assert.Equal("template", root.GetProperty("type").GetString());

        var template = root.GetProperty("template");
        Assert.Equal("order_notification", template.GetProperty("name").GetString());
        Assert.Equal("fr", template.GetProperty("language").GetProperty("code").GetString());

        var parameters = template.GetProperty("components")[0].GetProperty("parameters");
        Assert.Equal(3, parameters.GetArrayLength());
        Assert.Equal("Kouassi", parameters[0].GetProperty("text").GetString());
        Assert.Equal("4 500 FCFA", parameters[1].GetProperty("text").GetString());
        Assert.Equal("https://wazap.ci/track/123", parameters[2].GetProperty("text").GetString());

        Assert.Single(logService.OutboundLogs);
        var logged = logService.OutboundLogs[0];
        Assert.Equal("+2250787119520", logged.To);
        Assert.Equal("order_notification", logged.TemplateName);
        Assert.Equal("YCloud", logged.Provider);
        Assert.True(logged.Success);
        Assert.Equal("ycloud_msg_98765", logged.ProviderMessageId);
    }

    [Fact]
    public async Task SendTextMessageAsync_PostsDirectlyWithBodyAndLogs()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"msg_text_001","status":"sent"}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var logService = new FakeMessageLogService();
        var service = CreateSender(handler, logService);

        await service.SendTextMessageAsync("+2250102030405", "Bonjour de WAZAP !");

        Assert.NotNull(captured);
        var json = JsonDocument.Parse(await captured!.Content!.ReadAsStringAsync());
        var root = json.RootElement;

        Assert.Equal("+2250102030405", root.GetProperty("to").GetString());
        Assert.Equal("text", root.GetProperty("type").GetString());
        Assert.Equal("Bonjour de WAZAP !", root.GetProperty("text").GetProperty("body").GetString());

        Assert.Single(logService.OutboundLogs);
        var logged = logService.OutboundLogs[0];
        Assert.Equal("+2250102030405", logged.To);
        Assert.Equal("Bonjour de WAZAP !", logged.MessageText);
        Assert.Equal("YCloud", logged.Provider);
        Assert.True(logged.Success);
        Assert.Equal("msg_text_001", logged.ProviderMessageId);
    }

    [Fact]
    public async Task YCloudError_IdentifiesPermanentFailure_AndLogsFailure()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"error":{"code":"PHONE_NUMBER_NOT_VALID","message":"Invalid recipient phone number"}}""",
                Encoding.UTF8,
                "application/json")
        });

        var logService = new FakeMessageLogService();
        var service = CreateSender(handler, logService);

        var ex = await Assert.ThrowsAsync<WhatsAppSendException>(() =>
            service.SendTextMessageAsync("+2250000000000", "Test erreur"));

        Assert.True(ex.IsPermanent);
        Assert.Contains("PHONE_NUMBER_NOT_VALID", ex.Message);

        Assert.Single(logService.OutboundLogs);
        var logged = logService.OutboundLogs[0];
        Assert.Equal("+2250000000000", logged.To);
        Assert.Equal("Test erreur", logged.MessageText);
        Assert.Equal("YCloud", logged.Provider);
        Assert.False(logged.Success);
    }

    [Fact]
    public async Task YCloudError_RateLimit_IsTransient()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage((HttpStatusCode)429)
        {
            Content = new StringContent(
                """{"error":{"code":"RATE_LIMIT_EXCEEDED","message":"Too many requests"}}""",
                Encoding.UTF8,
                "application/json")
        });

        var service = CreateSender(handler);

        var ex = await Assert.ThrowsAsync<WhatsAppSendException>(() =>
            service.SendTextMessageAsync("+2250787119520", "Trop vite"));

        Assert.False(ex.IsPermanent);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    private sealed class FakeMessageLogService : IWhatsAppMessageLogService
    {
        public record LogEntry(
            string To,
            string? TemplateName,
            string? MessageText,
            string Provider,
            bool Success,
            string? ProviderMessageId,
            int? ErrorCode,
            string? ErrorMessage);

        public List<LogEntry> OutboundLogs { get; } = new();

        public Task LogOutboundAsync(
            string toPhoneNumber,
            string? templateName,
            string? messageText,
            string provider,
            bool success,
            string? providerMessageId = null,
            int? errorCode = null,
            string? errorMessage = null,
            Guid? orderId = null,
            Guid? recipientUserId = null,
            CancellationToken ct = default)
        {
            OutboundLogs.Add(new LogEntry(
                toPhoneNumber,
                templateName,
                messageText,
                provider,
                success,
                providerMessageId,
                errorCode,
                errorMessage));
            return Task.CompletedTask;
        }

        public Task LogInboundAsync(
            string fromPhoneNumber,
            string? messageText,
            string messageType,
            string provider,
            string? providerMessageId = null,
            Guid? orderId = null,
            Guid? senderUserId = null,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<WhatsAppMessageLogDto>> GetRecentLogsAsync(
            int count = 100,
            Guid? orderId = null,
            string? recipientPhone = null,
            CancellationToken ct = default) => Task.FromResult<IReadOnlyList<WhatsAppMessageLogDto>>(Array.Empty<WhatsAppMessageLogDto>());

        public Task<WhatsAppCostSummaryDto> GetCostSummaryAsync(
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            CancellationToken ct = default) => Task.FromResult(new WhatsAppCostSummaryDto(0, 0, 0, 0, 0m, new(), new(), 0m));
    }
}
