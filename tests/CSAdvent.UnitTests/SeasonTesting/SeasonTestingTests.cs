using System.Collections.Frozen;
using System.Security.Claims;
using CSAdvent.UnitTests.Calendar;
using CSAdvent.Web.Auth;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Data;
using CSAdvent.Web.SeasonTesting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CSAdvent.UnitTests.SeasonTesting;

public sealed class SeasonTestingTests
{
    // An October 2026 rehearsal of the 2026 season: December 1 is on October 20.
    private static readonly DateOnly October20 = new(2026, 10, 20);
    private static readonly DateTimeOffset October25Noon = new(2026, 10, 25, 12, 0, 0, TimeSpan.FromHours(-4));

    private static TestSeasons Testing(params TestSeason[] seasons)
    {
        var testSeasons = new TestSeasons(null!);
        testSeasons.Replace(seasons);
        return testSeasons;
    }

    private static TestSeason Rehearsal(int year = 2026, params string[] testers) =>
        new(year, October20, testers.ToFrozenSet(StringComparer.OrdinalIgnoreCase));

    private static AdventSchedule Schedule(DateTimeOffset now, TestSeasons testSeasons) =>
        new(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), new FakeTimeProvider(now), testSeasons);

    [Fact]
    public void ARehearsalMovesEveryDayOfAdvent()
    {
        var schedule = Schedule(October25Noon, Testing(Rehearsal()));

        Assert.Equal(October20, schedule.GetDate(2026, 1));
        Assert.Equal(new DateOnly(2026, 11, 13), schedule.GetDate(2026, 25));
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.FromHours(-4)), schedule.GetRevealTime(2026, 6));
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.FromHours(-4)), schedule.GetDayEndTime(2026, 6));
        Assert.Equal(6, schedule.GetDayOfAdvent(2026, new DateOnly(2026, 10, 25)));
        Assert.True(schedule.IsRevealed(2026, 6));
        Assert.False(schedule.IsRevealed(2026, 7));
    }

    [Fact]
    public void OtherSeasonsKeepDecember()
    {
        var schedule = Schedule(October25Noon, Testing(Rehearsal()));

        Assert.Equal(new DateOnly(2025, 12, 6), schedule.GetDate(2025, 6));
        Assert.Null(schedule.GetDayOfAdvent(2025, new DateOnly(2026, 10, 25)));
        Assert.False(schedule.IsRehearsing(2025));
        Assert.True(schedule.IsRehearsing(2026));
    }

    [Fact]
    public void ARehearsalCanFinishTheSeason()
    {
        var schedule = Schedule(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.FromHours(-5)), Testing(Rehearsal()));

        Assert.True(schedule.IsSeasonOver(2026));
    }

    [Fact]
    public void TestersGetTheTestCalendar()
    {
        var schedule = Schedule(October25Noon, Testing(Rehearsal()));
        SeasonDates[] seasons = [new(2025, null), new(2026, new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-4)), IsTesting: true)];

        Assert.Equal(2026, HomeService.Plan(seasons, schedule, canSee: _ => true).CalendarYear);
    }

    [Fact]
    public void EveryoneElseStillSeesTheSeasonComing()
    {
        // Even after the rehearsal has revealed every day, the season isn't over for anyone but its testers.
        var schedule = Schedule(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.FromHours(-5)), Testing(Rehearsal()));
        var opensAt = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.FromHours(-5));
        SeasonDates[] seasons = [new(2025, null), new(2026, opensAt, IsTesting: true)];

        var plan = HomeService.Plan(seasons, schedule, canSee: _ => false);

        Assert.Null(plan.CalendarYear);
        Assert.Equal(2025, plan.LastYear);
        Assert.Equal(new UpcomingSeason(2026, new DateOnly(2026, 12, 1), DaysUntilSignUps: 11), plan.NextSeason);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("organizer", true)]
    [InlineData("Tess", true)]
    [InlineData("stranger", false)]
    [InlineData("", false)]
    public void OnlyTestersAndOrganizersSeeASeasonBeingTested(string? who, bool canSee)
    {
        var http = new HttpContextAccessor();
        if (who is not null)
        {
            var user = who switch
            {
                "" => new ClaimsPrincipal(new ClaimsIdentity()),
                "organizer" => SignedIn(1, "matt"),
                _ => SignedIn(2, who),
            };
            http.HttpContext = new DefaultHttpContext { User = user };
        }

        var access = new SeasonAccess(Testing(Rehearsal(2026, "tess")), http, new StaticOptions(new AccountOptions { Admins = [1] }));

        Assert.Equal(canSee, access.CanSee(2026));
        Assert.True(access.CanSee(2025));
        Assert.Equal(canSee, access.Testing is not null);
    }

    [Fact]
    public void TestersCanClaimBeforeSignUpsOpenAndNobodyElseCanAfter()
    {
        var season = CalendarFixtures.BuildSeason(claimsOpenAt: new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-4)));
        season.TestingStartedAt = October25Noon;
        var slot = season.Slots.Single(s => s.Day == 20 && s.Part == 2);
        var schedule = Schedule(October25Noon, Testing(Rehearsal()));

        Assert.Equal(ClaimAvailability.Open, ClaimService.CheckAvailability(season, slot, schedule, isTester: true));
        Assert.Equal(ClaimAvailability.ClaimsNotOpen, ClaimService.CheckAvailability(season, slot, schedule, isTester: false));

        // Past its sign-up date, a season still being tested stays closed to everyone else.
        var later = Schedule(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.FromHours(-5)), Testing(Rehearsal()));
        Assert.Equal(ClaimAvailability.ClaimsNotOpen, ClaimService.CheckAvailability(season, slot, later, isTester: false));
    }

    [Theory]
    [InlineData("octocat", "octocat")]
    [InlineData("  @OctoCat ", "octocat")]
    [InlineData("https://github.com/Calvin-Allen", "calvin-allen")]
    [InlineData("https://github.com/mgroves/", "mgroves")]
    [InlineData("-leading", null)]
    [InlineData("double--hyphen", null)]
    [InlineData("has space", null)]
    [InlineData("https://example.com/octocat", null)]
    [InlineData("", null)]
    [InlineData("a1234567890123456789012345678901234567890", null)]
    public void AcceptsGitHubLoginsInTheUsualForms(string input, string? expected) =>
        Assert.Equal(expected, SeasonTestingService.NormalizeLogin(input));

    private static ClaimsPrincipal SignedIn(long githubUserId, string login) =>
        new(new ClaimsIdentity(AdventClaims.For(githubUserId, login, verifiedEmail: null, contributorId: 1), "test"));

    private sealed class StaticOptions(AccountOptions value) : IOptionsMonitor<AccountOptions>
    {
        public AccountOptions CurrentValue => value;

        public AccountOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<AccountOptions, string?> listener) => null;
    }
}
