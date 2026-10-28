using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>When a post's Wayback Machine snapshot is taken (#261).</summary>
public enum SnapshotMoment
{
    /// <summary>On its day: once the blurb check has seen the post, or else once its day is over.</summary>
    Live,

    /// <summary>
    /// Once its season is over. Past seasons' posts get one too, so every post still up has a copy, unless the dead-link check
    /// has found it gone or failing.
    /// </summary>
    AfterSeason,
}

/// <summary>Where a snapshot stands (#261).</summary>
public enum SnapshotStatus
{
    /// <summary>Waiting to be asked for, from <see cref="PostSnapshot.NextAttemptAt"/>.</summary>
    Due,

    /// <summary>The Wayback Machine is saving it (<see cref="PostSnapshot.JobId"/>); each run asks how it went.</summary>
    Capturing,

    /// <summary>Saved, at <see cref="PostSnapshot.SnapshotUrl"/>.</summary>
    Saved,

    /// <summary>The Wayback Machine couldn't save it, <see cref="Snapshots.SnapshotOptions.MaxAttempts"/> times. It isn't asked again.</summary>
    Failed,
}

/// <summary>
/// A Wayback Machine snapshot we asked for of one spot's post (#261), at one of its <see cref="SnapshotMoment"/>s, for the
/// address it had then: a post whose address changes is saved again. The dead-link check sends readers to the earliest one
/// saved once the post is gone. Only the public address of a post is involved, so nothing here is personal.
/// </summary>
public sealed class PostSnapshot
{
    public int Id { get; private set; }

    public int SlotId { get; init; }

    public Slot Slot { get; private set; } = null!;

    public SnapshotMoment Moment { get; init; }

    /// <summary>The address saved. A spot's new address gets snapshots of its own.</summary>
    public required string PostUrl { get; init; }

    public SnapshotStatus Status { get; set; }

    /// <summary>Tries the Wayback Machine couldn't save it on. Failed at <see cref="Snapshots.SnapshotOptions.MaxAttempts"/>.</summary>
    public int Attempts { get; set; }

    /// <summary>When it can be asked for (again).</summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    /// <summary>A run asking about it now (so two replicas don't) holds it until then.</summary>
    public DateTimeOffset? LeasedUntil { get; set; }

    /// <summary>The Save Page Now job, while it's capturing.</summary>
    public string? JobId { get; set; }

    /// <summary>When the Wayback Machine last started saving it.</summary>
    public DateTimeOffset? RequestedAt { get; set; }

    /// <summary>The snapshot, on the Wayback Machine.</summary>
    public string? SnapshotUrl { get; set; }

    /// <summary>When the Wayback Machine saved it, by its own timestamp.</summary>
    public DateTimeOffset? SavedAt { get; set; }

    /// <summary>What the Wayback Machine last said, for the organizers, when it didn't save it.</summary>
    public string? Detail { get; set; }
}

internal sealed class PostSnapshotConfiguration : IEntityTypeConfiguration<PostSnapshot>
{
    public void Configure(EntityTypeBuilder<PostSnapshot> builder)
    {
        builder.Property(p => p.Moment).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.PostUrl).HasMaxLength(500);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.JobId).HasMaxLength(100);
        builder.Property(p => p.SnapshotUrl).HasMaxLength(1000);
        builder.Property(p => p.Detail).HasMaxLength(500);

        // One of each moment per address, so two replicas adding the same one at once is fine (ON CONFLICT DO NOTHING).
        builder.HasIndex(p => new { p.SlotId, p.Moment, p.PostUrl }).IsUnique();
        builder.HasIndex(p => new { p.Status, p.NextAttemptAt });

        builder.HasOne(p => p.Slot)
            .WithMany()
            .HasForeignKey(p => p.SlotId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t => t.HasCheckConstraint("ck_post_snapshots_snapshot_url", @"snapshot_url ~ '^https://web\.archive\.org/web/'"));
    }
}
