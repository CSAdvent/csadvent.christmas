using CSAdvent.Web.Data;
using CSAdvent.Web.Data.Archive;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.UnitTests.Data;

public sealed class ArchiveSeederTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<ArchivedSeason> Archive = ArchiveSeeder.LoadSeasons();

    [Fact]
    public void ContainsEverySeasonFrom2017To2025()
    {
        Assert.Equal(Enumerable.Range(2017, 9), Archive.Select(s => s.Year));
    }

    [Theory]
    [InlineData(2017, 1, 25)]
    [InlineData(2018, 2, 47)]
    [InlineData(2019, 2, 48)]
    [InlineData(2020, 2, 42)]
    [InlineData(2021, 2, 50)]
    [InlineData(2022, 2, 50)]
    [InlineData(2023, 2, 50)]
    [InlineData(2024, 2, 48)]
    [InlineData(2025, 2, 48)]
    public void EachSeasonHasEverySlotAndTheExpectedPosts(int year, int slotsPerDay, int posts)
    {
        var season = Archive.Single(s => s.Year == year).ToSeason(Now);

        Assert.Equal(slotsPerDay, season.SlotsPerDay);
        Assert.Equal(25 * slotsPerDay, season.Slots.Count);
        Assert.Equal(posts, season.Slots.Count(s => s.PostUrl is not null));
    }

    [Fact]
    public void KeepsSlotsThatWereNeverFilled()
    {
        var slots = Archive.Single(s => s.Year == 2024).ToSeason(Now).Slots;

        // The 2024 archive still showed "Claim This Spot!" for December 17, part 1.
        Assert.False(slots.Single(s => s is { Day: 17, Part: 1 }).IsClaimed);
    }

    [Fact]
    public void KeepsAuthorsWhoNeverPublished()
    {
        var slot = Archive.Single(s => s.Year == 2019).ToSeason(Now).Slots.Single(s => s is { Day: 14, Part: 1 });

        Assert.Equal("Benjamin Howarth", slot.AuthorName);
        Assert.Null(slot.PostUrl);
    }

    [Fact]
    public void LinksTheEarlySeasonsToWhereTheyWereFirstHosted()
    {
        Assert.All(
            Archive.Where(s => s.Year <= 2019),
            s => Assert.StartsWith("[Originally hosted on crosscuttingconcerns.com](https://crosscuttingconcerns.com/", s.Notes));
        Assert.All(Archive.Where(s => s.Year > 2019), s => Assert.Null(s.Notes));
    }

    [Fact]
    public void EveryValueFitsTheSchema()
    {
        using var context = new AdventDbContext(
            new DbContextOptionsBuilder<AdventDbContext>().UseNpgsql("Host=localhost;Database=csadvent").Options);
        var slotType = context.Model.FindEntityType(typeof(Slot))!;
        int MaxLength(string property) => slotType.FindProperty(property)!.GetMaxLength()!.Value;

        foreach (var slot in Archive.SelectMany(s => s.Slots))
        {
            Assert.True(slot.AuthorName is not null || slot is { PostTitle: null, PostUrl: null }, $"{slot} has a post but no author");
            Assert.True((slot.AuthorName?.Length ?? 0) <= MaxLength(nameof(Slot.AuthorName)), $"{slot} author name is too long");
            Assert.True((slot.PostTitle?.Length ?? 0) <= MaxLength(nameof(Slot.PostTitle)), $"{slot} post title is too long");

            foreach (var url in new[] { slot.AuthorUrl, slot.PostUrl }.OfType<string>())
            {
                Assert.True(url.Length <= MaxLength(nameof(Slot.PostUrl)), $"{url} is too long");
                Assert.True(Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https", $"{url} isn't an http(s) link");
            }
        }
    }
}
