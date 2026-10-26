using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Organizers write, publish and share a season's retrospective, and authors see how their posts did. Uses the 2018 archive
/// (its visits and clicks are only ever added here), and the backfill writes and publishes every finished season's.
/// </summary>
public sealed partial class RetrospectiveTests(AppHostFixture fixture)
{
    private const string Page = "/manage/retrospectives/2018";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OrganizersWritePublishAndShareASeasonsLookBack()
    {
        var slot = await StartWithClicksAsync();
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        using var reader = fixture.CreateWebClient();

        Assert.Contains("Generate the draft", await organizer.GetPageAsync(Page));
        var generated = await organizer.PostFormAsync(Page, "generate", new());
        Assert.Equal($"{Page}?done=generated", TestBrowser.PathOf(generated.Headers.Location!));

        var draft = WebUtility.HtmlDecode(await organizer.GetPageAsync(Page));
        Assert.Contains("C# Advent 2018: a look back", draft);
        Assert.Contains("Readers clicked through to the posts **4 times** in December, and opened their pages on our site 1 time.", draft);
        Assert.Contains($"(2018/day/{slot.Day}/{slot.Part}?via=retrospective) by", draft);
        Assert.Contains("Clicks each day in December", draft);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync("/archive/2018/retrospective", CancellationToken)).StatusCode);
        Assert.DoesNotContain("archive/2018/retrospective", await reader.GetStringAsync("/archive/2018", CancellationToken));

        // A stale edit is refused; a fresh one is saved.
        var stale = await organizer.PostFormAsync(Page, "retrospective", new()
        {
            ["Form.Title"] = "Stale",
            ["Form.Body"] = "Stale",
            ["Form.Version"] = "1",
        });
        Assert.Contains("Another organizer saved it since you opened it", await stale.Content.ReadAsStringAsync(CancellationToken));

        var saved = await organizer.PostFormAsync(Page, "retrospective", new()
        {
            ["Form.Title"] = "C# Advent 2018, looking back",
            ["Form.Body"] = "Thank you, everyone!\n\n## Our favorite posts\n\nAll of them.",
            ["Form.ShowCharts"] = "true",
        });
        Assert.Equal($"{Page}?done=saved", TestBrowser.PathOf(saved.Headers.Location!));

        var published = await organizer.PostFormAsync(Page, "publication", new());
        Assert.Equal($"{Page}?done=published", TestBrowser.PathOf(published.Headers.Location!));

        var page = await reader.GetPageAsync("/archive/2018/retrospective");
        Assert.Contains(">C# Advent 2018, looking back</h1>", page);
        Assert.Contains("<h2>Our favorite posts</h2>", page);
        Assert.Contains("By the numbers", page);
        Assert.Contains("<meta property=\"og:title\" content=\"C# Advent 2018, looking back\"", page);
        Assert.Contains("href=\"archive/2018/retrospective\"", await reader.GetStringAsync("/archive/2018", CancellationToken));
        Assert.Contains("<loc>https://localhost:7092/archive/2018/retrospective</loc>", await reader.GetStringAsync("/sitemap.xml", CancellationToken));

        foreach (var feed in new[] { "/rss", "/archive/2018/rss" })
        {
            var items = XDocument.Parse(await reader.GetStringAsync(feed, CancellationToken)).Root!.Element("channel")!.Elements("item");
            var item = Assert.Single(items, i => i.Element("guid")!.Value == "csadvent-2018-retrospective");
            Assert.Equal("https://localhost:7092/archive/2018/retrospective?via=rss", item.Element("link")!.Value);
        }

        // Shared once per platform, from its own page.
        Assert.Contains("Share it", await organizer.GetPageAsync(Page));
        var marked = await PostSeasonFormAsync(organizer, "mark", SocialPlatform.Reddit);
        Assert.Equal(Page, TestBrowser.PathOf(marked.Headers.Location!));
        await PostSeasonFormAsync(organizer, "mark", SocialPlatform.Reddit);
        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.Equal(1, await db.SeasonSocialPosts.CountAsync(p => p.SeasonYear == 2018 && p.Kind == SeasonPostKind.Retrospective && p.Platform == SocialPlatform.Reddit, CancellationToken));
        }

        var unpublished = await organizer.PostFormAsync(Page, "publication", new());
        Assert.Equal($"{Page}?done=unpublished", TestBrowser.PathOf(unpublished.Headers.Location!));
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync("/archive/2018/retrospective", CancellationToken)).StatusCode);
        Assert.DoesNotContain("csadvent-2018-retrospective", await reader.GetStringAsync("/archive/2018/rss", CancellationToken));
    }

    [Fact]
    public async Task AuthorsSeeHowTheirOwnPostsDid()
    {
        var slot = await StartWithClicksAsync();
        using var author = await fixture.SignInAsync("retro-reader", 8501);
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var contributorId = await db.Contributors.Where(c => c.GithubUserId == 8501).Select(c => c.Id).SingleAsync(CancellationToken);
            await db.Slots.Where(s => s.Id == slot.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ContributorId, contributorId), CancellationToken);
        }

        var mySpots = await author.GetPageAsync("/my-spots");

        // Clicks, and visits to the post's page from before readers went straight to the post.
        Assert.Contains("4 clicks through to your post, and 1 visit to its page here until October 2026", mySpots);
        Assert.Contains("Only you see these for your posts", mySpots);
    }

    [Fact]
    public async Task TheBackfillWritesAndPublishesEveryFinishedSeason()
    {
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        using var reader = fixture.CreateWebClient();
        const string List = "/manage/retrospectives";

        if ((await organizer.GetPageAsync(List)).Contains("No retrospective yet", StringComparison.Ordinal))
        {
            var generated = await organizer.PostFormAsync(List, "backfill", new());
            Assert.StartsWith($"{List}?done=generated&count=", TestBrowser.PathOf(generated.Headers.Location!));
        }

        Assert.DoesNotContain("No retrospective yet", await organizer.GetPageAsync(List));
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var years = await db.Retrospectives.Select(r => r.SeasonYear).ToListAsync(CancellationToken);
            Assert.All(Enumerable.Range(2017, 9), year => Assert.Contains(year, years));
            Assert.DoesNotContain(years, year => year > 2089);
        }

        var unconfirmed = await organizer.PostFormAsync(List, "publish-all", new());
        Assert.Contains("Tick the box to publish them all.", await unconfirmed.Content.ReadAsStringAsync(CancellationToken));

        var published = await organizer.PostFormAsync(List, "publish-all", new() { ["PublishAll.Confirm"] = "true" });
        Assert.StartsWith($"{List}?done=published&count=", TestBrowser.PathOf(published.Headers.Location!));

        // The first season has nothing earlier to compare with.
        var first = await reader.GetPageAsync("/archive/2017/retrospective");
        Assert.Contains("This was the very first C# Advent.", first);
        Assert.Contains("Visits and clicks were first counted in 2026", first);
        Assert.Matches(PostsByAuthors(), await reader.GetPageAsync("/archive/2025/retrospective"));
    }

    /// <summary>
    /// Starts 2018 over without a retrospective, and gives one of its posts 4 clicks and a visit in December 2018. Returns
    /// that post. It's never the 2018 author <see cref="AuthorHistoryTests"/> links.
    /// </summary>
    private async Task<Slot> StartWithClicksAsync()
    {
        await using var db = await fixture.CreateDbContextAsync();
        await db.Retrospectives.Where(r => r.SeasonYear == 2018).ExecuteDeleteAsync(CancellationToken);

        var firstAuthor = await db.Slots.Where(s => s.SeasonYear == 2018 && s.PostUrl != null && s.AuthorName != null).OrderBy(s => s.Day).Select(s => s.AuthorName!).FirstAsync(CancellationToken);
        var slot = await db.Slots
            .Where(s => s.SeasonYear == 2018 && s.PostUrl != null && s.PostTitle != null && s.AuthorName != null && s.AuthorName != firstAuthor)
            .OrderByDescending(s => s.Day).ThenByDescending(s => s.Part)
            .FirstAsync(CancellationToken);

        if (!await db.EntryHits.AnyAsync(h => h.Slot.SeasonYear == 2018, CancellationToken))
        {
            var at = new DateTimeOffset(2018, 12, 5, 15, 0, 0, TimeSpan.Zero);
            db.EntryHits.AddRange(
                new EntryHit { SlotId = slot.Id, Kind = EntryHitKind.Click, Via = "bluesky", At = at },
                new EntryHit { SlotId = slot.Id, Kind = EntryHitKind.Click, Via = "bluesky", At = at.AddHours(1) },
                new EntryHit { SlotId = slot.Id, Kind = EntryHitKind.Click, Via = "calendar", At = at.AddDays(1) },
                new EntryHit { SlotId = slot.Id, Kind = EntryHitKind.Click, Via = null, At = at.AddDays(20) },
                new EntryHit { SlotId = slot.Id, Kind = EntryHitKind.View, Via = "bluesky", At = at });
            await db.SaveChangesAsync(CancellationToken);
        }

        return slot;
    }

    private async Task<HttpResponseMessage> PostSeasonFormAsync(HttpClient organizer, string action, SocialPlatform platform)
    {
        var html = await organizer.GetStringAsync(Page, CancellationToken);
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(AntiforgeryToken().Match(html).Groups["token"].Value),
            ["Year"] = "2018",
            ["Kind"] = nameof(SeasonPostKind.Retrospective),
            ["Platform"] = platform.ToString(),
        };
        return await organizer.PostAsync($"/manage/social/signups/{action}", new FormUrlEncodedContent(fields), CancellationToken);
    }

    [GeneratedRegex("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    [GeneratedRegex(@"<meta property=""og:description"" content=""A look back at C# Advent 2025: [0-9]+ posts by [0-9]+ authors\.""")]
    private static partial Regex PostsByAuthors();
}
