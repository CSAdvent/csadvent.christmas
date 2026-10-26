using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Readers go straight to the post (decided 2026-10-26): a revealed entry page's link sends people and crawlers to the author's
/// post, counted once as a click, while link previews and organizers get the page. Reads the seeded 2025 posts of December 16
/// and 17 (no other test reads or counts those), never following a redirect to them, and season 2580, which isn't revealed.
/// </summary>
public sealed class EntryRedirectTests(AppHostFixture fixture)
{
    private const int Year = 2580;
    private const string Browser = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0 Safari/537.36";
    private const string Googlebot = "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APersonFollowingAnEntryLinkGoesStraightToThePostCountedOnce()
    {
        var slot = await FindPostAsync(16, 1);
        using var person = CreateBrowser(Browser);

        var response = await person.GetAsync("/2025/day/16/1?via=bluesky", CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(slot.PostUrl, response.Headers.Location!.OriginalString);
        // The post's link can change, so nobody keeps the redirect, and search engines never list it.
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.True(response.Headers.CacheControl.Private);
        Assert.Equal("noindex", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));

        // Our own pages link it without a tag (author pages, search, previous and next), and an unknown tag is "other".
        Assert.Equal(HttpStatusCode.Redirect, (await person.GetAsync("/2025/day/16/1", CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await person.GetAsync("/2025/day/16/1?via=myspace", CancellationToken)).StatusCode);

        // Blazor's enhanced navigation, from a page still open since before #185, fetches the link before loading it (#150).
        using var enhanced = new HttpRequestMessage(HttpMethod.Get, "/2025/day/16/1?via=bluesky");
        enhanced.Headers.TryAddWithoutValidation("Accept", "text/html; blazor-enhanced-nav=on");
        Assert.Equal(HttpStatusCode.Redirect, (await person.SendAsync(enhanced, CancellationToken)).StatusCode);

        Assert.Equal([(EntryHitKind.Click, "bluesky"), (EntryHitKind.Click, null), (EntryHitKind.Click, "other")], await HitsAsync(slot.Id));
    }

    [Theory]
    [InlineData("facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)", "facebook")]
    [InlineData("http.rb/5.2.0 (Mastodon/4.3.0; +https://mastodon.social/)", "mastodon")]
    [InlineData("Mozilla/5.0 (compatible; Bluesky Cardyb/1.1; +mailto:support@bsky.app)", "bluesky")]
    public async Task LinkPreviewsGetThePageWithOurCardAndAreNotCounted(string userAgent, string via)
    {
        var slot = await FindPostAsync(16, 2);
        using var preview = CreateBrowser(userAgent);

        var page = await preview.GetPageAsync($"/2025/day/16/2?via={via}");

        var url = "https://localhost:7092/2025/day/16/2";
        Assert.Contains($"<link rel=\"canonical\" href=\"{url}\"", page);
        Assert.Contains($"<meta property=\"og:url\" content=\"{url}\"", page);
        Assert.Contains($"<meta property=\"og:title\" content=\"{Encode(slot.PostTitle!)}\"", page);
        Assert.Contains("<meta property=\"og:image\" content=\"https://localhost:7092/cards/2025/16/2.jpg?v=", page);
        Assert.Contains("<meta name=\"twitter:card\" content=\"summary_large_image\"", page);
        Assert.Contains("<meta property=\"article:published_time\" content=\"2025-12-16T00:00:00-05:00\"", page);
        Assert.DoesNotContain("application/ld+json", page);
        Assert.Contains($"href=\"go/2025/16/2?via={via}\"", page);
        Assert.Empty(await HitsAsync(slot.Id));
    }

    [Fact]
    public async Task SearchEnginesAndOtherBotsAreSentToThePostUncounted()
    {
        var slot = await FindPostAsync(17, 1);

        foreach (var agent in new[] { Googlebot, "SomeNewBot/0.1 (+https://example.com/bot)", "curl/8.9.1" })
        {
            using var bot = CreateBrowser(agent);
            var response = await bot.GetAsync("/2025/day/17/1?via=rss", CancellationToken);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(slot.PostUrl, response.Headers.Location!.OriginalString);
            Assert.Equal("noindex", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        }

        Assert.Empty(await HitsAsync(slot.Id));
    }

    [Fact]
    public async Task OrganizersGetThePageUncounted()
    {
        var slot = await FindPostAsync(17, 2);
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        organizer.DefaultRequestHeaders.UserAgent.ParseAdd(Browser);

        var page = await organizer.GetPageAsync("/2025/day/17/2?via=x");

        Assert.Contains($">{Encode(slot.PostTitle!)}</h1>", page);
        Assert.Contains("href=\"go/2025/17/2?via=x\"", page);
        Assert.Empty(await HitsAsync(slot.Id));
    }

    [Fact]
    public async Task BeforeTheRevealItsTheComingPageForEveryoneAndNothingIsCounted()
    {
        await TestSeasons.EnsureAsync(fixture, Year, claimsOpen: false, season =>
        {
            var slot = season.Slots.Single(s => s is { Day: 4, Part: 1 });
            slot.AuthorName = "Rita Redirect";
            slot.PostTitle = "A secret 2580 title";
            slot.PostUrl = "https://example.com/secret-2580";
        });
        int slotId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            slotId = await db.Slots.Where(s => s.SeasonYear == Year && s.Day == 4 && s.Part == 1).Select(s => s.Id).SingleAsync(CancellationToken);
        }

        foreach (var agent in new[] { Browser, Googlebot, AppHostFixture.LinkPreviewAgent })
        {
            using var visitor = CreateBrowser(agent);
            var page = await visitor.GetPageAsync($"/{Year}/day/4/1?via=bluesky");

            Assert.Contains("Coming on December 4", page);
            Assert.DoesNotContain("secret 2580", page);
            Assert.DoesNotContain("secret-2580", page);
            Assert.DoesNotContain("Rita Redirect", page);
        }

        Assert.Empty(await HitsAsync(slotId));
    }

    [Fact]
    public async Task TheTestSeasonIsNotFoundForAnyoneWhoCantSeeIt()
    {
        // Whether or not the test season exists right now (SeasonTestingTests makes one), it doesn't for these visitors.
        foreach (var agent in new[] { Browser, Googlebot, AppHostFixture.LinkPreviewAgent })
        {
            using var visitor = CreateBrowser(agent);

            Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync($"/{Season.TestYear}/day/3/1", CancellationToken)).StatusCode);
        }
    }

    [Fact]
    public async Task TheSitemapListsNoEntryPagesAndTheCalendarLinksNone()
    {
        using var browser = CreateBrowser(Browser);

        var sitemap = await browser.GetStringAsync("/sitemap.xml", CancellationToken);
        Assert.Contains("<loc>https://localhost:7092/archive/2025</loc>", sitemap);
        Assert.DoesNotContain("/day/", sitemap);

        var archive = await browser.GetPageAsync("/archive/2025");
        Assert.Contains("href=\"go/2025/16/1?via=calendar\"", archive);
        Assert.DoesNotContain("href=\"2025/day/", archive);
    }

    private HttpClient CreateBrowser(string userAgent)
    {
        var browser = fixture.CreateBrowser();
        browser.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
        return browser;
    }

    private async Task<Slot> FindPostAsync(int day, int part)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.Slots.AsNoTracking()
            .SingleAsync(s => s.SeasonYear == 2025 && s.Day == day && s.Part == part && s.PostUrl != null && s.PostTitle != null, CancellationToken);
    }

    private async Task<List<(EntryHitKind Kind, string? Via)>> HitsAsync(int slotId)
    {
        await using var db = await fixture.CreateDbContextAsync();
        var hits = await db.EntryHits.Where(h => h.SlotId == slotId).OrderBy(h => h.Id).ToListAsync(CancellationToken);
        return [.. hits.Select(h => (h.Kind, h.Via))];
    }

    private static string Encode(string text) => System.Text.Encodings.Web.HtmlEncoder.Default.Encode(text);
}
