using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>
/// The private half of a slot: how to reach its author. Kept apart from <see cref="Slot"/> so it never shows on the public
/// calendar. It stays after the season, so authors (including those an organizer added by hand) hear about the next one;
/// dropping out, an organizer releasing the spot, or asking us deletes it.
/// </summary>
public sealed class SlotContact
{
    public int SlotId { get; private set; }

    public Slot Slot { get; private set; } = null!;

    public required string Email { get; set; }

    /// <summary>
    /// When the author turned off the daily "today's posts" emails for the season, through the link in one of them. Emails
    /// about their own spot still go out.
    /// </summary>
    public DateTimeOffset? DailyEmailsOptedOutAt { get; set; }
}

internal sealed class SlotContactConfiguration : IEntityTypeConfiguration<SlotContact>
{
    public void Configure(EntityTypeBuilder<SlotContact> builder)
    {
        builder.HasKey(c => c.SlotId);
        builder.Property(c => c.Email).HasMaxLength(254);

        builder.HasOne(c => c.Slot)
            .WithOne(s => s.Contact)
            .HasForeignKey<SlotContact>(c => c.SlotId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
