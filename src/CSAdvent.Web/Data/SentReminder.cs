using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

public enum ReminderKind
{
    /// <summary>The slot has a title and URL: "your day is coming up".</summary>
    ContributorOnTrack,

    /// <summary>The slot is missing its title or URL: "action needed".</summary>
    ContributorActionNeeded,

    /// <summary>Tells the organizer a claimed slot has no contact email.</summary>
    AdminMissingContact,

    /// <summary>The organizer's summary of each reminder run.</summary>
    AdminDailySummary,
}

/// <summary>
/// A reminder email that was sent. The unique index on kind, run date and slot means only one app replica can record
/// (and so send) each reminder.
/// </summary>
public sealed class SentReminder
{
    public int Id { get; private set; }

    public ReminderKind Kind { get; init; }

    /// <summary>The day of the reminder run, in the reveal time zone.</summary>
    public DateOnly RunDate { get; init; }

    /// <summary>The slot the reminder is about. Empty for the organizer's daily summary.</summary>
    public int? SlotId { get; init; }

    public Slot? Slot { get; private set; }

    public DateTimeOffset SentAt { get; init; }
}

internal sealed class SentReminderConfiguration : IEntityTypeConfiguration<SentReminder>
{
    public void Configure(EntityTypeBuilder<SentReminder> builder)
    {
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(30);

        builder.HasIndex(r => new { r.Kind, r.RunDate, r.SlotId })
            .IsUnique()
            .AreNullsDistinct(false);

        builder.HasOne(r => r.Slot)
            .WithMany()
            .HasForeignKey(r => r.SlotId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
