using System.Text.Json;
using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Vérifie l'extraction des événements entrants d'un payload Meta WhatsApp Cloud
/// (<c>entry[0].changes[0].value.messages[0]</c>) : texte, boutons, localisation et médias,
/// avec normalisation du <c>from</c> en E.164 avec « + ».
/// </summary>
public class MetaWebhookParserTests
{
    private static MetaWebhookEvent? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return MetaWebhookParser.TryParse(document.RootElement);
    }

    private static string Envelope(string messageJson)
    {
        const string payload = """
            {
              "object": "whatsapp_business_account",
              "entry": [
                {
                  "id": "600239053135985",
                  "changes": [
                    {
                      "field": "messages",
                      "value": {
                        "messaging_product": "whatsapp",
                        "metadata": {
                          "display_phone_number": "22575803801",
                          "phone_number_id": "735886129615120"
                        },
                        "contacts": [ { "profile": { "name": "Wazap" }, "wa_id": "2250747639363" } ],
                        "messages": [ @@MESSAGE@@ ]
                      }
                    }
                  ]
                }
              ]
            }
            """;
        return payload.Replace("@@MESSAGE@@", messageJson);
    }

    [Fact]
    public void NonMetaPayload_ReturnsNull()
    {
        var evt = Parse("""{"data":{"subscriber":{"phoneNumber":"+2250747639363"},"message":{"text":"Bonjour"}}}""");
        Assert.Null(evt);
    }

    [Fact]
    public void TextMessage_ExtractsFromAndBody()
    {
        var evt = Parse(Envelope("""
            {
              "from": "2250747639363",
              "id": "wamid.HBgLTjI1",
              "timestamp": "1690000000",
              "type": "text",
              "text": { "body": "je veux livrer" }
            }
            """));

        Assert.NotNull(evt);
        Assert.Equal("+2250747639363", evt!.From);
        Assert.Equal("je veux livrer", evt.Text);
        Assert.Null(evt.MediaId);
        Assert.Null(evt.Latitude);
    }

    [Fact]
    public void ButtonMessage_ExtractsPayloadAndTitle()
    {
        var evt = Parse(Envelope("""
            {
              "from": "2250747639363",
              "id": "wamid.btn",
              "timestamp": "1690000000",
              "type": "button",
              "button": { "text": "ACCEPT", "payload": "bc10e4e3" }
            }
            """));

        Assert.NotNull(evt);
        Assert.Equal("ACCEPT", evt!.ButtonTitle);
        Assert.Equal("bc10e4e3", evt.ButtonId);
    }

    [Fact]
    public void InteractiveButtonReply_ExtractsReply()
    {
        var evt = Parse(Envelope("""
            {
              "from": "2250747639363",
              "id": "wamid.it",
              "timestamp": "1690000000",
              "type": "interactive",
              "interactive": {
                "type": "button_reply",
                "button_reply": { "id": "CONFIRM_123", "title": "Confirmer" }
              }
            }
            """));

        Assert.NotNull(evt);
        Assert.Equal("CONFIRM_123", evt!.ButtonId);
        Assert.Equal("Confirmer", evt.ButtonTitle);
    }

    [Fact]
    public void ImageMessage_ExtractsMediaIdAndMimeType()
    {
        var evt = Parse(Envelope("""
            {
              "from": "2250747639363",
              "id": "wamid.img",
              "timestamp": "1690000000",
              "type": "image",
              "image": {
                "caption": "CNI",
                "mime_type": "image/jpeg",
                "sha256": "abc",
                "id": "1234567890"
              }
            }
            """));

        Assert.NotNull(evt);
        Assert.Equal("1234567890", evt!.MediaId);
        Assert.Equal("image/jpeg", evt.MimeType);
        Assert.Null(evt.MediaUrl);
    }

    [Fact]
    public void LocationMessage_ExtractsCoordinates()
    {
        var evt = Parse(Envelope("""
            {
              "from": "2250747639363",
              "id": "wamid.loc",
              "timestamp": "1690000000",
              "type": "location",
              "location": { "latitude": 5.32, "longitude": -4.02 }
            }
            """));

        Assert.NotNull(evt);
        Assert.Equal(5.32, evt!.Latitude);
        Assert.Equal(-4.02, evt.Longitude);
    }

    [Fact]
    public void StatusesOnlyPayload_ReturnsNullEvent()
    {
        // Les notifications de statut (delivered/read) n'ont pas de « messages » :
        // aucun événement à traiter (le contrôleur répondra 200 et ignorera).
        var payload = """
            {
              "object": "whatsapp_business_account",
              "entry": [
                {
                  "id": "600239053135985",
                  "changes": [
                    {
                      "field": "messages",
                      "value": {
                        "messaging_product": "whatsapp",
                        "statuses": [
                          {
                            "id": "wamid.dlr",
                            "status": "delivered",
                            "timestamp": "1690000000"
                          }
                        ]
                      }
                    }
                  ]
                }
              ]
            }
            """;

        var evt = Parse(payload);
        Assert.Null(evt);
    }
}