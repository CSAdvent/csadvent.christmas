using CSAdvent.Web.Calendar;
using CSAdvent.Web.Cards;
using SkiaSharp;

namespace CSAdvent.UnitTests.Cards;

public sealed class CardTests
{
    // Ten pixels per character, so widths are easy to reason about.
    private static float Measure(string text) => text.Length * 10;

    [Fact]
    public void WrapsAtSpacesWithinTheWidth() =>
        Assert.Equal(["one two", "three four", "five"], TextLayout.Wrap("one two three four five", Measure, 100));

    [Fact]
    public void FitsAtTheLargestSizeThatTakesFewEnoughLines()
    {
        // At size 2 each character is 20 wide; at size 1, 10.
        var (size, lines) = TextLayout.Fit("one two three four", size => text => text.Length * 10 * size, 100, 2, [2f, 1f]);

        Assert.Equal(1f, size);
        Assert.Equal(["one two", "three four"], lines);
    }

    [Fact]
    public void CutsTheLastLineShortWhenNothingFits()
    {
        var (size, lines) = TextLayout.Fit("one two three four five six seven", _ => Measure, 100, 2, [1f]);

        Assert.Equal(1f, size);
        Assert.Equal("one two", lines[0]);
        Assert.EndsWith(TextLayout.Ellipsis, lines[1]);
        Assert.True(Measure(lines[1]) <= 100);
    }

    [Fact]
    public void CardsAreFullSizeJpegsUnderBlueskysLimit()
    {
        using var renderer = new CardRenderer();

        foreach (var card in new[]
        {
            renderer.RenderEntry(new EntryCard(2026, 3, 1, 2, "Span<T> all the way down", "Ada Lovelace")),
            renderer.RenderSeason(2024, 50),
            renderer.RenderSeason(2026, null),
            renderer.RenderSeason(2026, null, new DateOnly(2026, 11, 1)),
        })
        {
            using var bitmap = SKBitmap.Decode(card);
            Assert.Equal((1200, 630), (bitmap.Width, bitmap.Height));
            Assert.True(card.Length < 1_000_000, $"A card is {card.Length} bytes; Bluesky's limit is 1 MB.");
            Assert.Equal([0xFF, 0xD8], card[..2]);
        }
    }

    [Fact]
    public void CharactersTheFontCantDrawAreLeftOut()
    {
        using var renderer = new CardRenderer();

        Assert.Equal("Happy holidays from C# Advent", CardRenderer.Printable("🎄 Happy  holidays\tfrom C# Advent 🎅", renderer.Bold));
        Assert.Equal("Ünïcödé café – naïve", CardRenderer.Printable("Ünïcödé café – naïve", renderer.Bold));
    }

    [Fact]
    public void CardLinksChangeWhenTheirTextDoes()
    {
        var card = new EntryCard(2026, 3, 1, 2, "First title", "Ada");

        Assert.StartsWith("cards/2026/3/1.jpg?v=", CardLinks.Entry(card));
        Assert.NotEqual(CardLinks.Entry(card), CardLinks.Entry(card with { Title = "Fixed title" }));
        Assert.Equal("cards/2024.jpg?v=50", CardLinks.Season(2024, 50));
        Assert.Equal("cards/2026.jpg", CardLinks.Season(2026, null));
        Assert.Equal("cards/2026.jpg?v=signups-2026-11-01", CardLinks.Upcoming(2026, new DateOnly(2026, 11, 1)));
    }

    [Fact]
    public void BetweenSeasonsTheHomeCardNeverShowsTheFinishedSeason()
    {
        var finished = new FinishedSeason(2025, PostCount: 50, AuthorCount: 45);

        Assert.Equal(CardLinks.General, CardLinks.Home(new HomePage(null, finished, NextSeason: null)));
        Assert.Equal(CardLinks.General, CardLinks.Home(new HomePage(null, finished, new UpcomingSeason(2026, SignUpsOpenOn: null, DaysUntilSignUps: null))));
        Assert.Equal("cards/2026.jpg?v=signups-2026-11-01", CardLinks.Home(new HomePage(null, finished, new UpcomingSeason(2026, new DateOnly(2026, 11, 1), 9))));
    }
}
