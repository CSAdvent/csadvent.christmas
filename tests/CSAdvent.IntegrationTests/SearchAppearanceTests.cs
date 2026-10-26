using System.Text.Json.Nodes;
using AngleSharp.Html.Parser;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// What search engines see: the calendar's links, which go through the tracked redirects to the authors' posts, titles and
/// structured data (#195), and nothing about a post before its reveal (#5). Crawlers following an entry page's link go
/// straight to the post like readers (decided 2026-10-26). Reads the seeded 2023 archive (from December 15 on, which no other
/// test changes) and uses season 2440, which isn't revealed.
/// </summary>
public sealed class SearchAppearanceTests(AppHostFixture fixture)
{
    private const string Site = "https://localhost:7092";
    private const string Googlebot = "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CrawlersFollowTheArchivesLinksAndEntryPagesToThePosts()
    {
        var slot = await FindPostAsync();
        using var browser = fixture.CreateBrowser();
        browser.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", Googlebot);

        // The title goes through /go, and the label isn't a link: the entry's page would lead to the post too.
        var archive = await browser.GetPageAsync("/archive/2023");
        Assert.Contains($"href=\"go/2023/{slot.Day}/{slot.Part}?via=calendar\"", archive);
        Assert.DoesNotContain($"href=\"2023/day/{slot.Day}/{slot.Part}\"", archive);

        // Crawlers follow /go and entry pages to the authors' posts, but the redirects themselves are never listed.
        var robots = await browser.GetStringAsync("/robots.txt", CancellationToken);
        Assert.DoesNotContain("/go", robots);
        Assert.DoesNotContain("/day", robots);
        foreach (var path in new[] { $"/go/2023/{slot.Day}/{slot.Part}", $"/2023/day/{slot.Day}/{slot.Part}" })
        {
            var response = await browser.GetAsync(path, CancellationToken);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(slot.PostUrl, response.Headers.Location!.OriginalString);
            Assert.Equal("noindex", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        }
    }

    [Fact]
    public async Task TheHomePageDescribesTheSiteAndItsSeason()
    {
        // A season that isn't over, so the home page is about a season whichever test runs first.
        await EnsureSeasonAsync();
        using var browser = fixture.CreateBrowser();

        var home = await browser.GetPageAsync("/");

        Assert.Equal("C# Advent: two C# posts a day, December 1–25", (await new HtmlParser().ParseDocumentAsync(home, CancellationToken)).Title);
        var site = Assert.Single(await JsonLdAsync(home), d => (string?)d["@type"] == "WebSite");
        Assert.Equal($"{Site}/", (string?)site["url"]);
        Assert.Contains("https://bsky.app/profile/csadvent.christmas", site["publisher"]!["sameAs"]!.AsArray().Select(u => (string?)u));
        // The season on the calendar (one of the tests' far-future ones), or between seasons the next one, as an event.
        var season = Assert.Single(await JsonLdAsync(home), d => (string?)d["@type"] == "Event");
        Assert.StartsWith("C# Advent 2", (string?)season["name"]);
    }

    [Fact]
    public async Task ASeasonIsAnEventAndAnEntryPageHasOnlyItsPreviewTags()
    {
        var slot = await FindPostAsync();
        using var browser = fixture.CreateBrowser();

        var season = Assert.Single(await JsonLdAsync(await browser.GetPageAsync("/archive/2023")));
        Assert.Equal("Event", (string?)season["@type"]);
        Assert.Equal("2023-12-01T00:00:00-05:00", (string?)season["startDate"]);
        Assert.Equal("2023-12-26T00:00:00-05:00", (string?)season["endDate"]);
        Assert.Equal($"{Site}/archive/2023", (string?)season["url"]);

        // Only link previews (and organizers) get an entry's page now, and they read its Open Graph tags, so it has no
        // structured data: search engines go straight to the author's post.
        using var preview = fixture.CreateLinkPreview();
        var page = await preview.GetPageAsync($"/2023/day/{slot.Day}/{slot.Part}?via=bluesky");
        Assert.Contains($"<meta property=\"article:published_time\" content=\"2023-12-{slot.Day:00}T00:00:00-05:00\"", page);
        Assert.Empty(await JsonLdAsync(page));
    }

    [Fact]
    public async Task NothingAboutAPostIsDescribedOrLinkedBeforeItsReveal()
    {
        await EnsureSeasonAsync();
        using var browser = fixture.CreateBrowser();

        var page = await browser.GetPageAsync("/2440/day/3/1");

        Assert.Contains("Coming on December 3", page);
        Assert.DoesNotContain("article:published_time", page);
        Assert.DoesNotContain("application/ld+json", page);
        foreach (var secret in new[] { "Hidden Hana", "secret 2440", "secret-2440", "Secret 2440" })
        {
            Assert.DoesNotContain(secret, page);
        }

        // Its calendar, wherever it's shown, doesn't link the page yet.
        Assert.DoesNotContain("href=\"2440/day/", await browser.GetPageAsync("/"));
    }

    [Fact]
    public async Task TheNotFoundPageNamesTheSite()
    {
        using var browser = fixture.CreateBrowser();

        var response = await browser.GetAsync("/no-such-page", CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var page = await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync(CancellationToken), CancellationToken);
        Assert.Equal("Not found · C# Advent", page.Title);
    }

    /// <summary>Season 2440, with sign-ups open and a post on December 3 that won't be revealed for centuries.</summary>
    private Task EnsureSeasonAsync() =>
        TestSeasons.EnsureAsync(fixture, 2440, claimsOpen: true, season =>
        {
            var slot = season.Slots.Single(s => s is { Day: 3, Part: 1 });
            slot.AuthorName = "Hidden Hana";
            slot.PostTitle = "A secret 2440 title";
            slot.PostUrl = "https://example.com/secret-2440";
            slot.PostSummary = "Secret 2440 summary";
        });

    private async Task<Slot> FindPostAsync()
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.Slots.AsNoTracking()
            .Where(s => s.SeasonYear == 2023 && s.Day >= 15 && s.PostUrl != null && s.PostTitle != null && s.AuthorName != null)
            .OrderBy(s => s.Day).ThenBy(s => s.Part)
            .FirstAsync(CancellationToken);
    }

    private static async Task<List<JsonNode>> JsonLdAsync(string html)
    {
        var document = await new HtmlParser().ParseDocumentAsync(html, CancellationToken);
        return [.. document.QuerySelectorAll("head script[type='application/ld+json']").Select(script => JsonNode.Parse(script.TextContent)!)];
    }
}
