using System.Text.Json;
using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

public class YCloudWebhookParserTests
{
    [Fact]
    public void IsYCloudPayload_InboundMessage_ReturnsTrue()
    {
        var json = """
        {
            "id": "evt_djeIQXaQPQyUcRFi",
            "type": "whatsapp.inbound_message.received",
            "apiVersion": "v2",
            "createTime": "2026-09-28T15:40:00Z",
            "whatsappInboundMessage": {
                "messaging_product": "whatsapp",
                "messages": [
                    {
                        "from": "2250544051972",
                        "id": "wamid.HBgLMzE2NTA1NTU5ODc2FQIAEh...",
                        "timestamp": "1646136000",
                        "type": "text",
                        "text": {
                            "body": "DISPO"
                        }
                    }
                ]
            }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        Assert.True(YCloudWebhookParser.IsYCloudPayload(doc.RootElement));
    }

    [Fact]
    public void IsYCloudPayload_MetaOrOtherPayload_ReturnsFalse()
    {
        var jsonMeta = """
        {
            "object": "whatsapp_business_account",
            "entry": []
        }
        """;
        using var doc = JsonDocument.Parse(jsonMeta);
        Assert.False(YCloudWebhookParser.IsYCloudPayload(doc.RootElement));
    }

    [Fact]
    public void ParseAll_TextMessageDISPO_ExtractsFromTextAndMessageId()
    {
        var json = """
        {
            "id": "evt_12345",
            "type": "whatsapp.inbound_message.received",
            "whatsappInboundMessage": {
                "messages": [
                    {
                        "from": "2250544051972",
                        "id": "wamid.TEST_DISPO_1",
                        "type": "text",
                        "text": {
                            "body": "DISPO"
                        }
                    }
                ]
            }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var events = YCloudWebhookParser.ParseAll(doc.RootElement);

        Assert.Single(events);
        var ev = events[0];
        Assert.Equal("+2250544051972", ev.From);
        Assert.Equal("DISPO", ev.Text);
        Assert.Equal("wamid.TEST_DISPO_1", ev.MessageId);
    }

    [Fact]
    public void ParseAll_InteractiveButtonReply_ExtractsButtonIdAndTitle()
    {
        var json = """
        {
            "id": "evt_interactive",
            "type": "whatsapp.inbound_message.received",
            "whatsappInboundMessage": {
                "messages": [
                    {
                        "from": "2250544051972",
                        "id": "wamid.BTN_DISPO_1",
                        "type": "interactive",
                        "interactive": {
                            "type": "button_reply",
                            "button_reply": {
                                "id": "DISPO",
                                "title": "🟢 DISPO"
                            }
                        }
                    }
                ]
            }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var events = YCloudWebhookParser.ParseAll(doc.RootElement);

        Assert.Single(events);
        var ev = events[0];
        Assert.Equal("+2250544051972", ev.From);
        Assert.Equal("DISPO", ev.ButtonId);
        Assert.Equal("🟢 DISPO", ev.ButtonTitle);
    }

    [Fact]
    public void ParseAll_ImageMessage_ExtractsMediaIdAndMimeType()
    {
        var json = """
        {
            "id": "evt_cni",
            "type": "whatsapp.inbound_message.received",
            "whatsappInboundMessage": {
                "messages": [
                    {
                        "from": "2250544051972",
                        "id": "wamid.IMG_1",
                        "type": "image",
                        "image": {
                            "id": "media_cni_12345",
                            "mime_type": "image/jpeg"
                        }
                    }
                ]
            }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var events = YCloudWebhookParser.ParseAll(doc.RootElement);

        Assert.Single(events);
        var ev = events[0];
        Assert.Equal("+2250544051972", ev.From);
        Assert.Equal("media_cni_12345", ev.MediaId);
        Assert.Equal("image/jpeg", ev.MimeType);
    }

    [Fact]
    public void ParseAll_StatusUpdated_ReturnsEmptyList()
    {
        var json = """
        {
            "id": "evt_status",
            "type": "whatsapp.message.updated",
            "whatsappMessage": {
                "status": "delivered"
            }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var events = YCloudWebhookParser.ParseAll(doc.RootElement);

        Assert.Empty(events);
    }

    [Fact]
    public async Task YCloud_InboundDISPO_TriggersRiderOnboarding()
    {
        var harness = new WebhookHarness(teamPhone: "+2250500000000");
        const string candidate = "+2250544051972";

        await harness.SendYCloudInboundAsync(candidate, "DISPO");

        var lead = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
            harness.Context.Leads, l => l.WhatsAppNumber == candidate);
        Assert.Equal("whatsapp-livreur", lead.Source);
        Assert.Equal(Wazap.Domain.Enums.LeadStatus.New, lead.Status);

        var reply = harness.LastMessageTo(candidate);
        Assert.NotNull(reply);
        Assert.Contains("Bienvenue chez WAZAP Livreur", reply);
        Assert.Contains("Cocody", reply);
    }
}
