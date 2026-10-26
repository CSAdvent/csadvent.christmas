using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Content;

/// <summary>An organizer's GitHub profile photo, as GitHub sent it.</summary>
public sealed record OrganizerPhoto(byte[] Image, string ContentType);

/// <summary>
/// The organizers' GitHub profile photos for the About page, fetched by the site and served as its own (#168), so visitors'
/// browsers never ask GitHub for them, which would give GitHub their IP address. Only the logins in <c>Site:Organizers</c>
/// are fetched, so it can't be used to fetch anything else. A photo is kept in memory for a day, and a failed fetch is tried
/// again after a few minutes.
/// </summary>
public sealed class OrganizerPhotos(HttpClient http, IOptionsMonitor<SiteOptions> site, TimeProvider timeProvider)
{
    /// <summary>The size asked for: the founder's photo is 128 pixels across, so this is sharp on high-resolution screens.</summary>
    public const int Size = 256;

    private const int MaxBytes = 1024 * 1024;

    private static readonly TimeSpan KeptFor = TimeSpan.FromDays(1);

    private static readonly TimeSpan FailureKeptFor = TimeSpan.FromMinutes(5);

    private static readonly string[] ImageTypes = ["image/png", "image/jpeg", "image/gif", "image/webp"];

    private readonly ConcurrentDictionary<string, (OrganizerPhoto? Photo, DateTimeOffset Until)> _photos = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A client that gives up on GitHub after 10 seconds. Its own, so the factory's retries don't hold up the page.</summary>
    public static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CSAdvent/1.0 (+https://csadvent.christmas/about)");
        return http;
    }

    /// <summary>The photo's link on our site (<c>about/photos/{login}</c>).</summary>
    public static string Link(string login) => $"about/photos/{Uri.EscapeDataString(login.ToLowerInvariant())}";

    /// <summary>The organizer's photo, or null if <paramref name="login"/> isn't an organizer's or GitHub didn't send one.</summary>
    public async Task<OrganizerPhoto?> GetAsync(string login, CancellationToken cancellationToken = default)
    {
        if (site.CurrentValue.Organizers.FirstOrDefault(o => string.Equals(o.GitHub, login, StringComparison.OrdinalIgnoreCase)) is not { } organizer)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (_photos.TryGetValue(organizer.GitHub, out var kept) && kept.Until > now)
        {
            return kept.Photo;
        }

        var photo = await FetchAsync(organizer.GitHub, cancellationToken);
        _photos[organizer.GitHub] = (photo, now + (photo is null ? FailureKeptFor : KeptFor));
        return photo;
    }

    private async Task<OrganizerPhoto?> FetchAsync(string login, CancellationToken cancellationToken)
    {
        try
        {
            // GitHub redirects this to the photo itself, on avatars.githubusercontent.com.
            using var response = await http.GetAsync($"https://github.com/{Uri.EscapeDataString(login)}.png?size={Size}", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode
                || response.Content.Headers.ContentType?.MediaType is not { } contentType
                || !ImageTypes.Contains(contentType)
                || response.Content.Headers.ContentLength > MaxBytes)
            {
                return null;
            }

            var image = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return image.Length is > 0 and <= MaxBytes ? new OrganizerPhoto(image, contentType) : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}

public static class OrganizerPhotoEndpoints
{
    public static WebApplication MapOrganizerPhotos(this WebApplication app)
    {
        app.MapGet("/about/photos/{login}", async (string login, OrganizerPhotos photos, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (await photos.GetAsync(login, cancellationToken) is not { } photo)
            {
                return Results.NotFound();
            }

            context.Response.Headers.CacheControl = "public, max-age=86400";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(photo.Image, photo.ContentType);
        });

        return app;
    }
}
