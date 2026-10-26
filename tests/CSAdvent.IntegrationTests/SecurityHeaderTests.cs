using System.Security.Cryptography;
using System.Text;
using AngleSharp.Html.Parser;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// The security headers (#165) on pages, files and redirects, and a Content-Security-Policy that allows every inline script
/// the pages really render, whatever <c>App.razor</c> loads. Uses season 2442 and GitHub user ID 244201.
/// </summary>
public sealed class SecurityHeaderTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/")]
    [InlineData("/archive/2024")]
    [InlineData("/rss")]
    [InlineData("/images/csadvent-logo.png")]
    [InlineData("/this-page-does-not-exist")]
    public async Task EveryResponseHasTheSecurityHeadersAndNoServerHeader(string path)
    {
        using var browser = fixture.CreateBrowser();

        var response = await browser.GetAsync(path, CancellationToken);

        AssertSecurityHeaders(response);
        Assert.Contains("img-src 'self';", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        Assert.Empty(response.Headers.Server);
        // Development (the tests' environment) never sends HSTS, which would stick to localhost; production sends it over https.
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task TheWwwRedirectHasThemToo()
    {
        using var browser = fixture.CreateBrowser();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/archive");
        request.Headers.Host = "www.localhost";

        var response = await browser.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task OnlyOrganizerPagesCanShowGitHubsAvatars()
    {
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);

        var response = await organizer.GetAsync("/manage/authors", CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "img-src 'self' https://github.com https://avatars.githubusercontent.com;",
            Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task ThePolicyAllowsEveryInlineScriptThePagesRender()
    {
        await TestSeasons.EnsureAsync(fixture, 2442, claimsOpen: true);
        string entry;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var slot = await db.Slots.AsNoTracking()
                .Where(s => s.SeasonYear == 2023 && s.PostUrl != null && s.PostTitle != null && s.AuthorName != null)
                .OrderBy(s => s.Day).ThenBy(s => s.Part)
                .FirstAsync(CancellationToken);
            entry = $"/2023/day/{slot.Day}/{slot.Part}";
        }

        using var reader = fixture.CreateBrowser();
        using var preview = fixture.CreateLinkPreview();
        using var author = await fixture.SignInAsync("csp-author", 244201);
        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        var pages = new (HttpClient Browser, string Path)[]
        {
            (reader, "/"), (reader, "/archive"), (reader, "/archive/2023"), (preview, entry), (reader, "/about"), (reader, "/authors"),
            (reader, "/others"), (reader, "/announcements"), (reader, "/backups/2442"), (reader, "/no-such-page"),
            (author, "/my-spots"), (author, "/profile"), (author, "/backups/2442"), (author, "/claim/2442/5/1"),
            (organizer, "/manage"), (organizer, "/manage/entries"), (organizer, "/manage/social"), (organizer, "/manage/emails"),
            (organizer, "/manage/authors"),
        };

        var parser = new HtmlParser();
        foreach (var (browser, path) in pages)
        {
            var response = await browser.GetAsync(path, CancellationToken);
            Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound, $"{path}: {(int)response.StatusCode}");
            var policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
            var scriptSources = policy.Split("; ").Single(d => d.StartsWith("script-src ", StringComparison.Ordinal)).Split(' ');
            var document = await parser.ParseDocumentAsync(await response.Content.ReadAsStringAsync(CancellationToken));

            // Inline scripts (the import map) run only if the policy has their hash. JSON-LD is data, not a script that runs.
            foreach (var script in document.Scripts.Where(s => !s.HasAttribute("src") && s.Type != "application/ld+json"))
            {
                var hash = $"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script.Text)))}'";
                Assert.True(scriptSources.Contains(hash), $"{path} has an inline {script.Type ?? "script"} the policy doesn't allow: {hash}, not in {string.Join(' ', scriptSources)}");
            }

            // Inline event handlers and javascript: links would be refused too.
            foreach (var element in document.All)
            {
                Assert.DoesNotContain(element.Attributes, a => a.Name.StartsWith("on", StringComparison.Ordinal) && a.Name.Length > 2);
                Assert.False(
                    element.GetAttribute("href")?.TrimStart().StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) == true,
                    $"{path} has a javascript: link");
            }
        }
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        var policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.StartsWith("default-src 'self'; script-src 'self'", policy);
        Assert.Contains("object-src 'none'", policy);
        Assert.Contains("frame-ancestors 'self'", policy);
        Assert.Contains("camera=()", Assert.Single(response.Headers.GetValues("Permissions-Policy")));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("SAMEORIGIN", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("strict-origin-when-cross-origin", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }
}
