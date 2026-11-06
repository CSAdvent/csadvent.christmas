using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>
/// An organizer invited on <c>/manage/organizers</c>, by GitHub login (stored lowercase), so they can be invited before
/// they've ever signed in. The invitation becomes theirs by GitHub user ID (<see cref="GithubUserId"/>) when someone with
/// that login next signs in with GitHub, and from then on only that user ID counts: logins can be renamed, and a login
/// that's been given up can be registered by anyone. Until then it gives nobody anything. The default organizers in
/// <c>Accounts:Admins</c> aren't here: they're organizers whatever this table holds.
/// </summary>
public sealed class Organizer
{
    public int Id { get; set; }

    /// <summary>The login they were invited as. It doesn't follow a rename; their contributor record has their current one.</summary>
    public required string GithubLogin { get; set; }

    /// <summary>Whose invitation it is, once they've signed in. Null while it's still waiting for them.</summary>
    public long? GithubUserId { get; set; }

    public DateTimeOffset InvitedAt { get; set; }

    /// <summary>The GitHub user ID of the organizer who invited them, if it's known.</summary>
    public long? InvitedByGithubUserId { get; set; }
}

internal sealed class OrganizerConfiguration : IEntityTypeConfiguration<Organizer>
{
    public void Configure(EntityTypeBuilder<Organizer> builder)
    {
        builder.Property(o => o.GithubLogin).HasMaxLength(SeasonTester.MaxLoginLength);

        // Someone is an organizer once, whatever logins they were invited as. Invitations still waiting have no user ID,
        // and PostgreSQL doesn't count those as duplicates.
        builder.HasIndex(o => o.GithubUserId).IsUnique();

        // One waiting invitation per login.
        builder.HasIndex(o => o.GithubLogin).IsUnique().HasFilter("github_user_id IS NULL");

        builder.ToTable(t => t.HasCheckConstraint("ck_organizers_login", "github_login = lower(github_login)"));
    }
}
