using System.ComponentModel.DataAnnotations;
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
/// The announcements list (#49): signing up with a confirmed email, and unsubscribing. Links in emails carry the subscriber's
/// ID protected with the app's data protection keys, so they work without signing in and can't be forged. Nothing here says
/// whether an address is already on the list, so the form can't be used to find out who's subscribed.
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

    /// <summary>How long a confirmation link works, and how long an unconfirmed address is kept.</summary>
    public static readonly TimeSpan ConfirmWithin = TimeSpan.FromDays(7);

    /// <summary>Entering an unconfirmed address again sends a new confirmation only after this long.</summary>
    public static readonly TimeSpan ResendAfter = TimeSpan.FromHours(24);

    private ITimeLimitedDataProtector ConfirmProtector =>
        dataProtection.CreateProtector("CSAdvent.Announcements.Confirm").ToTimeLimitedDataProtector();

    private IDataProtector StopProtector => dataProtection.CreateProtector("CSAdvent.Announcements.Unsubscribe");

    /// <summary>
    /// Adds the address unconfirmed and emails it a confirmation link. An address already confirmed, or sent a confirmation
    /// in the last day, gets nothing, so nobody can flood someone's inbox through the form.
    /// </summary>
    public async Task SubscribeAsync(string email, CancellationToken cancellationToken = default)
    {
        var address = email.Trim().ToLowerInvariant();
        var now = timeProvider.GetUtcNow();
        var subscriber = await db.AnnouncementSubscribers.SingleOrDefaultAsync(s => s.Email == address, cancellationToken);
        if (subscriber is { ConfirmedAt: not null } || (subscriber is not null && now - subscriber.ConfirmationSentAt < ResendAfter))
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
            await sender.SendAsync(await composer.ComposeAsync(Confirmation(ConfirmLink(subscriber.Id), site.Value), new EmailRecipient(address, address)), cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Without the address: the logs never carry personal details. Entering it again after a day sends another.
            LogConfirmationFailed(exception, subscriber.Id);
        }
    }

    /// <summary>Confirms the subscriber. Returns false if they're gone (unsubscribed, or never confirmed in time).</summary>
    public async Task<bool> ConfirmAsync(int subscriberId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        await db.AnnouncementSubscribers
            .Where(s => s.Id == subscriberId && s.ConfirmedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConfirmedAt, now), cancellationToken);
        return await db.AnnouncementSubscribers.AnyAsync(s => s.Id == subscriberId, cancellationToken);
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

    public string ConfirmLink(int subscriberId) =>
        $"{site.Value.BaseUrl.TrimEnd('/')}{ConfirmPath}?token={ConfirmProtector.Protect(Id(subscriberId), ConfirmWithin)}";

    /// <summary>The link in every announcement's footer and its one-click <c>List-Unsubscribe</c> header.</summary>
    public string StopLink(int subscriberId) =>
        $"{site.Value.BaseUrl.TrimEnd('/')}{StopPath}?token={StopProtector.Protect(Id(subscriberId))}";

    public bool TryReadConfirmToken(string? token, out int subscriberId) => TryRead(() => ConfirmProtector.Unprotect(token!), token, out subscriberId);

    public bool TryReadStopToken(string? token, out int subscriberId) => TryRead(() => StopProtector.Unprotect(token!), token, out subscriberId);

    /// <summary>The email that confirms an address before it gets anything else.</summary>
    public static EmailContent Confirmation(string confirmLink, SiteOptions site) => new()
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

    private static bool TryRead(Func<string> unprotect, string? token, out int subscriberId)
    {
        subscriberId = 0;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        try
        {
            return int.TryParse(unprotect(), CultureInfo.InvariantCulture, out subscriberId);
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
}
