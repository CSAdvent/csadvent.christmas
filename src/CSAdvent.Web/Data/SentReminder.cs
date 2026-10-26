using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>
/// What a <see cref="SentReminder"/> records. Stored by name, so values can be removed: <c>ContributorOnTrack</c>,
/// <c>AdminMissingContact</c> and <c>AdminDailySummary</c> were never sent by any version, and went in #188.
/// </summary>
public enum ReminderKind
{
    /// <summary>The slot has no post URL yet: "we need your link", 7, 5, 3, 2 and 1 days before its day.</summary>
    ContributorActionNeeded,

    /// <summary>The slot still has no post URL on its day: the last call, a few hours before it's announced.</summary>
    ContributorDayOf,

    /// <summary>
    /// The daily "today's posts" email to an author in the season. Recorded against the author's first slot, so someone
    /// with two spots gets it once.
    /// </summary>
    ContributorDailyShare,

    /// <summary>
    /// Told the organizers every backup poster had been asked about a spot and nobody took it. No longer recorded here: it's
    /// once per round now, on <see cref="BackupRound.NobodyLeftAt"/> (#153). Kept for the records already made.
    /// </summary>
    AdminBackupsExhausted,

    /// <summary>
    /// Tells the organizers a backup is standing by to take a spot whose post was found without the C# Advent blurb, so they can
    /// keep the author (#148). Its time is when they were told: the backup takes the spot no sooner than an hour later.
    /// </summary>
    AdminBlurbBackupStandingBy,

    /// <summary>
    /// The organizers' morning digest (#171), once per season and day of its Advent (or of its rehearsal). Recorded against the
    /// season's first slot, so each season gets its own, and resetting the test season forgets its digests.
    /// </summary>
    AdminMorningDigest,
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

    /// <summary>The slot the reminder is about. Every kind recorded so far has one.</summary>
    public int? SlotId { get; init; }

    public Slot? Slot { get; private set; }

    public DateTimeOffset SentAt { get; init; }
}

public static class SentReminderQueries
{
    /// <summary>
    /// The slots that already have a <paramref name="kind"/> reminder recorded for <paramref name="runDate"/>. A run skips
    /// them rather than insert a record the unique index refuses, because EF Core logs every refusal as an error before the
    /// code can handle it (#157). The index still settles two runs, or replicas, recording the same reminder at once.
    /// </summary>
    public static IQueryable<int> RecordedSlots(this IQueryable<SentReminder> reminders, ReminderKind kind, DateOnly runDate) =>
        reminders.Where(r => r.Kind == kind && r.RunDate == runDate && r.SlotId != null).Select(r => r.SlotId!.Value);
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
