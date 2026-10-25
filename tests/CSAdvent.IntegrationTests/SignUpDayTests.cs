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
