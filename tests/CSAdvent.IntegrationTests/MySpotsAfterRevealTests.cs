using CSAdvent.Web.Contributors;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Changing a post on My spots once it's revealed, in season 2481 (#155): a new link waits for an organizer, and a new title
/// or summary shows straight away and tells the organizers. Pages run on the real clock, long before this season, so these
/// drive the services with a fake one.
/// </summary>
public sealed class MySpotsAfterRevealTests(AppHostFixture fixture)
{
    private const int Year = 2481;

    // Noon on December 6, when day 6 is revealed and day 7 isn't.
    private static readonly DateTimeOffset December6Noon = new(Year, 12, 6, 12, 0, 0, TimeSpan.FromHours(-5));

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ANewLinkWaitsForAnOrganizerToApproveIt()
    {
        var (contributorId, slotId) = await GiveSpotAsync(24811, "reveal-ada", day: 6, part: 1, post: ("Records, revisited", "https://example.com/ada/records"));

        await using (var db = await fixture.CreateDbContextAsync())
        {
            var services = new ServicesOnAClock(db, December6Noon);
            var outcome = await services.MySpots().SaveAsync(contributorId, slotId, await FormAsync(db, slotId, url: "https://scam.example/records"), CancellationToken);

            Assert.Equal(MySpotOutcome.LinkRequested, outcome);
            Assert.Equal("https://example.com/ada/records", await UrlAsync(slotId));
            Assert.Equal(["post-links-new-request"], services.Sent.All.Select(e => e.Kind));
            Assert.Equal("hello@csadvent.christmas", services.Sent.All[0].To.Address);

            var spot = await new ServicesOnAClock(db, December6Noon).MySpots().GetAsync(contributorId, slotId, CancellationToken);
            Assert.Equal((true, "https://scam.example/records"), (spot!.IsPublished, spot.PendingLink));
        }

        await using (var db = await fixture.CreateDbContextAsync())
        {
            var postLinks = new ServicesOnAClock(db, December6Noon).PostLinks();
            var request = await postLinks.GetPendingAsync(contributorId, CancellationToken);
            var change = Assert.Single(request!.Changes);
            Assert.Equal(("https://example.com/ada/records", "https://scam.example/records"), (change.OldUrl, change.NewUrl));

            Assert.Equal(PostLinkDecision.Approved, await postLinks.ApproveAsync(request.Id, request.Version, "organizer", CancellationToken));
        }

        Assert.Equal("https://scam.example/records", await UrlAsync(slotId));
    }

    [Fact]
    public async Task ANewTitleShowsStraightAwayAndTellsTheOrganizers()
    {
        var (contributorId, slotId) = await GiveSpotAsync(24812, "reveal-bea", day: 6, part: 2, post: ("Span all the way", "https://example.com/bea/spans"));

        await using var db = await fixture.CreateDbContextAsync();
        var services = new ServicesOnAClock(db, December6Noon);
        var form = await FormAsync(db, slotId, title: "Span<T> all the way down");
        form.PostSummary = "Slicing without copying.";

        Assert.Equal(MySpotOutcome.Saved, await services.MySpots().SaveAsync(contributorId, slotId, form, CancellationToken));

        await using var check = await fixture.CreateDbContextAsync();
        var slot = await check.Slots.SingleAsync(s => s.Id == slotId, CancellationToken);
        Assert.Equal(("Span<T> all the way down", "Slicing without copying.", "https://example.com/bea/spans"), (slot.PostTitle, slot.PostSummary, slot.PostUrl));
        var email = Assert.Single(services.Sent.All);
        Assert.Equal(("post-changed", "hello@csadvent.christmas"), (email.Kind, email.To.Address));
        Assert.Equal("reveal-bea changed the title and summary of their post for December 6", email.Subject);
        Assert.Contains("Was: Span all the way", email.Text);
        Assert.False(await check.PostLinkRequests.AnyAsync(r => r.ContributorId == contributorId, CancellationToken));
    }

    [Fact]
    public async Task BeforeTheRevealEverythingSavesStraightAway()
    {
        var (contributorId, slotId) = await GiveSpotAsync(24813, "reveal-cy", day: 7, part: 1, post: ("Draft title", "https://example.com/cy/draft"));

        await using var db = await fixture.CreateDbContextAsync();
        var services = new ServicesOnAClock(db, December6Noon);

        Assert.Equal(MySpotOutcome.Saved, await services.MySpots().SaveAsync(contributorId, slotId, await FormAsync(db, slotId, "Final title", "https://example.com/cy/final"), CancellationToken));
        Assert.Equal("https://example.com/cy/final", await UrlAsync(slotId));
        Assert.Empty(services.Sent.All);
    }

