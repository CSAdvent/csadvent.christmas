using System.Text.RegularExpressions;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Sign-up day: forms whose state changed before they were posted, the limits on what one person can make the site email,
/// and how organizers settle requests. Each scenario has its own season from 2310 to 2329.
/// </summary>
public sealed partial class SignUpDayTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SubmittingAfterTheSpotWasApprovedForSomeoneElseSaysItsTaken()
    {
        const int year = 2310;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true);
        using var late = await fixture.SignInAsync("sud-late", 231001);
        var form = await late.GetPageAsync($"/claim/{year}/4/1");
        Assert.Contains("Request this spot", form);

        // While they fill it in, someone else's request for the same spot is approved.
        var early = await RequestAsync(year, "sud-early", 231002, day: 4, part: 1, "Early Bird");
        using (var organizer = await SignInAsOrganizerAsync())
        {
            await organizer.PostFormAsync($"/manage/requests/{early}", "approve", new() { ["Approve.SlotId"] = $"{await SlotIdAsync(year, 4, 1)}" });
        }

        var response = await late.PostAsync($"/claim/{year}/4/1", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "claim",
            ["__RequestVerificationToken"] = Token(form),
            ["Form.DisplayName"] = "Late Comer",
            ["Form.AgreeToTiming"] = "true",
            ["Form.AgreeToTerms"] = "true",
        }), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("This spot has already been claimed.", await response.Content.ReadAsStringAsync(CancellationToken));
        await using var db = await fixture.CreateDbContextAsync();
        Assert.False(await db.ClaimRequests.AnyAsync(r => r.Contributor.GithubUserId == 231001, CancellationToken));
    }

    [Fact]
    public async Task LeavingTheBackupListTwiceDoesntShowAnError()
    {
        const int year = 2313;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true);
        using var backup = await fixture.SignInAsync("sud-double", 231301, "Dub Click");
        await JoinBackupsAsync(backup, year, "Dub Click");
        var page = await backup.GetPageAsync($"/backups/{year}");

        // A double click: both posts come from the page with the form on it.
        var fields = new Dictionary<string, string>
        {
            ["_handler"] = "leave",
            ["__RequestVerificationToken"] = Token(page),
            ["Leave.Confirm"] = "true",
        };
        var first = await backup.PostAsync($"/backups/{year}", new FormUrlEncodedContent(fields), CancellationToken);
        var second = await backup.PostAsync($"/backups/{year}", new FormUrlEncodedContent(fields), CancellationToken);

        Assert.Equal($"/backups/{year}?done=left", TestBrowser.PathOf(first.Headers.Location!));
        Assert.Equal($"/backups/{year}?done=left", TestBrowser.PathOf(second.Headers.Location!));
        await using var db = await fixture.CreateDbContextAsync();
        Assert.False(await db.Substitutes.AnyAsync(s => s.SeasonYear == year, CancellationToken));
    }

    [Fact]
    public async Task OnePersonCanOnlyHaveAFewRequestsWaiting()
    {
        const int year = 2312;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true);
        using var eager = await fixture.SignInAsync("sud-eager", 231201);

        for (var day = 1; day <= 3; day++)
        {
            Assert.Equal(HttpStatusCode.Redirect, (await PostClaimAsync(eager, year, day, 1, "Eager Beaver")).StatusCode);
        }

        var fourth = await PostClaimAsync(eager, year, 4, 1, "Eager Beaver");

        Assert.Equal(HttpStatusCode.OK, fourth.StatusCode);
        Assert.Contains("You have 3 requests waiting", await fourth.Content.ReadAsStringAsync(CancellationToken));
        Assert.Contains("You have 3 requests waiting", await eager.GetPageAsync($"/claim/{year}/5/1"));
        await using var db = await fixture.CreateDbContextAsync();
        Assert.Equal(3, await db.ClaimRequests.CountAsync(r => r.Contributor.GithubUserId == 231201, CancellationToken));
        Assert.Equal(3, await fixture.CountEmailsAsync("sud-eager@example.com", "We received your C# Advent request"));
    }

    [Fact]
    public async Task OnePersonCanOnlyMakeAFewRequestsAnHour()
    {
        const int year = 2312;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true);
        using var busy = await fixture.SignInAsync("sud-busy", 231202);

        // Five requests in the last hour that aren't waiting any more, such as ones the organizers already approved.
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var contributorId = await db.Contributors.Where(c => c.GithubUserId == 231202).Select(c => c.Id).SingleAsync(CancellationToken);
            var slots = await db.Slots.Where(s => s.SeasonYear == year && s.Part == 2 && s.Day <= 5).Select(s => s.Id).ToListAsync(CancellationToken);
            db.ClaimRequests.AddRange(slots.Select(slotId => new ClaimRequest
            {
                SlotId = slotId,
                ContributorId = contributorId,
                DisplayName = "Busy Bee",
                Email = "sud-busy@example.com",
                TermsAcceptedAt = DateTimeOffset.UtcNow,
                TimingAcceptedAt = DateTimeOffset.UtcNow,
                SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
                Status = ClaimRequestStatus.Approved,
            }));
            await db.SaveChangesAsync(CancellationToken);
        }

        var response = await PostClaimAsync(busy, year, 10, 2, "Busy Bee");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("You&#x27;ve asked for a lot of spots in the last hour.", await response.Content.ReadAsStringAsync(CancellationToken));
        await using var check = await fixture.CreateDbContextAsync();
        Assert.Equal(5, await check.ClaimRequests.CountAsync(r => r.Contributor.GithubUserId == 231202, CancellationToken));
    }

    [Fact]
    public async Task LeavingAndJoiningTheBackupListAgainOnlyEmailsOnce()
    {
        const int year = 2311;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true);
        using var backup = await fixture.SignInAsync("sud-yoyo", 231101, "Yo Yo");

        await JoinBackupsAsync(backup, year, "Yo Yo");
        await fixture.WaitForEmailAsync("sud-yoyo@example.com", $"You're on the C# Advent {year} backup list");
        await backup.PostFormAsync($"/backups/{year}", "leave", new() { ["Leave.Confirm"] = "true" });

        var rejoined = await JoinBackupsAsync(backup, year, "Yo Yo");

        Assert.Equal($"/backups/{year}?done=rejoined", TestBrowser.PathOf(rejoined.Headers.Location!));
        Assert.Contains("You&#x27;re back on the list.", await backup.GetPageAsync(TestBrowser.PathOf(rejoined.Headers.Location!)));
        Assert.Equal(1, await fixture.CountEmailsAsync("sud-yoyo@example.com", $"You're on the C# Advent {year} backup list"));
    }

    [Fact]
    public async Task ApprovingARequestSettlesTheOthersForItsSpot()
    {
        const int year = 2314;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true);
        var first = await RequestAsync(year, "sud-first", 231401, day: 24, part: 1, "First Fan");
        var flexible = await RequestAsync(year, "sud-flexible", 231402, day: 24, part: 1, "Flexi Ble", comments: "December 23 works too");
        var third = await RequestAsync(year, "sud-third", 231403, day: 24, part: 1, "Third Wheel");
        using var organizer = await SignInAsOrganizerAsync();

        await organizer.PostFormAsync($"/manage/requests/{first}", "approve", new() { ["Approve.SlotId"] = $"{await SlotIdAsync(year, 24, 1)}" });

        var page = await organizer.GetPageAsync($"/manage/requests/{first}?done=approved");
        Assert.Contains("Settle the other requests for this spot", page);
        Assert.Contains("Flexi Ble", page);
        Assert.Contains("December 23 works too", page);
        Assert.Contains("Third Wheel", page);

        // While it waits, the third contributor's My spots says the spot has gone.
        using (var waiting = await fixture.SignInAsync("sud-third", 231403))
        {
            Assert.Contains("This spot has gone to someone else.", await waiting.GetPageAsync("/my-spots"));
        }

        // One goes to the day their comments suggest, and the rest are declined together.
        var december23 = await SlotIdAsync(year, 23, 1);
        var moved = await PostPlainFormAsync(organizer, $"/manage/requests/{first}", "/manage/requests/approve", new()
        {
            ["RequestId"] = $"{flexible}",
            ["SlotId"] = $"{december23}",
            ["ReturnTo"] = $"{first}",
        });
        Assert.Equal($"/manage/requests/{first}?settled=approved", TestBrowser.PathOf(moved.Headers.Location!));

        var declined = await PostPlainFormAsync(organizer, $"/manage/requests/{first}", "/manage/requests/decline-competing", new() { ["RequestId"] = $"{first}" });
        Assert.Equal($"/manage/requests/{first}?settled=declined", TestBrowser.PathOf(declined.Headers.Location!));
        Assert.DoesNotContain("Settle the other requests for this spot", await organizer.GetPageAsync($"/manage/requests/{first}"));

        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.Equal(("Flexi Ble", ClaimRequestStatus.Approved, december23), (
                (await db.Slots.SingleAsync(s => s.Id == december23, CancellationToken)).AuthorName,
                (await db.ClaimRequests.SingleAsync(r => r.Id == flexible, CancellationToken)).Status,
                (await db.ClaimRequests.SingleAsync(r => r.Id == flexible, CancellationToken)).SlotId));
            Assert.Equal(ClaimRequestStatus.Declined, (await db.ClaimRequests.SingleAsync(r => r.Id == third, CancellationToken)).Status);
        }

        var approval = await fixture.WaitForEmailAsync("sud-flexible@example.com", "You're on the C# Advent calendar: December 23");
        Assert.Contains($"December 23, {year} (post 1) is yours, instead of December 24, {year} (post 1).", approval.Text);
        var decline = await fixture.WaitForEmailAsync("sud-third@example.com", "About your C# Advent request for December 24");
        Assert.Contains("That spot has gone to another author", decline.Text);
        Assert.Contains("See the open days", decline.Text);
    }

    [Fact]
    public async Task ApprovingNeverPicksADifferentSpotByItself()
    {
        const int year = 2315;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true);
        var winner = await RequestAsync(year, "sud-winner", 231501, day: 3, part: 1, "Win Ner");
        var runnerUp = await RequestAsync(year, "sud-runner", 231502, day: 3, part: 1, "Runner Up");
        using var organizer = await SignInAsOrganizerAsync();
        await organizer.PostFormAsync($"/manage/requests/{winner}", "approve", new() { ["Approve.SlotId"] = $"{await SlotIdAsync(year, 3, 1)}" });

        var page = await organizer.GetPageAsync($"/manage/requests/{runnerUp}");
        Assert.Contains("has gone to Win Ner", page);
        Assert.Contains("<option value=\"0\"", page);

        var response = await organizer.PostFormAsync($"/manage/requests/{runnerUp}", "approve", new() { ["Approve.SlotId"] = "0" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Choose the spot to approve them into.", await response.Content.ReadAsStringAsync(CancellationToken));
        await using var db = await fixture.CreateDbContextAsync();
        Assert.Equal(ClaimRequestStatus.Pending, (await db.ClaimRequests.SingleAsync(r => r.Id == runnerUp, CancellationToken)).Status);
    }

    [Fact]
    public async Task DecliningARequestTellsTheContributor()
    {
        const int year = 2315;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true);
        var request = await RequestAsync(year, "sud-declined", 231503, day: 9, part: 2, "Dee Clined");
        using var organizer = await SignInAsOrganizerAsync();

        var response = await organizer.PostFormAsync($"/manage/requests/{request}", "decline", []);

        Assert.Equal($"/manage/requests/{request}?done=declined", TestBrowser.PathOf(response.Headers.Location!));
        var email = await fixture.WaitForEmailAsync("sud-declined@example.com", "About your C# Advent request for December 9");
        Assert.Contains("We couldn't approve your request this time.", email.Text);
        using var contributor = await fixture.SignInAsync("sud-declined", 231503);
        Assert.DoesNotContain("Waiting for approval", await contributor.GetPageAsync("/my-spots"));
    }

    [Fact]
    public async Task ContributorsCanWithdrawTheirOwnPendingRequests()
    {
        const int year = 2316;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true);
        var request = await RequestAsync(year, "sud-withdraw", 231601, day: 6, part: 1, "With Drawer");
        using var contributor = await fixture.SignInAsync("sud-withdraw", 231601);
        using var stranger = await fixture.SignInAsync("sud-stranger", 231602);
        Assert.Contains("Withdraw", await contributor.GetPageAsync("/my-spots"));

        var refused = await PostPlainFormAsync(stranger, "/my-spots", "/my-spots/requests/withdraw", new() { ["RequestId"] = $"{request}" });
        Assert.Equal("/my-spots?done=not-withdrawn", TestBrowser.PathOf(refused.Headers.Location!));

        var withdrawn = await PostPlainFormAsync(contributor, "/my-spots", "/my-spots/requests/withdraw", new() { ["RequestId"] = $"{request}" });

        Assert.Equal("/my-spots?done=withdrawn", TestBrowser.PathOf(withdrawn.Headers.Location!));
        var page = await contributor.GetPageAsync("/my-spots?done=withdrawn");
        Assert.Contains("You&#x27;ve withdrawn your request.", page);
        Assert.DoesNotContain("Waiting for approval", page);
        await using var db = await fixture.CreateDbContextAsync();
        var withdrawnRequest = await db.ClaimRequests.SingleAsync(r => r.Id == request, CancellationToken);
        Assert.Equal(ClaimRequestStatus.Withdrawn, withdrawnRequest.Status);
        Assert.NotNull(withdrawnRequest.DecidedAt);

        // They can ask for the same spot again.
        Assert.Equal(HttpStatusCode.Redirect, (await PostClaimAsync(contributor, year, 6, 1, "With Drawer")).StatusCode);
    }

    private Task<HttpClient> SignInAsOrganizerAsync() => fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

    /// <summary>A contributor requests a spot through the claim form, as they would on the site. Returns the request's ID.</summary>
    private async Task<int> RequestAsync(int year, string login, long githubUserId, int day, int part, string name, string? comments = null)
    {
        using var contributor = await fixture.SignInAsync(login, githubUserId);
        var response = await PostClaimAsync(contributor, year, day, part, name, comments);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return await RequestIdAsync(year, githubUserId, day, part);
    }

    private static Task<HttpResponseMessage> PostClaimAsync(HttpClient contributor, int year, int day, int part, string name, string? comments = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["Form.DisplayName"] = name,
            ["Form.AgreeToTiming"] = "true",
            ["Form.AgreeToTerms"] = "true",
        };
        if (comments is not null)
        {
            fields["Form.Comments"] = comments;
        }

        return contributor.PostFormAsync($"/claim/{year}/{day}/{part}", "claim", fields);
    }

    private static async Task<HttpResponseMessage> JoinBackupsAsync(HttpClient browser, int year, string name)
    {
        var response = await browser.PostFormAsync($"/backups/{year}", "backup", new() { ["Form.Name"] = name, ["Form.AgreeToTerms"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response;
    }

    private async Task<int> RequestIdAsync(int year, long githubUserId, int day, int part)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.ClaimRequests
            .Where(r => r.Contributor.GithubUserId == githubUserId && r.Slot.SeasonYear == year && r.Slot.Day == day && r.Slot.Part == part)
            .OrderByDescending(r => r.Id)
            .Select(r => r.Id)
            .FirstAsync(CancellationToken);
    }

    private async Task<int> SlotIdAsync(int year, int day, int part)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.Slots.Where(s => s.SeasonYear == year && s.Day == day && s.Part == part).Select(s => s.Id).SingleAsync(CancellationToken);
    }

    /// <summary>Posts a plain form (one that goes to a minimal API) the way a browser does, with the antiforgery token from its page.</summary>
    private static async Task<HttpResponseMessage> PostPlainFormAsync(HttpClient browser, string page, string action, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = Token(await browser.GetPageAsync(page));
        return await browser.PostAsync(action, new FormUrlEncodedContent(fields), CancellationToken);
    }

    private static string Token(string page) => WebUtility.HtmlDecode(AntiforgeryToken().Match(page).Groups["token"].Value);

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
