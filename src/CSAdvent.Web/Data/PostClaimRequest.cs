using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

public enum PostClaimStatus
{
    Pending,
    Approved,
    Declined,
}

/// <summary>
/// A signed-in contributor asking for a finished season's post to be added to their account (#120): a post nobody is linked
/// to yet. An organizer approves it, which links the post to them, or declines it. No email address is kept here: emails use
/// the contributor's verified GitHub email. A decided request is deleted 30 days later (<c>ContactRetention</c>).
/// </summary>
public sealed class PostClaimRequest
{
    public int Id { get; private set; }

    public int SlotId { get; init; }

    public Slot Slot { get; private set; } = null!;

    public int ContributorId { get; init; }

    public Contributor Contributor { get; private set; } = null!;

    /// <summary>Anything that helps the organizers check, such as the name or blog they wrote under then.</summary>
    public string? Note { get; init; }

    public DateTimeOffset SubmittedAt { get; init; }

    public PostClaimStatus Status { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>The organizer's GitHub login.</summary>
    public string? DecidedBy { get; set; }

    /// <summary>Concurrency token (PostgreSQL <c>xmin</c>), so two organizers can't both decide it.</summary>
    public uint Version { get; private set; }
}

internal sealed class PostClaimRequestConfiguration : IEntityTypeConfiguration<PostClaimRequest>
{
    public void Configure(EntityTypeBuilder<PostClaimRequest> builder)
    {
        builder.Property(r => r.Note).HasMaxLength(1000);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(r => r.DecidedBy).HasMaxLength(39);
        builder.Property(r => r.Version).IsRowVersion();

        // One pending request per person and post; asking again after a decision is a new request.
        builder.HasIndex(r => new { r.SlotId, r.ContributorId })
            .IsUnique()
            .HasFilter($"status = '{nameof(PostClaimStatus.Pending)}'");
        builder.HasIndex(r => new { r.Status, r.SubmittedAt });
        builder.HasIndex(r => r.ContributorId);

        builder.HasOne(r => r.Slot)
            .WithMany()
            .HasForeignKey(r => r.SlotId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Contributor)
            .WithMany()
            .HasForeignKey(r => r.ContributorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
