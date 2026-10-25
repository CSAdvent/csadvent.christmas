using System.Security.Cryptography;
using System.Text;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CSAdvent.Web.Email;

/// <summary>
/// The unsubscribe link in every optional email to a person (<see cref="EmailComposer"/> adds it). Its token is the recipient's
/// address protected with the app's data protection keys, so the link works without signing in and can't be made for
/// someone else's address.
/// </summary>
public sealed class EmailOptOutLinks(IDataProtectionProvider dataProtection, IOptions<SiteOptions> site)
{
    /// <summary>The link. A GET shows a confirmation; a POST (the page's button, or one-click unsubscribe) opts out.</summary>
    public const string StopPath = "/emails/stop";

    /// <summary>Turns emails back on, from the confirmation page.</summary>
    public const string ResumePath = "/emails/resume";

    /// <summary>The confirmation page.</summary>
    public const string PagePath = "/emails/unsubscribe";

    private IDataProtector Protector => dataProtection.CreateProtector("CSAdvent.Emails.OptOut");

    public string CreateLink(string address) => $"{site.Value.BaseUrl.TrimEnd('/')}{StopPath}?token={CreateToken(address)}";

    public string CreateToken(string address) => Protector.Protect(Normalize(address));

    public bool TryReadToken(string? token, out string address)
    {
        address = "";
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        try
        {
            address = Protector.Unprotect(token);
            return address.Contains('@', StringComparison.Ordinal);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Whether the address is the organizers' inbox, which never gets an unsubscribe link and is never skipped.</summary>
    public bool IsOrganizers(string address) => string.Equals(Normalize(address), Normalize(site.Value.ContactEmail), StringComparison.Ordinal);

    public static string Normalize(string address) => address.Trim().ToLowerInvariant();

    /// <summary>What the do-not-email list keeps instead of the address: its SHA-256, as hex.</summary>
    public static string Fingerprint(string address) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(address))));
}

/// <summary>Whether an address unsubscribed from C# Advent's optional emails.</summary>
public interface IEmailOptOutList
{
    Task<bool> IsOptedOutAsync(string address, CancellationToken cancellationToken = default);
}

/// <summary>
/// The do-not-email list: addresses that unsubscribed from C# Advent's optional emails. <see cref="OptOutAwareEmailSender"/>
/// checks it before every optional email. Only fingerprints are kept (<see cref="EmailOptOutLinks.Fingerprint"/>).
/// </summary>
public sealed class EmailOptOuts(AdventDbContext db, TimeProvider timeProvider) : IEmailOptOutList
{
    public Task<bool> IsOptedOutAsync(string address, CancellationToken cancellationToken = default)
    {
        var fingerprint = EmailOptOutLinks.Fingerprint(address);
        return db.EmailOptOuts.AnyAsync(o => o.Fingerprint == fingerprint, cancellationToken);
    }

    public async Task OptOutAsync(string address, CancellationToken cancellationToken = default)
    {
        if (await IsOptedOutAsync(address, cancellationToken))
        {
            return;
        }

        db.EmailOptOuts.Add(new EmailOptOut { Fingerprint = EmailOptOutLinks.Fingerprint(address), OptedOutAt = timeProvider.GetUtcNow() });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Unsubscribed twice at once (a mail provider's one-click and the button, say): either way it's done.
        }
    }

    public Task OptInAsync(string address, CancellationToken cancellationToken = default)
    {
        var fingerprint = EmailOptOutLinks.Fingerprint(address);
        return db.EmailOptOuts.Where(o => o.Fingerprint == fingerprint).ExecuteDeleteAsync(cancellationToken);
    }
}
