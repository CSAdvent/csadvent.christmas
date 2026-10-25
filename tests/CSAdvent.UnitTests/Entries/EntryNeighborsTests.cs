using CSAdvent.UnitTests.Calendar;
using CSAdvent.Web.Data;
using CSAdvent.Web.Entries;

namespace CSAdvent.UnitTests.Entries;

public sealed class EntryNeighborsTests
{
    private static readonly AdjacentEntry[] Posts =
    [
        new(3, 2, "Day 3, post 2", "Cy"),
        new(1, 1, "Day 1, post 1", "Ada"),
        new(2, 1, "Day 2, post 1", "Bea"),
        new(3, 1, "Day 3, post 1", "Cal"),
        new(4, 1, "Day 4, post 1", "Dee"),
    ];

    [Fact]
    public void PreviousAndNextFollowTheCalendarAcrossDaysAndParts()
    {
        var (previous, next) = EntryService.Neighbors(Posts, 3, 1, _ => true);

        Assert.Equal("Day 2, post 1", previous?.PostTitle);
        Assert.Equal("Day 3, post 2", next?.PostTitle);
    }

    [Fact]
    public void SpotsWithoutAPostAreSkipped()
    {
        // Day 1, post 2 and day 2, post 2 have no post, so they aren't in the list.
        var (previous, next) = EntryService.Neighbors(Posts, 2, 1, _ => true);

        Assert.Equal("Day 1, post 1", previous?.PostTitle);
        Assert.Equal("Day 3, post 1", next?.PostTitle);
    }

    [Fact]
    public void TheFirstPostHasNoPreviousOne() =>
        Assert.Null(EntryService.Neighbors(Posts, 1, 1, _ => true).Previous);

    [Fact]
    public void NothingLinksToAPostBeforeItsReveal()
    {
        // Day 3 is revealed, day 4 isn't yet.
        var (previous, next) = EntryService.Neighbors(Posts, 3, 2, day => day <= 3);

        Assert.Equal("Day 3, post 1", previous?.PostTitle);
        Assert.Null(next);
    }

    [Fact]
    public void ReadersOnlySeeRevealedPostsAndNeverTheTestSeason()
    {
        var schedule = CalendarFixtures.Schedule(CalendarFixtures.December10);

        Assert.True(RevealedPosts.IsShown(2026, 10, schedule));
        Assert.False(RevealedPosts.IsShown(2026, 11, schedule));
        Assert.False(RevealedPosts.IsShown(Season.TestYear, 1, schedule));
    }
}
