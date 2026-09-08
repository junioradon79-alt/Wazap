using Microsoft.EntityFrameworkCore;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Routage WhatsApp de la réputation livreur : « AVIS » (liste des avis du livreur) et
/// « REPONDRE <n°> <texte> » (réponse à un avis), de bout en bout via le webhook.
/// </summary>
public class WebhookRatingReplyTests
{
    [Fact]
    public async Task Avis_NoRatings_ReturnsFriendlyMessage()
    {
        using var h = new WebhookHarness();
        var rider = AddRider(h);
        await h.Context.SaveChangesAsync();

        await h.SendAsync(rider.PhoneNumber!, "AVIS");

        Assert.Contains("Aucun avis", h.LastMessageTo(rider.PhoneNumber!));
    }

    [Fact]
    public async Task Avis_WithRatings_ReturnsNumberedList()
    {
        using var h = new WebhookHarness();
        var rider = AddRider(h);
        await AddRatingAsync(h, rider);
        await h.Context.SaveChangesAsync();

        await h.SendAsync(rider.PhoneNumber!, "AVIS");

        var reply = h.LastMessageTo(rider.PhoneNumber!);
        Assert.Contains("1. ⭐ 5/5", reply);
        Assert.Contains("REPONDRE", reply);
    }

    [Fact]
    public async Task Repondre_SavesReply_AndConfirms()
    {
        using var h = new WebhookHarness();
        var rider = AddRider(h);
        var order = await AddRatingAsync(h, rider);
        await h.Context.SaveChangesAsync();

        await h.SendAsync(rider.PhoneNumber!, "REPONDRE 1 Merci pour votre confiance !");

        var reply = h.LastMessageTo(rider.PhoneNumber!);
        Assert.Contains("enregistrée", reply);

        var rating = (await h.Context.RiderRatings
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync()).Single(r => r.OrderId == order.Id);
        Assert.Equal("Merci pour votre confiance !", rating.Reply);
        Assert.NotNull(rating.RepliedAt);
    }

    [Fact]
    public async Task Repondre_InvalidFormat_ShowsHelp()
    {
        using var h = new WebhookHarness();
        var rider = AddRider(h);
        await AddRatingAsync(h, rider);
        await h.Context.SaveChangesAsync();

        await h.SendAsync(rider.PhoneNumber!, "REPONDRE");

        Assert.Contains("Format : REPONDRE", h.LastMessageTo(rider.PhoneNumber!));
    }

    [Fact]
    public async Task Repondre_OutOfRangeIndex_ShowsHelp()
    {
        using var h = new WebhookHarness();
        var rider = AddRider(h);
        await AddRatingAsync(h, rider);
        await h.Context.SaveChangesAsync();

        await h.SendAsync(rider.PhoneNumber!, "REPONDRE 2 merci");

        Assert.Contains("Aucun avis n°2", h.LastMessageTo(rider.PhoneNumber!));
    }

    [Fact]
    public async Task Avis_AsVendor_IsNotIntercepted()
    {
        using var h = new WebhookHarness();
        var vendor = new User("boutique", "hash", UserRole.Vendor, "+2250700000009");
        h.Context.Users.Add(vendor);
        await h.Context.SaveChangesAsync();

        // Un vendeur n'a pas de commande « AVIS » : le message ne doit pas être avalé
        // par la réputation (aucune réponse générée par ce chemin).
        await h.SendAsync(vendor.PhoneNumber!, "AVIS");

        Assert.Null(h.LastMessageTo(vendor.PhoneNumber!));
    }

    private static User AddRider(WebhookHarness h, string phone = "+2250700000001")
    {
        var rider = new User("livreur-avis", "hash", UserRole.Rider, phone);
        h.Context.Users.Add(rider);
        return rider;
    }

    private static async Task<Order> AddRatingAsync(WebhookHarness h, User rider)
    {
        var order = new Order("Awa", "+2250708091011", "+2250700000002", "1 colis", 5000m);
        order.ConfirmByVendor();
        order.AwaitRiderAcceptance();
        order.AssignRider(rider.PhoneNumber!);
        order.LinkRider(rider.Id);
        order.MarkReadyForPickup();
        order.MarkPickedUp();
        order.MarkInTransit();
        order.MarkDelivered();
        h.Context.Orders.Add(order);

        h.Context.RiderRatings.Add(new RiderRating(order.Id, rider.Id, order.ClientWhatsAppNumber, 5));
        await h.Context.SaveChangesAsync();
        return order;
    }
}