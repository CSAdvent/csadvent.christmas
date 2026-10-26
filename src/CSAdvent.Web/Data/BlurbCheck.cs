using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>Where a post stands on the C# Advent blurb (#128).</summary>
public enum BlurbStatus
{
    /// <summary>
    /// Not checked yet, or its page doesn't load (or doesn't seem to be the post) and it's still more than a few hours before
    /// its deadline, so it's most likely not published yet (#159). Checked again on every run.
    /// </summary>
    Waiting,

    /// <summary>Claude found the blurb, or an organizer said it's there.</summary>
    Passed,

    /// <summary>
    /// No link back near the top of a page that's clearly the post, Claude says the blurb is missing, or an organizer said so.
    /// Its author is emailed and a backup poster is lined up.
    /// </summary>
    Missing,

    /// <summary>
    /// Claude is unsure, the post seems to talk to it, or the page has no link back and doesn't seem to be the post (#148): an
    /// organizer decides.
    /// </summary>
    NeedsDecision,

    /// <summary>
    /// The page didn't load in time on its day, or Claude couldn't answer. An organizer decides; it's never the author's fault.
    /// </summary>
    CouldNotCheck,

    /// <summary>An organizer let it through without the blurb (Announce anyway).</summary>
    Waived,
}

/// <summary>
/// The blurb check of one spot's post: its status, for the post at <see cref="PostUrl"/>, Claude's last answer, and who has been
/// told about it. A new URL starts it again. Only the public text of posts goes to Claude, so nothing here is personal.
/// </summary>
public sealed class BlurbCheck
{
    public int Id { get; private set; }

    public int SlotId { get; init; }

    public Slot Slot { get; private set; } = null!;

    /// <summary>The post this is about. When the spot's URL changes, the check starts again.</summary>
    public required string PostUrl { get; set; }

    public BlurbStatus Status { get; set; }

    /// <summary>When it last changed status.</summary>
    public DateTimeOffset StatusSince { get; set; }

    /// <summary>Why, for the organizers and the author: "No link to csadvent.christmas near the top", say.</summary>
    public string? Detail { get; set; }

    /// <summary>Claude's quote of the blurb, from its last answer.</summary>
    public string? Quote { get; set; }

    /// <summary>Claude's one-sentence reason, from its last answer.</summary>
    public string? Reason { get; set; }

    /// <summary>A hash of the post's opening as last read, so an unchanged post isn't sent to Claude again.</summary>
    public string? ContentHash { get; set; }

    public DateTimeOffset? CheckedAt { get; set; }

    /// <summary>A run checking it now (so two replicas don't check it at once) holds it until then.</summary>
    public DateTimeOffset? LeasedUntil { get; set; }

    // An organizer's decision, which stands until the post changes or someone checks again.

    /// <summary>The organizer's GitHub login. While set, runs leave the status alone.</summary>
    public string? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>"Use a backup": the next backup who accepts takes the spot straight away rather than at the deadline.</summary>
    public DateTimeOffset? BackupRequestedAt { get; set; }

    // Who has been told, so each email goes once per state.

    /// <summary>When the author was emailed that the blurb is missing. Cleared once it passes, so a later failure emails again.</summary>
    public DateTimeOffset? AuthorEmailedAt { get; set; }

    /// <summary>The status the organizers were last emailed about (it needs a decision, or couldn't be checked).</summary>
    public BlurbStatus? OrganizersEmailedAbout { get; set; }

    /// <summary>When the organizers heard that the deadline passed with nobody to take the spot.</summary>
    public DateTimeOffset? DeadlineEmailedAt { get; set; }
}

internal sealed class BlurbCheckConfiguration : IEntityTypeConfiguration<BlurbCheck>
{
    public void Configure(EntityTypeBuilder<BlurbCheck> builder)
    {
        builder.Property(c => c.PostUrl).HasMaxLength(500);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Detail).HasMaxLength(500);
        builder.Property(c => c.Quote).HasMaxLength(1000);
        builder.Property(c => c.Reason).HasMaxLength(1000);
        builder.Property(c => c.ContentHash).HasMaxLength(64);
        builder.Property(c => c.DecidedBy).HasMaxLength(39);
        builder.Property(c => c.OrganizersEmailedAbout).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(c => c.SlotId).IsUnique();

        builder.HasOne(c => c.Slot)
            .WithMany()
            .HasForeignKey(c => c.SlotId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
