using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>The organizer area: seasons and claim approvals. Uses seasons 2101-2103.</summary>
public sealed class ManageTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private Task<HttpClient> SignInAsOrganizerAsync() => fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

    [Theory]
    [InlineData("/manage/seasons")]
    [InlineData("/manage/seasons/2026")]
    [InlineData("/manage/requests")]
    [InlineData("/manage/requests/1")]
    public async Task ContributorsCannotOpenOrganizerPages(string path)
    {
        using var browser = await fixture.SignInAsync("not-an-organizer", 7101);

        var response = await browser.GetAsync(path, CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/access-denied", TestBrowser.PathOf(response.Headers.Location!));
    }

    [Fact]
    public async Task PastSeasonsAreHistoryOpenedFromTheOverview()
    {
        using var organizer = await SignInAsOrganizerAsync();

        // The archive's 2025 season is finished: the overview links into it, and nothing offers to change its sign-ups.
        var overview = await organizer.GetStringAsync("/manage", CancellationToken);
        Assert.Contains(">Past seasons</h2>", overview);
        Assert.Contains("href=\"manage/entries?year=2025\"", overview);

        var seasons = await organizer.GetStringAsync("/manage/seasons", CancellationToken);
        Assert.DoesNotContain("href=\"manage/seasons/2025\"", seasons);
        Assert.Contains("so its sign-ups can", await organizer.GetStringAsync("/manage/seasons/2025", CancellationToken));

        Assert.Contains("C# Advent 2025 is over, so this is its history.", await organizer.GetStringAsync("/manage/entries?year=2025", CancellationToken));
    }

    [Fact]
    public async Task OrganizersCreateSeasonsWithSignUpsInTheRevealTimeZone()
    {
        using var organizer = await SignInAsOrganizerAsync();
        var fields = new Dictionary<string, string>
        {
            ["Form.Year"] = "2101",
            ["Form.SlotsPerDay"] = "2",
            ["Form.ClaimsOpen"] = "2101-12-15T09:30",
        };

        var response = await organizer.PostFormAsync("/manage/seasons", "create-season", new(fields));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/manage/seasons?done=created", TestBrowser.PathOf(response.Headers.Location!));
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var season = await db.Seasons.Include(s => s.Slots).SingleAsync(s => s.Year == 2101, CancellationToken);
            Assert.Equal(50, season.Slots.Count);
            // 9:30 AM Eastern Standard Time is 14:30 UTC.
            Assert.Equal(new DateTimeOffset(2101, 12, 15, 14, 30, 0, TimeSpan.Zero), season.ClaimsOpenAt);
        }

        var again = await organizer.PostFormAsync("/manage/seasons", "create-season", new(fields));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Contains("There&#x27;s already a season for 2101.", await again.Content.ReadAsStringAsync(CancellationToken));
    }

    [Fact]
    public async Task OrganizersScheduleAndUnscheduleSignUps()
    {
        await TestSeasons.EnsureAsync(fixture, 2103, claimsOpen: false);
        using var organizer = await SignInAsOrganizerAsync();

        var scheduled = await organizer.PostFormAsync("/manage/seasons/2103", "sign-ups", new() { ["Form.ClaimsOpen"] = "2103-11-01T00:00" });
        Assert.Equal("/manage/seasons?done=saved", TestBrowser.PathOf(scheduled.Headers.Location!));
        Assert.NotNull(await ClaimsOpenAtAsync(2103));

        await organizer.PostFormAsync("/manage/seasons/2103", "sign-ups", new() { ["Form.ClaimsOpen"] = "" });
        Assert.Null(await ClaimsOpenAtAsync(2103));
    }

    [Fact]
    public async Task ApprovingARequestClaimsTheSpotAndCanOnlyHappenOnce()
    {
        await TestSeasons.EnsureAsync(fixture, 2102, claimsOpen: true);
        var requestId = await RequestAsync("approve-ada", 7102, day: 3, part: 1, "Ada Lovelace", link: "https://example.com/ada");
        using var organizer = await SignInAsOrganizerAsync();

        var list = await organizer.GetStringAsync("/manage/requests?year=2102", CancellationToken);
        Assert.Contains("Ada Lovelace", list);

        var slotId = await SlotIdAsync(2102, 3, 1);
        var response = await organizer.PostFormAsync($"/manage/requests/{requestId}", "approve", new() { ["Approve.SlotId"] = $"{slotId}" });

        Assert.Equal($"/manage/requests/{requestId}?done=approved", TestBrowser.PathOf(response.Headers.Location!));
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.Include(s => s.Contact).Include(s => s.Contributor).SingleAsync(s => s.Id == slotId, CancellationToken);
            Assert.Equal(("Ada Lovelace", "https://example.com/ada", 7102L, "approve-ada@example.com"),
                (slot.AuthorName, slot.AuthorUrl, slot.Contributor!.GithubUserId, slot.Contact!.Email));
            var request = await db.ClaimRequests.SingleAsync(r => r.Id == requestId, CancellationToken);
            Assert.Equal(ClaimRequestStatus.Approved, request.Status);
            Assert.NotNull(request.DecidedAt);
        }

        var approval = await fixture.WaitForEmailAsync("approve-ada@example.com", "You're on the C# Advent calendar: December 3");
        Assert.Contains("You&#x27;re in, Ada Lovelace!", approval.Html);

        // The approve form is gone once the request is decided; posting it again changes nothing.
        var again = await organizer.PostAsync(
            $"/manage/requests/{requestId}",
            new FormUrlEncodedContent([new("_handler", "approve"), new("Approve.SlotId", $"{slotId}"), new("__RequestVerificationToken", await TokenAsync(organizer, $"/manage/requests/{requestId}"))]),
            CancellationToken);
        Assert.Contains("already been approved, declined or withdrawn", await again.Content.ReadAsStringAsync(CancellationToken));
    }

    [Fact]
    public async Task ASpotGoesToOneRequestAndTheOthersCanBeDeclined()
    {
        await TestSeasons.EnsureAsync(fixture, 2102, claimsOpen: true);
        var first = await RequestAsync("first-in", 7103, day: 4, part: 1, "First In");
        var second = await RequestAsync("second-in", 7104, day: 4, part: 1, "Second In");
        var slotId = await SlotIdAsync(2102, 4, 1);
        using var organizer = await SignInAsOrganizerAsync();

        var reviewPage = await organizer.GetStringAsync($"/manage/requests/{second}", CancellationToken);
        Assert.Contains("Other requests for this spot", reviewPage);
        Assert.Contains("First In", reviewPage);

        await organizer.PostFormAsync($"/manage/requests/{first}", "approve", new() { ["Approve.SlotId"] = $"{slotId}" });
        var refused = await organizer.PostAsync(
            $"/manage/requests/{second}",
            new FormUrlEncodedContent([new("_handler", "approve"), new("Approve.SlotId", $"{slotId}"), new("__RequestVerificationToken", await TokenAsync(organizer, $"/manage/requests/{second}"))]),
            CancellationToken);
        Assert.Contains("That spot has just been claimed", await refused.Content.ReadAsStringAsync(CancellationToken));

        var declined = await organizer.PostFormAsync($"/manage/requests/{second}", "decline", []);
        Assert.Equal($"/manage/requests/{second}?done=declined", TestBrowser.PathOf(declined.Headers.Location!));
        await using var db = await fixture.CreateDbContextAsync();
        Assert.Equal(ClaimRequestStatus.Declined, (await db.ClaimRequests.SingleAsync(r => r.Id == second, CancellationToken)).Status);
        Assert.Equal("First In", (await db.Slots.SingleAsync(s => s.Id == slotId, CancellationToken)).AuthorName);
    }

    [Fact]
    public async Task ARequestCanBeApprovedIntoADifferentOpenSpot()
    {
        await TestSeasons.EnsureAsync(fixture, 2102, claimsOpen: true);
        var requestId = await RequestAsync("second-choice", 7105, day: 5, part: 1, "Second Choice");
        var otherSlotId = await SlotIdAsync(2102, 6, 2);
        using var organizer = await SignInAsOrganizerAsync();

        await organizer.PostFormAsync($"/manage/requests/{requestId}", "approve", new() { ["Approve.SlotId"] = $"{otherSlotId}" });

        await using var db = await fixture.CreateDbContextAsync();
        Assert.Equal("Second Choice", (await db.Slots.SingleAsync(s => s.Id == otherSlotId, CancellationToken)).AuthorName);
        Assert.Null((await db.Slots.SingleAsync(s => s.SeasonYear == 2102 && s.Day == 5 && s.Part == 1, CancellationToken)).AuthorName);
        Assert.Equal(otherSlotId, (await db.ClaimRequests.SingleAsync(r => r.Id == requestId, CancellationToken)).SlotId);
    }

    /// <summary>A contributor requests a spot through the claim form, as they would on the site. Returns the request's ID.</summary>
    private async Task<int> RequestAsync(string login, long githubUserId, int day, int part, string name, string? link = null)
    {
        using var contributor = await fixture.SignInAsync(login, githubUserId);
        var fields = new Dictionary<string, string>
        {
            ["Form.DisplayName"] = name,
            ["Form.AgreeToTiming"] = "true",
            ["Form.AgreeToTerms"] = "true",
        };
        if (link is not null)
        {
            fields["Form.Link"] = link;
        }

        var response = await contributor.PostFormAsync($"/claim/2102/{day}/{part}", "claim", fields);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        await using var db = await fixture.CreateDbContextAsync();
        return await db.ClaimRequests
            .Where(r => r.Contributor.GithubUserId == githubUserId && r.Slot.SeasonYear == 2102 && r.Slot.Day == day && r.Slot.Part == part)
            .Select(r => r.Id)
            .SingleAsync(CancellationToken);
    }

    private async Task<int> SlotIdAsync(int year, int day, int part)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.Slots.Where(s => s.SeasonYear == year && s.Day == day && s.Part == part).Select(s => s.Id).SingleAsync(CancellationToken);
    }

    private async Task<DateTimeOffset?> ClaimsOpenAtAsync(int year)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.Seasons.Where(s => s.Year == year).Select(s => s.ClaimsOpenAt).SingleAsync(CancellationToken);
    }

    /// <summary>An antiforgery token from a page that no longer shows the form being posted.</summary>
    private static async Task<string> TokenAsync(HttpClient browser, string path)
    {
        var page = await browser.GetStringAsync(path, CancellationToken);
        return System.Text.RegularExpressions.Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"(?<value>[^\"]+)\"").Groups["value"].Value;
    }
}
