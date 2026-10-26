using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Entry pages, their preview tags, and the tracked "Read the post" redirect, using the seeded history (revealed long
/// ago) and a far-future test season (not revealed). A revealed entry's page is for link previews (and organizers): readers
/// go straight to the post (<see cref="EntryRedirectTests"/>).
/// </summary>
public sealed class EntryPageTests(AppHostFixture fixture)
{
    private const string Browser = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0 Safari/537.36";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARevealedEntryHasItsOwnPageWithPreviewTags()
    {
        var (slot, sibling) = await FindDayWithTwoPostsAsync(2020);
        using var preview = fixture.CreateLinkPreview();

        var page = await preview.GetPageAsync($"/2020/day/{slot.Day}/{slot.Part}?via=bluesky");

        Assert.Contains(Encode(slot.PostTitle!), page);
        Assert.Contains(Encode(slot.AuthorName!), page);
        Assert.Contains($"href=\"go/2020/{slot.Day}/{slot.Part}?via=bluesky\"", page);
        Assert.Contains($"href=\"2020/day/{slot.Day}/{sibling.Part}\"", page);

        // The canonical and Open Graph URLs drop the ?via= tag.
        var url = $"https://localhost:7092/2020/day/{slot.Day}/{slot.Part}";
        Assert.Contains($"<link rel=\"canonical\" href=\"{url}\"", page);
        Assert.Contains($"<meta property=\"og:url\" content=\"{url}\"", page);
        Assert.Contains($"<meta property=\"og:title\" content=\"{Encode(slot.PostTitle!)}\"", page);
        Assert.Contains("<meta property=\"og:type\" content=\"article\"", page);
        Assert.Contains($"<meta property=\"og:image\" content=\"https://localhost:7092/cards/2020/{slot.Day}/{slot.Part}.jpg?v=", page);
        Assert.Contains("<meta property=\"og:image:width\" content=\"1200\"", page);
        Assert.Contains("<meta name=\"twitter:card\" content=\"summary_large_image\"", page);
        Assert.Contains("<meta name=\"twitter:site\" content=\"@csadvent\"", page);
        Assert.Contains("<meta name=\"fediverse:creator\" content=\"@csadvent@mastodon.social\"", page);
    }

    [Fact]
    public async Task ClicksAreCountedForPeopleOnlyWithTheirSource()
    {
        var (slot, _) = await FindDayWithTwoPostsAsync(2021);
        var page = $"/2021/day/{slot.Day}/{slot.Part}";
        var go = $"/go/2021/{slot.Day}/{slot.Part}";

        using (var person = CreateBrowser(Browser))
        {
            // The entry page's link (from our Mastodon post, say) goes straight to the post, counted as a click.
            var followed = await person.GetAsync($"{page}?via=mastodon", CancellationToken);
            Assert.Equal(HttpStatusCode.Redirect, followed.StatusCode);
            Assert.Equal(slot.PostUrl, followed.Headers.Location!.OriginalString);

            // Blazor's enhanced navigation fetched the link before loading it as a page (#150). Only the page load counts.
            using var enhanced = new HttpRequestMessage(HttpMethod.Get, $"{go}?via=calendar");
            enhanced.Headers.TryAddWithoutValidation("Accept", "text/html; blazor-enhanced-nav=on");
            Assert.Equal(HttpStatusCode.Redirect, (await person.SendAsync(enhanced, CancellationToken)).StatusCode);

            var click = await person.GetAsync($"{go}?via=calendar", CancellationToken);
            Assert.Equal(HttpStatusCode.Redirect, click.StatusCode);
            Assert.Equal(slot.PostUrl, click.Headers.Location!.OriginalString);
        }

        // Link previews get the page and the redirect but aren't counted.
        using (var previewer = fixture.CreateLinkPreview())
        {
            Assert.Equal(HttpStatusCode.OK, (await previewer.GetAsync(page, CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await previewer.GetAsync(go, CancellationToken)).StatusCode);
        }

        await using var db = await fixture.CreateDbContextAsync();
        var hits = await db.EntryHits.Where(h => h.SlotId == slot.Id).OrderBy(h => h.Id).ToListAsync(CancellationToken);
        Assert.Equal([(EntryHitKind.Click, "mastodon"), (EntryHitKind.Click, "calendar")], hits.Select(h => (h.Kind, h.Via)));
    }

    [Fact]
    public async Task BeforeTheRevealNothingAboutThePostIsShownOrLinked()
    {
        await TestSeasons.EnsureAsync(fixture, 2113, claimsOpen: false, season =>
        {
            var slot = season.Slots.Single(s => s is { Day: 4, Part: 1 });
            slot.AuthorName = "Secret Sam";
            slot.PostTitle = "A secret 2113 title";
            slot.PostUrl = "https://example.com/secret";
        });
        using var client = fixture.CreateWebClient();

        var page = await client.GetStringAsync("/2113/day/4/1", CancellationToken);

        Assert.Contains("Coming on December 4", page);
        Assert.Contains("<meta property=\"og:image\" content=\"https://localhost:7092/images/social-card.jpg\"", page);
        Assert.DoesNotContain("Secret Sam", page);
        Assert.DoesNotContain("secret 2113", page);
        Assert.DoesNotContain("example.com/secret", page);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/go/2113/4/1", CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/cards/2113/4/1.jpg", CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task RevealedSpotsWithoutAPostAndUnknownSpotsAreNotFound()
    {
        int day, part;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var empty = await db.Slots.Where(s => s.SeasonYear == 2019 && s.PostUrl == null).OrderBy(s => s.Day).FirstAsync(CancellationToken);
            (day, part) = (empty.Day, empty.Part);
        }

        using var client = fixture.CreateWebClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/2019/day/{day}/{part}", CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/go/2019/{day}/{part}", CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/2019/day/26/1", CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/go/2019/26/1", CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/99999/day/1/1", CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task CalendarsLinkPostsThroughTheRedirectAndEveryPageHasPreviewTags()
    {
        // The home page's card is its season's, so there has to be a season on the calendar, whichever tests ran before
        // this one (#198): season 2623's sign-ups are open. Without one, it would be the general card.
        await TestSeasons.EnsureAsync(fixture, 2623, claimsOpen: true);
        using var client = fixture.CreateWebClient();

        var archive = await client.GetStringAsync("/archive/2022", CancellationToken);
        Assert.Contains("href=\"go/2022/", archive);
        Assert.Contains("?via=calendar\"", archive);
        Assert.Contains("<meta property=\"og:title\" content=\"C# Advent 2022\"", archive);

        var home = await client.GetStringAsync("/", CancellationToken);
        Assert.Contains("<meta property=\"og:url\" content=\"https://localhost:7092\"", home);
        Assert.Contains("<meta property=\"og:image\" content=\"https://localhost:7092/cards/", home);
    }

    [Fact]
    public async Task EntryAndSeasonCardsAreGeneratedAndCached()
    {
        var (slot, _) = await FindDayWithTwoPostsAsync(2023);
        using var client = fixture.CreateWebClient();

        foreach (var path in new[] { $"/cards/2023/{slot.Day}/{slot.Part}.jpg", "/cards/2023.jpg" })
        {
            var card = await client.GetAsync(path, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, card.StatusCode);
            Assert.Equal("image/jpeg", card.Content.Headers.ContentType!.MediaType);
            Assert.Equal("public, max-age=86400", card.Headers.CacheControl!.ToString());
            Assert.True((await card.Content.ReadAsByteArrayAsync(CancellationToken)).Length < 1_000_000);
        }

        // By the versioned link the entry page gives link previews, it never changes, so it's kept for good (#186).
        using var preview = fixture.CreateLinkPreview();
        var page = await preview.GetPageAsync($"/2023/day/{slot.Day}/{slot.Part}");
        var link = System.Text.RegularExpressions.Regex.Match(page, "<meta property=\"og:image\" content=\"https://localhost:7092(?<path>[^\"]+)\"").Groups["path"].Value;
        var versioned = await client.GetAsync(WebUtility.HtmlDecode(link), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, versioned.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", versioned.Headers.CacheControl!.ToString());

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/cards/1999.jpg", CancellationToken)).StatusCode);
    }

    private HttpClient CreateBrowser(string userAgent)
    {
        var browser = fixture.CreateBrowser();
        browser.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return browser;
    }

    /// <summary>A day in an archived season where both posts exist, so the entry page shows the other one too.</summary>
    private async Task<(Slot Slot, Slot Sibling)> FindDayWithTwoPostsAsync(int year)
    {
        await using var db = await fixture.CreateDbContextAsync();
        var slots = await db.Slots
            .AsNoTracking()
            .Where(s => s.SeasonYear == year && s.PostUrl != null && s.PostTitle != null && s.AuthorName != null)
            .ToListAsync(CancellationToken);
        var day = slots.GroupBy(s => s.Day).Where(d => d.Count() == 2).OrderByDescending(d => d.Key).First();
        return (day.Single(s => s.Part == 1), day.Single(s => s.Part == 2));
    }

    private static string Encode(string text) => System.Text.Encodings.Web.HtmlEncoder.Default.Encode(text);
}
