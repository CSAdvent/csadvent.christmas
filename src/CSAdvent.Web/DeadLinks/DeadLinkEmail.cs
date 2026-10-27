using CSAdvent.Web.Calendar;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static CSAdvent.Web.Email.EmailBlocks;

namespace CSAdvent.Web.DeadLinks;

/// <summary>A post found dead, for the organizers' email.</summary>
/// <param name="CopyLookedUp">Whether the Wayback Machine has been asked for a copy yet.</param>
public sealed record DeadPost(
    int SlotId,
    int Year,
    int Day,
    int Part,
    int SlotsPerDay,
    string? Title,
    string? AuthorName,
    string PostUrl,
    string? Detail,
    string? ArchivedUrl,
    bool CopyLookedUp)
{
    public string Spot => SlotsPerDay > 1 ? $"December {Day}, {Year} · Post {Part}" : $"December {Day}, {Year}";
}

/// <summary>
/// Tells the organizers about the posts the dead-link check found dead (#201): one email a day listing them, rather than an
/// email per post, because the first pass over the archive finds dozens within a few days. <see cref="LinkCheckWorker"/> runs it
/// after each check, as a step of its own.
/// <list type="bullet">
/// <item>From <see cref="LinkCheckOptions.EmailAt"/> (8 AM Eastern), it sends every post found dead before then that the
/// organizers haven't heard about, so a post found later in the day waits for the next morning's email.</item>
/// <item>Each post is claimed (<see cref="LinkCheck.OrganizersEmailedAt"/>) with a conditional update before the email is
/// queued, so replicas never both list it, and the claims are undone if it can't be queued, so a later run tries again.</item>
/// <item>Never a post an organizer marked gone or checked themselves, since they've seen it. A post that loads again and dies
/// again is in a later email.</item>
/// </list>
/// </summary>
public sealed class DeadLinkEmail(
    AdventDbContext db,
    AdventSchedule schedule,
    EmailComposer composer,
    IOptions<LinkCheckOptions> options,
    IOptions<SiteOptions> site)
{
    /// <summary>Sends the email if any post is due in it. Returns how many posts it listed.</summary>
    /// <param name="onlyYear">Only this season (the integration tests use it so seasons don't affect each other).</param>
    public async Task<int> RunAsync(DateTimeOffset now, int? onlyYear = null, CancellationToken cancellationToken = default)
    {
        var foundBefore = Cutoff(now, options.Value.EmailAt, schedule.TimeZone);
        var due = await LinkCheckService.Current(db.LinkChecks)
            .AsNoTracking()
            .Where(c => c.Status == LinkStatus.Dead && c.OrganizersEmailedAt == null && c.DeadSince <= foundBefore
                && (onlyYear == null || c.Slot.SeasonYear == onlyYear))
            .OrderByDescending(c => c.Slot.SeasonYear).ThenBy(c => c.Slot.Day).ThenBy(c => c.Slot.Part)
            .Select(c => new
            {
                c.Id,
                Post = new DeadPost(
                    c.SlotId,
                    c.Slot.SeasonYear,
                    c.Slot.Day,
                    c.Slot.Part,
                    c.Slot.Season.SlotsPerDay,
                    c.Slot.PostTitle,
                    c.Slot.AuthorName,
                    c.PostUrl,
                    c.Detail,
                    c.ArchivedUrl,
                    c.ArchiveCheckedAt != null),
            })
            .ToListAsync(cancellationToken);

        List<int> claimed = [];
        List<DeadPost> posts = [];
        foreach (var row in due)
        {
            var claim = await db.LinkChecks
                .Where(c => c.Id == row.Id && c.Status == LinkStatus.Dead && c.OrganizersEmailedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.OrganizersEmailedAt, now), cancellationToken);
            if (claim == 1)
            {
                claimed.Add(row.Id);
                posts.Add(row.Post);
            }
        }

        if (posts.Count == 0)
        {
            return 0;
        }

        if (!await composer.TrySendAsync(Email(posts, options.Value.FailuresBeforeDead, site.Value), site.Value.OrganizersInbox, "the dead links email", cancellationToken))
        {
            // A later run tries again.
            await db.LinkChecks
                .Where(c => claimed.Contains(c.Id) && c.OrganizersEmailedAt == now)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.OrganizersEmailedAt, (DateTimeOffset?)null), CancellationToken.None);
            return 0;
        }

        return posts.Count;
    }

    /// <summary>The last time it was <paramref name="at"/> in the reveal time zone, at or before <paramref name="now"/>.</summary>
    public static DateTimeOffset Cutoff(DateTimeOffset now, TimeOnly at, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(now, timeZone).DateTime;
        var date = DateOnly.FromDateTime(local);
        if (TimeOnly.FromDateTime(local) < at)
        {
            date = date.AddDays(-1);
        }

        var unspecified = date.ToDateTime(at, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, timeZone.GetUtcOffset(unspecified)).ToUniversalTime();
    }

    public static EmailContent Email(IReadOnlyList<DeadPost> posts, int failuresBeforeDead, SiteOptions site)
    {
        var baseUrl = site.BaseUrl.TrimEnd('/');
        var one = posts.Count == 1;
        var withCopy = posts.Count(p => p.ArchivedUrl is not null);
        return new EmailContent
        {
            Kind = "dead-links-found",
            Subject = one ? "An older C# Advent post is gone" : $"{posts.Count} older C# Advent posts are gone",
            Preview = one
                ? withCopy == 1 ? "Readers now get its saved copy." : "There's no saved copy of it yet, so readers still go to its address."
                : withCopy switch
                {
                    0 => "There's no saved copy of any of them yet.",
                    _ when withCopy == posts.Count => "Readers now get a saved copy of each of them.",
                    _ => $"Readers now get a saved copy of {withCopy} of them.",
                },
            Heading = one ? "A post's link is dead" : $"{posts.Count} posts' links are dead",
            Body =
            [
                Paragraph(one
                    ? $"The dead-link check couldn't load this post {failuresBeforeDead} times in a row, days apart, so it's marked dead."
                    : $"The dead-link check couldn't load these posts {failuresBeforeDead} times in a row, days apart, so they're marked dead."),
                new ListBlock([.. posts.Select(post => Line(post, baseUrl))]),
                Paragraph(
                    "On the dead links page you can check a post again, say it works (for a site that only turns checks like ours away), " +
                    "give it a new link if its author moved it, or give it a saved copy of your own."),
                Button("Open the dead links page", $"{baseUrl}/{LinkCheckService.DeadLinksPage}"),
            ],
            Reason = "You're getting this email because you're a C# Advent organizer.",
        };
    }

    /// <summary>"December 3, 2019 · Post 2: Title by Ada. https://… The page answered 404 Not Found. Readers get its saved copy. Open"</summary>
    private static List<EmailSpan> Line(DeadPost post, string baseUrl)
    {
        List<EmailSpan> line =
        [
            $"{SingleLine($"{post.Spot}: {post.Title ?? "Untitled"} by {post.AuthorName}.")} ",
            Link(post.PostUrl, post.PostUrl),
            post.Detail is { } detail ? $" {SingleLine(detail)}" : "",
        ];

        if (post.ArchivedUrl is { } copy)
        {
            line.AddRange([" Readers get ", Link("its saved copy", copy), "."]);
        }
        else
        {
            line.Add(post.CopyLookedUp
                ? " The Wayback Machine has no copy of it, so readers still go to its address."
                : " It hasn't been looked up at the Wayback Machine yet, so readers still go to its address.");
        }

        line.AddRange([" ", Link("Open", $"{baseUrl}/{LinkCheckService.DeadLinksPage}#post-{post.SlotId}")]);
        return line;
    }
}
