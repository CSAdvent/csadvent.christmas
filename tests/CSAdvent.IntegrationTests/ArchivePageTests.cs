using System.Text.RegularExpressions;
using CSAdvent.Web.Data;

namespace CSAdvent.IntegrationTests;

/// <summary>The archive pages, rendered from the seeded 2017-2025 history.</summary>
public sealed partial class ArchivePageTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ArchiveListsEveryFinishedSeason()
    {
        var html = await GetHtmlAsync("/archive");

        Assert.All(Enumerable.Range(2017, 9), year => Assert.Contains($"href=\"archive/{year}\"", html));
        // Seasons the database tests create are in the future, so they're not over and not listed.
        Assert.DoesNotContain("href=\"archive/209", html);
    }

    [Fact]
    public async Task SeasonPageShowsEveryDayAndPost()
    {
        var html = await GetHtmlAsync("/archive/2017");

        Assert.Contains(">C# Advent 2017</h1>", html);
        Assert.Equal(25, DayCard().Count(html));
        Assert.Contains("href=\"https://g33klady.com/2017/12/01/c-advent-calendar-combining-integration-and-ui-automation-in-c/\"", html);
        Assert.Contains("Combining Integration and UI Automation in C#", Regex.Replace(html, "<[^>]+>", ""));
        Assert.Contains("href=\"https://crosscuttingconcerns.com/The-First-C-Advent-Calendar\"", html);
    }

    [Fact]
    public async Task EntriesWithoutAPostShowNoPostAndDoNotNameTheAuthor()
    {
        var html = await GetHtmlAsync("/archive/2019");

        Assert.Contains("No post", html);
        Assert.DoesNotContain("Benjamin Howarth", html);
    }

    [Theory]
    [InlineData("/archive/2016")]
    [InlineData("/archive/99999")]
    [InlineData("/archive/not-a-year")]
    public async Task UnknownSeasonsReturnNotFound(string path)
    {
        using var client = fixture.CreateWebClient();

        var response = await client.GetAsync(path, CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SeasonsThatAreNotOverReturnNotFound()
    {
        await using (var db = await fixture.CreateDbContextAsync())
        {
            db.Seasons.Add(Season.Create(2096, slotsPerDay: 2, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(CancellationToken);
        }

        using var client = fixture.CreateWebClient();
        var response = await client.GetAsync("/archive/2096", CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheTailwindStylesheetIsServed()
    {
        var html = await GetHtmlAsync("/");
        var stylesheet = Stylesheet().Match(html);
        Assert.True(stylesheet.Success, "The page doesn't link the app stylesheet.");

        using var client = fixture.CreateWebClient();
        var response = await client.GetAsync("/" + stylesheet.Groups["href"].Value, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("tailwindcss", await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private async Task<string> GetHtmlAsync(string path)
    {
        using var client = fixture.CreateWebClient();
        var response = await client.GetAsync(path, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(CancellationToken);
    }

    [GeneratedRegex("id=\"december-\\d+\"")]
    private static partial Regex DayCard();

    [GeneratedRegex("<link rel=\"stylesheet\" href=\"(?<href>app[^\"]*\\.css)\"")]
    private static partial Regex Stylesheet();
}
