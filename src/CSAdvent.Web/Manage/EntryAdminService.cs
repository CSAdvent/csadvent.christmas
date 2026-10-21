using System.ComponentModel.DataAnnotations;
using CSAdvent.Web.Backups;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Manage;

public enum EntryStatus
{
    Open,
    ClaimedWithoutPost,
    TitleOnly,
    UrlOnly,
    Ready,
}

public sealed record EntryListItem(int SlotId, int Day, int Part, string? AuthorName, bool HasContact, string? PostTitle, string? PostUrl, EntryStatus Status);

public sealed record EntryDetails(
    int SlotId,
    int Year,
    int Day,
    int Part,
    int SlotsPerDay,
    string? ContributorLogin,
    bool IsRevealed,
    uint Version,
    EntryForm Values);

public enum EntrySaveOutcome
{
    Saved,
    Released,
    ChangedByOthers,
    NotFound,
}

/// <summary>
/// Organizers editing any spot: fixing names and links, adding a post's title and URL, putting a substitute into an open
/// spot, or releasing a spot after a dropout. Edits carry the spot's row version, so two organizers can't overwrite each
/// other without noticing.
/// </summary>
public sealed class EntryAdminService(AdventDbContext db, AdventSchedule schedule, TimeProvider timeProvider, BackupService backups)
{
    public static EntryStatus StatusOf(string? authorName, string? postTitle, string? postUrl) =>
        (authorName, postTitle, postUrl) switch
        {
            (null, _, _) => EntryStatus.Open,
            (_, not null, not null) => EntryStatus.Ready,
            (_, not null, null) => EntryStatus.TitleOnly,
            (_, null, not null) => EntryStatus.UrlOnly,
            _ => EntryStatus.ClaimedWithoutPost,
        };

    public async Task<List<EntryListItem>> ListAsync(int year, CancellationToken cancellationToken = default)
    {
        var slots = await db.Slots
            .Where(s => s.SeasonYear == year)
            .OrderBy(s => s.Day).ThenBy(s => s.Part)
            .Select(s => new { s.Id, s.Day, s.Part, s.AuthorName, HasContact = s.Contact != null, s.PostTitle, s.PostUrl })
            .ToListAsync(cancellationToken);

        return [.. slots.Select(s => new EntryListItem(s.Id, s.Day, s.Part, s.AuthorName, s.HasContact, s.PostTitle, s.PostUrl, StatusOf(s.AuthorName, s.PostTitle, s.PostUrl)))];
    }

    public async Task<EntryDetails?> GetAsync(int slotId, CancellationToken cancellationToken = default)
    {
        var slot = await db.Slots
            .AsNoTracking()
            .Include(s => s.Season)
            .Include(s => s.Contact)
            .Include(s => s.Contributor)
            .SingleOrDefaultAsync(s => s.Id == slotId, cancellationToken);

        return slot is null
            ? null
            : new EntryDetails(
                slot.Id,
                slot.SeasonYear,
                slot.Day,
                slot.Part,
                slot.Season.SlotsPerDay,
                slot.Contributor?.GithubLogin,
                schedule.IsRevealed(slot),
                slot.Version,
                new EntryForm
                {
                    AuthorName = slot.AuthorName,
                    AuthorUrl = slot.AuthorUrl,
                    ContactEmail = slot.Contact?.Email,
                    PostTitle = slot.PostTitle,
                    PostUrl = slot.PostUrl,
                    Version = slot.Version,
                });
    }

    public async Task<EntrySaveOutcome> SaveAsync(int slotId, EntryForm form, CancellationToken cancellationToken = default)
    {
        var slot = await LoadForUpdateAsync(slotId, form.Version, cancellationToken);
        if (slot is null)
        {
            return EntrySaveOutcome.NotFound;
        }

        var authorName = Clean(form.AuthorName);
        slot.AuthorName = authorName;
        slot.AuthorUrl = Clean(form.AuthorUrl);
        slot.PostTitle = Clean(form.PostTitle);
        slot.PostUrl = Clean(form.PostUrl);

        // A spot an organizer empties is no longer the contributor's.
        if (authorName is null)
        {
            slot.ContributorId = null;
        }

        SetContact(slot, Clean(form.ContactEmail));
        return await SaveChangesAsync(EntrySaveOutcome.Saved, cancellationToken);
    }

