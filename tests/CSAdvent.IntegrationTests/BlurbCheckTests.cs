using System.Net.Http.Json;
using System.Text.RegularExpressions;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// The blurb check (#128), on the built-in sample posts with the dry-run stand-in for Claude, so nothing reaches Anthropic or
/// the internet. Runs are driven through the development endpoint at chosen times: posts are looked at from the day before their
/// reveal (midnight Eastern is 05:00 UTC in December), and post 1's deadline is its 10 AM Eastern announcement (15:00 UTC).
/// Each test uses its own season and day, so subjects don't collide.
/// </summary>
public sealed partial class BlurbCheckTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AMissingBlurbEmailsTheAuthorOnceAndPassesOnceItsThere()
    {
        const int year = 2131;
        var slotId = await SeasonWithPostAsync(year, day: 5, "Bea Blurb", "bea-2131@example.com", Sample("link-only", year));

        // Nothing before the day before; then it's checked, but nobody hears about it before the reveal.
        Assert.Equal(new BlurbRun(0, 0), await RunAsync(year, "2131-12-04T03:00:00Z"));
        Assert.Equal(new BlurbRun(1, 0), await RunAsync(year, "2131-12-04T12:00:00Z"));
        Assert.Equal(BlurbStatus.Missing, (await CheckOfAsync(slotId)).Status);

        // From the reveal its author is emailed, once.
        Assert.Equal(1, (await RunAsync(year, "2131-12-05T05:30:00Z")).Emailed);
        var email = await fixture.WaitForEmailAsync("bea-2131@example.com", "Please add the C# Advent blurb to your post for December 5");
        Assert.Contains("It doesn't say the post is part of the 2131 C# Advent calendar.", email.Text);
        Assert.Contains("Please add it by 10:00 AM Eastern on December 5", email.Text);
        Assert.Equal(0, (await RunAsync(year, "2131-12-05T05:45:00Z")).Emailed);

        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        Assert.Contains("Missing blurb", await organizer.GetPageAsync($"/manage/entries?year={year}"));
        Assert.Contains("Not posted automatically until its blurb check passes.", await organizer.GetPageAsync($"/manage/social?year={year}&day=5"));

        // The author adds it, and the next run sees it.
        await SetPostUrlAsync(slotId, Sample("with-blurb", year));
        await RunAsync(year, "2131-12-05T06:00:00Z");
        Assert.Equal(BlurbStatus.Passed, (await CheckOfAsync(slotId)).Status);
        Assert.DoesNotContain("Not posted automatically until its blurb check passes.", await organizer.GetPageAsync($"/manage/social?year={year}&day=5"));
    }

    [Fact]
    public async Task ABackupStandingByTakesTheSpotAtTheDeadline()
    {
        const int year = 2132;
        var slotId = await SeasonWithPostAsync(year, day: 6, "Rex Refuses", "rex-2132@example.com", Sample("without-blurb", year));
        using var backup = await fixture.SignInAsync("bk-blurb-2132", 7321, "Bo Backup");
        Assert.Equal(HttpStatusCode.Redirect, (await backup.PostFormAsync($"/backups/{year}", "backup", new() { ["Form.Name"] = "Bo Backup", ["Form.AgreeToTerms"] = "true" })).StatusCode);

        // At the reveal the author hears about it, and the first backup is asked straight away.
        await RunAsync(year, "2132-12-06T05:30:00Z");
        await fixture.WaitForEmailAsync("rex-2132@example.com", "Please add the C# Advent blurb to your post for December 6");
        var ask = await fixture.WaitForEmailAsync("bk-blurb-2132@example.com", "Can you fill in on C# Advent, December 6?");
        Assert.Contains("doesn't open with the C# Advent blurb", ask.Text);
        var offer = OfferLink().Match(ask.Text).Value;

        // Their own post has to have the blurb too.
        var refused = await backup.PostFormAsync(offer, "accept", Accept("Bo Backup", Sample("link-only", year)));
        Assert.Equal($"{offer}?error=blurb", TestBrowser.PathOf(refused.Headers.Location!));
        Assert.Contains("Your post doesn't open with the C# Advent blurb either.", await backup.GetPageAsync($"{offer}?error=blurb"));

        await backup.PostFormAsync(offer, "accept", Accept("Bo Backup", Sample("with-blurb", year)));
        Assert.Contains("If the original post still doesn&#x27;t open with the C# Advent blurb at 10:00 AM Eastern on December 6", await backup.GetPageAsync(offer));

        // The backup takes the spot at the deadline, and the new post is checked like any other.
        Assert.Equal(0, (await RunBackupsAsync(year, "2132-12-06T14:59:00Z")).Used);
        Assert.Equal(1, (await RunBackupsAsync(year, "2132-12-06T15:00:00Z")).Used);
        var replaced = await fixture.WaitForEmailAsync("rex-2132@example.com", "Your C# Advent spot went to a backup poster");
        Assert.Contains("didn't open with the C# Advent blurb", replaced.Text);
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.SingleAsync(s => s.Id == slotId, CancellationToken);
            Assert.Equal(("Bo Backup", Sample("with-blurb", year)), (slot.AuthorName, slot.PostUrl));
        }

        await RunAsync(year, "2132-12-06T15:05:00Z");
        Assert.Equal(BlurbStatus.Passed, (await CheckOfAsync(slotId)).Status);
    }

    [Fact]
    public async Task TheOrganizersDecideWhatClaudeCant()
    {
        const int year = 2133;
        var slotId = await SeasonWithPostAsync(year, day: 9, "Uma Unsure", "uma-2133@example.com", Sample("unsure", year));

        await RunAsync(year, "2133-12-09T05:30:00Z");
        var email = await fixture.WaitForEmailAsync("hello@csadvent.christmas", "C# Advent, December 9: a post's blurb needs your decision");
        Assert.Contains("Dry run: this sample asks for a person to decide.", email.Text);

        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        var entry = await organizer.GetPageAsync($"/manage/entries/{slotId}");
        Assert.Contains("Needs a decision", entry);
        Assert.Contains("Claude's answer", entry);

        var decided = await PostBlurbFormAsync(organizer, $"/manage/entries/{slotId}", "decide", slotId, new() { ["Decision"] = "Passed" });
        Assert.Equal($"/manage/entries/{slotId}?blurb=decided", TestBrowser.PathOf(decided.Headers.Location!));
        var check = await CheckOfAsync(slotId);
        Assert.Equal((BlurbStatus.Passed, "organizer"), (check.Status, check.DecidedBy));

        // An organizer's decision stands.
        await RunAsync(year, "2133-12-09T06:00:00Z");
        Assert.Equal(BlurbStatus.Passed, (await CheckOfAsync(slotId)).Status);
    }

    [Fact]
    public async Task AnOrganizerCanHandTheSpotToABackupStraightAway()
    {
        const int year = 2134;
        var slotId = await SeasonWithPostAsync(year, day: 11, "Wes Wont", "wes-2134@example.com", Sample("with-blurb", year));
        using var backup = await fixture.SignInAsync("bk-blurb-2134", 7341, "Di Backup");
        await backup.PostFormAsync($"/backups/{year}", "backup", new() { ["Form.Name"] = "Di Backup", ["Form.AgreeToTerms"] = "true" });

        // The post passes, but its author wrote to say they won't keep the blurb.
        await RunAsync(year, "2134-12-11T05:30:00Z");
        Assert.Equal(BlurbStatus.Passed, (await CheckOfAsync(slotId)).Status);
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        await PostBlurbFormAsync(organizer, $"/manage/entries/{slotId}", "backup", slotId, []);

        // The backup is asked right away, and one who accepts takes the spot without waiting for the deadline.
        var ask = await fixture.WaitForEmailAsync("bk-blurb-2134@example.com", "Can you fill in on C# Advent, December 11?");
        await backup.PostFormAsync(OfferLink().Match(ask.Text).Value, "accept", Accept("Di Backup", Sample("own-words", year)));
        await fixture.WaitForEmailAsync("bk-blurb-2134@example.com", "Your post is on the C# Advent calendar: December 11");
        await using var db = await fixture.CreateDbContextAsync();
        Assert.Equal("Di Backup", (await db.Slots.SingleAsync(s => s.Id == slotId, CancellationToken)).AuthorName);
    }

    [Fact]
    public async Task AuthorsSeeTheBlurbToPasteAndWhereTheirPostStands()
    {
        const int year = 2135;
        await SeasonWithPostAsync(year, day: 3, "Pat Paste", "blurb-pat-2135@example.com", Sample("link-only", year));
        await RunAsync(year, "2135-12-03T05:30:00Z");

        using var author = await fixture.SignInAsync("blurb-pat-2135", 7351, "Pat Paste");
        var page = await author.GetPageAsync("/my-spots");

        Assert.Contains("Open your post with the C# Advent blurb", page);
        Assert.Contains("This post is part of the [2135 C# Advent calendar](https://csadvent.christmas): two C# posts a day", page);
        Assert.Contains("C# Advent blurb: missing. It doesn&#x27;t say the post is part of the 2135 C# Advent calendar.", page);
    }

    private async Task<int> SeasonWithPostAsync(int year, int day, string author, string email, string postUrl)
    {
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true, season =>
        {
            var slot = season.Slots.Single(s => s.Day == day && s.Part == 1);
            slot.AuthorName = author;
            slot.PostTitle = $"{author}'s post";
            slot.PostUrl = postUrl;
            slot.Contact = new SlotContact { Email = email };
        });

        await using var db = await fixture.CreateDbContextAsync();
        return await db.Slots.Where(s => s.SeasonYear == year && s.Day == day && s.Part == 1).Select(s => s.Id).SingleAsync(CancellationToken);
    }

    private async Task SetPostUrlAsync(int slotId, string postUrl)
    {
        await using var db = await fixture.CreateDbContextAsync();
        await db.Slots.Where(s => s.Id == slotId).ExecuteUpdateAsync(s => s.SetProperty(x => x.PostUrl, postUrl), CancellationToken);
    }

    private async Task<BlurbCheck> CheckOfAsync(int slotId)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.BlurbChecks.AsNoTracking().SingleAsync(c => c.SlotId == slotId, CancellationToken);
    }

    private async Task<BlurbRun> RunAsync(int year, string at)
    {
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/blurbs/run?year={year}&at={Uri.EscapeDataString(at)}", null, CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BlurbRun>(CancellationToken))!;
    }

    private async Task<BackupRun> RunBackupsAsync(int year, string at)
    {
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/backups/run?year={year}&at={Uri.EscapeDataString(at)}", null, CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BackupRun>(CancellationToken))!;
    }

    /// <summary>A blurb button on an organizer page: a plain form, with the page's antiforgery token.</summary>
    private static async Task<HttpResponseMessage> PostBlurbFormAsync(HttpClient organizer, string page, string action, int slotId, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = Token(await organizer.GetPageAsync(page));
        fields["SlotId"] = slotId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        fields["ReturnUrl"] = page.TrimStart('/');
        return await organizer.PostAsync($"/manage/blurbs/{action}", new FormUrlEncodedContent(fields), CancellationToken);
    }

    private static Dictionary<string, string> Accept(string name, string postUrl) => new()
    {
        ["Form.AuthorName"] = name,
        ["Form.PostTitle"] = "A post with the blurb",
        ["Form.PostUrl"] = postUrl,
    };

    private static string Sample(string name, int year) => $"https://samples.csadvent.test/{name}?year={year}";

    private static string Token(string page) => System.Net.WebUtility.HtmlDecode(AntiforgeryToken().Match(page).Groups["token"].Value);

    [GeneratedRegex(@"/backups/offers/\d+")]
    private static partial Regex OfferLink();

    [GeneratedRegex("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    private sealed record BlurbRun(int Checked, int Emailed);

    private sealed record BackupRun(int Asked, int Used, int Withdrawn, int Exhausted);
}
