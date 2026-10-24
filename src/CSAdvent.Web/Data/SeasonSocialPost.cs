using CSAdvent.Web.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

public enum SeasonPostKind
{
    /// <summary>"Sign-ups open in 8 days": posted by hand whenever an organizer likes, as often as they like.</summary>
    SignUpsSoon,

    /// <summary>"Sign-ups are open": posted automatically on the day, once per platform.</summary>
    SignUpsOpen,

    /// <summary>"A look back at C# Advent 2026": the season's published retrospective, posted by hand once per platform.</summary>
    Retrospective,
}

/// <summary>
/// A post about a season rather than an entry, on one platform: "sign-ups open soon", posted by hand
/// (<c>/manage/social/signups</c>); "sign-ups are open", posted automatically at the morning send time on the day sign-ups
/// open (once per season and platform), from the page, or marked posted after copying it; or the season's retrospective,
/// once per platform, from its page (<c>/manage/retrospectives/{year}</c>).
/// </summary>
public sealed class SeasonSocialPost
{
    public int Id { get; private set; }

    public int SeasonYear { get; init; }

    public SeasonPostKind Kind { get; init; }

    public Season Season { get; private set; } = null!;

    public SocialPlatform Platform { get; init; }

    public SocialPostStatus Status { get; set; }

    public DateTimeOffset At { get; set; }

    /// <summary>A link to the post on the platform, if there is one.</summary>
    public string? PostUrl { get; set; }

    /// <summary>The organizer's GitHub login, or <c>automation</c>.</summary>
    public required string MarkedBy { get; set; }

    public string? Error { get; set; }
}

internal sealed class SeasonSocialPostConfiguration : IEntityTypeConfiguration<SeasonSocialPost>
{
    public void Configure(EntityTypeBuilder<SeasonSocialPost> builder)
    {
        builder.Property(p => p.Platform).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.PostUrl).HasMaxLength(500);
        builder.Property(p => p.MarkedBy).HasMaxLength(39);
        builder.Property(p => p.Error).HasMaxLength(1000);

        // "Sign-ups are open" and the retrospective go out once per platform; "soon" can go out as often as an organizer likes.
        builder.HasIndex(p => new { p.SeasonYear, p.Kind, p.Platform }).IsUnique().HasFilter("kind <> 'SignUpsSoon'");
        builder.HasIndex(p => new { p.SeasonYear, p.Kind });

        builder.HasOne(p => p.Season)
            .WithMany()
            .HasForeignKey(p => p.SeasonYear)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t => t.HasCheckConstraint("ck_season_social_posts_post_url", "post_url ~* '^https?://'"));
    }
}
