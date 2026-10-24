using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>
/// Someone who asked for C# Advent's announcements by email (#49): when each season's sign-ups open, and the organizers'
/// occasional news. Kept until they unsubscribe; an address that's never confirmed is deleted after a week.
/// </summary>
public sealed class AnnouncementSubscriber
{
    public int Id { get; private set; }

    /// <summary>Lowercase, so an address can only be on the list once.</summary>
    public required string Email { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When they followed the link in the confirmation email. Only confirmed subscribers get announcements.</summary>
    public DateTimeOffset? ConfirmedAt { get; set; }

    /// <summary>When the last confirmation email went out, so entering the same address again can't flood its inbox.</summary>
    public DateTimeOffset ConfirmationSentAt { get; set; }
}

internal sealed class AnnouncementSubscriberConfiguration : IEntityTypeConfiguration<AnnouncementSubscriber>
{
    public void Configure(EntityTypeBuilder<AnnouncementSubscriber> builder)
    {
        builder.Property(s => s.Email).HasMaxLength(254);
        builder.HasIndex(s => s.Email).IsUnique();
    }
}
