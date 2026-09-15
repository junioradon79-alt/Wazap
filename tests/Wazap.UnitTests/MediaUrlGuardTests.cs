using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Garde-fou des URL de médias issues des webhooks.
/// <para>
/// L'URL d'un média arrive dans le corps d'un webhook, donc d'un tiers : le serveur la
/// téléchargeait en y joignant son jeton d'envoi WhatsApp (Bearer Meta, query WhatChimp).
/// Une URL forgée vers l'hôte de l'attaquant livrait donc le jeton, et une URL vers
/// <c>169.254.169.254</c> ou <c>localhost</c> transformait le serveur en sonde du réseau interne.
/// </para>
/// </summary>
public class MediaUrlGuardTests
{
    [Theory]
    [InlineData("https://lookaside.fbsbx.com/whatsapp_business/attachments/?mid=123")]
    [InlineData("https://graph.facebook.com/v25.0/123456789")]
    [InlineData("https://scontent.xx.fbcdn.net/v/t1.0-9/photo.jpg")]
    public void UrlMetaOfficielle_EstAcceptee(string url)
        => Assert.True(MediaUrlGuard.IsTrustedMetaMediaUrl(url));

    [Theory]
    [InlineData("https://169.254.169.254/latest/meta-data/")]      // métadonnées cloud
    [InlineData("https://localhost:5000/health/details")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://10.0.0.5/")]
    [InlineData("https://192.168.1.10/")]
    [InlineData("https://172.16.4.4/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://attaquant.example.com/vol-du-jeton")]
    [InlineData("http://lookaside.fbsbx.com/insecure")]             // HTTP : le jeton partirait en clair
    [InlineData("file:///etc/passwd")]
    [InlineData("pas-une-url")]
    [InlineData("")]
    [InlineData(null)]
    public void UrlNonFiable_EstRefusee(string? url)
        => Assert.False(MediaUrlGuard.IsTrustedMetaMediaUrl(url));

    [Fact]
    public void SousDomaineUsurpateur_EstRefuse()
    {
        // « facebook.com.attaquant.tld » contient bien « facebook.com » mais n'en est pas un sous-domaine.
        Assert.False(MediaUrlGuard.IsTrustedMetaMediaUrl("https://facebook.com.attaquant.tld/x"));
        Assert.False(MediaUrlGuard.IsTrustedMetaMediaUrl("https://notfacebook.com/x"));
    }

    [Theory]
    [InlineData("https://app.whatchimp.com/api/v1/whatsapp/media/1")]
    [InlineData("https://whatchimp.com/x")]
    public void UrlWhatChimp_EstReconnue(string url)
        => Assert.True(MediaUrlGuard.IsTrustedWhatChimpUrl(url));

    [Theory]
    [InlineData("https://attaquant.example.com/media")]
    [InlineData("https://s3.amazonaws.com/bucket/media.jpg")]
    public void UrlTierce_NEstPasWhatChimp(string url)
        => Assert.False(MediaUrlGuard.IsTrustedWhatChimpUrl(url));

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("192.168.0.1", true)]
    [InlineData("fc00::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("whatchimp.com", false)]
    public void HotePriveOuLocal_EstDetecte(string host, bool expected)
        => Assert.Equal(expected, MediaUrlGuard.IsPrivateOrLoopbackHost(host));
}
