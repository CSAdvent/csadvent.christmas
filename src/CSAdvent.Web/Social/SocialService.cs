using CSAdvent.Web.Calendar;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CSAdvent.Web.Social;

/// <summary>One entry's announcements for a day, with what's been posted.</summary>
public sealed record EntryAnnouncements(
    int SlotId,
    int Part,
    string Title,
    string Author,
    IReadOnlyList<FormattedAnnouncement> Platforms,
    IReadOnlyDictionary<SocialPlatform, SocialPost> Posted);

public sealed record AnnouncementDay(int Year, int Day, bool IsRevealed, DateTimeOffset RevealsAt, IReadOnlyList<EntryAnnouncements> Entries);

public enum MarkOutcome
{
    Marked,
    NotRevealed,
    NotFound,
}

/// <summary>
/// The announcements for each day's entries, and which have been posted where. Entries without a post (dropouts) get
/// none. Organizers can preview upcoming days, but nothing can be marked posted before its reveal (#5).
/// </summary>
public sealed class SocialService(AdventDbContext db, AdventSchedule schedule, TimeProvider timeProvider, IOptions<SiteOptions> site)
{
    public async Task<AnnouncementDay> GetDayAsync(int year, int day, CancellationToken cancellationToken = default)
    {
        var slots = await db.Slots
            .AsNoTracking()
            .Where(s => s.SeasonYear == year && s.Day == day && s.AuthorName != null && s.PostTitle != null && s.PostUrl != null)
            .OrderBy(s => s.Part)
            .Select(s => new
            {
                s.Id,
                s.Part,
                s.Season.SlotsPerDay,
                Title = s.PostTitle!,
                Author = s.AuthorName!,
                Accounts = s.Contributor == null
                    ? null
                    : new AuthorAccounts(s.Contributor.BlueskyHandle, s.Contributor.MastodonHandle, s.Contributor.LinkedInUrl, s.Contributor.FacebookUrl, s.Contributor.RedditUsername),
            })
            .ToListAsync(cancellationToken);

        var slotIds = slots.Select(s => s.Id).ToList();
        var posts = await db.SocialPosts.AsNoTracking().Where(p => slotIds.Contains(p.SlotId)).ToListAsync(cancellationToken);

        var mastodonInstance = MastodonInstance(site.Value.MastodonCreator);
        var entries = slots.Select(s =>
        {
            var announcement = new Announcement(year, day, s.Part, s.SlotsPerDay, s.Title, s.Author, s.Accounts ?? AuthorAccounts.None, site.Value.BaseUrl);
            return new EntryAnnouncements(
                s.Id,
                s.Part,
                s.Title,
                s.Author,
                [.. Enum.GetValues<SocialPlatform>().Select(platform => AnnouncementFormatter.Format(announcement, platform, mastodonInstance))],
                posts.Where(p => p.SlotId == s.Id).ToDictionary(p => p.Platform));
        });

        return new AnnouncementDay(year, day, schedule.IsRevealed(year, day), schedule.GetRevealTime(year, day), [.. entries]);
    }

    /// <summary>Records that an entry was posted on a platform (or updates the record). Not before the entry's reveal.</summary>
    public async Task<MarkOutcome> MarkPostedAsync(int slotId, SocialPlatform platform, string? postUrl, string markedBy, CancellationToken cancellationToken = default)
    {
        var slot = await db.Slots.AsNoTracking()
            .Where(s => s.Id == slotId && s.AuthorName != null && s.PostTitle != null && s.PostUrl != null)
            .Select(s => new { s.SeasonYear, s.Day })
            .SingleOrDefaultAsync(cancellationToken);
        if (slot is null)
        {
            return MarkOutcome.NotFound;
        }

        if (!schedule.IsRevealed(slot.SeasonYear, slot.Day))
        {
            return MarkOutcome.NotRevealed;
        }

        var existing = await db.SocialPosts.SingleOrDefaultAsync(p => p.SlotId == slotId && p.Platform == platform, cancellationToken);
        if (existing is null)
        {
            db.SocialPosts.Add(new SocialPost
            {
                SlotId = slotId,
                Platform = platform,
                Status = SocialPostStatus.Posted,
                At = timeProvider.GetUtcNow(),
                PostUrl = postUrl,
                MarkedBy = markedBy,
            });
        }
        else
        {
            existing.Status = SocialPostStatus.Posted;
            existing.At = timeProvider.GetUtcNow();
            existing.PostUrl = postUrl ?? existing.PostUrl;
            existing.MarkedBy = markedBy;
            existing.Error = null;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another organizer marked it at the same moment; it's marked either way.
        }

        return MarkOutcome.Marked;
    }

    public Task UnmarkAsync(int slotId, SocialPlatform platform, CancellationToken cancellationToken = default) =>
        db.SocialPosts.Where(p => p.SlotId == slotId && p.Platform == platform).ExecuteDeleteAsync(cancellationToken);

    /// <summary>The server of a Mastodon handle such as <c>@csadvent@hachyderm.io</c>, for the share link.</summary>
    public static string? MastodonInstance(string? handle) =>
        handle?.Split('@', StringSplitOptions.RemoveEmptyEntries) is [_, var server] ? server : null;
}
