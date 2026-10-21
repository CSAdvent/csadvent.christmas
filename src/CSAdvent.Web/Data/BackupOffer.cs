using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

public enum BackupOfferStatus
{
    /// <summary>Emailed; they can accept or decline. After <see cref="BackupOffer.ExpiresAt"/> the next backup is asked too.</summary>
    Asked,

    /// <summary>They said not this time, so the next backup is asked.</summary>
    Declined,

    /// <summary>They sent a post. It goes on the calendar at the reveal if the original author's link still hasn't arrived.</summary>
    Accepted,

    /// <summary>Their post replaced the original author's.</summary>
    Used,

    /// <summary>Closed without being used: the original author's link arrived, or someone else stepped in.</summary>
    Withdrawn,
}

/// <summary>
/// One backup poster asked about one spot whose author hadn't sent their post's link. Everything here is private, and goes
/// when the season's backup list is deleted.
/// </summary>
public sealed class BackupOffer
{
    public int Id { get; private set; }

    public int SlotId { get; init; }

    public Slot Slot { get; private set; } = null!;

    public int SubstituteId { get; init; }

    public Substitute Substitute { get; private set; } = null!;

    public DateTimeOffset AskedAt { get; init; }

    /// <summary>When the next backup gets asked too. They can still accept after this, while the spot needs a post.</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    public BackupOfferStatus Status { get; set; }

    public DateTimeOffset? RespondedAt { get; set; }

    // What goes on the calendar if this offer is used.

    public string? AuthorName { get; set; }

    public string? AuthorUrl { get; set; }

    public string? PostTitle { get; set; }

    public string? PostUrl { get; set; }

    /// <summary>Concurrency token (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; private set; }
}

internal sealed class BackupOfferConfiguration : IEntityTypeConfiguration<BackupOffer>
{
    public void Configure(EntityTypeBuilder<BackupOffer> builder)
    {
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.AuthorName).HasMaxLength(100);
        builder.Property(o => o.AuthorUrl).HasMaxLength(500);
        builder.Property(o => o.PostTitle).HasMaxLength(300);
        builder.Property(o => o.PostUrl).HasMaxLength(500);
        builder.Property(o => o.Version).IsRowVersion();

        // Each backup is asked about a spot at most once.
        builder.HasIndex(o => new { o.SlotId, o.SubstituteId }).IsUnique();

        // Only one backup can be waiting to take a spot: a second acceptance hits this index. Used offers aren't included, so
        // if a backup who filled a spot drops out too, the spot can be offered again; the slot's xmin guards using it twice.
        builder.HasIndex(o => o.SlotId)
            .IsUnique()
            .HasDatabaseName("ix_backup_offers_one_taker")
            .HasFilter($"status = '{nameof(BackupOfferStatus.Accepted)}'");

        builder.HasOne(o => o.Slot)
            .WithMany()
            .HasForeignKey(o => o.SlotId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(o => o.Substitute)
            .WithMany()
            .HasForeignKey(o => o.SubstituteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_backup_offers_author_url", "author_url ~* '^https?://'");
            t.HasCheckConstraint("ck_backup_offers_post_url", "post_url ~* '^https?://'");
        });
    }
}
