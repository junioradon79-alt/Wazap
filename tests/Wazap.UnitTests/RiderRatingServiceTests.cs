using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wazap.API.Services;
using Wazap.Application.Configuration;
using Wazap.Domain.Entities;
using Wazap.Domain.Enums;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Réputation livreur — compléments : le livreur consulte ses avis (« AVIS ») et y répond
/// (« REPONDRE <n°> <texte> ») ; la page admin liste les avis (client masqué) et agrège les
/// moyennes par livreur.
/// </summary>
public sealed class RiderRatingServiceTests
{
    private static RiderRatingService CreateService(TestDbContext db, RiderReputationOptions? options = null)
        => new RiderRatingService(db.Context, options ?? new RiderReputationOptions(),
            NullLogger<RiderRatingService>.Instance);

    private static User NewRider(TestDbContext db, string phone = "+2250700000002")
    {
        var rider = new User("rider-reputation", "hash", UserRole.Rider, phone);
        db.Context.Users.Add(rider);
        return rider;
    }

    private static Order NewOrder(TestDbContext db, string clientPhone, Guid riderId)
    {
        var order = new Order("Awa", clientPhone, "+2250700000001", "1 colis", 5000m);
        order.ConfirmByVendor();
        order.AwaitRiderAcceptance();
        order.AssignRider("+2250700000002");
        order.LinkRider(riderId);
        order.MarkReadyForPickup();
        order.MarkPickedUp();
        order.MarkInTransit();
        order.MarkDelivered();
        db.Context.Orders.Add(order);
        return order;
    }

    private static RiderRating AddRating(TestDbContext db, Order order, Guid riderId,
        int score, string? comment = null)
    {
        var rating = new RiderRating(order.Id, riderId, order.ClientWhatsAppNumber, score, comment);
        db.Context.RiderRatings.Add(rating);
        return rating;
    }

    // ------------------------------------------------------------ Entité : réponse du livreur

    [Fact]
    public void ReplyAs_StoresTrimmedReply_WithTimestamp()
    {
        var rating = new RiderRating(Guid.NewGuid(), Guid.NewGuid(), "+225", 3);

        rating.ReplyAs("  Merci pour votre confiance  ");

        Assert.Equal("Merci pour votre confiance", rating.Reply);
        Assert.NotNull(rating.RepliedAt);
    }

    [Fact]
    public void ReplyAs_RejectsBlankReply()
    {
        var rating = new RiderRating(Guid.NewGuid(), Guid.NewGuid(), "+225", 3);

        Assert.Throws<ArgumentException>(() => rating.ReplyAs("   "));
    }

    [Fact]
    public void ReplyAs_TruncatesToMaxLength()
    {
        var rating = new RiderRating(Guid.NewGuid(), Guid.NewGuid(), "+225", 4);

        rating.ReplyAs(new string('x', 800));

        Assert.Equal(RiderRating.MaxReplyLength, rating.Reply!.Length);
    }

    // ------------------------------------------------------------ Parsing des commandes WhatsApp

    [Theory]
    [InlineData("REPONDRE 2 Merci pour tout", 2, "Merci pour tout")]
    [InlineData("repondre 1 ok", 1, "ok")]
    [InlineData("  REPONDRE   3   texte libre ", 3, "texte libre")]
    public void ParseReply_ReadsIndexAndMessage(string text, int expectedIndex, string expectedReply)
    {
        Assert.True(RiderRatingService.TryParseReplyCommand(text, out var index, out var reply));

        Assert.Equal(expectedIndex, index);
        Assert.Equal(expectedReply, reply);
    }

    [Theory]
    [InlineData("REPONDRE")]
    [InlineData("REPONDRE 0 merci")]
    [InlineData("REPONDRE 6 merci")]
    [InlineData("REPONDRE abc merci")]
    [InlineData("REPONDRE 2")]
    [InlineData("REPOND")]
    public void ParseReply_RejectsInvalidInput(string text)
    {
        Assert.False(RiderRatingService.TryParseReplyCommand(text, out var index, out var reply));

        Assert.Equal(0, index);
        Assert.Equal(string.Empty, reply);
    }

    [Theory]
    [InlineData("AVIS", true)]
    [InlineData(" avis ", true)]
    [InlineData("AVIS 2", false)]
    [InlineData("bonjour", false)]
    public void IsMyRatingsCommand_DetectsOnlyTheCommand(string? text, bool expected)
        => Assert.Equal(expected, RiderRatingService.IsMyRatingsCommand(text));

