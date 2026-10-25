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

    private static string Token(string page) => WebUtility.HtmlDecode(AntiforgeryToken().Match(page).Groups["token"].Value);

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
