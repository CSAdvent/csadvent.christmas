using CSAdvent.Web.Calendar;
using CSAdvent.Web.Data;
using CSAdvent.Web.SeasonTesting;
using CSAdvent.Web.Social;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Entries;

/// <summary>Links to an entry's page and to its tracked "Read the post" redirect.</summary>
public static class EntryLinks
{
    /// <summary>The entry's page on our site, relative to the base URL. Social posts link here, for the preview card.</summary>
    public static string Page(int year, int day, int part, string? via = null) => $"{year}/day/{day}/{part}{Query(via)}";

    /// <summary>The redirect that counts a click and sends the reader on to the author's post.</summary>
    public static string Go(int year, int day, int part, string? via = null) => $"go/{year}/{day}/{part}{Query(via)}";

    public static string Absolute(string baseUrl, string relative) => $"{baseUrl.TrimEnd('/')}/{relative}";

    private static string Query(string? via) => via is null ? "" : $"?via={Uri.EscapeDataString(via)}";
}

/// <summary>A revealed post on the same day, for the entry page.</summary>
public sealed record SiblingEntry(int Part, string PostTitle, string AuthorName);

/// <summary>
/// An entry's page. Before its reveal it carries nothing about the post or its author (#5), only when it's revealed.
/// </summary>
public sealed record EntryView(
    int SlotId,
    int Year,
    int Day,
    int Part,
    int SlotsPerDay,
    bool IsRevealed,
    DateTimeOffset RevealsAt,
    string? PostTitle,
    string? PostUrl,
    string? AuthorName,
    string? AuthorUrl,
    IReadOnlyList<AuthorLink> AuthorLinks,
    IReadOnlyList<SiblingEntry> Siblings,
    bool IsSeasonOver)
{
    public string Spot => SlotsPerDay > 1 ? $"December {Day}, {Year} · Post {Part}" : $"December {Day}, {Year}";
}

/// <summary>Entry pages, the tracked redirect to each post, and their visit and click counts.</summary>
public sealed class EntryService(AdventDbContext db, AdventSchedule schedule, TimeProvider timeProvider, SeasonAccess access)
{
    /// <summary>
    /// The entry, or nothing if the spot doesn't exist, or it's been revealed without a post (an author who never published
    /// isn't named, and gets no page).
    /// </summary>
    public async Task<EntryView?> GetAsync(int year, int day, int part, CancellationToken cancellationToken = default)
    {
        // A season being tested has no entries, as far as anyone but its testers can tell.
        if (!access.CanSee(year))
        {
            return null;
        }

        var slots = await db.Slots
            .AsNoTracking()
            .Where(s => s.SeasonYear == year && s.Day == day)
            .OrderBy(s => s.Part)
            .Select(s => new
            {
                s.Id,
                s.Part,
                s.Season.SlotsPerDay,
                s.AuthorName,
                s.AuthorUrl,
                s.PostTitle,
                s.PostUrl,
                GithubLogin = s.Contributor != null ? s.Contributor.GithubLogin : null,
                Bluesky = s.Contributor != null ? s.Contributor.BlueskyHandle : null,
                Mastodon = s.Contributor != null ? s.Contributor.MastodonHandle : null,
                LinkedIn = s.Contributor != null ? s.Contributor.LinkedInUrl : null,
                Facebook = s.Contributor != null ? s.Contributor.FacebookUrl : null,
                Reddit = s.Contributor != null ? s.Contributor.RedditUsername : null,
            })
            .ToListAsync(cancellationToken);
        var slot = slots.SingleOrDefault(s => s.Part == part);
        if (slot is null)
        {
            return null;
        }

        var revealsAt = schedule.GetRevealTime(year, day);
        if (!schedule.IsRevealed(year, day))
        {
            return new EntryView(slot.Id, year, day, part, slot.SlotsPerDay, false, revealsAt, null, null, null, null, [], [], IsSeasonOver: false);
        }

        if (slot.PostTitle is null || slot.PostUrl is null || slot.AuthorName is null)
        {
            return null;
        }

        return new EntryView(
            slot.Id,
            year,
            day,
            part,
            slot.SlotsPerDay,
            true,
            revealsAt,
            slot.PostTitle,
            slot.PostUrl,
            slot.AuthorName,
            AuthorLinks.ForName(slot.AuthorUrl, slot.GithubLogin),
            AuthorLinks.All(slot.AuthorUrl, slot.GithubLogin, new AuthorAccounts(slot.Bluesky, slot.Mastodon, slot.LinkedIn, slot.Facebook, slot.Reddit)),
            [.. slots
                .Where(s => s.Part != part && s.PostTitle is not null && s.PostUrl is not null && s.AuthorName is not null)
                .Select(s => new SiblingEntry(s.Part, s.PostTitle!, s.AuthorName!))],
            schedule.IsSeasonOver(year));
    }

    /// <summary>The post a click should go to: only a revealed entry's own post, so the redirect can't be pointed anywhere else.</summary>
    public async Task<(int SlotId, string PostUrl)?> FindPostAsync(int year, int day, int part, CancellationToken cancellationToken = default)
    {
        if (!access.CanSee(year) || !schedule.IsRevealed(year, day))
        {
            return null;
        }

        var slot = await db.Slots
            .AsNoTracking()
            .Where(s => s.SeasonYear == year && s.Day == day && s.Part == part && s.PostUrl != null && s.AuthorName != null)
            .Select(s => new { s.Id, s.PostUrl })
            .SingleOrDefaultAsync(cancellationToken);
        return slot is null ? null : (slot.Id, slot.PostUrl!);
    }

    public async Task RecordAsync(int slotId, EntryHitKind kind, string? via, CancellationToken cancellationToken = default)
    {
        db.EntryHits.Add(new EntryHit { SlotId = slotId, Kind = kind, Via = EntryVia.Normalize(via), At = timeProvider.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Clicks per slot for a season, for the organizers' entries list.</summary>
    public Task<Dictionary<int, int>> CountClicksAsync(int year, CancellationToken cancellationToken = default) =>
        db.EntryHits
            .Where(h => h.Slot.SeasonYear == year && h.Kind == EntryHitKind.Click)
            .GroupBy(h => h.SlotId)
            .Select(g => new { SlotId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.SlotId, g => g.Count, cancellationToken);
}
