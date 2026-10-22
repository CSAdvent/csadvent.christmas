using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>
/// An Other Advents calendar's logo, kept by the calendar's address, so every season's list (and the suggested list) shares
/// it. Fetched from the calendar's site in the background, or uploaded by an organizer.
/// </summary>
public sealed class OtherAdventLogo
{
    public int Id { get; private set; }

    public required string Url { get; set; }

    /// <summary>A PNG of at most 128×128 pixels, drawn by <see cref="Content.LogoImages"/>. Null until a fetch succeeds.</summary>
    public byte[]? Png { get; set; }

    /// <summary>An organizer uploaded it, so the background fetch leaves it alone.</summary>
    public bool IsUploaded { get; set; }

    /// <summary>When the PNG last changed. Its links carry this, so browsers pick up a new logo.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>The last fetch, whether or not it found a logo, so a site without one isn't asked again every minute.</summary>
    public DateTimeOffset? LastAttemptAt { get; set; }
}

internal sealed class OtherAdventLogoConfiguration : IEntityTypeConfiguration<OtherAdventLogo>
{
    public void Configure(EntityTypeBuilder<OtherAdventLogo> builder)
    {
        builder.Property(l => l.Url).HasMaxLength(500);
        builder.HasIndex(l => l.Url).IsUnique();
    }
}
