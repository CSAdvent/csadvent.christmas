using System.Collections.Frozen;
using System.Security.Claims;
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
    private const int TestYear = Season.TestYear;

    // The test season's rehearsal: December 1 is on October 20.
    private static readonly DateOnly October20 = new(2026, 10, 20);
    private static readonly DateTimeOffset October25Noon = new(2026, 10, 25, 12, 0, 0, TimeSpan.FromHours(-4));

    private static TestSeasons Testing(params string[] testers)
    {
        var testSeasons = new TestSeasons(null!);
        testSeasons.Replace([new TestSeason(TestYear, October20, testers.ToFrozenSet(StringComparer.OrdinalIgnoreCase))]);
        return testSeasons;
    }

    private static AdventSchedule Schedule(DateTimeOffset now, TestSeasons testSeasons) =>
        new(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), new FakeTimeProvider(now), testSeasons);

    [Fact]
    public void TheRehearsalMovesEveryDayOfTheTestSeason()
    {
        var schedule = Schedule(October25Noon, Testing());

        Assert.Equal(October20, schedule.GetDate(TestYear, 1));
        Assert.Equal(new DateOnly(2026, 11, 13), schedule.GetDate(TestYear, 25));
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.FromHours(-4)), schedule.GetRevealTime(TestYear, 6));
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.FromHours(-4)), schedule.GetDayEndTime(TestYear, 6));
        Assert.Equal(6, schedule.GetDayOfAdvent(TestYear, new DateOnly(2026, 10, 25)));
        Assert.True(schedule.IsRevealed(TestYear, 6));
        Assert.False(schedule.IsRevealed(TestYear, 7));
    }

    [Fact]
    public void RealSeasonsKeepDecember()
    {
        var schedule = Schedule(October25Noon, Testing());

        Assert.Equal(new DateOnly(2026, 12, 6), schedule.GetDate(2026, 6));
        Assert.Null(schedule.GetDayOfAdvent(2026, new DateOnly(2026, 10, 25)));
        Assert.False(schedule.IsRehearsing(2026));
        Assert.True(schedule.IsRehearsing(TestYear));
    }

    [Fact]
    public void TheRehearsalCanFinishTheTestSeason()
    {
        var schedule = Schedule(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.FromHours(-5)), Testing());

        Assert.True(schedule.IsSeasonOver(TestYear));
    }

    [Fact]
    public void InTestModeTheHomePageShowsTheTestSeason()
    {
        // Even during a real season's calendar, test mode is about the test season.
        var schedule = Schedule(new DateTimeOffset(2026, 12, 3, 12, 0, 0, TimeSpan.FromHours(-5)), Testing());
        SeasonDates[] seasons = [new(2026, new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-4))), new(TestYear, null, IsTest: true)];

        Assert.Equal(TestYear, HomeService.Plan(seasons, schedule).CalendarYear);
        Assert.Equal(2026, HomeService.Plan([seasons[0]], schedule).CalendarYear);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("organizer", true)]
    [InlineData("Tess", true)]
    [InlineData("stranger", false)]
    [InlineData("", false)]
    public void OnlyTestersAndOrganizersSeeTheTestSeason(string? who, bool canSee)
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

        var access = Access(http, Testing("tess"));

        Assert.Equal(canSee, access.CanSee(TestYear));
        Assert.True(access.CanSee(2026));
        Assert.Equal(canSee, access.TestSeason is not null);
    }

    [Fact]
    public void TestModeIsForTestersWithTheCookie()
    {
        var context = new DefaultHttpContext { User = SignedIn(2, "tess") };
        context.Request.Headers.Cookie = $"{SeasonAccess.TestModeCookie}=on";
        var http = new HttpContextAccessor { HttpContext = context };

        Assert.True(Access(http, Testing("tess")).InTestMode);
        Assert.False(Access(http, Testing("someone-else")).InTestMode);

        // Without a test season there's nothing to switch to.
        Assert.False(Access(http, new TestSeasons(null!)).InTestMode);
    }

    [Fact]
    public void TheTestSeasonIsAlwaysOpenToTheTestersWhoCanSeeIt()
    {
        var season = Season.Create(TestYear, slotsPerDay: 2, October25Noon);
        var slot = season.Slots.Single(s => s.Day == 20 && s.Part == 2);

        Assert.Equal(ClaimAvailability.Open, ClaimService.CheckAvailability(season, slot, Schedule(October25Noon, Testing())));
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

    private static SeasonAccess Access(IHttpContextAccessor http, TestSeasons testSeasons) =>
        new(testSeasons, http, new StaticOptions(new AccountOptions { Admins = [1] }));

    private static ClaimsPrincipal SignedIn(long githubUserId, string login) =>
        new(new ClaimsIdentity(AdventClaims.For(githubUserId, login, verifiedEmail: null, contributorId: 1), "test"));

    private sealed class StaticOptions(AccountOptions value) : IOptionsMonitor<AccountOptions>
    {
        public AccountOptions CurrentValue => value;

        public AccountOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<AccountOptions, string?> listener) => null;
    }
}
