using System.Security.Cryptography;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Reminders;

/// <summary>
/// Turning off the daily "today's posts" emails. Each email carries a link with a token that names the recipient's slot,
/// protected with the app's data protection keys (kept in the database), so the link works without signing in and can't
/// be forged for someone else.
/// </summary>
public sealed class DailyEmailOptOut(AdventDbContext db, IDataProtectionProvider dataProtection, IOptions<SiteOptions> site, TimeProvider timeProvider)
{
    public const string Path = "/emails/daily/stop";

    private IDataProtector Protector => dataProtection.CreateProtector("CSAdvent.DailyEmails.OptOut");

    /// <summary>The link in the footer and the <c>List-Unsubscribe</c> header. A GET shows a confirmation; a POST opts out.</summary>
    public string CreateLink(int slotId) => $"{site.Value.BaseUrl.TrimEnd('/')}{Path}?token={Protector.Protect(slotId.ToString(System.Globalization.CultureInfo.InvariantCulture))}";

    public bool TryReadToken(string? token, out int slotId)
    {
        slotId = 0;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        try
        {
            return int.TryParse(Protector.Unprotect(token), System.Globalization.CultureInfo.InvariantCulture, out slotId);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>The season the token's slot belongs to, or nothing if the slot (or its contact details) is gone.</summary>
    public Task<int?> FindSeasonAsync(int slotId, CancellationToken cancellationToken = default) =>
        db.SlotContacts.Where(c => c.SlotId == slotId).Select(c => (int?)c.Slot.SeasonYear).SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Opts the slot's author out for the whole season: every spot they have with the same email. Returns false when the
    /// slot's contact details no longer exist (for example after its author dropped out).
    /// </summary>
    public async Task<bool> OptOutAsync(int slotId, CancellationToken cancellationToken = default)
    {
        var contact = await db.SlotContacts
            .Where(c => c.SlotId == slotId)
            .Select(c => new { c.Email, c.Slot.SeasonYear })
            .SingleOrDefaultAsync(cancellationToken);
        if (contact is null)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        var email = contact.Email.ToLowerInvariant();
        await db.SlotContacts
            .Where(c => c.Slot.SeasonYear == contact.SeasonYear && c.Email.ToLower() == email && c.DailyEmailsOptedOutAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.DailyEmailsOptedOutAt, now), cancellationToken);
        return true;
    }
}
