using CSAdvent.Web.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

public enum SocialPostingMode
{
    /// <summary>Organizers copy and paste the announcement, then mark it posted. The default.</summary>
    Manual,

    /// <summary>Each announcement is posted at its time with nobody at the keyboard (#61). Only with credentials.</summary>
    Automatic,

    /// <summary>Not posted on this platform at all, so it's left off the social page.</summary>
    Off,
}

/// <summary>
/// How announcements go out on one platform, changed by organizers on the social page without a redeploy, and the last
/// check of its credentials. A platform without a row is <see cref="SocialPostingMode.Manual"/>.
/// </summary>
public sealed class SocialPlatformSetting
{
    public SocialPlatform Platform { get; init; }

    public SocialPostingMode Mode { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The organizer's GitHub login.</summary>
    public required string UpdatedBy { get; set; }

    /// <summary>When the credentials were last checked, by an organizer or the daily check.</summary>
    public DateTimeOffset? CheckedAt { get; set; }

    /// <summary>Why the last check failed, or null if it passed.</summary>
    public string? CheckError { get; set; }
}

internal sealed class SocialPlatformSettingConfiguration : IEntityTypeConfiguration<SocialPlatformSetting>
{
    public void Configure(EntityTypeBuilder<SocialPlatformSetting> builder)
    {
        builder.HasKey(s => s.Platform);
        builder.Property(s => s.Platform).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Mode).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.UpdatedBy).HasMaxLength(39);
        builder.Property(s => s.CheckError).HasMaxLength(1000);
    }
}