    [Fact]
    public async Task ARevealedPostStaysOnTheCalendar()
    {
        var (contributorId, slotId) = await GiveSpotAsync(24814, "reveal-dot", day: 5, part: 1, post: ("Kept", "https://example.com/dot/kept"));

        await using var db = await fixture.CreateDbContextAsync();
        var form = await FormAsync(db, slotId);
        (form.PostTitle, form.PostUrl) = (null, null);

        Assert.Equal(MySpotOutcome.PostRequired, await new ServicesOnAClock(db, December6Noon).MySpots().SaveAsync(contributorId, slotId, form, CancellationToken));
        Assert.Equal("https://example.com/dot/kept", await UrlAsync(slotId));
    }

    [Fact]
    public async Task ALateAuthorCanAddTheirPostOnTheirDayButNotAfter()
    {
        var (contributorId, onTheDay) = await GiveSpotAsync(24815, "reveal-eve", day: 6, part: 1, post: null, year: Year + 1);
        var (_, dayBefore) = await GiveSpotAsync(24815, "reveal-eve", day: 5, part: 2, post: null, year: Year + 1);
        var december6 = December6Noon.AddYears(1);

        await using var db = await fixture.CreateDbContextAsync();
        var mySpots = new ServicesOnAClock(db, december6).MySpots();

        Assert.Equal(MySpotOutcome.Saved, await mySpots.SaveAsync(contributorId, onTheDay, await FormAsync(db, onTheDay, "Late but here", "https://example.com/eve/late"), CancellationToken));
        Assert.Equal(MySpotOutcome.TooLate, await mySpots.SaveAsync(contributorId, dayBefore, await FormAsync(db, dayBefore, "Too late", "https://example.com/eve/too-late"), CancellationToken));
        Assert.Null(await UrlAsync(dayBefore));
    }

    [Fact]
    public async Task NothingIsSavedWhileAnotherRequestForNewLinksWaits()
    {
        var (contributorId, slotId) = await GiveSpotAsync(24816, "reveal-fay", day: 4, part: 2, post: ("Old title", "https://example.com/fay/old"));
        await using (var db = await fixture.CreateDbContextAsync())
        {
            db.PostLinkRequests.Add(new PostLinkRequest
            {
                ContributorId = contributorId,
                SubmittedAt = DateTimeOffset.UtcNow,
                Status = PostLinkRequestStatus.Pending,
                Changes = [new PostLinkChange { SlotId = slotId, OldUrl = "https://example.com/fay/old", NewUrl = "https://example.com/fay/first-try" }],
            });
            await db.SaveChangesAsync(CancellationToken);
        }

        await using (var db = await fixture.CreateDbContextAsync())
        {
            var form = await FormAsync(db, slotId, "New title", "https://example.com/fay/second-try");

            Assert.Equal(MySpotOutcome.LinkAlreadyRequested, await new ServicesOnAClock(db, December6Noon).MySpots().SaveAsync(contributorId, slotId, form, CancellationToken));
        }

        await using var check = await fixture.CreateDbContextAsync();
        var slot = await check.Slots.SingleAsync(s => s.Id == slotId, CancellationToken);
        Assert.Equal(("Old title", "https://example.com/fay/old"), (slot.PostTitle, slot.PostUrl));
    }

    /// <summary>The spot's form as My spots shows it, with any changes.</summary>
    private static async Task<MySpotForm> FormAsync(AdventDbContext db, int slotId, string? title = null, string? url = null)
    {
        var slot = await db.Slots.AsNoTracking().SingleAsync(s => s.Id == slotId, CancellationToken);
        return new MySpotForm
        {
            AuthorName = slot.AuthorName!,
            PostTitle = title ?? slot.PostTitle,
            PostUrl = url ?? slot.PostUrl,
            PostSummary = slot.PostSummary,
            Version = slot.Version,
        };
    }

    private async Task<string?> UrlAsync(int slotId)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.Slots.Where(s => s.Id == slotId).Select(s => s.PostUrl).SingleAsync(CancellationToken);
    }

    private async Task<(int ContributorId, int SlotId)> GiveSpotAsync(long githubUserId, string login, int day, int part, (string Title, string Url)? post, int year = Year)
    {
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true);
        await using var db = await fixture.CreateDbContextAsync();
        var contributor = await db.Contributors.SingleOrDefaultAsync(c => c.GithubUserId == githubUserId, CancellationToken);
        if (contributor is null)
        {
            contributor = new Contributor
            {
                GithubUserId = githubUserId,
                GithubLogin = login,
                Email = $"{login}@example.com",
                EmailVerifiedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Contributors.Add(contributor);
            await db.SaveChangesAsync(CancellationToken);
        }

        var slot = await db.Slots.SingleAsync(s => s.SeasonYear == year && s.Day == day && s.Part == part, CancellationToken);
        slot.AuthorName = login;
        slot.ContributorId = contributor.Id;
        (slot.PostTitle, slot.PostUrl) = (post?.Title, post?.Url);
        await db.SaveChangesAsync(CancellationToken);
        return (contributor.Id, slot.Id);
    }
}
