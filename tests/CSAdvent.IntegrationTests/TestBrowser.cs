using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using CSAdvent.Web.Data;

namespace CSAdvent.IntegrationTests;

/// <summary>Helpers that drive the site the way a browser does: sign in, and post a page's Blazor form.</summary>
internal static partial class TestBrowser
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    /// <summary>A fresh browser session signed in with dev sign-in.</summary>
    public static async Task<HttpClient> SignInAsync(this AppHostFixture fixture, string login, long githubUserId, string? name = null)
    {
        var browser = fixture.CreateBrowser();
        var nameQuery = name is null ? "" : $"&name={Uri.EscapeDataString(name)}";
        var response = await browser.GetAsync($"/dev/signin?login={login}&id={githubUserId}{nameQuery}", CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return browser;
    }

    /// <summary>Loads the page for its antiforgery token, then posts the named form with the given fields.</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(this HttpClient browser, string path, string formName, Dictionary<string, string> fields)
    {
        var page = await browser.GetStringAsync(path, CancellationToken);
        fields["__RequestVerificationToken"] = AntiforgeryToken().Match(page).Groups["value"].Value;
        fields["_handler"] = formName;

        return await browser.PostAsync(path, new FormUrlEncodedContent(fields), CancellationToken);
    }

    public static string PathOf(Uri location) =>
        location.IsAbsoluteUri ? location.PathAndQuery : "/" + location.OriginalString.TrimStart('/');

    [GeneratedRegex("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"(?<value>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}

/// <summary>
/// Seasons the tests create, each exactly once per test run. Every test class uses its own years, all later than 2089,
/// which the home page tests rely on being the earliest season that isn't over.
/// </summary>
internal static class TestSeasons
{
    private static readonly ConcurrentDictionary<int, Lazy<Task>> Created = new();

    public static Task EnsureAsync(AppHostFixture fixture, int year, bool claimsOpen, Action<Season>? configure = null) =>
        Created.GetOrAdd(year, _ => new Lazy<Task>(async () =>
        {
            await using var db = await fixture.CreateDbContextAsync();
            var season = Season.Create(year, slotsPerDay: 2, DateTimeOffset.UtcNow);
            season.ClaimsOpenAt = claimsOpen ? DateTimeOffset.UtcNow.AddDays(-1) : null;
            configure?.Invoke(season);
            db.Seasons.Add(season);
            await db.SaveChangesAsync();
        })).Value;
}
