using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

public enum ClaimRequestStatus
{
    Pending,
    Approved,
    Archived,
}

/// <summary>
/// A contributor asking for a slot. The organizer either approves it, which copies it into the slot, or archives it.
/// Everything here is private.
/// </summary>
public sealed class ClaimRequest
{
    public int Id { get; private set; }

    /// <summary>The requested slot. The organizer can move a request to another day or part before approving it.</summary>
    public int SlotId { get; set; }

    public Slot Slot { get; set; } = null!;

    public int ContributorId { get; init; }

    public Contributor Contributor { get; private set; } = null!;

    public required string DisplayName { get; set; }

    public required string Email { get; set; }

    /// <summary>The contributor's own site or profile, not the post.</summary>
    public string? Link { get; set; }

    /// <summary>Anything else for the organizer, such as a second-choice date.</summary>
    public string? Comments { get; set; }

    public DateTimeOffset TermsAcceptedAt { get; init; }

    public DateTimeOffset TimingAcceptedAt { get; init; }

    public DateTimeOffset SubmittedAt { get; init; }

    public ClaimRequestStatus Status { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>Concurrency token (PostgreSQL <c>xmin</c>), so approving twice can't apply twice.</summary>
    public uint Version { get; private set; }
}

internal sealed class ClaimRequestConfiguration : IEntityTypeConfiguration<ClaimRequest>
{
    public void Configure(EntityTypeBuilder<ClaimRequest> builder)
    {
        builder.Property(r => r.DisplayName).HasMaxLength(100);
        builder.Property(r => r.Email).HasMaxLength(254);
        builder.Property(r => r.Link).HasMaxLength(500);
        builder.Property(r => r.Comments).HasMaxLength(1000);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Version).IsRowVersion();

        builder.HasIndex(r => r.SlotId);

        // A contributor can't have two pending requests for the same slot.
        builder.HasIndex(r => new { r.SlotId, r.ContributorId })
            .IsUnique()
            .HasFilter($"status = '{nameof(ClaimRequestStatus.Pending)}'");

        builder.HasOne(r => r.Slot)
            .WithMany()
            .HasForeignKey(r => r.SlotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Contributor)
            .WithMany()
            .HasForeignKey(r => r.ContributorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t => t.HasCheckConstraint("ck_claim_requests_link", "link ~* '^https?://'"));
    }
}
