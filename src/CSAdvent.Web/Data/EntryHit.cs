using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

public enum EntryHitKind
{
    /// <summary>Someone opened the entry's page on our site.</summary>
    View,

    /// <summary>Someone followed "Read the post" (or a calendar title) through to the author's post.</summary>
    Click,
}

/// <summary>
/// One visit or click on an entry, for the season's statistics (#62). There's no personal data: no IP address, cookie or
/// user agent, just the entry, when, and where the link came from (<c>?via=</c>).
/// </summary>
public sealed class EntryHit
{
    public long Id { get; private set; }

    public int SlotId { get; init; }

    public Slot Slot { get; private set; } = null!;

    public EntryHitKind Kind { get; init; }

    /// <summary>Where the link came from, such as <c>bluesky</c>, <c>rss</c> or <c>calendar</c> (see <c>EntryVia</c>). Empty when unknown.</summary>
    public string? Via { get; init; }

    public DateTimeOffset At { get; init; }
}

internal sealed class EntryHitConfiguration : IEntityTypeConfiguration<EntryHit>
{
    public void Configure(EntityTypeBuilder<EntryHit> builder)
    {
        builder.Property(h => h.Kind).HasConversion<string>().HasMaxLength(10);
        builder.Property(h => h.Via).HasMaxLength(20);

        builder.HasIndex(h => new { h.SlotId, h.Kind });
        builder.HasIndex(h => h.At);

        builder.HasOne(h => h.Slot)
            .WithMany()
            .HasForeignKey(h => h.SlotId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
