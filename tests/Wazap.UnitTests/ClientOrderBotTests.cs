using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Services;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Wazap.Infrastructure.Data;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Bot WhatsApp de COMMANDE CLIENT : un numéro inconnu qui veut commander est guidé
/// (article → commerce → adresse), puis une commande réelle est créée au compte du vendeur.
/// Si le commerce possède un catalogue, le client compose son panier par numéro (lignes de
/// commande + montant calculé) ; sinon le mode texte libre d'origine s'applique.
/// </summary>
public class ClientOrderBotTests
{
    private const string ClientPhone = "+2250700000001";
    private const string VendorPhone = "+2250700000002";

    [Fact]
    public async Task NonIntent_IsNotConsumed_WithoutDraft()
    {
        using var context = TestInfra.NewContext("bot-non-intent");
        var sender = new RecordingWhatsAppSender();
        var bot = NewBot(context, sender);

        var consumed = await bot.TryHandleAsync(ClientPhone, "bonjour, vous livrez dans quel quartier ?");

        Assert.False(consumed);
        Assert.Empty(await context.ClientOrderDrafts.ToListAsync());
        Assert.Empty(sender.TextMessages);
    }

    [Fact]
    public async Task PartnerIntent_LeavesHandToProspectBot()
    {
        using var context = TestInfra.NewContext("bot-partner");
        var sender = new RecordingWhatsAppSender();
        var bot = NewBot(context, sender);

        // « activer » + « mon commerce » = intention de PARTENARIAT : le bot prospects garde la main.
        var consumed = await bot.TryHandleAsync(ClientPhone,
            "je veux activer mon commerce, mes clients commandent déjà par WhatsApp");

        Assert.False(consumed);
        Assert.Empty(await context.ClientOrderDrafts.ToListAsync());
    }

    [Fact]
    public async Task OrderIntent_StartsConversation()
    {
        using var context = TestInfra.NewContext("bot-start");
        var sender = new RecordingWhatsAppSender();
        var bot = NewBot(context, sender);

        var consumed = await bot.TryHandleAsync(ClientPhone, "bonjour je veux commander 2 poulets");

        Assert.True(consumed);
        var draft = await context.ClientOrderDrafts.SingleAsync();
        Assert.Equal(ClientOrderDraftStage.AwaitingItems, draft.Stage);
        Assert.Equal(ClientPhone, draft.ClientWhatsAppNumber);
        Assert.Contains("Étape 1/3", LastMessage(sender, ClientPhone));
    }

    [Fact]
    public async Task Disabled_IsNotConsumed()
    {
        using var context = TestInfra.NewContext("bot-disabled");
        var sender = new RecordingWhatsAppSender();
        var bot = new ClientOrderBotService(context, sender,
            new ConfigStub(("ClientOrderBot:Enabled", "false")),
            NullLogger<ClientOrderBotService>.Instance);

        Assert.False(await bot.TryHandleAsync(ClientPhone, "je veux commander"));
        Assert.Empty(await context.ClientOrderDrafts.ToListAsync());
    }

    [Fact]
    public async Task Cancel_EndsConversation()
    {
        using var context = TestInfra.NewContext("bot-cancel");
        var sender = new RecordingWhatsAppSender();
        var bot = NewBot(context, sender);

        await bot.TryHandleAsync(ClientPhone, "commande");
        var consumed = await bot.TryHandleAsync(ClientPhone, "ANNULER");

        Assert.True(consumed);
        var draft = await context.ClientOrderDrafts.SingleAsync();
        Assert.Equal(ClientOrderDraftStage.Cancelled, draft.Stage);
        Assert.Contains("annulée", LastMessage(sender, ClientPhone));

        // Conversation terminée : un message sans intention n'est plus consommé.
        Assert.False(await bot.TryHandleAsync(ClientPhone, "ok merci"));
    }

