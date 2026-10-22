using System.Security.Cryptography;
using System.Text;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace CSAdvent.Web.Feed;

public static class FeedEndpoints
{
    public const string ContentType = "application/rss+xml; charset=utf-8";

    // Feed readers poll often: they may keep the feed for 10 minutes, and after that an unchanged feed costs a 304.
    private const string CacheControl = "public, max-age=600";

    public static WebApplication MapFeeds(this WebApplication app)
    {
        app.MapGet("/rss", async (HttpContext context, RssFeed feed, CancellationToken cancellationToken) =>
            Respond(context, feed.Write(await feed.GetItemsAsync(cancellationToken: cancellationToken))));

        app.MapGet("/archive/{year:int}/rss", async (int year, HttpContext context, RssFeed feed, AdventDbContext db, CancellationToken cancellationToken) =>
            await db.Seasons.AnyAsync(s => s.Year == year, cancellationToken)
                ? Respond(context, feed.Write(await feed.GetItemsAsync(year, cancellationToken), year))
                : Results.NotFound());

        return app;
    }

    private static IResult Respond(HttpContext context, string xml)
    {
        var etag = $"\"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(xml)))[..16]}\"";
        context.Response.Headers.ETag = etag;
        context.Response.Headers.CacheControl = CacheControl;

        return context.Request.Headers.IfNoneMatch.Contains(etag)
            ? Results.StatusCode(StatusCodes.Status304NotModified)
            : Results.Text(xml, ContentType, Encoding.UTF8);
    }
}
