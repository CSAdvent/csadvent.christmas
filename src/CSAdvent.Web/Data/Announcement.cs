using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

public enum AnnouncementKind
{
    /// <summary>Written by an organizer at <c>/manage/announcements</c>.</summary>
    Organizer,

    /// <summary>Queued automatically when a season's sign-ups open: once per season.</summary>
    SignUpsOpen,
}

/// <summary>
/// An email to everyone on the announcements list (#49). A draft until it's sent; from then on it's delivered a few at a
/// time (<see cref="AnnouncementDelivery"/>) and can't be edited.
/// </summary>
public sealed class Announcement
{
    public int Id { get; private set; }

    public AnnouncementKind Kind { get; init; }

    /// <summary>The season a <see cref="AnnouncementKind.SignUpsOpen"/> announcement is about: unique, so it goes out once.</summary>
    public int? SeasonYear { get; init; }

    public required string Subject { get; set; }

    /// <summary>Plain text; a blank line starts a new paragraph.</summary>
    public required string Message { get; set; }

    public string? ButtonLabel { get; set; }

    public string? ButtonUrl { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>The organizer's GitHub login, or <c>automation</c>.</summary>
    public required string CreatedBy { get; set; }

    /// <summary>When it was sent. Subscribers confirmed by then get it; null while it's a draft.</summary>
    public DateTimeOffset? SentAt { get; set; }

    /// <summary>When the last subscriber got it.</summary>
    public DateTimeOffset? FinishedAt { get; set; }
}

/// <summary>One subscriber's copy of an announcement, recorded before it's sent, so it's never sent twice.</summary>
public sealed class AnnouncementDelivery
{
    public int Id { get; private set; }

    public int AnnouncementId { get; init; }

    public Announcement Announcement { get; private set; } = null!;

    public int SubscriberId { get; init; }

    public AnnouncementSubscriber Subscriber { get; private set; } = null!;

    public DateTimeOffset SentAt { get; init; }
}

internal sealed class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Subject).HasMaxLength(150);
        builder.Property(a => a.Message).HasMaxLength(5000);
        builder.Property(a => a.ButtonLabel).HasMaxLength(60);
        builder.Property(a => a.ButtonUrl).HasMaxLength(500);
        builder.Property(a => a.CreatedBy).HasMaxLength(39);

        builder.HasIndex(a => new { a.Kind, a.SeasonYear }).IsUnique().HasFilter("season_year IS NOT NULL");
        builder.ToTable(t => t.HasCheckConstraint("ck_announcements_button_url", "button_url IS NULL OR button_url ~* '^https?://'"));
    }
}

internal sealed class AnnouncementDeliveryConfiguration : IEntityTypeConfiguration<AnnouncementDelivery>
{
    public void Configure(EntityTypeBuilder<AnnouncementDelivery> builder)
    {
        builder.HasIndex(d => new { d.AnnouncementId, d.SubscriberId }).IsUnique();
        builder.HasIndex(d => d.SentAt);

        builder.HasOne(d => d.Announcement).WithMany().HasForeignKey(d => d.AnnouncementId).OnDelete(DeleteBehavior.Cascade);

        // Unsubscribing deletes the subscriber, and their deliveries with them.
        // Named here because the generated name is longer than PostgreSQL's 63 characters.
        builder.HasOne(d => d.Subscriber).WithMany().HasForeignKey(d => d.SubscriberId).OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_announcement_deliveries_subscriber_id");
    }
}
