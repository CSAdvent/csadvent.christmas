using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// What My spots tells contributors in season 2483 (#175): each spot's deadlines, the blurb in one format at a time, their
/// backup asks and the backup list, and their spots and requests marked on the calendar.
/// </summary>
public sealed class MySpotsDeadlineTests(AppHostFixture fixture)
{
    private const int Year = 2483;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EachSpotSaysWhatsDueAndTheBlurbComesAfterTheSpots()
    {
        using var browser = await fixture.SignInAsync("deadline-ada", 24831);
        var slotId = await GiveSpotAsync(24831, day: 5, part: 1);

        var page = await browser.GetPageAsync("/my-spots");

        Assert.Contains("Add your post&#x27;s link by <time", page);
        Assert.Contains(">6:00 PM Eastern, December 4</time>. Then we start asking backup posters", page);
        Assert.Contains("Revealed at <time", page);
        Assert.Contains(">10:00 AM Eastern, December 5</time>. Your post needs the blurb by then", page);
        Assert.True(page.IndexOf($"href=\"my-spots/{slotId}\"", StringComparison.Ordinal) < page.IndexOf("Open your post with the C# Advent blurb", StringComparison.Ordinal));
        Assert.Contains($"name=\"blurb-format-{Year}\"", page);

        // Not on the backup list yet, and its sign-ups are open.
        Assert.Contains($"<a href=\"backups/{Year}\" class=\"font-medium text-advent-700 underline\">Join the backup list</a>", page);
    }

    [Fact]
    public async Task BackupAsksAreOnMySpotsToo()
    {
        using var browser = await fixture.SignInAsync("deadline-bea", 24832);
        await TestSeasons.EnsureAsync(fixture, Year, claimsOpen: true);
        int offerId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var contributorId = await db.Contributors.Where(c => c.GithubUserId == 24832).Select(c => c.Id).SingleAsync(CancellationToken);
            var season = await db.Seasons.SingleAsync(s => s.Year == Year, CancellationToken);
            var substitute = new Substitute { ContributorId = contributorId, Name = "Bea", Email = "deadline-bea@example.com", SignedUpAt = DateTimeOffset.UtcNow };
            season.Substitutes.Add(substitute);
            await db.SaveChangesAsync(CancellationToken);

            var slot = await db.Slots.SingleAsync(s => s.SeasonYear == Year && s.Day == 9 && s.Part == 1, CancellationToken);
            var offer = new BackupOffer
            {
                SlotId = slot.Id,
                SubstituteId = substitute.Id,
                AskedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(4),
                Status = BackupOfferStatus.Asked,
            };
            db.BackupOffers.Add(offer);
            await db.SaveChangesAsync(CancellationToken);
            offerId = offer.Id;
        }

        var page = await browser.GetPageAsync("/my-spots");

        Assert.Contains("We&#x27;ve asked you to fill December 9 &#xB7; Post 1.", page);
        Assert.Contains($"href=\"backups/offers/{offerId}\"", page);
        Assert.Contains("on the backup list. We&#x27;ll email you if a spot needs you.", page);
        Assert.DoesNotContain("Join the backup list", page);
    }

    [Fact]
    public async Task WithoutASpotTheyAreSentToTheCalendar()
    {
        using var browser = await fixture.SignInAsync("deadline-cy", 24833);

        var page = await browser.GetPageAsync("/my-spots");

        // An empty href is the home page, which Blazor writes without its value.
        Assert.Contains("Pick an open day on <a href class=\"font-medium text-advent-700 underline\">the calendar</a>", page);
    }

    [Fact]
    public async Task TheirSpotsAndRequestsAreMarkedForTheCalendar()
    {
        using var browser = await fixture.SignInAsync("deadline-dee", 24834);
        await GiveSpotAsync(24834, day: 12, part: 2);
        await using var db = await fixture.CreateDbContextAsync();
        var contributorId = await db.Contributors.Where(c => c.GithubUserId == 24834).Select(c => c.Id).SingleAsync(CancellationToken);
        var requested = await db.Slots.SingleAsync(s => s.SeasonYear == Year && s.Day == 13 && s.Part == 1, CancellationToken);
        db.ClaimRequests.Add(new ClaimRequest
        {
            SlotId = requested.Id,
            ContributorId = contributorId,
            DisplayName = "Dee",
            Email = "deadline-dee@example.com",
            TermsAcceptedAt = DateTimeOffset.UtcNow,
            TimingAcceptedAt = DateTimeOffset.UtcNow,
            SubmittedAt = DateTimeOffset.UtcNow,
            Status = ClaimRequestStatus.Pending,
        });
        await db.SaveChangesAsync(CancellationToken);

        var marks = await new ServicesOnAClock(db, DateTimeOffset.UtcNow).MySpots().GetCalendarMarksAsync(contributorId, Year, CancellationToken);

        Assert.Equal([(12, 2)], marks.Mine);
        Assert.Equal([(13, 1)], marks.Requested);
    }

    private async Task<int> GiveSpotAsync(long githubUserId, int day, int part)
    {
        await TestSeasons.EnsureAsync(fixture, Year, claimsOpen: true);
        await using var db = await fixture.CreateDbContextAsync();
        var contributorId = await db.Contributors.Where(c => c.GithubUserId == githubUserId).Select(c => c.Id).SingleAsync(CancellationToken);
        var slot = await db.Slots.SingleAsync(s => s.SeasonYear == Year && s.Day == day && s.Part == part, CancellationToken);
        slot.AuthorName = "Deadline author";
        slot.ContributorId = contributorId;
        await db.SaveChangesAsync(CancellationToken);
        return slot.Id;
    }
}
