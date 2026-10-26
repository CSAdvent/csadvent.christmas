using System.Globalization;
using CSAdvent.Web.Backups;
using CSAdvent.Web.Manage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>
/// One round of asking the backup posters about a spot (#153). Each backup is asked once per round, in sign-up order, and
/// when everyone's been asked the organizers hear about it once per round. A new round starts when the spot needs a backup
/// for a new reason or a new author (<see cref="HolderOf"/>): dropped in November with nobody taking it, then given to a new
/// author whose link is missing in December, say, everyone's asked again. Rounds only go forward: the spot's latest round is
/// the one being asked. Nothing here is personal: the author is their contributor ID, or the public name on the spot.
/// </summary>
public sealed class BackupRound
{
    public int Id { get; private set; }

    public int SlotId { get; init; }

    public Slot Slot { get; private set; } = null!;

    /// <summary>1 for the spot's first round, then one more for each new one.</summary>
    public int Number { get; init; }

    public BackupReason Reason { get; init; }

    /// <summary>Who or what the spot was waiting on (<see cref="HolderOf"/>).</summary>
    public required string Holder { get; init; }

    /// <summary>
    /// The spot's offers with a higher ID than this are the round's: it's the spot's highest offer ID when the round started
    /// (0 if there were none), so offers don't need a column of their own.
    /// </summary>
    public int OffersAfter { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    /// <summary>When the organizers were told nobody's left to ask. Set by the run (or replica) that sends it, once per round.</summary>
    public DateTimeOffset? NobodyLeftAt { get; set; }

    /// <summary>
    /// The round a spot needing a backup for <paramref name="reason"/> is in: its latest round, if that's for the same reason
    /// and the same holder, or else a new one after it (not saved yet), whose offers are those after
    /// <paramref name="highestOfferId"/>.
    /// </summary>
    public static BackupRound For(BackupRound? latest, Slot slot, BackupReason reason, int highestOfferId, DateTimeOffset now)
    {
        var holder = HolderOf(slot, reason);
        return latest is not null && latest.Reason == reason && latest.Holder == holder
            ? latest
            : new BackupRound
            {
                SlotId = slot.Id,
                Number = (latest?.Number ?? 0) + 1,
                Reason = reason,
                Holder = holder,
                OffersAfter = highestOfferId,
                StartedAt = now,
            };
    }

    /// <summary>
    /// Who or what a spot needing a backup for <paramref name="reason"/> is waiting on, which starts a new round when it
    /// changes. For a dropped spot it's the dropout itself (another dropout later is a new round); otherwise it's the author:
    /// their contributor ID, or for an author an organizer added by hand, their name whatever its case or spacing, so fixing a
    /// typo doesn't ask everyone again. Times are to the second, because PostgreSQL keeps microseconds and .NET keeps more.
    /// </summary>
    public static string HolderOf(Slot slot, BackupReason reason) => reason switch
    {
        BackupReason.DroppedOut => string.Create(CultureInfo.InvariantCulture, $"dropped:{slot.DroppedOutAt?.ToUnixTimeSeconds()}"),
        _ when slot.ContributorId is { } contributorId => string.Create(CultureInfo.InvariantCulture, $"contributor:{contributorId}"),
        _ => $"name:{AuthorService.Key(slot.AuthorName ?? "")}",
    };
}

internal sealed class BackupRoundConfiguration : IEntityTypeConfiguration<BackupRound>
{
    public void Configure(EntityTypeBuilder<BackupRound> builder)
    {
        builder.Property(r => r.Reason).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Holder).HasMaxLength(150);

        // Two runs (or replicas) starting the same spot's next round at once: one of them gets it.
        builder.HasIndex(r => new { r.SlotId, r.Number }).IsUnique();

        builder.HasOne(r => r.Slot)
            .WithMany()
            .HasForeignKey(r => r.SlotId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
