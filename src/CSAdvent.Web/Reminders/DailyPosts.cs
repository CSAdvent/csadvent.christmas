using CSAdvent.Web.Calendar;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Entries;
using CSAdvent.Web.Social.Posting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Reminders;

/// <summary>A day of Advent's posts, as the daily emails list them, once it's time to send them.</summary>
/// <param name="RunDate">Today in the season's own calendar (<see cref="AdventSchedule.InSeason"/>), which emails sent once a day are recorded against.</param>
public sealed record DailyEdition(int Year, int Day, DateOnly RunDate, IReadOnlyList<SharedPost> Posts);

/// <summary>
/// When a day's posts go out by email (#209), to the season's authors (<see cref="ReminderService"/>) and to readers who asked
/// for them (<see cref="Announcements.ReaderDailyEmailService"/>): from <see cref="ReminderOptions.SendAt"/> as soon as every
/// post of the day is ready, or from the cutoff (<see cref="ReminderOptions.DailyEmailCutoff"/>) with whichever are, so a post
/// that arrives during the morning isn't left out. Either way each email still goes once.
/// </summary>
public sealed class DailyPosts(
    AdventDbContext db,
    AdventSchedule schedule,
    IOptions<ReminderOptions> options,
    IOptions<SocialPostingOptions> posting,
    IOptions<SiteOptions> site)
{
    /// <summary>When the daily email stops waiting for the rest of the day's posts: the configured time, or the day's last announcement.</summary>
    public static TimeOnly CutoffFor(int slotsPerDay, ReminderOptions reminders, SocialPostingOptions posting) =>
        reminders.DailyEmailCutoff ?? posting.TimeFor(slotsPerDay);

    /// <summary>
    /// Whether it's time for the daily email: never before the morning send time or without a post to share, and then once
    /// every post of the day is ready, or from the cutoff with what is.
    /// </summary>
    public static bool IsDue(TimeOnly now, TimeOnly sendAt, TimeOnly cutoff, int ready, int slotsPerDay) =>
        ready > 0 && now >= sendAt && (ready >= slotsPerDay || now >= cutoff);

    /// <summary>The season's posts for today if it's time to send them, or null, and a later run asks again.</summary>
    public async Task<DailyEdition?> GetDueAsync(int year, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var local = schedule.ToLocalTime(now).DateTime;
        var today = DateOnly.FromDateTime(local);

        // Only today's posts, which are revealed at midnight: never a post before its reveal (#5).
        if (schedule.GetDayOfAdvent(year, today) is not { } day || now < schedule.GetRevealTime(year, day))
        {
            return null;
        }

        // A post is ready once it has its title and link, unless it was found without the C# Advent blurb (#128): its author
        // may add it, or a backup take the spot, before the cutoff. An open spot isn't ready either, in case it's filled.
        var slotsPerDay = await db.Seasons.Where(s => s.Year == year).Select(s => s.SlotsPerDay).SingleAsync(cancellationToken);
        var ready = await db.Slots
            .AsNoTracking()
            .Where(s => s.SeasonYear == year && s.Day == day && s.AuthorName != null && s.PostTitle != null && s.PostUrl != null
                && !db.BlurbChecks.Any(c => c.SlotId == s.Id && c.PostUrl == s.PostUrl && c.Status == BlurbStatus.Missing))
            .OrderBy(s => s.Part)
            .Select(s => new { s.Part, PostTitle = s.PostTitle!, AuthorName = s.AuthorName! })
            .ToListAsync(cancellationToken);
        if (!IsDue(TimeOnly.FromDateTime(local), options.Value.SendAt, CutoffFor(slotsPerDay, options.Value, posting.Value), ready.Count, slotsPerDay))
        {
            return null;
        }

        // Links go through the tracked redirect, tagged as email.
        return new DailyEdition(
            year,
            day,
            schedule.InSeason(year, today),
            [.. ready.Select(s => new SharedPost(s.PostTitle, EntryLinks.Absolute(site.Value.BaseUrl, EntryLinks.Go(year, day, s.Part, "email")), s.AuthorName))]);
    }
}