    [Fact]
    public async Task TooManyInvalidAttempts_AbandonsConversation()
    {
        using var context = TestInfra.NewContext("bot-attempts");
        var sender = new RecordingWhatsAppSender();
        var bot = NewBot(context, sender);

        await bot.TryHandleAsync(ClientPhone, "commande");
        await bot.TryHandleAsync(ClientPhone, "x");
        await bot.TryHandleAsync(ClientPhone, "y");
        await bot.TryHandleAsync(ClientPhone, "z");

        var draft = await context.ClientOrderDrafts.SingleAsync();
        Assert.Equal(ClientOrderDraftStage.Cancelled, draft.Stage);
        Assert.Contains("abandonnée", LastMessage(sender, ClientPhone));
    }

    [Fact]
    public async Task TextMode_FullFlow_CreatesOrderAtVendor()
    {
        using var context = TestInfra.NewContext("bot-text-flow");
        var sender = new RecordingWhatsAppSender();
        var bot = NewBot(context, sender);
        var vendor = new User("Chez Thalia", "hash", UserRole.Vendor, VendorPhone);
        context.Users.Add(vendor);
        await context.SaveChangesAsync();

        await bot.TryHandleAsync(ClientPhone, "je commande un poulet");
        await bot.TryHandleAsync(ClientPhone, "1 poulet braisé + alloco");

        var draft = await context.ClientOrderDrafts.SingleAsync();
        Assert.Equal(ClientOrderDraftStage.AwaitingVendor, draft.Stage);
        Assert.Equal("1 poulet braisé + alloco", draft.Description);
        Assert.Contains("Étape 2/3", LastMessage(sender, ClientPhone));

        // Commerce sans catalogue : on passe directement à l'adresse.
        await bot.TryHandleAsync(ClientPhone, "chez thalia");
        Assert.Equal(ClientOrderDraftStage.AwaitingAddress, draft.Stage);
        Assert.Contains("où livrer", LastMessage(sender, ClientPhone));

        await bot.TryHandleAsync(ClientPhone, "Marcory, rue Princesse");

        var order = await context.Orders.SingleAsync();
        Assert.Equal(vendor.Id, order.VendorUserId);
        Assert.Equal(ClientPhone, order.ClientWhatsAppNumber);
        Assert.Equal(VendorPhone, order.VendorWhatsAppNumber);
        Assert.Equal(OrderStatus.PendingVendorConfirmation, order.Status);
        Assert.Equal(0m, order.Amount);
        Assert.Contains("1 poulet braisé + alloco", order.Description);
        Assert.Contains("Marcory, rue Princesse", order.Description);

        Assert.Equal(ClientOrderDraftStage.Completed, draft.Stage);
        Assert.Equal(order.Id, draft.OrderId);
        Assert.Equal("Marcory, rue Princesse", draft.Address);

        Assert.Contains(sender.TextMessages,
            m => m.Phone == VendorPhone && m.Message.Contains("Nouvelle commande client"));
        Assert.Contains("Commande #", LastMessage(sender, ClientPhone));
    }

    [Fact]
    public async Task CatalogMode_MenuThenSelection_CreatesOrderWithLines()
    {
        using var context = TestInfra.NewContext("bot-catalog");
        var sender = new RecordingWhatsAppSender();
        var bot = NewBot(context, sender);
        var vendor = new User("ChezThalia", "hash", UserRole.Vendor, VendorPhone);
        context.Users.Add(vendor);
        context.VendorProducts.Add(new VendorProduct(vendor.Id, "Poulet braisé", "portion 1", 2500m, "🍗"));
        context.VendorProducts.Add(new VendorProduct(vendor.Id, "Attiéké", "portion 1", 500m));
        await context.SaveChangesAsync();

        await bot.TryHandleAsync(ClientPhone, "commande");
        await bot.TryHandleAsync(ClientPhone, "poulet et attiéké");
        await bot.TryHandleAsync(ClientPhone, "ChezThalia");

        var draft = await context.ClientOrderDrafts.SingleAsync();
        Assert.Equal(ClientOrderDraftStage.AwaitingProductChoice, draft.Stage);
        Assert.Equal(2, draft.GetProductCatalog().Count);
        var menu = LastMessage(sender, ClientPhone);
        Assert.Contains("Poulet braisé", menu);
        Assert.Contains("Attiéké", menu);
        Assert.Contains("FCFA", menu);

        await bot.TryHandleAsync(ClientPhone, "1 2");
        Assert.Equal(ClientOrderDraftStage.AwaitingAddress, draft.Stage);
        Assert.Contains("où livrer", LastMessage(sender, ClientPhone));

        await bot.TryHandleAsync(ClientPhone, "Cocody Angré");

        var order = await context.Orders.SingleAsync();
        var lines = await context.OrderLines.Where(l => l.OrderId == order.Id).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.Equal(2, order.OrderLines.Count);
        Assert.Equal(3000m, order.Amount); // 2 500 + 500, quantité 1
        Assert.Contains(lines, l => l.ProductName == "Poulet braisé" && l.Quantity == 1 && l.UnitPrice == 2500m);
        Assert.Contains(lines, l => l.ProductName == "Attiéké" && l.UnitPrice == 500m);
        Assert.Contains("Livraison : Cocody Angré", order.Description);
        Assert.Equal(ClientOrderDraftStage.Completed, draft.Stage);
    }

