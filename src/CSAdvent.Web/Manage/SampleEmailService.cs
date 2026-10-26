using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Manage;

/// <summary>Someone a sample email can go to, and why they're on the list.</summary>
public sealed record SampleRecipient(string Address, string Label);

public enum SampleEmailOutcome
{
    /// <summary>In the email queue, ahead of everything but other urgent emails.</summary>
    Sent,

    /// <summary>The email or the address isn't one of the choices on the page.</summary>
    NotAllowed,

    /// <summary>It couldn't be queued (the database failed). The email service's own refusals show in the email queue.</summary>
    Failed,
}

/// <summary>
/// Sends any email in <see cref="EmailCatalog"/> right away, with its sample data, so organizers can see what lands in an
/// inbox without waiting for the date that would trigger it. It only goes to people on the page's list (the organizer,
/// the organizers' inbox, or a signed-in tester), never to an address typed in. It goes through the email queue like
/// everything else, first in line, so a sample proves the queue is sending too.
/// </summary>
public sealed class SampleEmailService(AdventDbContext db, EmailComposer composer, IOptions<SiteOptions> site)
{
    /// <summary>Starts every sample's subject, so nobody mistakes it for the real thing.</summary>
    public const string SubjectPrefix = "[Sample] ";

    public async Task<IReadOnlyList<SampleRecipient>> GetRecipientsAsync(ClaimsPrincipal organizer, CancellationToken cancellationToken = default)
    {
        var recipients = new List<SampleRecipient>();
        if (organizer.FindFirstValue(ClaimTypes.Email) is { Length: > 0 } mine)
        {
            recipients.Add(new SampleRecipient(mine, $"Me ({mine})"));
        }

        recipients.Add(new SampleRecipient(site.Value.ContactEmail, $"The organizers' inbox ({site.Value.ContactEmail})"));

        // Testers who've signed in, at the verified address they'd get real emails at.
        var testers = await db.Contributors
            .AsNoTracking()
            .Where(c => c.Email != null && c.EmailVerifiedAt != null
                && db.SeasonTesters.Any(t => t.SeasonYear == Season.TestYear && t.GithubUserId == c.GithubUserId))
            .OrderBy(c => c.GithubLogin)
            .Select(c => new { c.GithubLogin, Email = c.Email! })
            .ToListAsync(cancellationToken);
        recipients.AddRange(testers.Select(t => new SampleRecipient(t.Email, $"Tester {t.GithubLogin} ({t.Email})")));

        return [.. recipients.DistinctBy(r => r.Address, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<SampleEmailOutcome> SendAsync(string key, string address, ClaimsPrincipal organizer, CancellationToken cancellationToken = default)
    {
        var recipients = await GetRecipientsAsync(organizer, cancellationToken);
        if (EmailCatalog.Find(key) is not { } entry || !recipients.Any(r => string.Equals(r.Address, address, StringComparison.OrdinalIgnoreCase)))
        {
            return SampleEmailOutcome.NotAllowed;
        }

        var content = entry.Sample(site.Value);
        // Whatever the email is, an organizer is waiting for this one, and it isn't about anyone's spot or list.
        return await composer.TrySendAsync(
            content with { Subject = SubjectPrefix + content.Subject, Priority = EmailPriority.Urgent, SendBefore = null, Reference = null },
            new EmailRecipient(address),
            "a sample",
            cancellationToken)
            ? SampleEmailOutcome.Sent
            : SampleEmailOutcome.Failed;
    }
}

public sealed class SampleEmailForm
{
    [Required(ErrorMessage = "Choose an email.")]
    public string Key { get; set; } = "";

    [Required(ErrorMessage = "Choose who to send it to.")]
    public string To { get; set; } = "";
}
