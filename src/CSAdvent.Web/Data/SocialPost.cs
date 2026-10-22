using CSAdvent.Web.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

public enum SocialPostStatus
{
    Posted,

    /// <summary>Automatic posting (#61) tried and failed; <see cref="SocialPost.Error"/> says why.</summary>
    Failed,
}

/// <summary>
/// An entry's announcement on one platform: marked by an organizer after posting it by hand, or recorded by automatic
/// posting (#61). One per entry and platform.
/// </summary>
public sealed class SocialPost
{
    public int Id { get; private set; }

    public int SlotId { get; init; }

    public Slot Slot { get; private set; } = null!;

    public SocialPlatform Platform { get; init; }

    public SocialPostStatus Status { get; set; }

    public DateTimeOffset At { get; set; }

    /// <summary>A link to the post on the platform, if there is one.</summary>
    public string? PostUrl { get; set; }

    /// <summary>The organizer's GitHub login, or <c>automation</c>.</summary>
    public required string MarkedBy { get; set; }

    public string? Error { get; set; }
}

internal sealed class SocialPostConfiguration : IEntityTypeConfiguration<SocialPost>
{
    public void Configure(EntityTypeBuilder<SocialPost> builder)
    {
        builder.Property(p => p.Platform).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.PostUrl).HasMaxLength(500);
        builder.Property(p => p.MarkedBy).HasMaxLength(39);
        builder.Property(p => p.Error).HasMaxLength(1000);

        builder.HasIndex(p => new { p.SlotId, p.Platform }).IsUnique();

        builder.HasOne(p => p.Slot)
            .WithMany()
            .HasForeignKey(p => p.SlotId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t => t.HasCheckConstraint("ck_social_posts_post_url", "post_url ~* '^https?://'"));
    }
}
