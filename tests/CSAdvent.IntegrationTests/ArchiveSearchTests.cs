using System.Net;
using System.Text.RegularExpressions;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Searching the archive (#204): the seeded history is revealed, and season 2333 isn't, so nothing about its post may show,
/// not even in a count or a page that's any different from a search that matches nothing.
/// </summary>
public sealed partial class ArchiveSearchTests(AppHostFixture fixture)
{
    private const int Year = 2333;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FindsRevealedPostsByTitleAndFiltersBySeason()
    {
        Slot slot;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            slot = await db.Slots.AsNoTracking().SingleAsync(s => s.SeasonYear == 2017 && s.Day == 10 && s.Part == 1, CancellationToken);
        }

        using var reader = fixture.CreateWebClient();

        var byTitle = await reader.GetPageAsync($"/archive/search?q={Uri.EscapeDataString(slot.PostTitle!)}");
        Assert.Contains("href=\"2017/day/10/1\"", byTitle);
        Assert.Matches(PostsMatch(), Status(byTitle));

        var season = await reader.GetPageAsync("/archive/search?year=2017");
        Assert.Equal("25 posts in C# Advent 2017.", Status(season));
        Assert.Contains("href=\"2017/day/1/1\"", season);
        Assert.Contains("<option value=\"2017\" selected", season);

        // A mistyped season is ignored, not an error.
        Assert.DoesNotContain("role=\"status\"", await reader.GetPageAsync("/archive/search?year=soon"));
    }

    [Fact]
    public async Task PostsBeforeTheirRevealAreNeverFoundOrCounted()
    {
        await TestSeasons.EnsureAsync(fixture, Year, claimsOpen: false, season =>
        {
            var slot = season.Slots.Single(s => s is { Day: 1, Part: 1 });
            slot.AuthorName = "Quentin Quokka";
            slot.PostTitle = "Zanzibar quokkas and spans";
            slot.PostUrl = "https://example.com/zanzibar";
            slot.PostSummary = "Wallabies explain everything";
        });
        using var reader = fixture.CreateWebClient();

        var nothing = WithoutTokens(await reader.GetPageAsync("/archive/search?q=xylophonic"));
        foreach (var query in new[] { "quokkas", "quentin", "wallabies", "zanzibar" })
        {
            var page = await reader.GetPageAsync($"/archive/search?q={query}");

            Assert.Equal($"No posts match “{query}”. Try fewer words, or another spelling.", Status(page));
            // The same page as a search that matches nothing at all.
            Assert.Equal(nothing.Replace("xylophonic", "QUERY", StringComparison.Ordinal), WithoutTokens(page).Replace(query, "QUERY", StringComparison.Ordinal));
        }

        var season = await reader.GetPageAsync($"/archive/search?year={Year}");
        Assert.Equal($"No posts in C# Advent {Year}.", Status(season));
        Assert.Equal(
            WithoutTokens(await reader.GetPageAsync("/archive/search?year=1999")).Replace("1999", "YEAR", StringComparison.Ordinal),
            WithoutTokens(season).Replace($"{Year}", "YEAR", StringComparison.Ordinal));
        Assert.DoesNotContain($"<option value=\"{Year}\"", season);
        Assert.DoesNotContain($"<option value=\"{Season.TestYear}\"", season);
    }

    [Fact]
    public async Task TheArchiveLinksTheSearchAndSearchEnginesStayOut()
    {
        using var reader = fixture.CreateWebClient();

        Assert.Contains("action=\"archive/search\"", await reader.GetPageAsync("/archive"));
        Assert.Contains("Disallow: /archive/search\n", await reader.GetStringAsync("/robots.txt", CancellationToken));
    }

    /// <summary>The page without Blazor's protected component state, which is new on every request.</summary>
    private static string WithoutTokens(string page) => ProtectedPayload().Replace(page, "");

    /// <summary>What the page says it found.</summary>
    private static string Status(string page) => WebUtility.HtmlDecode(StatusLine().Match(page).Groups["status"].Value);

    [GeneratedRegex("role=\"status\">(?<status>[^<]*)</p>")]
    private static partial Regex StatusLine();

    [GeneratedRegex(@"CfDJ8[A-Za-z0-9_\-+/=]*")]
    private static partial Regex ProtectedPayload();

    [GeneratedRegex(@"^\d+ posts? match(es)? “")]
    private static partial Regex PostsMatch();
}
