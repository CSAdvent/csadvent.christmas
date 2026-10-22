using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>
/// Someone invited to test a season before its sign-ups open, by GitHub login (stored lowercase), so they can be added
/// before they've ever signed in.
/// </summary>
public sealed class SeasonTester
{
    /// <summary>GitHub logins are at most 39 characters.</summary>
    public const int MaxLoginLength = 39;

    public int SeasonYear { get; set; }

    public Season Season { get; private set; } = null!;

    public required string GithubLogin { get; set; }

    public DateTimeOffset AddedAt { get; set; }
}

internal sealed class SeasonTesterConfiguration : IEntityTypeConfiguration<SeasonTester>
{
    public void Configure(EntityTypeBuilder<SeasonTester> builder)
    {
        builder.HasKey(t => new { t.SeasonYear, t.GithubLogin });
        builder.Property(t => t.GithubLogin).HasMaxLength(SeasonTester.MaxLoginLength);

        builder.HasOne(t => t.Season)
            .WithMany(s => s.Testers)
            .HasForeignKey(t => t.SeasonYear)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t => t.HasCheckConstraint("ck_season_testers_login", "github_login = lower(github_login)"));
    }
}
