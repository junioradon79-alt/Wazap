using Wazap.Application.Exceptions;
using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// WhatChimp répond HTTP 200 même quand l'envoi échoue : le verdict est dans le corps.
/// Sans cette vérification, un template refusé par Meta était compté comme envoyé.
/// </summary>
public class WhatChimpGatewayResponseTests
{
    [Fact]
    public void RefusedSend_Throws()
    {
        // Réponse réellement observée dans les logs de production.
        const string body = """
            {"status":"0","message":"Sending message outside 24 hour window is not allowed. You can only send template message to this user."}
            """;

        var ex = Assert.Throws<WhatsAppSendException>(
            () => WhatChimpService.EnsureGatewayAccepted(body, "template order_confirm vers +2250700000000"));

        Assert.Contains("24 hour window", ex.Message);
        Assert.Contains("order_confirm", ex.Message);
    }

    [Theory]
    // Refus qu'un nouvel essai ne peut pas lever.
    [InlineData("Sending message outside 24 hour window is not allowed.")]
    [InlineData("Template name does not exist in the translation")]
    [InlineData("number of parameters does not match")]
    public void PermanentRefusal_IsFlaggedPermanent(string message)
    {
        var body = $$"""{"status":"0","message":"{{message}}"}""";

        var ex = Assert.Throws<WhatsAppSendException>(() => WhatChimpService.EnsureGatewayAccepted(body, "ctx"));

        Assert.True(ex.IsPermanent);
    }

    [Fact]
    public void TransientRefusal_IsRetried()
    {
        var body = """{"status":"0","message":"Service temporarily unavailable"}""";

        var ex = Assert.Throws<WhatsAppSendException>(() => WhatChimpService.EnsureGatewayAccepted(body, "ctx"));

        Assert.False(ex.IsPermanent);
    }

    [Theory]
    [InlineData("""{"status":"1","message":"Message sent"}""")]
    [InlineData("""{"message":"ok"}""")]          // pas de champ status
    [InlineData("""{"status":true}""")]
    [InlineData("OK")]                             // corps non JSON
    [InlineData("[1,2,3]")]                        // JSON mais pas un objet
    [InlineData("")]
    [InlineData(null)]
    public void AnythingButAnExplicitFailure_IsAccepted(string? body)
    {
        // Prudence délibérée : on ne fait échouer que sur un « status » explicitement
        // négatif, pour ne jamais casser des envois qui fonctionnent.
        WhatChimpService.EnsureGatewayAccepted(body, "ctx");
    }
}