    [Fact]
    public async Task MultipleVendors_ClientChoosesByNumber()
    {
        using var context = TestInfra.NewContext("bot-multi-vendors");
        var sender = new RecordingWhatsAppSender();
        var bot = NewBot(context, sender);
        context.Users.Add(new User("Chez Alpha", "hash", UserRole.Vendor, "+2250700000010"));
        context.Users.Add(new User("Chez Alpha Bis", "hash", UserRole.Vendor, "+2250700000011"));
        await context.SaveChangesAsync();

        await bot.TryHandleAsync(ClientPhone, "commande");
        await bot.TryHandleAsync(ClientPhone, "une pizza");
        await bot.TryHandleAsync(ClientPhone, "chez alpha");

        var draft = await context.ClientOrderDrafts.SingleAsync();
        Assert.Equal(ClientOrderDraftStage.AwaitingVendorChoice, draft.Stage);
        Assert.Equal(2, draft.GetVendorCandidates().Count);
        Assert.Contains("répondez avec le NUMÉRO", LastMessage(sender, ClientPhone));

        await bot.TryHandleAsync(ClientPhone, "2");
        Assert.Equal(ClientOrderDraftStage.AwaitingAddress, draft.Stage);
    }

    [Fact]
    public async Task VendorResolvedBySpacedPhoneNumber()
    {
        using var context = TestInfra.NewContext("bot-vendor-phone");
        var sender = new RecordingWhatsAppSender();
        var bot = NewBot(context, sender);
        context.Users.Add(new User("Boutique Awa", "hash", UserRole.Vendor, "+2250708091011"));
        await context.SaveChangesAsync();

        await bot.TryHandleAsync(ClientPhone, "commande");
        await bot.TryHandleAsync(ClientPhone, "1 attiéké poisson");
        await bot.TryHandleAsync(ClientPhone, "07 08 09 10 11");

        var draft = await context.ClientOrderDrafts.SingleAsync();
        Assert.Equal(ClientOrderDraftStage.AwaitingAddress, draft.Stage);
        Assert.Contains("Boutique Awa", LastMessage(sender, ClientPhone));
    }

    [Fact]
    public async Task UnknownVendor_ReinvitesAndKeepsConversationOpen()
    {
        using var context = TestInfra.NewContext("bot-unknown-vendor");
        var sender = new RecordingWhatsAppSender();
        var bot = NewBot(context, sender);

        await bot.TryHandleAsync(ClientPhone, "commande");
        await bot.TryHandleAsync(ClientPhone, "1 poulet");
        await bot.TryHandleAsync(ClientPhone, "Chez Personne Inconnue");

        var draft = await context.ClientOrderDrafts.SingleAsync();
        Assert.Equal(ClientOrderDraftStage.AwaitingVendor, draft.Stage);
        Assert.Equal(1, draft.StageAttempts);
        Assert.Contains("Aucun commerce", LastMessage(sender, ClientPhone));
    }

    private static ClientOrderBotService NewBot(ApplicationDbContext context, RecordingWhatsAppSender sender,
        params (string Key, string? Value)[] config)
        => new(context, sender, new ConfigStub(config), NullLogger<ClientOrderBotService>.Instance);

    private static string? LastMessage(RecordingWhatsAppSender sender, string phone)
        => sender.TextMessages.LastOrDefault(m => m.Phone == phone).Message;
}
