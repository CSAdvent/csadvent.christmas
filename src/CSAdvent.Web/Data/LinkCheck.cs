using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>Whether a published post is still at its address (#201).</summary>
public enum LinkStatus
{
    /// <summary>Not checked yet, or its address changed since it was.</summary>
    Unchecked,

    /// <summary>Its page loaded, or an organizer said it works.</summary>
    Working,

    /// <summary>It didn't load the last time or two. It's checked again sooner, and it's dead if it keeps failing.</summary>
    Failing,

    /// <summary>
    /// It failed <see cref="DeadLinks.LinkCheckOptions.FailuresBeforeDead"/> checks in a row, days apart, or an organizer said it's
    /// gone. Readers get its archived copy, if it has one.
    /// </summary>
    Dead,

    /// <summary>The site turned the check away (403, 429 and the like), so we can't tell. Readers still get the post.</summary>
    Refused,
}

/// <summary>
/// The dead-link check of one spot's post (#201): whether it's still at <see cref="PostUrl"/>, when it's checked next, and the
/// archived copy readers get while it's dead. Only the public address of a post is involved, so nothing here is personal.
/// </summary>
public sealed class LinkCheck
{
    public int Id { get; private set; }

    public int SlotId { get; init; }

    public Slot Slot { get; private set; } = null!;

    /// <summary>The address this is about. When the spot's address changes, the check starts again.</summary>
    public required string PostUrl { get; set; }

    public LinkStatus Status { get; set; }

    /// <summary>What the last check found, for the organizers: "The page answered 404 Not Found", say.</summary>
    public string? Detail { get; set; }

    /// <summary>Checks in a row that didn't load. Dead at <see cref="DeadLinks.LinkCheckOptions.FailuresBeforeDead"/>.</summary>
    public int Failures { get; set; }

    /// <summary>When the failures in a row started.</summary>
    public DateTimeOffset? FailingSince { get; set; }

    /// <summary>When it was found dead.</summary>
    public DateTimeOffset? DeadSince { get; set; }

    public DateTimeOffset? CheckedAt { get; set; }

    /// <summary>When it's due to be checked again. Runs take the most overdue first, a few at a time.</summary>
    public DateTimeOffset NextCheckAt { get; set; }

    /// <summary>A run checking it now (so two replicas don't check it at once) holds it until then.</summary>
    public DateTimeOffset? LeasedUntil { get; set; }

    /// <summary>The copy readers are sent to while it's dead: the Wayback Machine's, or one an organizer gave.</summary>
    public string? ArchivedUrl { get; set; }

    /// <summary>
    /// The organizer who set (or cleared) the archived copy. While set, runs never look one up in its place.
    /// </summary>
    public string? ArchivedBy { get; set; }

    /// <summary>When the Wayback Machine was last asked for a copy, so a post it has none of is only asked about now and then.</summary>
    public DateTimeOffset? ArchiveCheckedAt { get; set; }

    /// <summary>
    /// The organizer who said it works, or that it's dead. While set, runs leave it alone, until its address changes or someone
    /// checks it again.
    /// </summary>
    public string? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>
    /// When the organizers heard it's dead: in the day's email (<see cref="DeadLinks.DeadLinkEmail"/>), or by marking it gone
    /// or checking it themselves. Cleared when it isn't dead any more, or its check starts again, so they hear again if it
    /// dies again.
    /// </summary>
    public DateTimeOffset? OrganizersEmailedAt { get; set; }
}

internal sealed class LinkCheckConfiguration : IEntityTypeConfiguration<LinkCheck>
{
    public void Configure(EntityTypeBuilder<LinkCheck> builder)
    {
        builder.Property(c => c.PostUrl).HasMaxLength(500);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Detail).HasMaxLength(500);
        builder.Property(c => c.ArchivedUrl).HasMaxLength(1000);
        builder.Property(c => c.ArchivedBy).HasMaxLength(39);
        builder.Property(c => c.DecidedBy).HasMaxLength(39);

        builder.HasIndex(c => c.SlotId).IsUnique();
        builder.HasIndex(c => c.NextCheckAt);
        builder.HasIndex(c => c.Status);

        builder.HasOne(c => c.Slot)
            .WithMany()
            .HasForeignKey(c => c.SlotId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t => t.HasCheckConstraint("ck_link_checks_archived_url", "archived_url ~* '^https?://'"));
    }
}
