using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.Application.Configuration;
using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// L'indice envoyé à WhatChimp (<c>variable1</c>, <c>variable2</c>…) doit venir de la CLÉ
/// du dictionnaire, jamais de son ordre d'énumération — que .NET ne garantit pas. Une
/// inversion enverrait par exemple le nom du client à la place du code de commande.
/// </summary>
public class WhatChimpVariableOrderTests
{
    private static (WhatChimpService Service, CapturingHandler Handler) CreateService()
    {
        var handler = new CapturingHandler();
        var client = new HttpClient(handler);
        var config = new ConfigStub(
            ("WhatChimp:ApiToken", "token-de-test"),
            ("WhatChimp:PhoneNumberId", "123456"),
            ("WhatChimp:BaseUrl", "https://gateway.test/"));

        return (new WhatChimpService(client, config, NullLogger<WhatChimpService>.Instance,
            new IvoryCoastNumberingOptions()), handler);
    }

    [Fact]
    public async Task Variables_AreIndexedByKey_NotByEnumerationOrder()
    {
        var (service, handler) = CreateService();

        // Clés volontairement insérées à l'envers : seul le numéro de la clé doit compter.
        var variables = new Dictionary<string, string>
        {
            ["3"] = "Awa Kone",
            ["1"] = "Ibrahim Traore",
            ["2"] = "A1B2C3D4"
        };

        await service.SendTemplateAsync("+2250700000000", "rider_assigned_vendor", variables);

        // Uri.ToString() renormalise les %20 en espaces : on compare sur la forme décodée.
        Assert.Contains("variable1=Ibrahim Traore", handler.LastUrl);
        Assert.Contains("variable2=A1B2C3D4", handler.LastUrl);
        Assert.Contains("variable3=Awa Kone", handler.LastUrl);
    }

    [Fact]
    public async Task Variables_AreSentInAscendingOrder()
    {
        var (service, handler) = CreateService();

        var variables = new Dictionary<string, string>
        {
            ["2"] = "deuxieme",
            ["1"] = "premier"
        };

        await service.SendTemplateAsync("+2250700000000", "un_template", variables);

        Assert.True(handler.LastUrl.IndexOf("variable1=", StringComparison.Ordinal)
                    < handler.LastUrl.IndexOf("variable2=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TemplateSend_UsesTemplateNameAndLanguageCode_WithoutMessageType()
    {
        var (service, handler) = CreateService();

        var variables = new Dictionary<string, string> { ["1"] = "Chez Thalia" };

        await service.SendTemplateAsync("+2250700000000", "prospect_approach_v2", variables);

        Assert.Contains("template_name=prospect_approach_v2", handler.LastUrl);
        Assert.Contains("language_code=fr", handler.LastUrl);
        Assert.DoesNotContain("message_type", handler.LastUrl);
        Assert.Contains("variable1=", handler.LastUrl);
    }

    [Fact]
    public async Task TextSend_DoesNotUseMessageType()
    {
        var (service, handler) = CreateService();

        await service.SendTextMessageAsync("+2250700000000", "Bonjour");

        Assert.Contains("message=Bonjour", handler.LastUrl);
        Assert.DoesNotContain("message_type", handler.LastUrl);
    }

    [Fact]
    public async Task NonNumericKey_IsRejected()
    {
        var (service, _) = CreateService();

        var variables = new Dictionary<string, string> { ["nom"] = "Awa" };

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendTemplateAsync("+2250700000000", "un_template", variables));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string LastUrl { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUrl = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":"1","message":"Message sent"}""")
            });
        }
    }
}
