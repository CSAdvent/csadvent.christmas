using System.Globalization;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using CSAdvent.Web.Blurbs;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// When the blurb check isn't sure enough to act alone: pages that aren't the post (#148), posts not published yet (#159), a
/// backup standing by for a post the check failed (#148), and authors checking their own post before their day (#174). On the
/// sample pages with the dry-run stand-in for Claude. Midnight Eastern is 05:00 UTC in December, post 1's deadline is its
/// 10 AM Eastern announcement (15:00 UTC), and a post that isn't published yet waits until 3 hours before that (12:00 UTC).
/// Each test uses its own season (2460 on) and day, so subjects don't collide.
/// </summary>
public sealed partial class BlurbDecisionTests(AppHostFixture fixture)
{
    private const string Organizers = "hello@csadvent.christmas";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APageThatIsntThePostGoesToTheOrganizersNotToTheBackups()
    {
        const int year = 2460;
        var slotId = await SeasonWithPostAsync(year, day: 14, "Jay Script", "jay-2460@example.com", Sample("javascript-shell", year));
        using var backup = await fixture.SignInAsync("bk-decide-2460", 24601, "Bea Backup");
        await JoinBackupsAsync(backup, year, "Bea Backup");

        // A blog that needs JavaScript has no link back that we can see, but no title either, so it isn't failed: it may not be
        // published yet, so it waits.
        Assert.Equal(new BlurbRun(1, 0), await RunAsync(year, "2460-12-14T05:30:00Z"));
        var waiting = await CheckOfAsync(slotId);
        Assert.Equal((BlurbStatus.Waiting, BlurbChecker.NotThePost), (waiting.Status, waiting.Detail));

        // Three hours before its deadline an organizer decides, and nobody else hears about it.
        await RunAsync(year, "2460-12-14T12:00:00Z");
        Assert.Equal(BlurbStatus.NeedsDecision, (await CheckOfAsync(slotId)).Status);
        var email = await fixture.WaitForEmailAsync(Organizers, "C# Advent, December 14: a post's blurb needs your decision");
        Assert.Contains("doesn't seem to be the post", email.Text);
        Assert.Equal(0, await fixture.CountEmailsAsync("jay-2460@example.com", "Please add the C# Advent blurb"));
        await using var db = await fixture.CreateDbContextAsync();
        Assert.False(await db.BackupOffers.AnyAsync(o => o.SlotId == slotId, CancellationToken));
    }

    [Fact]
    public async Task APostThatIsntPublishedYetWaitsUntilAFewHoursBeforeItsDeadline()
    {
        const int year = 2461;
        var slotId = await SeasonWithPostAsync(year, day: 16, "Mo Morning", "mo-2461@example.com", Sample("no-such-page", year));

        // Many authors publish on the morning of their day, so a page that doesn't load is waited for.
        await RunAsync(year, "2461-12-16T05:30:00Z");
        await RunAsync(year, "2461-12-16T11:45:00Z");
        Assert.Equal(BlurbStatus.Waiting, (await CheckOfAsync(slotId)).Status);
        Assert.Equal(0, await fixture.CountEmailsAsync(Organizers, "C# Advent, December 16: a post couldn't be checked"));

        // From 3 hours before its deadline it couldn't be checked, and once that's lasted half an hour the organizers hear.
        await RunAsync(year, "2461-12-16T12:00:00Z");
        Assert.Equal(BlurbStatus.CouldNotCheck, (await CheckOfAsync(slotId)).Status);
        Assert.Equal(0, (await RunAsync(year, "2461-12-16T12:15:00Z")).Emailed);
        Assert.Equal(1, (await RunAsync(year, "2461-12-16T12:30:00Z")).Emailed);
        var email = await fixture.WaitForEmailAsync(Organizers, "C# Advent, December 16: a post couldn't be checked for the blurb");
        Assert.Contains("404 Not Found", email.Text);
    }

    [Fact]
    public async Task TheOrganizersCanKeepTheAuthorBeforeABackupTakesTheSpot()
    {
        const int year = 2462;
        var slotId = await SeasonWithPostAsync(year, day: 15, "Kim Kept", "kim-2462@example.com", Sample("without-blurb", year));
        using var backup = await fixture.SignInAsync("bk-decide-2462", 24621, "Cy Backup");
        await JoinBackupsAsync(backup, year, "Cy Backup");

        await RunAsync(year, "2462-12-15T05:30:00Z");
        var offer = OfferLink().Match((await fixture.WaitForEmailAsync("bk-decide-2462@example.com", "Can you fill in on C# Advent, December 15?")).Text).Value;
        await backup.PostFormAsync(offer, "accept", Accept("Cy Backup", Sample("with-blurb", year)));

        // Once the backup is standing by, the organizers hear, with the time it takes the spot.
        await RunAsync(year, "2462-12-15T06:00:00Z");
        var standingBy = await fixture.WaitForEmailAsync(Organizers, "C# Advent, December 15: a backup is standing by for a post without the blurb");
        Assert.Contains("They take the spot at 10:00 AM Eastern on December 15", standingBy.Text);
        Assert.Contains($"/manage/entries/{slotId}#blurb", standingBy.Text);

        // The blurb is there after all (say, in a box the check doesn't read): keeping the author thanks the backup.
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        Assert.Contains("Cy Backup is standing by", await organizer.GetPageAsync($"/manage/entries/{slotId}"));
        await PostBlurbFormAsync(organizer, $"/manage/entries/{slotId}", "decide", slotId, new() { ["Decision"] = "Passed" });
        await fixture.WaitForEmailAsync("bk-decide-2462@example.com", "You're off the hook for C# Advent, December 15");

        Assert.Equal(0, (await RunBackupsAsync(year, "2462-12-15T15:00:00Z")).Used);
        await using var db = await fixture.CreateDbContextAsync();
        Assert.Equal("Kim Kept", (await db.Slots.SingleAsync(s => s.Id == slotId, CancellationToken)).AuthorName);
        Assert.Equal(BackupOfferStatus.Withdrawn, (await db.BackupOffers.SingleAsync(o => o.SlotId == slotId, CancellationToken)).Status);
    }

