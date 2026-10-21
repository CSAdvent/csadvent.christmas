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

    /// <summary>
    /// Loads the page, then posts the named form with the given fields, the antiforgery token, and the page's other hidden
    /// inputs (such as a row version) unless the fields set them. Hidden inputs of other forms are posted too, which is
    /// harmless: Blazor only binds the fields of the form being submitted.
    /// </summary>
    public static async Task<HttpResponseMessage> PostFormAsync(this HttpClient browser, string path, string formName, Dictionary<string, string> fields)
    {
        var page = await browser.GetStringAsync(path, CancellationToken);

        foreach (Match input in HiddenInput().Matches(page))
        {
            var name = NameAttribute().Match(input.Value).Groups["name"].Value;
            if (name.Length > 0 && !fields.ContainsKey(name))
            {
                fields[name] = System.Net.WebUtility.HtmlDecode(ValueAttribute().Match(input.Value).Groups["value"].Value);
            }
        }

        fields["_handler"] = formName;
        return await browser.PostAsync(path, new FormUrlEncodedContent(fields), CancellationToken);
    }

    /// <summary>The value of a hidden input on a page.</summary>
    public static string HiddenValue(string page, string name) =>
        HiddenInput().Matches(page)
            .Select(input => input.Value)
            .Where(input => NameAttribute().Match(input).Groups["name"].Value == name)
            .Select(input => ValueAttribute().Match(input).Groups["value"].Value)
            .Single();

    public static string PathOf(Uri location) =>
        location.IsAbsoluteUri ? location.PathAndQuery : "/" + location.OriginalString.TrimStart('/');

    [GeneratedRegex("<input[^>]*type=\"hidden\"[^>]*>")]
    private static partial Regex HiddenInput();

    [GeneratedRegex("name=\"(?<name>[^\"]*)\"")]
    private static partial Regex NameAttribute();

    [GeneratedRegex("value=\"(?<value>[^\"]*)\"")]
    private static partial Regex ValueAttribute();
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
