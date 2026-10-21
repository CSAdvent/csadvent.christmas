using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>
/// The private half of a slot: how to reach its author. Kept apart from <see cref="Slot"/> so contact details can be
/// deleted after the season while the public calendar stays intact.
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
