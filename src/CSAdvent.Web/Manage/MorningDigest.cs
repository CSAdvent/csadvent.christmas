using System.Globalization;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using CSAdvent.Web.Reminders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static CSAdvent.Web.Email.EmailBlocks;

namespace CSAdvent.Web.Manage;

/// <summary>
/// The organizers' morning digest (#171): the Today panel (<see cref="TodayService"/>) in their inbox at
/// <see cref="ReminderOptions.DigestAt"/> (8 AM Eastern), once a day for each season on its days of Advent, so only in December,
/// and on the days of the test season's rehearsal. <see cref="ReminderWorker"/> runs it every 15 minutes, after the blurb
/// check, the reminders and the backups, so it sees what they did.
/// <list type="bullet">
/// <item>Recorded in <c>sent_reminders</c> like the other once-a-day emails, against the season's first slot and the date in the
/// season's own calendar (<see cref="AdventSchedule.InSeason"/>): each season gets its own, moving the rehearsal sends the new
/// day's, and resetting the test season forgets them. Each run skips what's recorded (#157), and one that can't be queued
/// removes its record so a later run tries again.</item>
/// <item>It goes through the email queue to the organizers' inbox, so it's urgent and has no unsubscribe link, and it's no use
/// once its day is over.</item>
/// <item>It never names a post, or links to it, before its reveal: tomorrow's spots say only whether their post is in.</item>
/// </list>
/// </summary>
public sealed class MorningDigest(
    AdventDbContext db,
    AdventSchedule schedule,
    TodayService reports,
    EmailComposer composer,
    IOptions<ReminderOptions> options,
    IOptions<SiteOptions> site)
{
    private const string ForOrganizers = "You're getting this email because you're a C# Advent organizer.";

    /// <summary>Sends each season's digest that's due. Returns how many went.</summary>
    /// <param name="onlyYear">Only this season ("send due now" for the test season, and the integration tests).</param>
    public async Task<int> RunAsync(DateTimeOffset now, int? onlyYear = null, CancellationToken cancellationToken = default)
    {
        var local = schedule.ToLocalTime(now).DateTime;
        if (TimeOnly.FromDateTime(local) < options.Value.DigestAt)
        {
            return 0;
        }

        var today = DateOnly.FromDateTime(local);
        var years = (await db.Seasons.Select(s => s.Year).ToListAsync(cancellationToken))
            .Where(year => (onlyYear is null || year == onlyYear) && schedule.GetDayOfAdvent(year, today) is not null)
            .Order()
            .ToList();

        var sent = 0;
        foreach (var year in years)
        {
            if (await SendAsync(year, today, now, cancellationToken))
            {
                sent++;
            }
        }

        return sent;
    }

    private async Task<bool> SendAsync(int year, DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var firstSlot = await db.Slots
            .Where(s => s.SeasonYear == year)
            .OrderBy(s => s.Day).ThenBy(s => s.Part)
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var runDate = schedule.InSeason(year, today);
        if (firstSlot is not { } slotId
            || await db.SentReminders.RecordedSlots(ReminderKind.AdminMorningDigest, runDate).ContainsAsync(slotId, cancellationToken))
        {
            return false;
        }

        var report = await reports.GetAsync(now, year, cancellationToken);
        if (report.Seasons is not [{ Today: { } day } season])
        {
            return false;
        }

        // It's about today, so it's no use tomorrow.
        var content = Email(report, season, site.Value) with { SendBefore = schedule.GetDayEndTime(year, day.Day) };

        var record = new SentReminder { Kind = ReminderKind.AdminMorningDigest, RunDate = runDate, SlotId = slotId, SentAt = now };
        db.SentReminders.Add(record);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // Another run (or replica) recorded it at the same moment.
            db.Entry(record).State = EntityState.Detached;
            return false;
        }

        if (!await composer.TrySendAsync(content, site.Value.OrganizersInbox, $"the {year} morning digest", cancellationToken))
        {
            // A later run tries again.
            db.SentReminders.Remove(record);
            await db.SaveChangesAsync(CancellationToken.None);
            return false;
        }

        return true;
    }

    /// <summary>The digest of one season's day: the same as its part of the Today panel, without what isn't public yet.</summary>
    public static EmailContent Email(TodayReport report, TodaySeason season, SiteOptions site)
    {
        var baseUrl = site.BaseUrl.TrimEnd('/');
        var day = (season.Today ?? season.Days[0]).Day;
        var overview = season.IsTest ? $"{baseUrl}/manage?year={season.Year}" : $"{baseUrl}/manage";

        List<EmailBlock> body =
        [
            Paragraph(season.IsTest
                ? $"Here's the test season (C# Advent {season.Year}) on December {day} of its rehearsal. None of it is public."
                : $"Here's where C# Advent {season.Year} stands this morning."),
        ];

        foreach (var shown in season.Days)
        {
            body.Add(Paragraph($"{(shown.IsToday ? "Today" : "Tomorrow")}, December {shown.Day}:"));
            body.Add(shown.Spots.Count == 0
                ? Paragraph("No spots.")
                : new ListBlock([.. shown.Spots.Select(spot => SpotLine(spot, shown, report, season, baseUrl))]));
        }

        if (season.Backups.Count > 0)
        {
            body.Add(Paragraph("Backup posters:"));
            body.Add(new ListBlock([.. season.Backups.Select(backup => (IReadOnlyList<EmailSpan>)
            [
                backup.StandingBy
                    ? $"{season.Spot(backup.Day, backup.Part)}: {backup.Name} is standing by to take the spot."
                    : $"{season.Spot(backup.Day, backup.Part)}: {backup.Name} has been asked, and has until {report.Describe(backup.ExpiresAt)} to answer.",
            ])]));
        }

        if (season.FailedPosts.Count > 0)
        {
            body.Add(Paragraph("Posts that didn't go out:"));
            body.Add(new ListBlock([.. season.FailedPosts.Select(post => (IReadOnlyList<EmailSpan>)
            [
                $"{season.Spot(post.Day, post.Part)} on {post.Platform}: ",
                Link(post.Unconfirmed ? "did it go out?" : "failed", $"{baseUrl}/manage/social?year={season.Year}&day={post.Day}"),
                post.Error is { } error ? $" {error}" : "",
            ])]));
        }

        if (report.FailedEmails.Count > 0)
        {
            var kinds = report.FailedEmails
                .GroupBy(e => EmailCatalog.Find(e.Kind)?.Name ?? e.Kind)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => string.Create(CultureInfo.InvariantCulture, $"{g.Key} ({g.Count()})"));
            body.Add(Paragraph(
                $"{Plural(report.FailedEmails.Count, "email")} the queue gave up on: {string.Join(", ", kinds)}. ",
                Link("Retry or discard them", $"{baseUrl}/manage/emails/queue"),
                "."));
        }

        body.Add(Details(
            new DetailRow("Requests waiting", Count(report.Queues.Requests)),
            new DetailRow("Post claims waiting", Count(report.Queues.PostClaims)),
            new DetailRow("Link changes waiting", Count(report.Queues.LinkChanges)),
            new DetailRow("Emails waiting to go", Count(report.Queues.EmailsWaiting)),
            new DetailRow("Failed emails", Count(report.Queues.FailedEmails))));
        body.Add(Button("Open the organizer overview", overview));

        return new EmailContent
        {
            Kind = "morning-digest",
            Subject = $"C# Advent {season.Year}, December {day}: the morning digest",
            Preview = Preview(report, season),
            Heading = season.IsTest ? $"December {day} at a glance (test season)" : $"December {day} at a glance",
            Body = body,
            Reason = ForOrganizers,
        };
    }

    /// <summary>
    /// One spot. Before its reveal, only whether its post is in: never its title or link, and nothing a backup has sent.
    /// Contact addresses stay on the organizer pages.
    /// </summary>
    private static IReadOnlyList<EmailSpan> SpotLine(TodaySpot spot, TodayDay day, TodayReport report, TodaySeason season, string baseUrl)
    {
        var entry = spot.Entry;
        List<EmailSpan> line = [spot.NeedsAttention ? "Needs attention: " : ""];
        line.Add(season.SlotsPerDay > 1 ? $"Post {entry.Part}: " : "");
        if (entry.AuthorName is not { } author)
        {
            line.Add(TodayLabels.Entry(entry));
            line.Add(".");
            return line;
        }

        line.Add(author);
        if (day.IsRevealed && entry.PostUrl is { } url)
        {
            line.Add(", ");
            line.Add(Link(entry.PostTitle ?? url, url));
        }

        line.Add($". {TodayLabels.Entry(entry)}.");
        if (spot.Blurb is { } blurb)
        {
            line.Add($" {TodayLabels.Blurb(blurb)}.");
        }

        if (spot.Posting.Count > 0)
        {
            line.Add(" " + string.Join("; ", spot.Posting.Select(p =>
                $"{p.Platform}: {LowerFirst(TodayLabels.Posting(p, spot.AnnouncedAt, report.TimeZone))}")) + ".");
        }

        line.Add(" ");
        line.Add(Link("Open", $"{baseUrl}/manage/entries/{entry.SlotId}"));
        return line;
    }

    /// <summary>What needs the organizers, in a line: "2 spots need attention, 1 email failed."</summary>
    public static string Preview(TodayReport report, TodaySeason season)
    {
        var attention = season.Days.Sum(d => d.Spots.Count(s => s.NeedsAttention));
        List<string> parts = [];
        if (attention > 0)
        {
            parts.Add(attention == 1 ? "1 spot needs attention" : $"{Count(attention)} spots need attention");
        }

        if (season.FailedPosts.Count > 0)
        {
            parts.Add(season.FailedPosts.Count == 1 ? "1 post didn't go out" : $"{Count(season.FailedPosts.Count)} posts didn't go out");
        }

        if (report.FailedEmails.Count > 0)
        {
            parts.Add(report.FailedEmails.Count == 1 ? "1 email failed" : $"{Count(report.FailedEmails.Count)} emails failed");
        }

        return parts.Count == 0 ? "Nothing needs you this morning." : string.Join(", ", parts) + ".";
    }

    private static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);

    /// <summary>"Automatic at 10 AM" as "automatic at 10 AM", after a platform's name.</summary>
    private static string LowerFirst(string label) => label.Length == 0 ? label : char.ToLowerInvariant(label[0]) + label[1..];

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{Count(count)} {noun}s";
}
