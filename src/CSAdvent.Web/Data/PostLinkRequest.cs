using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

public enum PostLinkRequestStatus
{
    Pending,
    Approved,
    Declined,

    /// <summary>The contributor took it back before an organizer decided, to fix it or ask again.</summary>
    Withdrawn,
}

/// <summary>
/// A signed-in contributor asking for new links to their posts from finished seasons, such as after moving their blog to a new
/// domain: one request for every post they want changed. An organizer checks the new links and approves the request, which
/// changes them all, or declines it. Each person has at most one pending request. No email address is kept here: emails use
/// the contributor's verified GitHub email. A decided or withdrawn request is deleted 30 days later (<c>ContactRetention</c>).
/// </summary>
public sealed class PostLinkRequest
{
    public int Id { get; private set; }

    public int ContributorId { get; init; }

    public Contributor Contributor { get; private set; } = null!;

    /// <summary>Anything that helps the organizers check, such as where the blog moved from and to.</summary>
    public string? Note { get; init; }

    public DateTimeOffset SubmittedAt { get; init; }

    public PostLinkRequestStatus Status { get; set; }

    /// <summary>When an organizer decided it, or the contributor withdrew it.</summary>
    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>The organizer's GitHub login. Empty for a withdrawn request.</summary>
    public string? DecidedBy { get; set; }

    public List<PostLinkChange> Changes { get; init; } = [];

    /// <summary>Concurrency token (PostgreSQL <c>xmin</c>), so two organizers can't both decide it.</summary>
    public uint Version { get; private set; }
}

/// <summary>One post's link in a <see cref="PostLinkRequest"/>: what it was when they asked, and what they want it to be.</summary>
public sealed class PostLinkChange
{
    public int Id { get; private set; }

    public int RequestId { get; init; }

    public int SlotId { get; init; }

    public Slot Slot { get; private set; } = null!;

    /// <summary>The post's link when they asked, so the organizers can see what changed and undo it if they need to.</summary>
    public required string OldUrl { get; init; }

    public required string NewUrl { get; init; }
}

internal sealed class PostLinkRequestConfiguration : IEntityTypeConfiguration<PostLinkRequest>
{
    public void Configure(EntityTypeBuilder<PostLinkRequest> builder)
    {
        builder.Property(r => r.Note).HasMaxLength(1000);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(r => r.DecidedBy).HasMaxLength(39);
        builder.Property(r => r.Version).IsRowVersion();

        // One pending request per person; they withdraw it to change it.
        builder.HasIndex(r => r.ContributorId)
            .IsUnique()
            .HasFilter($"status = '{nameof(PostLinkRequestStatus.Pending)}'");
        builder.HasIndex(r => new { r.ContributorId, r.SubmittedAt });
        builder.HasIndex(r => new { r.Status, r.SubmittedAt });

        builder.HasOne(r => r.Contributor)
            .WithMany()
            .HasForeignKey(r => r.ContributorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Changes)
            .WithOne()
            .HasForeignKey(c => c.RequestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PostLinkChangeConfiguration : IEntityTypeConfiguration<PostLinkChange>
{
    public void Configure(EntityTypeBuilder<PostLinkChange> builder)
    {
        builder.Property(c => c.OldUrl).HasMaxLength(500);
        builder.Property(c => c.NewUrl).HasMaxLength(500);

        builder.HasIndex(c => new { c.RequestId, c.SlotId }).IsUnique();
        builder.HasIndex(c => c.SlotId);

        builder.HasOne(c => c.Slot)
            .WithMany()
            .HasForeignKey(c => c.SlotId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t => t.HasCheckConstraint("ck_post_link_changes_new_url", "new_url ~* '^https?://'"));
    }
}
