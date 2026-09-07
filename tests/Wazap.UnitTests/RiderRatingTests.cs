using Wazap.API.Services;
using Wazap.Domain.Entities;
using Xunit;

namespace Wazap.UnitTests;

/// <summary>
/// Réputation livreur : note 1-5 laissée par le client après livraison (« NOTE 5 »).
/// </summary>
public class RiderRatingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Rating_AcceptsScoresInRange(int score)
    {
        var rating = new RiderRating(Guid.NewGuid(), Guid.NewGuid(), "+2250700000000", score);

        Assert.Equal(score, rating.Score);
        Assert.Null(rating.Comment);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void Rating_RejectsScoresOutOfRange(int score)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RiderRating(Guid.NewGuid(), Guid.NewGuid(), "+2250700000000", score));
    }

    [Fact]
    public void Rating_TrimsCommentAndTreatsBlankAsAbsent()
    {
        var withComment = new RiderRating(Guid.NewGuid(), Guid.NewGuid(), "+225", 4, "  trop lent  ");
        var withBlank = new RiderRating(Guid.NewGuid(), Guid.NewGuid(), "+225", 4, "   ");

        Assert.Equal("trop lent", withComment.Comment);
        Assert.Null(withBlank.Comment);
    }

    [Theory]
    [InlineData("NOTE 5", 5, null)]
    [InlineData("note 3", 3, null)]
    [InlineData("  NOTE   4  ", 4, null)]
    [InlineData("NOTE 2 livreur en retard", 2, "livreur en retard")]
    public void ParseScore_ReadsScoreAndOptionalComment(string text, int expectedScore, string? expectedComment)
    {
        Assert.True(RiderRatingService.TryParseScore(text, out var score, out var comment));

        Assert.Equal(expectedScore, score);
        Assert.Equal(expectedComment, comment);
    }

    [Theory]
    [InlineData("NOTE")]        // pas de score
    [InlineData("NOTE 0")]      // hors bornes
    [InlineData("NOTE 6")]
    [InlineData("NOTE bien")]   // pas un nombre
    [InlineData("NOTE -2")]
    public void ParseScore_RejectsInvalidInput(string text)
    {
        Assert.False(RiderRatingService.TryParseScore(text, out var score, out var comment));

        Assert.Equal(0, score);
        Assert.Null(comment);
    }

    [Fact]
    public void ParseScore_TruncatesVeryLongComment()
    {
        var text = "NOTE 1 " + new string('x', 500);

        Assert.True(RiderRatingService.TryParseScore(text, out _, out var comment));

        Assert.Equal(300, comment!.Length);
    }

    [Theory]
    [InlineData("NOTE 5", true)]
    [InlineData("note", true)]
    [InlineData("  NOTE 4 ", true)]
    [InlineData("NOTEZ moi", false)]   // ne doit pas capter un mot qui commence par NOTE
    [InlineData("bonjour", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsRatingCommand_DetectsOnlyTheCommand(string? text, bool expected)
    {
        Assert.Equal(expected, RiderRatingService.IsRatingCommand(text));
    }
}
