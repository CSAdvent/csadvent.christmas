using System.Globalization;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// The dead-link check (#201), on the stand-in pages (<c>*.links.csadvent.test</c>, never fetched) and the stand-in archive, so
/// nothing reaches authors' sites or the Internet Archive. Runs go through the development endpoint at chosen times after each
/// season is over (the end of December 25 is 05:00 UTC on the 26th), because the AppHost turns the worker off in Testing.
/// Seasons 2680-2689.
/// </summary>
public sealed partial class DeadLinkTests(AppHostFixture fixture)
{
    private static readonly DateTimeOffset January5 = new(2681, 1, 5, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADeadPostIsFoundSlowlyAndReadersGetItsSavedCopy()
    {
        const int year = 2680;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: false, season =>
        {
            Post(season, 1, 1, "Ada", "https://ada.links.csadvent.test/gone-archived");
            Post(season, 1, 2, "Bob", "https://bob.links.csadvent.test/working");
            Post(season, 2, 1, "Cy", "https://cy.links.csadvent.test/gone");
            Post(season, 2, 2, "Dee", "https://dee.links.csadvent.test/refused");
        });

        // Two posts a run, the most overdue first, and nothing more until they're due again.
        Assert.Equal(new LinkRun(2, 0, 0), await RunAsync(year, January5));
        Assert.Equal(new LinkRun(2, 0, 0), await RunAsync(year, January5.AddMinutes(15)));
        Assert.Equal(new LinkRun(0, 0, 0), await RunAsync(year, January5.AddMinutes(30)));

        // The two that didn't load are checked again a day later, then two days after that, when they're dead. Only one has a
        // saved copy at the (stand-in) Wayback Machine.
        Assert.Equal(new LinkRun(2, 0, 0), await RunAsync(year, January5.AddDays(1).AddHours(1)));
        Assert.Equal(new LinkRun(0, 0, 0), await RunAsync(year, January5.AddDays(2)));
        Assert.Equal(new LinkRun(2, 2, 1), await RunAsync(year, January5.AddDays(3).AddHours(1)));

        var checks = await ChecksAsync(year);
        Assert.Equal((LinkStatus.Dead, 3, "https://web.archive.org/web/26801201120000/https://ada.links.csadvent.test/gone-archived"), (checks[(1, 1)].Status, checks[(1, 1)].Failures, checks[(1, 1)].ArchivedUrl));
        Assert.Equal("The page answered 404 Not Found.", checks[(1, 1)].Detail);
        Assert.Equal(LinkStatus.Working, checks[(1, 2)].Status);
        Assert.Equal((LinkStatus.Dead, (string?)null), (checks[(2, 1)].Status, checks[(2, 1)].ArchivedUrl));
        Assert.NotNull(checks[(2, 1)].ArchiveCheckedAt);
        Assert.Equal(LinkStatus.Refused, checks[(2, 2)].Status);

        // Readers following a dead post's link get its saved copy, and otherwise the post (on a clock in the season's December,
        // when its posts are revealed).
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var entries = new ServicesOnAClock(db, new DateTimeOffset(2680, 12, 10, 12, 0, 0, TimeSpan.Zero)).Entries();
            async Task<string> DestinationAsync(int day, int part)
            {
                var post = (await entries.FindPostAsync(year, day, part, CancellationToken))!.Value;
                return (await entries.DestinationAsync(post.SlotId, post.PostUrl, CancellationToken)).Url;
            }

            Assert.Equal(checks[(1, 1)].ArchivedUrl, await DestinationAsync(1, 1));
            Assert.Equal("https://bob.links.csadvent.test/working", await DestinationAsync(1, 2));
            Assert.Equal("https://cy.links.csadvent.test/gone", await DestinationAsync(2, 1));
            Assert.Equal("https://dee.links.csadvent.test/refused", await DestinationAsync(2, 2));
        }

