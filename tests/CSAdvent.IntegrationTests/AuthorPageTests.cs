using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Public author pages (#203). Early-December 2021 archive posts (no other test changes those) get names of this test's own,
/// and season 2332, which isn't revealed, gives the same authors posts that must never show.
/// </summary>
public sealed class AuthorPageTests(AppHostFixture fixture)
{
    private const int Year = 2332;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnArchiveAuthorHasAPageByNameWithOnlyTheirRevealedPosts()
    {
        await ArrangeAsync();
        using var reader = fixture.CreateWebClient();

        var page = await reader.GetPageAsync("/authors/name/%C3%A4rchie%20quill");

        Assert.Contains($">{Encode("Ärchie Quill")}</h1>", page);
        Assert.Contains("2 posts, for C# Advent 2021.", page);
        Assert.Contains("href=\"2021/day/2/1\"", page);
        Assert.Contains("href=\"2021/day/2/2\"", page);
        Assert.Contains("<link rel=\"canonical\" href=\"https://localhost:7092/authors/name/%C3%A4rchie%20quill\"", page);
        Assert.DoesNotContain("Unrevealed Quill", page);
        Assert.DoesNotContain($"{Year}/day", page);

        // Its name in any case or spacing finds it, and its entries link to it.
        Assert.Contains($">{Encode("Ärchie Quill")}</h1>", await reader.GetPageAsync("/authors/name/%C3%84RCHIE%20%20QUILL"));
        Assert.Contains("href=\"authors/name/%C3%A4rchie%20quill\"", await reader.GetPageAsync("/2021/day/2/1"));
    }

    [Fact]
    public async Task ALinkedAuthorHasAPageByLoginWithTheirLinks()
    {
        await ArrangeAsync();
        using var reader = fixture.CreateWebClient();

        var page = await reader.GetPageAsync("/authors/author-page-lin");

        Assert.Contains(">Lin Linked</h1>", page);
        Assert.Contains("href=\"2021/day/3/1\"", page);
        Assert.Contains("href=\"https://github.com/author-page-lin\"", page);
        Assert.Contains("href=\"https://bsky.app/profile/lin.bsky.social\"", page);
        Assert.DoesNotContain("secret 2332", page);
        Assert.Contains(">Lin Linked</h1>", await reader.GetPageAsync("/authors/AUTHOR-PAGE-LIN"));
        Assert.Contains("href=\"authors/author-page-lin\"", await reader.GetPageAsync("/2021/day/3/1"));
    }

    [Fact]
    public async Task AnArchiveAuthorLinkedSinceMovesToTheirAccountsPage()
    {
        await ArrangeAsync();
        using var browser = fixture.CreateBrowser();

        var response = await browser.GetAsync("/authors/name/moved%20morgan", CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/authors/author-page-mo", TestBrowser.PathOf(response.Headers.Location!));
    }

    [Fact]
    public async Task AuthorsWithoutARevealedPostHaveNoPage()
    {
        await ArrangeAsync();
        using var reader = fixture.CreateWebClient();

        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync("/authors/name/fiona%20future", CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync("/authors/author-page-fiona", CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync("/authors/nobody-at-all-2332", CancellationToken)).StatusCode);

        var index = await reader.GetPageAsync("/authors");
        Assert.Contains("href=\"authors/name/%C3%A4rchie%20quill\"", index);
        Assert.Contains("href=\"authors/author-page-lin\"", index);
        Assert.DoesNotContain("Fiona Future", index);
        Assert.Contains("href=\"authors\"", await reader.GetPageAsync("/archive"));

        var sitemap = await reader.GetStringAsync("/sitemap.xml", CancellationToken);
        Assert.Contains("<loc>https://localhost:7092/authors</loc>", sitemap);
        Assert.Contains("<loc>https://localhost:7092/authors/name/%C3%A4rchie%20quill</loc>", sitemap);
        Assert.Contains("<loc>https://localhost:7092/authors/author-page-lin</loc>", sitemap);
        Assert.DoesNotContain("fiona", sitemap);
    }

    /// <summary>Names the archive posts and fills season 2332, once for the whole class.</summary>
    private Task ArrangeAsync() => Arranged.GetOrAdd(fixture, _ => new Lazy<Task>(async () =>
    {
        // Signing in makes the contributors; Fiona only has a post that isn't revealed.
        foreach (var (login, id) in new[] { ("author-page-lin", 233201L), ("author-page-mo", 233202L), ("author-page-fiona", 233203L) })
        {
            using var browser = await fixture.SignInAsync(login, id);
        }

        await TestSeasons.EnsureAsync(fixture, Year, claimsOpen: false);
        await using var db = await fixture.CreateDbContextAsync();
        var contributors = await db.Contributors.Where(c => c.GithubUserId >= 233201 && c.GithubUserId <= 233203).ToDictionaryAsync(c => c.GithubLogin, CancellationToken);
        contributors["author-page-lin"].BlueskyHandle = "lin.bsky.social";

        async Task NameAsync(int year, int day, int part, string name, Contributor? contributor = null, string? title = null)
        {
            var slot = await db.Slots.SingleAsync(s => s.SeasonYear == year && s.Day == day && s.Part == part, CancellationToken);
            slot.AuthorName = name;
            slot.ContributorId = contributor?.Id;
            if (title is not null)
            {
                slot.PostTitle = title;
                slot.PostUrl = $"https://example.com/{year}/{day}/{part}";
            }
        }

        // The same author whatever the case or spacing; the page uses the latest post's name.
        await NameAsync(2021, 2, 1, "ärchie  quill");
        await NameAsync(2021, 2, 2, "Ärchie Quill");
        await NameAsync(2021, 3, 1, "Lin Linked", contributors["author-page-lin"]);
        await NameAsync(2021, 4, 1, "Moved Morgan", contributors["author-page-mo"]);
        await NameAsync(Year, 3, 1, "Ärchie Quill", title: "Unrevealed Quill");
        await NameAsync(Year, 3, 2, "Fiona Future", title: "Fiona's future post");
        await NameAsync(Year, 4, 1, "Fiona Future", contributors["author-page-fiona"], "Fiona's linked future post");
        await NameAsync(Year, 5, 1, "Lin Linked", contributors["author-page-lin"], "Lin's secret 2332 post");
        await db.SaveChangesAsync(CancellationToken);
    })).Value;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<AppHostFixture, Lazy<Task>> Arranged = new();

    private static string Encode(string text) => System.Text.Encodings.Web.HtmlEncoder.Default.Encode(text);
}