    [Fact]
    public async Task ABackupWhoAcceptsLateWaitsAnHourAfterTheOrganizersHear()
    {
        const int year = 2463;
        var slotId = await SeasonWithPostAsync(year, day: 17, "Lou Late", "lou-2463@example.com", Sample("without-blurb", year));
        using var backup = await fixture.SignInAsync("bk-decide-2463", 24631, "Di Backup");
        await JoinBackupsAsync(backup, year, "Di Backup");

        await RunAsync(year, "2463-12-17T05:30:00Z");
        var offer = OfferLink().Match((await fixture.WaitForEmailAsync("bk-decide-2463@example.com", "Can you fill in on C# Advent, December 17?")).Text).Value;
        await backup.PostFormAsync(offer, "accept", Accept("Di Backup", Sample("with-blurb", year)));

        // Nothing has told the organizers yet, so at the deadline the backup waits.
        Assert.Equal(0, (await RunBackupsAsync(year, "2463-12-17T15:00:00Z")).Used);

        // They hear at 10:30 AM Eastern, and the backup takes the spot an hour later.
        await RunAsync(year, "2463-12-17T15:30:00Z");
        var standingBy = await fixture.WaitForEmailAsync(Organizers, "C# Advent, December 17: a backup is standing by for a post without the blurb");
        Assert.Contains("They take the spot at 11:30 AM Eastern on December 17", standingBy.Text);
        Assert.Equal(0, (await RunBackupsAsync(year, "2463-12-17T16:29:00Z")).Used);
        Assert.Equal(1, (await RunBackupsAsync(year, "2463-12-17T16:30:00Z")).Used);
        await using var db = await fixture.CreateDbContextAsync();
        Assert.Equal("Di Backup", (await db.Slots.SingleAsync(s => s.Id == slotId, CancellationToken)).AuthorName);
    }

    [Fact]
    public async Task AuthorsCheckTheirPostBeforeTheirDayAndNobodyElseHears()
    {
        const int year = 2464;
        var slotId = await SeasonWithPostAsync(year, day: 20, "Ann Asks", "blurb-ann-2464@example.com", Sample("link-only", year));
        using var author = await fixture.SignInAsync("blurb-ann-2464", 24641, "Ann Asks");

        var page = await author.GetPageAsync("/my-spots");
        Assert.Contains("C# Advent blurb: not checked yet.", page);
        Assert.Contains(">Check my post</button>", page);

        var response = await PostCheckAsync(author, "/my-spots", slotId);
        Assert.Equal($"/my-spots?blurb=checked&slot={slotId}#blurb-{slotId}", TestBrowser.PathOf(response.Headers.Location!));
        page = await author.GetPageAsync($"/my-spots?blurb=checked&slot={slotId}");
        Assert.Contains("We&#x27;ve just looked at your post.", page);
        Assert.Contains("C# Advent blurb: missing. It doesn&#x27;t say the post is part of the 2464 C# Advent calendar.", page);
        Assert.Contains("Until your day starts, this is only for you.", page);

        // Before the reveal it's only information: no email, no backups.
        await using var db = await fixture.CreateDbContextAsync();
        Assert.Equal(BlurbStatus.Missing, (await db.BlurbChecks.SingleAsync(c => c.SlotId == slotId, CancellationToken)).Status);
        Assert.True((await db.BlurbSelfChecks.SingleAsync(c => c.SlotId == slotId, CancellationToken)).AskedClaude);
        Assert.False(await db.BackupOffers.AnyAsync(o => o.SlotId == slotId, CancellationToken));
        Assert.Equal(0, await fixture.CountEmailsAsync("blurb-ann-2464@example.com", "Please add the C# Advent blurb"));

        // Someone else's spot is a 404.
        using var stranger = await fixture.SignInAsync("blurb-stranger-2464", 24642);
        Assert.Equal(HttpStatusCode.NotFound, (await PostCheckAsync(stranger, "/my-spots", slotId)).StatusCode);
    }

