using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using static CSAdvent.Web.Email.EmailBlocks;

namespace CSAdvent.Web.Announcements;

/// <summary>
/// The announcements list (#49): signing up with a confirmed email, optionally for each December day's posts too (#206), and
/// unsubscribing. Links in emails carry the subscriber's ID protected with the app's data protection keys, so they work
/// without signing in and can't be forged. Nothing here says whether an address is already on the list, so the form can't be
/// used to find out who's subscribed.
/// </summary>
public sealed partial class AnnouncementSubscriptions(
    AdventDbContext db,
    IDataProtectionProvider dataProtection,
    EmailComposer composer,
    IEmailSender sender,
    IOptions<SiteOptions> site,
    TimeProvider timeProvider,
    ILogger<AnnouncementSubscriptions> logger)
{
    public const string ConfirmPath = "/emails/announcements/confirm";
    public const string StopPath = "/emails/announcements/stop";
    public const string DailyPostsStopPath = "/emails/announcements/daily-posts/stop";

    /// <summary>Added to a confirmation link's ID when the daily posts were asked for, so confirming turns them on.</summary>
    private const string DailyPostsMark = ":daily-posts";

    /// <summary>How long a confirmation link works, and how long an unconfirmed address is kept.</summary>
    public static readonly TimeSpan ConfirmWithin = TimeSpan.FromDays(7);

    /// <summary>Entering an unconfirmed address again sends a new confirmation only after this long.</summary>
    public static readonly TimeSpan ResendAfter = TimeSpan.FromHours(24);

    private ITimeLimitedDataProtector ConfirmProtector =>
        dataProtection.CreateProtector("CSAdvent.Announcements.Confirm").ToTimeLimitedDataProtector();

    private IDataProtector StopProtector => dataProtection.CreateProtector("CSAdvent.Announcements.Unsubscribe");

    private IDataProtector DailyPostsStopProtector => dataProtection.CreateProtector("CSAdvent.Announcements.DailyPosts.Unsubscribe");

    /// <summary>
    /// Adds the address unconfirmed and emails it a confirmation link, which turns on the daily posts too if they're asked
    /// for. An address already getting everything it asks for, or sent a confirmation in the last day, gets nothing, so nobody
    /// can flood someone's inbox through the form. A confirmed subscriber asking for the daily posts confirms them the same
    /// way, so nobody can sign someone else up for them.
    /// </summary>
    public async Task SubscribeAsync(string email, bool dailyPosts = false, CancellationToken cancellationToken = default)
    {
        var address = email.Trim().ToLowerInvariant();
        var now = timeProvider.GetUtcNow();
        var subscriber = await db.AnnouncementSubscribers.SingleOrDefaultAsync(s => s.Email == address, cancellationToken);
        var hasEverything = subscriber is { ConfirmedAt: not null } && (!dailyPosts || subscriber.DailyPostsSince is not null);
        if (hasEverything || (subscriber is not null && now - subscriber.ConfirmationSentAt < ResendAfter))
        {
            return;
        }

        if (subscriber is null)
        {
            subscriber = new AnnouncementSubscriber { Email = address, CreatedAt = now, ConfirmationSentAt = now };
            db.AnnouncementSubscribers.Add(subscriber);
        }
        else
        {
            subscriber.ConfirmationSentAt = now;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The same address, entered twice at once: the other request sends the confirmation.
            return;
        }

        try
        {
            var confirmation = Confirmation(ConfirmLink(subscriber.Id, dailyPosts), site.Value, dailyPosts);
            await sender.SendAsync(await composer.ComposeAsync(confirmation, new EmailRecipient(address, address)), cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Without the address: the logs never carry personal details. Entering it again after a day sends another.
            LogConfirmationFailed(exception, subscriber.Id);
        }
    }

    /// <summary>
    /// Confirms the subscriber, and turns on the daily posts if the link asked for them. Returns false if they're gone
    /// (unsubscribed, or never confirmed in time).
    /// </summary>
    public async Task<bool> ConfirmAsync(int subscriberId, bool dailyPosts = false, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        await db.AnnouncementSubscribers
            .Where(s => s.Id == subscriberId && s.ConfirmedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConfirmedAt, now), cancellationToken);
        if (dailyPosts)
        {
            await db.AnnouncementSubscribers
                .Where(s => s.Id == subscriberId && s.DailyPostsSince == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.DailyPostsSince, now), cancellationToken);
        }

        return await db.AnnouncementSubscribers.AnyAsync(s => s.Id == subscriberId, cancellationToken);
    }

    /// <summary>Turns off the daily posts and keeps them on the list. Returns false if they're gone.</summary>
    public async Task<bool> StopDailyPostsAsync(int subscriberId, CancellationToken cancellationToken = default)
    {
        await db.AnnouncementSubscribers
            .Where(s => s.Id == subscriberId && s.DailyPostsSince != null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DailyPostsSince, (DateTimeOffset?)null), cancellationToken);
        return await ExistsAsync(subscriberId, cancellationToken);
    }

    /// <summary>Deletes the subscriber: unsubscribing removes the address, not just a flag. Returns false if they were already gone.</summary>
    public async Task<bool> UnsubscribeAsync(int subscriberId, CancellationToken cancellationToken = default) =>
        await db.AnnouncementSubscribers.Where(s => s.Id == subscriberId).ExecuteDeleteAsync(cancellationToken) > 0;

    public Task<bool> ExistsAsync(int subscriberId, CancellationToken cancellationToken = default) =>
        db.AnnouncementSubscribers.AnyAsync(s => s.Id == subscriberId, cancellationToken);

    /// <summary>Deletes addresses never confirmed within <see cref="ConfirmWithin"/>, as the privacy page promises.</summary>
    public Task<int> DeleteUnconfirmedAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = timeProvider.GetUtcNow() - ConfirmWithin;
        return db.AnnouncementSubscribers
            .Where(s => s.ConfirmedAt == null && s.ConfirmationSentAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public string ConfirmLink(int subscriberId, bool dailyPosts = false) =>
        $"{site.Value.BaseUrl.TrimEnd('/')}{ConfirmPath}?token={ConfirmProtector.Protect(Id(subscriberId) + (dailyPosts ? DailyPostsMark : ""), ConfirmWithin)}";

    /// <summary>The link in every announcement's footer and its one-click <c>List-Unsubscribe</c> header.</summary>
    public string StopLink(int subscriberId) =>
        $"{site.Value.BaseUrl.TrimEnd('/')}{StopPath}?token={StopProtector.Protect(Id(subscriberId))}";

    /// <summary>The link in every daily posts email's footer and its one-click header. It turns off only the daily posts.</summary>
    public string DailyPostsStopLink(int subscriberId) =>
        $"{site.Value.BaseUrl.TrimEnd('/')}{DailyPostsStopPath}?token={DailyPostsStopProtector.Protect(Id(subscriberId))}";

    /// <summary>Reads a confirmation link: whose it is, and whether it turns on the daily posts too.</summary>
    public bool TryReadConfirmToken(string? token, out int subscriberId, out bool dailyPosts)
    {
        subscriberId = 0;
        dailyPosts = false;
        if (!TryRead(() => ConfirmProtector.Unprotect(token!), token, out var payload))
        {
            return false;
        }

        dailyPosts = payload.EndsWith(DailyPostsMark, StringComparison.Ordinal);
        return int.TryParse(dailyPosts ? payload[..^DailyPostsMark.Length] : payload, CultureInfo.InvariantCulture, out subscriberId);
    }

    public bool TryReadStopToken(string? token, out int subscriberId) => TryReadId(() => StopProtector.Unprotect(token!), token, out subscriberId);

    public bool TryReadDailyPostsStopToken(string? token, out int subscriberId) =>
        TryReadId(() => DailyPostsStopProtector.Unprotect(token!), token, out subscriberId);

    /// <summary>Whether the subscriber gets the daily posts, or null if they're gone.</summary>
    public async Task<bool?> GetsDailyPostsAsync(int subscriberId, CancellationToken cancellationToken = default) =>
        await db.AnnouncementSubscribers
            .Where(s => s.Id == subscriberId)
            .Select(s => (bool?)(s.DailyPostsSince != null))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>The email that confirms an address before it gets anything else, or that turns on the daily posts.</summary>
    public static EmailContent Confirmation(string confirmLink, SiteOptions site, bool dailyPosts = false) => dailyPosts
        ? new()
        {
            Subject = "Confirm C# Advent's daily posts",
            Preview = "One click, and we'll email you each day's new posts in December.",
            Heading = "Confirm your email",
            Body =
            [
                Paragraph(
                    "Someone (hopefully you) asked for C# Advent's emails at this address: each day's new posts from December 1 " +
                    "to 25, as well as an email when each season's sign-ups open and the organizers' occasional news. Confirm, " +
                    "and you're on the list."),
                Button("Confirm my email", confirmLink),
                Paragraph(
                    "If it wasn't you, ignore this email: nothing changes without confirming, and if the address isn't on the list " +
                    $"already, we delete the address within a week. The link works for {ConfirmWithin.Days} days."),
            ],
            Reason = "You're getting this email because this address was entered on csadvent.christmas.",
        }
        : new()
        {
            Subject = "Confirm your C# Advent announcements",
            Preview = "One click, and we'll email you when sign-ups open each year.",
            Heading = "Confirm your email",
            Body =
            [
                Paragraph(
                    "Someone (hopefully you) asked for C# Advent's announcements at this address: an email when each season's " +
                    "sign-ups open, and the organizers' occasional news. Confirm, and you're on the list."),
                Button("Confirm my email", confirmLink),
                Paragraph($"If it wasn't you, ignore this email: without confirming, we delete the address within a week. The link works for {ConfirmWithin.Days} days."),
            ],
            Reason = "You're getting this email because this address was entered on csadvent.christmas.",
        };

    private static string Id(int subscriberId) => subscriberId.ToString(CultureInfo.InvariantCulture);

    private static bool TryReadId(Func<string> unprotect, string? token, out int subscriberId)
    {
        subscriberId = 0;
        return TryRead(unprotect, token, out var payload) && int.TryParse(payload, CultureInfo.InvariantCulture, out subscriberId);
    }

    private static bool TryRead(Func<string> unprotect, string? token, [NotNullWhen(true)] out string? payload)
    {
        payload = null;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        try
        {
            payload = unprotect();
            return true;
        }
        catch (CryptographicException)
        {
            // Forged, mangled, or (for a confirmation) expired.
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't send the confirmation email to announcement subscriber {SubscriberId}")]
    private partial void LogConfirmationFailed(Exception exception, int subscriberId);
}

/// <summary>What the sign-up form posts. <see cref="Website"/> is a hidden trap for bots: a person never fills it in.</summary>
public sealed class SubscribeForm
{
    [Required, EmailAddress, StringLength(254)]
    public string? Email { get; set; }

    public string? Website { get; set; }

    /// <summary>The page to come back to: the home page or the announcements page.</summary>
    public string? ReturnUrl { get; set; }

    /// <summary>Whether they'd like each December day's posts by email too (#206).</summary>
    public bool DailyPosts { get; set; }
}
