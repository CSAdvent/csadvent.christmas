using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>
    /// GETs a page like <see cref="HttpClient.GetStringAsync(string)"/>, but a failure says what the server threw (from the
    /// development exception page), not only its status code.
    /// </summary>
    public static async Task<string> GetPageAsync(this HttpClient browser, string path)
    {
        using var response = await browser.GetAsync(path, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = StackError().Match(body) is { Success: true } match ? System.Net.WebUtility.HtmlDecode(match.Groups["error"].Value) : body[..Math.Min(body.Length, 500)];
            Assert.Fail($"GET {path} returned {(int)response.StatusCode}: {error}");
        }

        return body;
    }

    public static string PathOf(Uri location) =>
        location.IsAbsoluteUri ? location.PathAndQuery : "/" + location.OriginalString.TrimStart('/');

    [GeneratedRegex("<h2 class=\"stackerror\">(?<error>.*?)</h2>", RegexOptions.Singleline)]
    private static partial Regex StackError();

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

    public static async Task EnsureAsync(AppHostFixture fixture, int year, bool claimsOpen, Action<Season>? configure = null)
    {
        var creating = Created.GetOrAdd(year, _ => new Lazy<Task>(() => CreateAsync(fixture, year, claimsOpen, configure)));
        try
        {
            await creating.Value;
        }
        catch
        {
            // A failed attempt (the database slow to answer on a busy machine, say) isn't kept, so the next test that needs the
            // season tries again rather than failing at once with this one's error (#198).
            Created.TryRemove(new KeyValuePair<int, Lazy<Task>>(year, creating));
            throw;
        }
    }

    private static async Task CreateAsync(AppHostFixture fixture, int year, bool claimsOpen, Action<Season>? configure)
    {
        await using var db = await fixture.CreateDbContextAsync();

        // An attempt that failed after saving it leaves it there.
        if (await db.Seasons.AnyAsync(s => s.Year == year))
        {
            return;
        }

        var season = Season.Create(year, slotsPerDay: 2, DateTimeOffset.UtcNow);
        season.ClaimsOpenAt = claimsOpen ? DateTimeOffset.UtcNow.AddDays(-1) : null;
        configure?.Invoke(season);
        db.Seasons.Add(season);
        await db.SaveChangesAsync();
    }
}
