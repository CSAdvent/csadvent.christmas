using CSAdvent.Web.Contributors;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// The end of season 2480 (#146): My spots stays open to the last day's authors all through December 25, and closes at
/// midnight. Pages run on the real clock, long before this season, so these drive the service with a fake one.
/// </summary>
public sealed class MySpotsSeasonEndTests(AppHostFixture fixture)
{
    private const int Year = 2480;

    private static readonly DateTimeOffset ChristmasNoon = new(Year, 12, 25, 12, 0, 0, TimeSpan.FromHours(-5));
    private static readonly DateTimeOffset December26 = new(Year, 12, 26, 0, 0, 0, TimeSpan.FromHours(-5));

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheLastDaysAuthorsCanAddTheirPostUntilTheDayIsOver()
    {
        var (contributorId, slotId) = await GiveSpotAsync(24801, "season-end-noel", day: 25, part: 2);

        await using (var db = await fixture.CreateDbContextAsync())
        {
            var mySpots = Service(db, ChristmasNoon);

            Assert.Contains((await mySpots.GetOverviewAsync(contributorId, CancellationToken)).Spots, s => s.SlotId == slotId);
            var spot = await mySpots.GetAsync(contributorId, slotId, CancellationToken);
            Assert.NotNull(spot);

            var form = spot.Values;
            form.PostTitle = "Wrapping up with records";
            form.PostUrl = "https://example.com/noel/records";
            Assert.Equal(MySpotOutcome.Saved, await mySpots.SaveAsync(contributorId, slotId, form, CancellationToken));
            Assert.Empty(await mySpots.GetHistoryAsync(contributorId, CancellationToken));
        }

        await using (var db = await fixture.CreateDbContextAsync())
        {
            var mySpots = Service(db, December26);

            Assert.DoesNotContain((await mySpots.GetOverviewAsync(contributorId, CancellationToken)).Spots, s => s.SlotId == slotId);
            Assert.Null(await mySpots.GetAsync(contributorId, slotId, CancellationToken));

            var version = await db.Slots.Where(s => s.Id == slotId).Select(s => s.Version).SingleAsync(CancellationToken);
            var late = new MySpotForm { AuthorName = "Noel", PostTitle = "Too late", PostUrl = "https://example.com/noel/late", Version = version };
            Assert.Equal(MySpotOutcome.TooLate, await mySpots.SaveAsync(contributorId, slotId, late, CancellationToken));

            // It's their history now.
            Assert.Equal(["Wrapping up with records"], (await mySpots.GetHistoryAsync(contributorId, CancellationToken)).Select(p => p.Title));
        }
    }

    [Fact]
    public async Task RequestsForTheLastDayStayOnMySpotsUntilTheDayIsOver()
    {
        var (contributorId, _) = await GiveSpotAsync(24802, "season-end-ivy", day: 24, part: 1);
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slotId = await db.Slots.Where(s => s.SeasonYear == Year && s.Day == 25 && s.Part == 1).Select(s => s.Id).SingleAsync(CancellationToken);
            db.ClaimRequests.Add(new ClaimRequest
            {
                SlotId = slotId,
                ContributorId = contributorId,
                DisplayName = "Ivy",
                Email = "season-end-ivy@example.com",
                TermsAcceptedAt = DateTimeOffset.UtcNow,
                TimingAcceptedAt = DateTimeOffset.UtcNow,
                SubmittedAt = DateTimeOffset.UtcNow,
                Status = ClaimRequestStatus.Pending,
            });
            await db.SaveChangesAsync(CancellationToken);
        }

        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.Single((await Service(db, ChristmasNoon).GetOverviewAsync(contributorId, CancellationToken)).PendingRequests);
            Assert.Empty((await Service(db, December26).GetOverviewAsync(contributorId, CancellationToken)).PendingRequests);
        }
    }

    private static MySpotsService Service(AdventDbContext db, DateTimeOffset now) => new ServicesOnAClock(db, now).MySpots();

    private async Task<(int ContributorId, int SlotId)> GiveSpotAsync(long githubUserId, string login, int day, int part)
    {
        await TestSeasons.EnsureAsync(fixture, Year, claimsOpen: true);
        await using var db = await fixture.CreateDbContextAsync();
        var contributor = new Contributor
        {
            GithubUserId = githubUserId,
            GithubLogin = login,
            Email = $"{login}@example.com",
            EmailVerifiedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Contributors.Add(contributor);
        await db.SaveChangesAsync(CancellationToken);

        var slot = await db.Slots.SingleAsync(s => s.SeasonYear == Year && s.Day == day && s.Part == part, CancellationToken);
        slot.AuthorName = login;
        slot.ContributorId = contributor.Id;
        await db.SaveChangesAsync(CancellationToken);
        return (contributor.Id, slot.Id);
    }
}
