using CSAdvent.Web.Calendar;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static CSAdvent.Web.Email.EmailBlocks;

namespace CSAdvent.Web.Social.Posting;

/// <summary>
/// Checks the credentials of every platform in Automatic mode once a day, from <see cref="SocialPostingOptions.CheckAt"/>,
/// and emails the organizers when one fails, so a revoked or expired token shows up in November, not on December 1.
/// <see cref="SocialPostingWorker"/> runs it. A check an organizer ran that day counts, and dry-run platforms aren't checked.
/// </summary>
public sealed class SocialCredentialCheck(
    AdventDbContext db,
    AdventSchedule schedule,
    SocialPlatformModes modes,
    EmailComposer composer,
    IOptions<SocialPostingOptions> options,
    IOptions<SiteOptions> site)
{
    /// <summary>Checks what's due. Returns the platforms whose credentials failed.</summary>
    public async Task<IReadOnlyList<SocialPlatform>> RunAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var local = schedule.ToLocalTime(now).DateTime;
        if (TimeOnly.FromDateTime(local) < options.Value.CheckAt)
        {
            return [];
        }

        var startOfToday = schedule.FromLocalTime(local.Date);
        var failed = new List<SocialPlatform>();
        foreach (var platform in (await modes.GetAsync(cancellationToken)).Where(p => p.IsAutomatic && !p.IsDryRun))
        {
            // Claim today's check, so two replicas never both check and email. A check earlier today counts.
            var claimed = await db.SocialPlatformSettings
                .Where(s => s.Platform == platform.Platform && (s.CheckedAt == null || s.CheckedAt < startOfToday))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.CheckedAt, now), cancellationToken);
            if (claimed == 0)
            {
                continue;
            }

            if (await modes.CheckAsync(platform.Platform, SocialPostingService.Automation, cancellationToken) is { } error)
            {
                failed.Add(platform.Platform);
                await composer.TrySendAsync(
                    CredentialsFailing(platform.Platform, error, site.Value),
                    site.Value.OrganizersInbox,
                    $"the {platform.Platform} credentials",
                    cancellationToken);
            }
        }

        return failed;
    }

    /// <summary>Tells the organizers a platform's credentials failed today's check.</summary>
    public static EmailContent CredentialsFailing(SocialPlatform platform, string error, SiteOptions site) => new()
    {
        Kind = "social-credentials-failing",
        Subject = $"Automatic posting to {platform} has stopped working",
        Preview = $"C# Advent's {platform} credentials failed today's check.",
        Heading = $"{platform} needs new credentials",
        Body =
        [
            Paragraph($"Today's check of C# Advent's {platform} credentials failed, so automatic posts to {platform} will fail until they're fixed."),
            Details(new DetailRow($"What {platform} said", EmailBlocks.SingleLine(error))),
            Paragraph(
                "Replace them in the repository's production environment (docs/deployment.md in the repository says which secrets, and how " +
                "to get them), run the Deploy workflow, then press Check on the social page. Until then, post announcements by " +
                "hand from there."),
            Button("Open the social page", $"{site.BaseUrl.TrimEnd('/')}/manage/social"),
        ],
        Reason = "You're getting this email because you're a C# Advent organizer.",
    };
}
