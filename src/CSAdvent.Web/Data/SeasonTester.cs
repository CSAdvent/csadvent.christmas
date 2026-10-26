using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>
/// Someone invited to test a season, by GitHub login (stored lowercase), so they can be invited before they've ever signed
/// in. The invitation becomes theirs by GitHub user ID (<see cref="GithubUserId"/>) the first time someone with that login
/// signs in or visits signed in, and from then on only that user ID counts (#192): logins can be renamed, and a login that's
/// been given up can be registered by anyone.
/// </summary>
public sealed class SeasonTester
{
    /// <summary>GitHub logins are at most 39 characters.</summary>
    public const int MaxLoginLength = 39;

    public int SeasonYear { get; set; }

    public Season Season { get; private set; } = null!;

    /// <summary>The login they were invited as. It doesn't follow a rename; their contributor record has their current one.</summary>
    public required string GithubLogin { get; set; }

    /// <summary>Whose invitation it is, once it's been used. Null while it's still waiting for them.</summary>
    public long? GithubUserId { get; set; }

    public DateTimeOffset AddedAt { get; set; }
}

internal sealed class SeasonTesterConfiguration : IEntityTypeConfiguration<SeasonTester>
{
    public void Configure(EntityTypeBuilder<SeasonTester> builder)
    {
        builder.HasKey(t => new { t.SeasonYear, t.GithubLogin });
        builder.Property(t => t.GithubLogin).HasMaxLength(SeasonTester.MaxLoginLength);

        // Someone is a tester once per season, whatever logins they were invited as. Invitations still waiting have no user
        // ID, and PostgreSQL doesn't count those as duplicates.
        builder.HasIndex(t => new { t.SeasonYear, t.GithubUserId }).IsUnique();

        builder.HasOne(t => t.Season)
            .WithMany(s => s.Testers)
            .HasForeignKey(t => t.SeasonYear)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t => t.HasCheckConstraint("ck_season_testers_login", "github_login = lower(github_login)"));
    }
}
