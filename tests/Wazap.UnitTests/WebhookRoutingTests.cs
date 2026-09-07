using Microsoft.EntityFrameworkCore;
using Wazap.Application.Configuration;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Routage des commandes WhatsApp de bout en bout (webhook → services → base).
/// Le contrôleur fait 700 lignes et concentre tout le produit : c'est là que trois
/// défauts ont été trouvés le 07/09, sans qu'aucun test ne les couvre.
/// </summary>
public class WebhookRoutingTests
{
    private const string RiderPhone = "+2250700000001";
    private const string VendorPhone = "+2250700000002";
    private const string ClientPhone = "+2250700000003";

    private static User AddRider(WebhookHarness h, bool available = true)
    {
        var rider = new User("livreur", "hash", UserRole.Rider, RiderPhone);
        rider.SetAvailability(available);
        h.Context.Users.Add(rider);
        return rider;
    }

    private static User AddVendor(WebhookHarness h)
    {
        var vendor = new User("boutique", "hash", UserRole.Vendor, VendorPhone);
        h.Context.Users.Add(vendor);
        return vendor;
    }

    /// <summary>Course déjà livrée par ce livreur pour ce client (support des tests de note).</summary>
    private static async Task<Order> AddDeliveredOrderAsync(WebhookHarness h, User rider)
    {
        var order = new Order("Awa", ClientPhone, VendorPhone, "1 pagne", 5000m);
        order.ConfirmByVendor();
        order.AwaitRiderAcceptance();
        order.AssignRider(RiderPhone);
        order.LinkRider(rider.Id);
        order.MarkReadyForPickup();
        order.MarkPickedUp();
        order.MarkInTransit();
        order.MarkDelivered();

        h.Context.Orders.Add(order);
        await h.Context.SaveChangesAsync();
        return order;
    }

    // ---------------------------------------------------------------- Livreur : statuts

    [Fact]
    public async Task Recu_MovesAssignedOrderToInTransit()
    {
        using var h = new WebhookHarness();
        var rider = AddRider(h);

        var order = new Order("Awa", ClientPhone, VendorPhone, "1 pagne", 5000m);
        order.ConfirmByVendor();
        order.AwaitRiderAcceptance();
        order.AssignRider(RiderPhone);
        order.LinkRider(rider.Id);
        h.Context.Orders.Add(order);
        await h.Context.SaveChangesAsync();

        await h.SendAsync(RiderPhone, "RECU");

        Assert.Equal(OrderStatus.InTransit, order.Status);
    }

    [Fact]
    public async Task Zone_SetsRiderZone()
    {
        using var h = new WebhookHarness();
        var rider = AddRider(h);
        await h.Context.SaveChangesAsync();

        await h.SendAsync(RiderPhone, "ZONE Cocody");

        Assert.Equal("Cocody", rider.Zone);
    }

    [Fact]
    public async Task Indispo_TakesRiderOffline()
    {
        using var h = new WebhookHarness();
        var rider = AddRider(h, available: true);
        await h.Context.SaveChangesAsync();

        await h.SendAsync(RiderPhone, "INDISPO");

        Assert.False(rider.IsAvailable);
    }

    [Fact]
    public async Task Aide_ReturnsRoleSpecificMenu()
    {
        using var h = new WebhookHarness();
        AddRider(h);
        AddVendor(h);
        await h.Context.SaveChangesAsync();

        await h.SendAsync(RiderPhone, "AIDE");
        await h.SendAsync(VendorPhone, "AIDE");

        Assert.Contains("ACCEPTE", h.LastMessageTo(RiderPhone));
        Assert.Contains("LIVRAISON", h.LastMessageTo(VendorPhone));
    }

    // ------------------------------------------------------- Preuve de livraison (LIVRE)

    [Fact]
    public async Task Livre_WithCorrectClientCode_DeliversAndStampsVerification()
    {
        using var h = new WebhookHarness(new DeliveryProofOptions { RequireClientCode = true });
        var rider = AddRider(h);

        var order = new Order("Awa", ClientPhone, VendorPhone, "1 pagne", 5000m);
        order.ConfirmByVendor();
        order.AwaitRiderAcceptance();
        order.AssignRider(RiderPhone);
        order.LinkRider(rider.Id);
        var code = order.EnsureDeliveryCode();
        order.MarkReadyForPickup();
        order.MarkPickedUp();
        order.MarkInTransit();
        h.Context.Orders.Add(order);
        await h.Context.SaveChangesAsync();

        await h.SendAsync(RiderPhone, $"LIVRE CODE {code}");

        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.NotNull(order.DeliveryCodeVerifiedAt);
    }

