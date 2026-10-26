using CSAdvent.Web.Blurbs;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Social;

/// <summary>One entry's announcements for a day, with what's been posted.</summary>
/// <param name="Blurb">
/// Where its post stands on the C# Advent blurb (#128), when the season needs one and posts are being checked: a post that
/// hasn't been checked yet is <see cref="BlurbStatus.Waiting"/>.
/// </param>
public sealed record EntryAnnouncements(
    int SlotId,
    int Part,
    string Title,
    string Author,
    IReadOnlyList<FormattedAnnouncement> Platforms,
    IReadOnlyDictionary<SocialPlatform, SocialPost> Posted,
    BlurbState? Blurb = null)
{
    /// <summary>Whether automatic posting can announce it: no blurb check applies, or its post passed or was let through.</summary>
    public bool CanAnnounce => Blurb is null || Blurb.CanAnnounce;

    /// <summary>The post in one line, if its author gave one (#208).</summary>
    public string? Summary { get; init; }
}

public sealed record AnnouncementDay(int Year, int Day, int SlotsPerDay, bool IsRevealed, DateTimeOffset RevealsAt, IReadOnlyList<EntryAnnouncements> Entries);

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
public sealed class SocialService(
    AdventDbContext db,
    AdventSchedule schedule,
    BlurbQueries blurbs,
    TimeProvider timeProvider,
    IOptions<SiteOptions> site,
    IOptions<SocialAccountsOptions> accounts)
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
                Summary = s.PostSummary,
                Accounts = s.Contributor == null
                    ? null
                    : new AuthorAccounts(s.Contributor.BlueskyHandle, s.Contributor.MastodonHandle, s.Contributor.LinkedInUrl, s.Contributor.FacebookUrl, s.Contributor.RedditUsername, s.Contributor.XUsername),
            })
            .ToListAsync(cancellationToken);

        var slotIds = slots.Select(s => s.Id).ToList();
        var posts = await db.SocialPosts.AsNoTracking().Where(p => slotIds.Contains(p.SlotId)).ToListAsync(cancellationToken);

        // Seasons with the blurb rule hold each post until its check passes; one not checked yet is waiting.
        var checks = blurbs.IsEnabled && BlurbRules.AppliesTo(year) ? await blurbs.GetSeasonAsync(year, cancellationToken) : null;

        var entries = slots.Select(s =>
        {
            var announcement = new Announcement(year, day, s.Part, s.SlotsPerDay, s.Title, s.Author, s.Accounts ?? AuthorAccounts.None, site.Value.BaseUrl) { Summary = s.Summary };
            return new EntryAnnouncements(
                s.Id,
                s.Part,
                s.Title,
                s.Author,
                [.. Enum.GetValues<SocialPlatform>().Select(platform => AnnouncementFormatter.Format(announcement, platform, accounts.Value))],
                posts.Where(p => p.SlotId == s.Id).ToDictionary(p => p.Platform),
                checks is null ? null : checks.GetValueOrDefault(s.Id, new BlurbState(BlurbStatus.Waiting, null, schedule.Now, null, null, false)))
            {
                Summary = s.Summary,
            };
        });

        var slotsPerDay = await db.Seasons.Where(s => s.Year == year).Select(s => s.SlotsPerDay).SingleOrDefaultAsync(cancellationToken);
        return new AnnouncementDay(year, day, slotsPerDay, schedule.IsRevealed(year, day), schedule.GetRevealTime(year, day), [.. entries]);
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
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // Another organizer marked it at the same moment; it's marked either way.
        }

        return MarkOutcome.Marked;
    }

    public Task UnmarkAsync(int slotId, SocialPlatform platform, CancellationToken cancellationToken = default) =>
        db.SocialPosts.Where(p => p.SlotId == slotId && p.Platform == platform).ExecuteDeleteAsync(cancellationToken);
}