        // Organizers see them on the dead links page, the season's entries and the entry's own page.
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        var page = await organizer.GetPageAsync("/manage/authors/dead-links");
        Assert.Contains("https://ada.links.csadvent.test/gone-archived", page);
        Assert.Contains("https://web.archive.org/web/26801201120000/https://ada.links.csadvent.test/gone-archived", page);
        Assert.Contains("https://dee.links.csadvent.test/refused", page);
        Assert.DoesNotContain("https://bob.links.csadvent.test/working", page);
        Assert.Contains("href=\"manage/authors/dead-links\"", await organizer.GetPageAsync("/manage/authors"));
        Assert.Equal(2, Regex.Count(await organizer.GetPageAsync($"/manage/entries?year={year}"), ">Dead link</a>"));
    }

    [Fact]
    public async Task OrganizersMarkAPostGoneSetItsCopyOrGiveItANewLink()
    {
        const int year = 2681;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: false, season => Post(season, 3, 1, "Eve", "https://eve.links.csadvent.test/gone-archived"));
        var slotId = await SlotIdAsync(year, 3, 1);
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        // "It's gone": readers get its saved copy, looked up straight away.
        var gone = await PostAsync(organizer, "dead", slotId);
        Assert.Equal($"/manage/authors/dead-links?links=dead-{slotId}#post-{slotId}", gone.Headers.Location!.OriginalString);
        var check = await CheckAsync(slotId);
        Assert.Equal((LinkStatus.Dead, "organizer", "https://web.archive.org/web/26811203120000/https://eve.links.csadvent.test/gone-archived"), (check.Status, check.DecidedBy, check.ArchivedUrl));
        Assert.Contains("Marked as gone.", await organizer.GetPageAsync($"/manage/authors/dead-links?links=dead-{slotId}"));

        // A copy of their own, which has to be a web address.
        var invalid = await PostAsync(organizer, "archive", slotId, new() { ["Url"] = "not a web address" });
        Assert.Contains($"links=invalid-{slotId}", invalid.Headers.Location!.OriginalString);
        await PostAsync(organizer, "archive", slotId, new() { ["Url"] = "https://archive.ph/eve2681" });
        Assert.Equal(("https://archive.ph/eve2681", "organizer"), ((await CheckAsync(slotId)).ArchivedUrl, (await CheckAsync(slotId)).ArchivedBy));

        // Looking it up again hands it back to the Wayback Machine.
        await PostAsync(organizer, "lookup", slotId);
        Assert.Equal(("https://web.archive.org/web/26811203120000/https://eve.links.csadvent.test/gone-archived", (string?)null), ((await CheckAsync(slotId)).ArchivedUrl, (await CheckAsync(slotId)).ArchivedBy));

        // A new link, for an author who moved the post: refused over a change made in between, then saved and checked afresh.
        var stale = await PostAsync(organizer, "replace", slotId, new() { ["Url"] = "https://eve.links.csadvent.test/working-moved", ["Version"] = "1" });
        Assert.Contains($"links=changed-{slotId}", stale.Headers.Location!.OriginalString);
        var replaced = await PostAsync(organizer, "replace", slotId, new() { ["Url"] = "https://eve.links.csadvent.test/working-moved", ["Version"] = (await VersionAsync(slotId)).ToString(CultureInfo.InvariantCulture) });
        Assert.Contains($"links=replaced-{slotId}", replaced.Headers.Location!.OriginalString);
        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.Equal("https://eve.links.csadvent.test/working-moved", await db.Slots.Where(s => s.Id == slotId).Select(s => s.PostUrl).SingleAsync(CancellationToken));
        }

        check = await CheckAsync(slotId);
        Assert.Equal((LinkStatus.Unchecked, (string?)null, (string?)null), (check.Status, check.ArchivedUrl, check.DecidedBy));
        Assert.Equal(new LinkRun(1, 0, 0), await RunAsync(year, new DateTimeOffset(2682, 1, 10, 12, 0, 0, TimeSpan.Zero)));
        Assert.Equal(LinkStatus.Working, (await CheckAsync(slotId)).Status);

        // "It works" stands: runs leave it alone, until someone checks it again.
        await PostAsync(organizer, "working", slotId);
        Assert.Equal(new LinkRun(0, 0, 0), await RunAsync(year, new DateTimeOffset(2682, 3, 10, 12, 0, 0, TimeSpan.Zero)));
        await PostAsync(organizer, "check", slotId);
        check = await CheckAsync(slotId);
        Assert.Equal((LinkStatus.Working, (string?)null), (check.Status, check.DecidedBy));

        // Only organizers.
        using var contributor = await fixture.SignInAsync("links-2681", 26811);
        var denied = await contributor.PostAsync("/manage/links/dead", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(await contributor.GetPageAsync("/")),
            ["SlotId"] = slotId.ToString(CultureInfo.InvariantCulture),
        }), CancellationToken);
        Assert.StartsWith("/access-denied", TestBrowser.PathOf(denied.Headers.Location!));
    }

    [Fact]
    public async Task EachSiteIsCheckedAtMostOnceAnHourAndANewLinkStartsAgain()
    {
        const int year = 2682;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: false, season =>
        {
            Post(season, 1, 1, "Fay", "https://same.links.csadvent.test/working-1");
            Post(season, 1, 2, "Gus", "https://same.links.csadvent.test/working-2");
        });

        // Nothing while the season is on.
        Assert.Equal(new LinkRun(0, 0, 0), await RunAsync(year, new DateTimeOffset(2682, 12, 20, 12, 0, 0, TimeSpan.Zero)));

        var start = new DateTimeOffset(2683, 1, 5, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(new LinkRun(1, 0, 0), await RunAsync(year, start));
        Assert.Equal(new LinkRun(0, 0, 0), await RunAsync(year, start.AddMinutes(15)));
        Assert.Equal(new LinkRun(1, 0, 0), await RunAsync(year, start.AddMinutes(61)));

        // A post whose link changes (in the entry editor, say) is checked again from scratch.
        var slotId = await SlotIdAsync(year, 1, 1);
        await using (var db = await fixture.CreateDbContextAsync())
        {
            await db.Slots.Where(s => s.Id == slotId).ExecuteUpdateAsync(s => s.SetProperty(x => x.PostUrl, "https://other.links.csadvent.test/gone"), CancellationToken);
        }

        Assert.Equal(new LinkRun(1, 0, 0), await RunAsync(year, start.AddHours(3)));
        var check = await CheckAsync(slotId);
        Assert.Equal(("https://other.links.csadvent.test/gone", LinkStatus.Failing, 1), (check.PostUrl, check.Status, check.Failures));
    }

    [Fact]
    public async Task TheOrganizersGetOneEmailAMorningAboutThePostsFoundDead()
    {
        const int year = 2683;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: false, season =>
        {
            Post(season, 1, 1, "Hal", "https://hal.links.csadvent.test/gone-archived-2683");
            Post(season, 1, 2, "Ivy", "https://ivy.links.csadvent.test/gone-2683");
            Post(season, 2, 1, "Jo", "https://jo.links.csadvent.test/gone-2683");
        });

        // An organizer marks one gone themselves, so they don't need telling about it.
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        await PostAsync(organizer, "dead", await SlotIdAsync(year, 2, 1));

        // The other two fail three checks, days apart, and are found dead at 7 AM Eastern on January 8.
        var start = new DateTimeOffset(2684, 1, 5, 11, 0, 0, TimeSpan.Zero);
        Assert.Equal(new LinkRun(2, 0, 0), await RunAsync(year, start));
        Assert.Equal(new LinkRun(2, 0, 0), await RunAsync(year, start.AddDays(1).AddMinutes(30)));
        var foundDead = start.AddDays(3).AddHours(1);
        Assert.Equal(new LinkRun(2, 2, 1), await RunAsync(year, foundDead));

        // Nothing until 8 AM Eastern, then one email about both, once.
        Assert.Equal(0, await EmailAsync(year, foundDead.AddMinutes(30)));
        Assert.Equal(2, await EmailAsync(year, foundDead.AddMinutes(75)));
        Assert.Equal(0, await EmailAsync(year, foundDead.AddMinutes(90)));

        var email = await fixture.WaitForEmailAsync("hello@csadvent.christmas", "2 older C# Advent posts are gone", containing: "https://ivy.links.csadvent.test/gone-2683");
        Assert.Contains("Hal's post by Hal. https://hal.links.csadvent.test/gone-archived-2683 The page answered 404 Not Found.", email.Text);
        Assert.Contains("https://web.archive.org/web/26831201120000/https://hal.links.csadvent.test/gone-archived-2683", email.Text);
        Assert.Contains("Ivy's post by Ivy. https://ivy.links.csadvent.test/gone-2683 The page answered 404 Not Found. The Wayback Machine has no copy of it", email.Text);
        Assert.DoesNotContain("jo.links.csadvent.test", email.Text);
    }

    [Fact]
    public async Task TheDeadLinksPageListsEachAuthorsPostsTogetherWithTheirAddress()
    {
        // Signing in gives Kit an account with a verified address; their posts are under two names.
        using var kitsBrowser = await fixture.SignInAsync("links-2684", 26841);
        int kit;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            kit = await db.Contributors.Where(c => c.GithubUserId == 26841).Select(c => c.Id).SingleAsync(CancellationToken);
        }

        await TestSeasons.EnsureAsync(fixture, 2684, claimsOpen: false, season =>
        {
            Post(season, 1, 1, "Kit Grouped", "https://kit.links.csadvent.test/gone-2684").ContributorId = kit;
            Post(season, 2, 1, "Abe Grouped", "https://abe.links.csadvent.test/gone-2684").Contact = new SlotContact { Email = "abe-2684@example.com" };
        });
        await TestSeasons.EnsureAsync(fixture, 2685, claimsOpen: false, season =>
            Post(season, 1, 1, "Kit L. Grouped", "https://kit.links.csadvent.test/gone-2685").ContributorId = kit);

        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        foreach (var (year, day) in new[] { (2684, 1), (2684, 2), (2685, 1) })
        {
            await PostAsync(organizer, "dead", await SlotIdAsync(year, day, 1));
        }

        // Authors by name, then each author's posts oldest first.
        var page = await organizer.GetPageAsync("/manage/authors/dead-links");
        var abe = page.IndexOf("href=\"manage/authors/unlinked?key=abe%20grouped\"", StringComparison.Ordinal);
        var kitsAuthor = page.IndexOf($"href=\"manage/authors/{kit}\"", StringComparison.Ordinal);
        Assert.True(abe >= 0 && kitsAuthor > abe, "Abe comes before Kit.");
        Assert.Equal(kitsAuthor, page.LastIndexOf($"href=\"manage/authors/{kit}\"", StringComparison.Ordinal));
        Assert.Contains(">Kit L. Grouped</a>", page[kitsAuthor..], StringComparison.Ordinal);

        int[] order =
        [
            page.IndexOf("href=\"mailto:abe-2684@example.com\"", StringComparison.Ordinal),
            page.IndexOf("https://abe.links.csadvent.test/gone-2684", StringComparison.Ordinal),
            kitsAuthor,
            page.IndexOf("href=\"mailto:links-2684@example.com\"", StringComparison.Ordinal),
            page.IndexOf("https://kit.links.csadvent.test/gone-2684", StringComparison.Ordinal),
            page.IndexOf("https://kit.links.csadvent.test/gone-2685", StringComparison.Ordinal),
        ];
        Assert.All(order, index => Assert.True(index > abe));
        Assert.Equal(order.Order(), order);
    }

    private static Slot Post(Season season, int day, int part, string author, string url)
    {
        var slot = season.Slots.Single(s => s.Day == day && s.Part == part);
        slot.AuthorName = author;
        slot.PostTitle = $"{author}'s post";
        slot.PostUrl = url;
        return slot;
    }

    private async Task<LinkRun> RunAsync(int year, DateTimeOffset at)
    {
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/links/run?year={year}&at={Uri.EscapeDataString(at.ToString("O", CultureInfo.InvariantCulture))}", null, CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LinkRun>(CancellationToken))!;
    }

    /// <summary>The organizers' email about posts found dead, as if it were <paramref name="at"/>: how many posts it listed.</summary>
    private async Task<int> EmailAsync(int year, DateTimeOffset at)
    {
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/links/email?year={year}&at={Uri.EscapeDataString(at.ToString("O", CultureInfo.InvariantCulture))}", null, CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<int>(CancellationToken);
    }

    /// <summary>A dead-link button: a plain form, with the antiforgery token from the sign-out form every page has.</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient organizer, string action, int slotId, Dictionary<string, string>? fields = null)
    {
        fields ??= [];
        fields["__RequestVerificationToken"] = Token(await organizer.GetPageAsync("/manage/authors/dead-links"));
        fields["SlotId"] = slotId.ToString(CultureInfo.InvariantCulture);
        fields["ReturnUrl"] = $"manage/authors/dead-links#post-{slotId}";
        var response = await organizer.PostAsync($"/manage/links/{action}", new FormUrlEncodedContent(fields), CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response;
    }

    private async Task<Dictionary<(int Day, int Part), LinkCheck>> ChecksAsync(int year)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.LinkChecks.AsNoTracking().Include(c => c.Slot).Where(c => c.Slot.SeasonYear == year).ToDictionaryAsync(c => (c.Slot.Day, c.Slot.Part), CancellationToken);
    }

    private async Task<LinkCheck> CheckAsync(int slotId)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.LinkChecks.AsNoTracking().SingleAsync(c => c.SlotId == slotId, CancellationToken);
    }

    private async Task<int> SlotIdAsync(int year, int day, int part)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.Slots.Where(s => s.SeasonYear == year && s.Day == day && s.Part == part).Select(s => s.Id).SingleAsync(CancellationToken);
    }

    private async Task<uint> VersionAsync(int slotId)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.Slots.Where(s => s.Id == slotId).Select(s => s.Version).SingleAsync(CancellationToken);
    }

    private static string Token(string page) => System.Net.WebUtility.HtmlDecode(AntiforgeryToken().Match(page).Groups["token"].Value);

    [GeneratedRegex("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    private sealed record LinkRun(int Checked, int FoundDead, int FoundCopies);
}