    [Fact]
    public async Task Livre_WithWrongClientCode_IsRefusedAndCountsAttempt()
    {
        using var h = new WebhookHarness(new DeliveryProofOptions { RequireClientCode = true });
        var rider = AddRider(h);

        var order = new Order("Awa", ClientPhone, VendorPhone, "1 pagne", 5000m);
        order.ConfirmByVendor();
        order.AwaitRiderAcceptance();
        order.AssignRider(RiderPhone);
        order.LinkRider(rider.Id);
        var code = order.EnsureDeliveryCode();
        order.MarkReadyForPickup();
        order.MarkPickedUp();
        order.MarkInTransit();
        h.Context.Orders.Add(order);
        await h.Context.SaveChangesAsync();

        var wrong = code == "0000" ? "1111" : "0000";
        await h.SendAsync(RiderPhone, $"LIVRE CODE {wrong}");

        Assert.Equal(OrderStatus.InTransit, order.Status);
        Assert.Equal(1, order.DeliveryCodeAttempts);
        Assert.Contains("Code incorrect", h.LastMessageTo(RiderPhone));
    }

    [Fact]
    public async Task Livre_WithoutCode_IsRefusedWhenProofRequired()
    {
        using var h = new WebhookHarness(new DeliveryProofOptions { RequireClientCode = true });
        var rider = AddRider(h);

        var order = new Order("Awa", ClientPhone, VendorPhone, "1 pagne", 5000m);
        order.ConfirmByVendor();
        order.AwaitRiderAcceptance();
        order.AssignRider(RiderPhone);
        order.LinkRider(rider.Id);
        order.EnsureDeliveryCode();
        order.MarkReadyForPickup();
        order.MarkPickedUp();
        order.MarkInTransit();
        h.Context.Orders.Add(order);
        await h.Context.SaveChangesAsync();

        await h.SendAsync(RiderPhone, "LIVRE");

        Assert.Equal(OrderStatus.InTransit, order.Status);
    }

    // ------------------------------------------------------------------- Note du client

    [Fact]
    public async Task Note_FromClient_IsRecorded()
    {
        using var h = new WebhookHarness();
        var rider = AddRider(h);
        var order = await AddDeliveredOrderAsync(h, rider);

        await h.SendAsync(ClientPhone, "NOTE 5");

        var rating = await h.Context.RiderRatings.SingleAsync();
        Assert.Equal(5, rating.Score);
        Assert.Equal(order.Id, rating.OrderId);
        Assert.Equal(rider.Id, rating.RiderUserId);
    }

    [Fact]
    public async Task Note_FromClient_DoesNotCreateALead()
    {
        using var h = new WebhookHarness();
        var rider = AddRider(h);
        await AddDeliveredOrderAsync(h, rider);

        await h.SendAsync(ClientPhone, "NOTE 5");

        // Le client n'est pas un utilisateur enregistré : sans interception, sa note
        // serait passée au bot prospects et aurait créé un contact commercial.
        Assert.Empty(await h.Context.Leads.ToListAsync());
    }

    [Fact]
    public async Task Note_WithoutRecentDelivery_FallsThroughToProspectBot()
    {
        using var h = new WebhookHarness();

        await h.SendAsync("+2250799999999", "NOTE 5");

        // Aucune course à noter : le message ne doit pas être avalé.
        Assert.Empty(await h.Context.RiderRatings.ToListAsync());
        Assert.NotEmpty(await h.Context.Leads.ToListAsync());
    }

    [Fact]
    public async Task Note_Twice_IsRejectedTheSecondTime()
    {
        using var h = new WebhookHarness();
        var rider = AddRider(h);
        await AddDeliveredOrderAsync(h, rider);

        await h.SendAsync(ClientPhone, "NOTE 5");
        await h.SendAsync(ClientPhone, "NOTE 1");

        var rating = await h.Context.RiderRatings.SingleAsync();
        Assert.Equal(5, rating.Score);
        Assert.Contains("déjà noté", h.LastMessageTo(ClientPhone));
    }

    // ------------------------------------------------------------------- Bot prospects

    [Fact]
    public async Task UnknownNumber_CreatesALead()
    {
        using var h = new WebhookHarness();

        await h.SendAsync("+2250788888888", "Bonjour je suis commerçante à Cocody");

        var lead = await h.Context.Leads.SingleAsync();
        Assert.Contains("whatsapp", lead.Source);
    }

    [Fact]
    public async Task KnownVendor_IsNotTreatedAsAProspect()
    {
        using var h = new WebhookHarness();
        AddVendor(h);
        await h.Context.SaveChangesAsync();

        await h.SendAsync(VendorPhone, "un message quelconque");

        Assert.Empty(await h.Context.Leads.ToListAsync());

        // NB : ce test vérifie la RÈGLE (un utilisateur connu n'est pas un prospect), pas
        // la traduction SQL. Le prédicat non traduisible corrigé le 07/09 ne se reproduit
        // pas ici : le fournisseur InMemory évalue côté client et l'aurait accepté.
        // Seul PostgreSQL levait « The LINQ expression could not be translated ».
    }
}
