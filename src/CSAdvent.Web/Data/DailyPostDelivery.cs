using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>
/// One subscriber's copy of a day's posts (#206), recorded before it's sent, so nobody gets a day twice even across replicas.
/// Deleted with the subscriber when they unsubscribe.
/// </summary>
public sealed class DailyPostDelivery
{
    public int Id { get; private set; }

    public int SeasonYear { get; init; }

    public Season Season { get; private set; } = null!;

    /// <summary>The day of Advent, 1 to 25.</summary>
    public int Day { get; init; }

    public int SubscriberId { get; init; }

    public AnnouncementSubscriber Subscriber { get; private set; } = null!;

    public DateTimeOffset SentAt { get; init; }
}

internal sealed class DailyPostDeliveryConfiguration : IEntityTypeConfiguration<DailyPostDelivery>
{
    public void Configure(EntityTypeBuilder<DailyPostDelivery> builder)
    {
        builder.HasIndex(d => new { d.SeasonYear, d.Day, d.SubscriberId }).IsUnique();
        builder.HasIndex(d => d.SentAt);

        builder.HasOne(d => d.Season).WithMany().HasForeignKey(d => d.SeasonYear).OnDelete(DeleteBehavior.Cascade);

        // Unsubscribing deletes the subscriber, and their deliveries with them.
        builder.HasOne(d => d.Subscriber).WithMany().HasForeignKey(d => d.SubscriberId).OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_daily_post_deliveries_subscriber_id");

        builder.ToTable(t => t.HasCheckConstraint("ck_daily_post_deliveries_day", $"day BETWEEN 1 AND {Season.DaysInAdvent}"));
    }
}
