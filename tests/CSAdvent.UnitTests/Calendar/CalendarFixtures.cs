using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CSAdvent.UnitTests.Calendar;

/// <summary>A 2026 season seen at midday Eastern on December 10: days 1-10 are revealed and claims are open.</summary>
internal static class CalendarFixtures
{
    public static readonly DateTimeOffset December10 = new(2026, 12, 10, 12, 0, 0, TimeSpan.FromHours(-5));

    public static AdventSchedule Schedule(DateTimeOffset now) =>
        new(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), new FakeTimeProvider(now));

    /// <summary>
    /// Day 10: part 1 published by Ada, part 2 claimed by Bob, who never published.
    /// Day 11: part 1 claimed by Carol, part 2 open. Every other slot is open.
    /// </summary>
    public static Season BuildSeason(DateTimeOffset? claimsOpenAt)
    {
        var season = Season.Create(2026, slotsPerDay: 2, December10);
        season.ClaimsOpenAt = claimsOpenAt;

        Slot Get(int day, int part) => season.Slots.Single(s => s.Day == day && s.Part == part);

        Get(10, 1).AuthorName = "Ada";
        Get(10, 1).PostTitle = "Pattern matching all the things";
        Get(10, 1).PostUrl = "https://example.com/ada";
        Get(10, 2).AuthorName = "Bob";
        Get(11, 1).AuthorName = "Carol";
        Get(11, 1).AuthorUrl = "https://example.com/carol";
        Get(11, 1).PostTitle = "Not revealed yet";
        Get(11, 1).PostUrl = "https://example.com/carol/post";

        return season;
    }

    public static SeasonCalendar Calendar(DateTimeOffset? claimsOpenAt, DateTimeOffset? now = null) =>
        SeasonCalendar.From(BuildSeason(claimsOpenAt), Schedule(now ?? December10));
}
