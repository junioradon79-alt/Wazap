using System.Text.Json;
using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

public class WahaWebhookParserTests
{
    [Fact]
    public void IsWahaPayload_ValidPayload_ReturnsTrue()
    {
        var json = """
        {
            "event": "message",
            "session": "default",
            "payload": {
                "id": "msg-123",
                "from": "2250700000000@c.us",
                "body": "ACCEPTE 1234"
            }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        Assert.True(WahaWebhookParser.IsWahaPayload(doc.RootElement));
    }

    [Fact]
    public void IsWahaPayload_MetaOrOtherPayload_ReturnsFalse()
    {
        var jsonMeta = """
        {
            "object": "whatsapp_business_account",
            "entry": []
        }
        """;
        using var doc = JsonDocument.Parse(jsonMeta);
        Assert.False(WahaWebhookParser.IsWahaPayload(doc.RootElement));
    }

    [Fact]
    public void ParseAll_StandardMessage_ExtractsFromTextAndMessageId()
    {
        var json = """
        {
            "event": "message",
            "session": "default",
            "payload": {
                "id": "false_2250701020304@c.us_ABC",
                "from": "2250701020304@c.us",
                "fromMe": false,
                "body": "ACCEPTE OFF-99"
            }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var events = WahaWebhookParser.ParseAll(doc.RootElement);

        Assert.Single(events);
        var ev = events[0];
        Assert.Equal("+2250701020304", ev.From);
        Assert.Equal("ACCEPTE OFF-99", ev.Text);
        Assert.Equal("false_2250701020304@c.us_ABC", ev.MessageId);
    }

    [Fact]
    public void ParseAll_FromMe_IsIgnored()
    {
        var json = """
        {
            "event": "message",
            "session": "default",
            "payload": {
                "id": "true_2250701020304@c.us_XYZ",
                "from": "2250701020304@c.us",
                "fromMe": true,
                "body": "Ceci est notre propre message sortant"
            }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var events = WahaWebhookParser.ParseAll(doc.RootElement);

        Assert.Empty(events);
    }

    [Fact]
    public void ParseAll_GroupMessage_IsIgnored()
    {
        var json = """
        {
            "event": "message",
            "session": "default",
            "payload": {
                "id": "false_123456@g.us_XYZ",
                "from": "123456@g.us",
                "fromMe": false,
                "body": "Message dans un groupe"
            }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var events = WahaWebhookParser.ParseAll(doc.RootElement);

        Assert.Empty(events);
    }

    [Fact]
    public void ParseAll_LocationMessage_ExtractsCoordinates()
    {
        var json = """
        {
            "event": "message",
            "session": "default",
            "payload": {
                "id": "loc-1",
                "from": "22501020304@c.us",
                "fromMe": false,
                "location": {
                    "latitude": 5.348,
                    "longitude": -4.032
                }
            }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var events = WahaWebhookParser.ParseAll(doc.RootElement);

        Assert.Single(events);
        var ev = events[0];
        Assert.Equal("+22501020304", ev.From);
        Assert.Equal(5.348, ev.Latitude);
        Assert.Equal(-4.032, ev.Longitude);
    }

    [Fact]
    public void ParseAll_MediaMessage_ExtractsMediaUrlAndMimeType()
    {
        var json = """
        {
            "event": "message",
            "session": "default",
            "payload": {
                "id": "media-msg-1",
                "from": "22505060708@c.us",
                "fromMe": false,
                "hasMedia": true,
                "media": {
                    "url": "http://localhost:3000/api/files/default/cni.jpg",
                    "mimetype": "image/jpeg"
                }
            }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var events = WahaWebhookParser.ParseAll(doc.RootElement);

        Assert.Single(events);
        var ev = events[0];
        Assert.Equal("+22505060708", ev.From);
        Assert.Equal("http://localhost:3000/api/files/default/cni.jpg", ev.MediaUrl);
        Assert.Equal("image/jpeg", ev.MimeType);
    }
}
