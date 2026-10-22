using CSAdvent.Web.Manage;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Content;

/// <summary>Keeping links from around the web working: one canonical host, and the legacy site's URLs.</summary>
public static class SiteRedirects
{
    /// <summary>
    /// Sends <c>www.</c> requests to the canonical host (the one in <c>Site:BaseUrl</c>), keeping the path and query, so
    /// links to either work but there's one address. Other hosts (such as the Container Apps address) are left alone.
    /// </summary>
    public static WebApplication UseCanonicalHost(this WebApplication app)
    {
        var canonical = new Uri(app.Services.GetRequiredService<IOptions<SiteOptions>>().Value.BaseUrl).Host;
        app.Use(async (context, next) =>
        {
            if (context.Request.Host.Host.Equals($"www.{canonical}", StringComparison.OrdinalIgnoreCase))
            {
                var request = context.Request;
                context.Response.Redirect($"https://{canonical}{request.PathBase}{request.Path}{request.QueryString}", permanent: true);
                return;
            }

            await next();
        });
        return app;
    }

    /// <summary>The legacy site's pages that moved. (Archive pages and <c>/rss</c> kept their addresses.)</summary>
    public static WebApplication MapLegacyRedirects(this WebApplication app)
    {
        // The contributor page, where authors added their post.
        app.MapGet("/contributor", () => Results.Redirect("/my-spots", permanent: true));

        // The substitute sign-up: now the current season's backup list.
        app.MapGet("/substitute", async (SeasonAdminService seasons, CancellationToken cancellationToken) =>
            Results.Redirect(await seasons.GetCurrentYearAsync(cancellationToken) is { } year ? $"/backups/{year}" : "/"));

        return app;
    }
}