    [Theory]
    [InlineData("REPONDRE 1 merci", true)]
    [InlineData("REPONDRE", true)]
    [InlineData("repondre 2 ok", true)]
    [InlineData("reponse", false)]
    public void IsReplyCommand_DetectsOnlyTheCommand(string? text, bool expected)
        => Assert.Equal(expected, RiderRatingService.IsReplyCommand(text));

    // ------------------------------------------------------------ Service : liste des avis du livreur

    [Fact]
    public async Task ListMyRatings_NoRatings_ReturnsFriendlyMessage()
    {
        var db = new TestDbContext();
        await db.Context.SaveChangesAsync();
        var service = CreateService(db);

        var message = await service.ListMyRatingsTextAsync(Guid.NewGuid());

        Assert.Contains("Aucun avis", message);
    }

    [Fact]
    public async Task ListMyRatings_ListsRecentRatings_Numbered()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var rider = NewRider(db);
        var first = NewOrder(db, "+2250708091011", rider.Id);
        var second = NewOrder(db, "+2250708091012", rider.Id);
        AddRating(db, first, rider.Id, 4, "un peu lent");
        AddRating(db, second, rider.Id, 5);
        await context.SaveChangesAsync();
        var service = CreateService(db);

        var message = await service.ListMyRatingsTextAsync(rider.Id);

        Assert.Contains("REPONDRE", message);
        Assert.Contains("1. ⭐ 5/5", message);
        Assert.Contains("2. ⭐ 4/5 — « un peu lent »", message);
    }

    // ------------------------------------------------------------ Service : réponse du livreur

    [Fact]
    public async Task ReplyAsync_SavesReply_OnTargetRating()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var rider = NewRider(db);
        var first = NewOrder(db, "+2250708091011", rider.Id);
        var second = NewOrder(db, "+2250708091012", rider.Id);
        AddRating(db, first, rider.Id, 4, "lent");
        AddRating(db, second, rider.Id, 5);
        await context.SaveChangesAsync();
        var service = CreateService(db);

        var message = await service.ReplyAsync(rider.Id, 1, "Merci pour la confiance !");

        Assert.Contains("enregistrée", message);

        var ratings = await context.RiderRatings.OrderByDescending(r => r.CreatedAt).ToListAsync();
        Assert.Equal("Merci pour la confiance !", ratings[0].Reply);
        Assert.NotNull(ratings[0].RepliedAt);
        Assert.Null(ratings[1].Reply);
    }

    [Fact]
    public async Task ReplyAsync_InvalidIndex_ReturnsHelp_AndChangesNothing()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var rider = NewRider(db);
        var order = NewOrder(db, "+2250708091011", rider.Id);
        AddRating(db, order, rider.Id, 5);
        await context.SaveChangesAsync();
        var service = CreateService(db);

        var message = await service.ReplyAsync(rider.Id, 3, "Merci");

        Assert.Contains("REPONDRE", message);
        var rating = await context.RiderRatings.SingleAsync();
        Assert.Null(rating.Reply);
    }

    // ------------------------------------------------------------ Service : page admin

    [Fact]
    public async Task ListForAdmin_MasksClientPhone_AndAggregates()
    {
        var db = new TestDbContext();
        var context = db.Context;
        var rider = NewRider(db);
        var order = NewOrder(db, "+2250708091011", rider.Id);
        order.LinkVendor(Guid.NewGuid());
        AddRating(db, order, rider.Id, 4, "trop lent");
        await context.SaveChangesAsync();
        var service = CreateService(db);

        var board = await service.ListForAdminAsync();

        Assert.Single(board.Ratings);
        var line = board.Ratings[0];
        Assert.Equal(rider.Id, line.RiderId);
        Assert.Equal("rider-reputation", line.RiderName);
        Assert.Equal(4, line.Score);
        Assert.Equal("trop lent", line.Comment);
        Assert.Contains("••", line.MaskedClientPhone);
        Assert.DoesNotContain("080910", line.MaskedClientPhone);   // partie centrale dissimulée
        Assert.Single(board.RiderSummaries);
        Assert.Equal(4.0, board.RiderSummaries[0].AverageScore);
        Assert.Equal(1, board.RiderSummaries[0].RatingCount);
    }
}