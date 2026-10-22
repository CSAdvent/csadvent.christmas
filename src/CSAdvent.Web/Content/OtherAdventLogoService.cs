using System.Globalization;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Content;

public enum LogoResult
{
    Saved,

    /// <summary>The site has no icon we can use, or an upload isn't an image.</summary>
    NotFound,
}

/// <summary>Other Advents logos, by calendar address: fetched from the calendars' sites, or uploaded by organizers.</summary>
public sealed class OtherAdventLogoService(AdventDbContext db, LogoFetcher fetcher, TimeProvider timeProvider)
{
    /// <summary>A site without a usable icon is asked again after this long, in case it's added one.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromDays(1);

    /// <summary>The largest upload accepted, well over any real logo.</summary>
    public const int MaxUploadBytes = 1024 * 1024;

    /// <summary>A logo's link (<c>others/logos/{id}.png?v=…</c>). The version changes with the logo, so browsers can cache it.</summary>
    public static string Link(int id, DateTimeOffset? updatedAt) =>
        string.Create(CultureInfo.InvariantCulture, $"others/logos/{id}.png?v={updatedAt?.ToUnixTimeSeconds() ?? 0}");

    public Task<byte[]?> GetPngAsync(int id, CancellationToken cancellationToken = default) =>
        db.OtherAdventLogos.Where(l => l.Id == id).Select(l => l.Png).SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Addresses on any season's list, or the suggested list, without a logo, except those tried in the last
    /// <see cref="RetryAfter"/>.
    /// </summary>
    public async Task<IReadOnlyList<string>> FindMissingAsync(CancellationToken cancellationToken = default)
    {
        var retryBefore = timeProvider.GetUtcNow() - RetryAfter;
        var listed = await db.OtherAdvents.Select(a => a.Url).Distinct().ToListAsync(cancellationToken);
        var urls = listed.Concat(OtherAdventsService.Suggestions.Select(s => s.Url)).Distinct().ToList();

        var settled = await db.OtherAdventLogos
            .Where(l => urls.Contains(l.Url) && (l.Png != null || l.LastAttemptAt > retryBefore))
            .Select(l => l.Url)
            .ToListAsync(cancellationToken);

        return [.. urls.Except(settled)];
    }

    /// <summary>Fetches the site's logo. A failed fetch keeps any logo it already had.</summary>
    public async Task<LogoResult> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        var png = Uri.TryCreate(url, UriKind.Absolute, out var site) ? await fetcher.FetchAsync(site, cancellationToken) : null;

        var logo = await FindOrAddAsync(url, cancellationToken);
        var now = timeProvider.GetUtcNow();
        logo.LastAttemptAt = now;
        if (png is not null)
        {
            logo.Png = png;
            logo.IsUploaded = false;
            logo.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return png is null ? LogoResult.NotFound : LogoResult.Saved;
    }

    /// <summary>Replaces the logo with an organizer's image, redrawn like a fetched one.</summary>
    public async Task<LogoResult> UploadAsync(string url, byte[] image, CancellationToken cancellationToken = default)
    {
        if (image.Length > MaxUploadBytes || LogoImages.ToPng(image, out _) is not { } png)
        {
            return LogoResult.NotFound;
        }

        var logo = await FindOrAddAsync(url, cancellationToken);
        logo.Png = png;
        logo.IsUploaded = true;
        logo.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return LogoResult.Saved;
    }

    private async Task<OtherAdventLogo> FindOrAddAsync(string url, CancellationToken cancellationToken)
    {
        if (await db.OtherAdventLogos.SingleOrDefaultAsync(l => l.Url == url, cancellationToken) is { } logo)
        {
            return logo;
        }

        logo = new OtherAdventLogo { Url = url };
        db.OtherAdventLogos.Add(logo);
        return logo;
    }
}
