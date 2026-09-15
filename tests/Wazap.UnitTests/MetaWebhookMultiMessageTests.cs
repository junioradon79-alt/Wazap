using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wazap.Infrastructure.Services;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Webhooks entrants de l'API Meta WhatsApp Cloud : un payload peut porter PLUSIEURS messages
/// (<c>entry[] × changes[] × messages[]</c>) et la passerelle réessaie tout ce qui n'a pas
/// répondu 2xx.
/// <para>
/// Deux défauts corrigés ici : l'ancienne lecture ne traitait qu'UN SEUL message par payload
/// (le dernier), les autres étant perdus sans trace ni réessai ; et aucune déduplication
/// n'existait, si bien qu'un « LIVRAISON » rejoué créait une SECONDE commande et une seconde
/// vague d'offres aux mêmes livreurs (le vendeur payait deux crédits pour une seule course).
/// </para>
/// </summary>
public class MetaWebhookMultiMessageTests
{
    private static JsonElement Payload(params (string Id, string From, string Body)[] messages)
        => JsonSerializer.SerializeToElement(new
        {
            @object = "whatsapp_business_account",
            entry = new[]
            {
                new
                {
                    id = "1033291085991367",
                    changes = new[]
                    {
                        new
                        {
                            field = "messages",
                            value = new
                            {
                                messaging_product = "whatsapp",
                                messages = messages.Select(m => new
                                {
                                    id = m.Id,
                                    from = m.From,
                                    type = "text",
                                    text = new { body = m.Body }
                                }).ToArray()
                            }
                        }
                    }
                }
            }
        });

    // ---------------------------------------------------------------- Analyse du payload

    [Fact]
    public void ParseAll_LitTousLesMessages_DUnMemeChangement()
    {
        var payload = Payload(
            ("wamid.A", "2250700000301", "bonjour"),
            ("wamid.B", "2250700000302", "je veux livrer"));

        var events = MetaWebhookParser.ParseAll(payload);

        Assert.Equal(2, events.Count);
        Assert.Equal("+2250700000301", events[0].From);
        Assert.Equal("bonjour", events[0].Text);
        Assert.Equal("wamid.A", events[0].MessageId);
        Assert.Equal("+2250700000302", events[1].From);
        Assert.Equal("wamid.B", events[1].MessageId);
    }

    [Fact]
    public void ParseAll_LitToutesLesEntrees_EtTousLesChangements()
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            @object = "whatsapp_business_account",
            entry = new object[]
            {
                new
                {
                    id = "waba",
                    changes = new object[]
                    {
                        new { field = "messages", value = new { messages = new[] { new { id = "m1", from = "2250700000401", type = "text", text = new { body = "un" } } } } },
                        new { field = "messages", value = new { messages = new[] { new { id = "m2", from = "2250700000402", type = "text", text = new { body = "deux" } } } } }
                    }
                },
                new
                {
                    id = "waba2",
                    changes = new object[]
                    {
                        new { field = "messages", value = new { messages = new[] { new { id = "m3", from = "2250700000403", type = "text", text = new { body = "trois" } } } } }
                    }
                }
            }
        });

        var events = MetaWebhookParser.ParseAll(payload);

        Assert.Equal(3, events.Count);
        Assert.Equal(new[] { "m1", "m2", "m3" }, events.Select(e => e.MessageId).ToArray());
    }

    [Fact]
    public void ParseAll_PayloadSansMessage_RetourneUneListeVide()
    {
        // Accusé de réception / statut de livraison : rien à router, mais la requête reste
        // un succès (sinon la passerelle réessaierait indéfiniment).
        var payload = JsonSerializer.SerializeToElement(new
        {
            @object = "whatsapp_business_account",
            entry = new[] { new { id = "waba", changes = new[] { new { field = "messages", value = new { statuses = new[] { new { id = "x", status = "delivered" } } } } } } }
        });

        Assert.Empty(MetaWebhookParser.ParseAll(payload));
        Assert.Null(MetaWebhookParser.TryParse(payload));
    }

    [Fact]
    public void TryParse_ResteCompatible_RetourneLeDernierMessage()
    {
        var payload = Payload(
            ("wamid.A", "2250700000301", "premier"),
            ("wamid.B", "2250700000302", "dernier"));

        var parsed = MetaWebhookParser.TryParse(payload);

        Assert.NotNull(parsed);
        Assert.Equal("dernier", parsed!.Text);
        Assert.Equal("wamid.B", parsed.MessageId);
    }

    // ------------------------------------------------------- Routage et déduplication

    [Fact]
    public async Task PayloadMeta_AvecDeuxMessages_TraiteLesDeux()
    {
        using var harness = new WebhookHarness();

        await harness.Controller.Handle(Payload(
            ("wamid.A", "2250700000301", "bonjour"),
            ("wamid.B", "2250700000302", "bonjour")));

        // Un lead par numéro inconnu : la preuve que les DEUX messages ont été routés.
        Assert.Equal(2, await harness.Context.Leads.CountAsync());
    }

    [Fact]
    public async Task PayloadMeta_Rejoue_NeRetraitePasLesMessages()
    {
        using var harness = new WebhookHarness();
        var payload = Payload(
            ("wamid.A", "2250700000301", "bonjour"),
            ("wamid.B", "2250700000302", "bonjour"));

        await harness.Controller.Handle(payload);
        await harness.Controller.Handle(payload); // reprise de la passerelle (mêmes identifiants)

        Assert.Equal(2, await harness.Context.Leads.CountAsync());
        Assert.Equal(2, await harness.Context.ProcessedWebhookMessages.CountAsync());
    }
}