    /// <summary>
    /// Clears the spot completely (author, link, post, contributor and contact), so it's open again, and marks it dropped
    /// out so the backup posters are asked to fill it.
    /// </summary>
    public async Task<EntrySaveOutcome> ReleaseAsync(int slotId, uint version, CancellationToken cancellationToken = default)
    {
        var slot = await LoadForUpdateAsync(slotId, version, cancellationToken);
        if (slot is null)
        {
            return EntrySaveOutcome.NotFound;
        }

        slot.AuthorName = null;
        slot.AuthorUrl = null;
        slot.PostTitle = null;
        slot.PostUrl = null;
        slot.ContributorId = null;
        slot.DroppedOutAt = timeProvider.GetUtcNow();
        SetContact(slot, email: null);

        var outcome = await SaveChangesAsync(EntrySaveOutcome.Released, cancellationToken);
        if (outcome is EntrySaveOutcome.Released)
        {
            await backups.OfferNowAsync(slotId, timeProvider.GetUtcNow(), cancellationToken);
        }

        return outcome;
    }

    private async Task<Slot?> LoadForUpdateAsync(int slotId, uint version, CancellationToken cancellationToken)
    {
        var slot = await db.Slots.Include(s => s.Contact).SingleOrDefaultAsync(s => s.Id == slotId, cancellationToken);
        if (slot is not null)
        {
            // Compare against the version the organizer saw, not the one just loaded.
            db.Entry(slot).Property(s => s.Version).OriginalValue = version;
        }

        return slot;
    }

    private void SetContact(Slot slot, string? email)
    {
        if (email is null)
        {
            if (slot.Contact is not null)
            {
                db.SlotContacts.Remove(slot.Contact);
            }
        }
        else if (slot.Contact is null)
        {
            slot.Contact = new SlotContact { Email = email };
        }
        else
        {
            slot.Contact.Email = email;
        }
    }

    private async Task<EntrySaveOutcome> SaveChangesAsync(EntrySaveOutcome success, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return EntrySaveOutcome.ChangedByOthers;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>An organizer's edit of a spot. Every field is optional, but a post needs an author.</summary>
public sealed class EntryForm : IValidatableObject
{
    [StringLength(100, ErrorMessage = "Keep the name to 100 characters or fewer.")]
    public string? AuthorName { get; set; }

    [StringLength(500, ErrorMessage = "Keep the author's link to 500 characters or fewer.")]
    [HttpUrl(ErrorMessage = "The author's link must be a full web address that starts with https:// or http://.")]
    public string? AuthorUrl { get; set; }

    [StringLength(254, ErrorMessage = "Keep the email to 254 characters or fewer.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    public string? ContactEmail { get; set; }

    [StringLength(300, ErrorMessage = "Keep the post title to 300 characters or fewer.")]
    public string? PostTitle { get; set; }

    [StringLength(500, ErrorMessage = "Keep the post URL to 500 characters or fewer.")]
    [HttpUrl(ErrorMessage = "The post URL must be a full web address that starts with https:// or http://.")]
    public string? PostUrl { get; set; }

    /// <summary>The spot's row version when the form was shown.</summary>
    public uint Version { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(AuthorName) && (!string.IsNullOrWhiteSpace(PostTitle) || !string.IsNullOrWhiteSpace(PostUrl)))
        {
            yield return new ValidationResult("A post needs an author. Enter the author's name, or clear the post.", [nameof(AuthorName)]);
        }
    }
}

/// <summary>Releasing needs a ticked confirmation, so it can't happen by accident.</summary>
public sealed class ReleaseForm
{
    [Range(typeof(bool), "true", "true", ErrorMessage = "Tick the box to confirm you want to release this spot.")]
    public bool Confirm { get; set; }

    public uint Version { get; set; }
}
