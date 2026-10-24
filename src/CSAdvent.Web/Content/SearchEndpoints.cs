using System.Globalization;
using System.Text;
using System.Xml.Linq;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using CSAdvent.Web.Entries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Content;

/// <summary>
/// What search engines read (#38): <c>robots.txt</c>, which keeps them out of signed-in pages and tracked redirects, and
/// <c>sitemap.xml</c>, which lists the public pages, every finished season's archive and every revealed entry's page. The
/// test season is never listed.
/// </summary>
public static class SearchEndpoints
{
    private const string CacheControl = "public, max-age=3600";

    /// <summary>Pages that are signed in, personal, redirects or for organizers: nothing a search result should lead to.</summary>
    public static readonly IReadOnlyList<string> Disallowed =
        ["/manage", "/my-spots", "/profile", "/claim/", "/backups/", "/go/", "/test-post/", "/test-mode/", "/signin", "/emails/", "/announcements/", "/dev/"];

    /// <summary>The public pages that are always there, relative to the base URL.</summary>
    public static readonly IReadOnlyList<string> PublicPages = ["", "archive", "about", "others", "announcements", "privacy", "terms"];

    public static WebApplication MapSearch(this WebApplication app)
    {
        app.MapGet("/robots.txt", (IOptions<SiteOptions> site, HttpResponse response) =>
        {
            response.Headers.CacheControl = CacheControl;
            return Results.Text(Robots(site.Value.BaseUrl), "text/plain", Encoding.UTF8);
        });

        app.MapGet("/sitemap.xml", async (AdventDbContext db, AdventSchedule schedule, IOptions<SiteOptions> site, HttpResponse response, CancellationToken cancellationToken) =>
        {
            var seasons = await db.Seasons.AsNoTracking().Where(s => s.Year != Season.TestYear).Select(s => s.Year).ToListAsync(cancellationToken);
            var entries = await db.Slots
                .AsNoTracking()
                .Where(s => s.SeasonYear != Season.TestYear && s.AuthorName != null && s.PostTitle != null && s.PostUrl != null)
                .OrderBy(s => s.SeasonYear).ThenBy(s => s.Day).ThenBy(s => s.Part)
                .Select(s => new { s.SeasonYear, s.Day, s.Part })
                .ToListAsync(cancellationToken);

            var pages = PublicPages.Select(page => new SitemapPage(page, null))
                .Concat(seasons.Where(schedule.IsSeasonOver).Order().Select(year => new SitemapPage($"archive/{year}", schedule.GetDate(year, Season.DaysInAdvent))))
                // The reveal is checked here, not in SQL (#5).
                .Concat(entries.Where(e => schedule.IsRevealed(e.SeasonYear, e.Day))
                    .Select(e => new SitemapPage(EntryLinks.Page(e.SeasonYear, e.Day, e.Part), schedule.GetDate(e.SeasonYear, e.Day))));

            response.Headers.CacheControl = CacheControl;
            return Results.Text(Sitemap(site.Value.BaseUrl, pages), "application/xml", Encoding.UTF8);
        });

        return app;
    }

    public static string Robots(string baseUrl) =>
        $"User-agent: *\n{string.Concat(Disallowed.Select(path => $"Disallow: {path}\n"))}\nSitemap: {baseUrl.TrimEnd('/')}/sitemap.xml\n";

    public static string Sitemap(string baseUrl, IEnumerable<SitemapPage> pages)
    {
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var root = baseUrl.TrimEnd('/');
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "urlset", pages.Select(page => new XElement(
                ns + "url",
                new XElement(ns + "loc", $"{root}/{page.Path}"),
                page.LastModified is { } date ? new XElement(ns + "lastmod", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) : null))));
        return $"{document.Declaration}\n{document.Root}";
    }
}

/// <summary>A page in the sitemap, relative to the base URL, and when it last changed, if it's known.</summary>
public sealed record SitemapPage(string Path, DateOnly? LastModified);