    [Fact]
    public async Task SavingANewLinkChecksItStraightAway()
    {
        const int year = 2465;
        var slotId = await SeasonWithPostAsync(year, day: 21, "Sal Saves", "blurb-sal-2465@example.com", Sample("link-only", year));
        using var author = await fixture.SignInAsync("blurb-sal-2465", 24651, "Sal Saves");

        var saved = await author.PostFormAsync($"/my-spots/{slotId}", "spot", new()
        {
            ["Form.AuthorName"] = "Sal Saves",
            ["Form.PostTitle"] = SamplePosts.Title,
            ["Form.PostUrl"] = Sample("with-blurb", year),
        });

        Assert.Equal($"/my-spots/{slotId}?done=saved&blurb=checked&slot={slotId}", TestBrowser.PathOf(saved.Headers.Location!));
        Assert.Contains("C# Advent blurb: found. Thank you!", await author.GetPageAsync($"/my-spots/{slotId}?done=saved&blurb=checked&slot={slotId}"));
    }

    [Fact]
    public async Task EachAuthorCanCheckTenTimesAnHour()
    {
        const int year = 2466;
        var slotId = await SeasonWithPostAsync(year, day: 22, "Tess Tries", "blurb-tess-2466@example.com", Sample("no-such-page", year));
        using var author = await fixture.SignInAsync("blurb-tess-2466", 24661, "Tess Tries");

        for (var check = 0; check < BlurbSelfCheckService.MaxPerHour; check++)
        {
            var checkedNow = await PostCheckAsync(author, $"/my-spots/{slotId}", slotId);
            Assert.Equal($"/my-spots/{slotId}?blurb=checked&slot={slotId}#blurb-{slotId}", TestBrowser.PathOf(checkedNow.Headers.Location!));
        }

        var tooMany = await PostCheckAsync(author, $"/my-spots/{slotId}", slotId);
        Assert.Equal($"/my-spots/{slotId}?blurb=too-many&slot={slotId}#blurb-{slotId}", TestBrowser.PathOf(tooMany.Headers.Location!));
        var page = await author.GetPageAsync($"/my-spots/{slotId}?blurb=too-many&slot={slotId}");
        Assert.Contains("You&#x27;ve checked 10 times in the last hour", page);
        Assert.Contains("we couldn&#x27;t read your post yet. The post&#x27;s page answered 404 Not Found.", page);
    }

    private async Task<int> SeasonWithPostAsync(int year, int day, string author, string email, string postUrl)
    {
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true, season =>
        {
            var slot = season.Slots.Single(s => s.Day == day && s.Part == 1);
            slot.AuthorName = author;
            slot.PostTitle = SamplePosts.Title;
            slot.PostUrl = postUrl;
            slot.Contact = new SlotContact { Email = email };
        });

        await using var db = await fixture.CreateDbContextAsync();
        return await db.Slots.Where(s => s.SeasonYear == year && s.Day == day && s.Part == 1).Select(s => s.Id).SingleAsync(CancellationToken);
    }

    private static async Task JoinBackupsAsync(HttpClient backup, int year, string name) =>
        Assert.Equal(HttpStatusCode.Redirect, (await backup.PostFormAsync($"/backups/{year}", "backup", new() { ["Form.Name"] = name, ["Form.AgreeToTerms"] = "true" })).StatusCode);

    private async Task<BlurbCheck> CheckOfAsync(int slotId)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.BlurbChecks.AsNoTracking().SingleAsync(c => c.SlotId == slotId, CancellationToken);
    }

    private async Task<BlurbRun> RunAsync(int year, string at) => (await RunAsync<BlurbRun>("blurbs", year, at))!;

    private async Task<BackupRun> RunBackupsAsync(int year, string at) => (await RunAsync<BackupRun>("backups", year, at))!;

    private async Task<T?> RunAsync<T>(string what, int year, string at)
    {
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/{what}/run?year={year}&at={Uri.EscapeDataString(at)}", null, CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(CancellationToken);
    }

    /// <summary>"Check my post" on a My spots page: a plain form, with the page's antiforgery token.</summary>
    private static async Task<HttpResponseMessage> PostCheckAsync(HttpClient author, string page, int slotId)
    {
        var html = await author.GetStringAsync(page, CancellationToken);
        return await author.PostAsync("/my-spots/blurb-check", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(html),
            ["SlotId"] = slotId.ToString(CultureInfo.InvariantCulture),
            ["ReturnUrl"] = page.TrimStart('/'),
        }), CancellationToken);
    }

    /// <summary>A blurb button on an organizer page: a plain form, with the page's antiforgery token.</summary>
    private static async Task<HttpResponseMessage> PostBlurbFormAsync(HttpClient organizer, string page, string action, int slotId, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = Token(await organizer.GetPageAsync(page));
        fields["SlotId"] = slotId.ToString(CultureInfo.InvariantCulture);
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

    private sealed record BackupRun(int Asked, int Used, int Withdrawn, int Exhausted);
}
