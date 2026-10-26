using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Entry pages' previous and next links, and "a random post" (#205), from the seeded history (revealed long ago) and season
/// 2331, which isn't revealed.
/// </summary>
public sealed partial class EntryNavigationTests(AppHostFixture fixture)
{
    private const int Year = 2331;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EntryPagesLinkTheSeasonsPreviousAndNextPosts()
    {
        List<(int Day, int Part)> posts;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            posts = [.. (await db.Slots
                    .Where(s => s.SeasonYear == 2020 && s.PostUrl != null && s.PostTitle != null && s.AuthorName != null)
                    .Select(s => new { s.Day, s.Part })
                    .ToListAsync(CancellationToken))
                .OrderBy(s => s.Day).ThenBy(s => s.Part)
                .Select(s => (s.Day, s.Part))];
        }

        using var preview = fixture.CreateLinkPreview();
        var (before, middle, after) = (posts[19], posts[20], posts[21]);
        var page = await preview.GetPageAsync($"/2020/day/{middle.Day}/{middle.Part}");
        Assert.Contains($"href=\"2020/day/{before.Day}/{before.Part}\" rel=\"prev\"", page);
        Assert.Contains($"href=\"2020/day/{after.Day}/{after.Part}\" rel=\"next\"", page);

        var first = await preview.GetPageAsync($"/2020/day/{posts[0].Day}/{posts[0].Part}");
        Assert.DoesNotContain("rel=\"prev\"", first);
        Assert.Contains($"href=\"2020/day/{posts[1].Day}/{posts[1].Part}\" rel=\"next\"", first);

        var last = await preview.GetPageAsync($"/2020/day/{posts[^1].Day}/{posts[^1].Part}");
        Assert.Contains($"href=\"2020/day/{posts[^2].Day}/{posts[^2].Part}\" rel=\"prev\"", last);
        Assert.DoesNotContain("rel=\"next\"", last);
    }

    [Fact]
    public async Task PostsBeforeTheirRevealAreNeverLinked()
    {
        await EnsureSeasonAsync();
        using var reader = fixture.CreateWebClient();

        var page = await reader.GetPageAsync($"/{Year}/day/1/2");

        Assert.Contains("Coming on December 1", page);
        Assert.DoesNotContain("rel=\"prev\"", page);
        Assert.DoesNotContain("rel=\"next\"", page);
        Assert.DoesNotContain("Navigating 2331", page);
    }

    [Fact]
    public async Task ARandomPostIsAlwaysARevealedOneFromARealSeason()
    {
        await EnsureSeasonAsync();
        using var browser = fixture.CreateBrowser();

        for (var visit = 0; visit < 25; visit++)
        {
            var response = await browser.GetAsync("/random", CancellationToken);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
            // Only the seeded history is revealed: never season 2331, nor the test season (9000).
            Assert.Matches(ArchiveEntry(), response.Headers.Location!.OriginalString);
        }

        Assert.Contains("Disallow: /random\n", await browser.GetStringAsync("/robots.txt", CancellationToken));
        Assert.Contains("href=\"random\"", await browser.GetStringAsync("/archive", CancellationToken));
    }

    private Task EnsureSeasonAsync() => TestSeasons.EnsureAsync(fixture, Year, claimsOpen: false, season =>
    {
        foreach (var slot in season.Slots.Where(s => s.Day <= 2))
        {
            slot.AuthorName = "Nina Navigation";
            slot.PostTitle = $"Navigating 2331, day {slot.Day}, post {slot.Part}";
            slot.PostUrl = $"https://example.com/navigating-2331/{slot.Day}/{slot.Part}";
        }
    });

    [GeneratedRegex(@"^/20(1[7-9]|2[0-5])/day/\d{1,2}/[12]$")]
    private static partial Regex ArchiveEntry();
}
