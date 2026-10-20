using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>Someone who signed in with GitHub, matched by GitHub user ID because logins can change.</summary>
public sealed class Contributor
{
    public int Id { get; private set; }

    public long GithubUserId { get; init; }

    public required string GithubLogin { get; set; }

    /// <summary>The address the contributor verified. Private, and purged with the season's other contact details.</summary>
    public string? Email { get; set; }

    public DateTimeOffset? EmailVerifiedAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}

internal sealed class ContributorConfiguration : IEntityTypeConfiguration<Contributor>
{
    public void Configure(EntityTypeBuilder<Contributor> builder)
    {
        // GitHub logins are at most 39 characters.
        builder.Property(c => c.GithubLogin).HasMaxLength(39);
        builder.Property(c => c.Email).HasMaxLength(254);

        builder.HasIndex(c => c.GithubUserId).IsUnique();
    }
}
