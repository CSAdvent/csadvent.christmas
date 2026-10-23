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

    /// <summary>The name they'd like on the calendar, from their profile or their last claim. Prefills the next claim.</summary>
    public string? DisplayName { get; set; }

    /// <summary>The link they'd like their name to go to, from their profile or their last claim. Prefills the next claim.</summary>
    public string? WebsiteUrl { get; set; }

    /// <summary>
    /// When their social accounts were filled in from their GitHub profile. That happens on their first sign-in, and again
    /// only when they ask, so an account they remove doesn't come back.
    /// </summary>
    public DateTimeOffset? GithubSocialCheckedAt { get; set; }

    // Social accounts, so posts about their entries can tag them. Stored in the forms SocialAccounts produces.

    /// <summary>A Bluesky handle, such as <c>alice.bsky.social</c>.</summary>
    public string? BlueskyHandle { get; set; }

    /// <summary>A Mastodon handle, such as <c>@alice@hachyderm.io</c>.</summary>
    public string? MastodonHandle { get; set; }

    /// <summary>A LinkedIn profile URL, such as <c>https://www.linkedin.com/in/alice</c>.</summary>
    public string? LinkedInUrl { get; set; }

    /// <summary>A Facebook profile or Page URL, such as <c>https://www.facebook.com/alice.dev</c>.</summary>
    public string? FacebookUrl { get; set; }

    /// <summary>A Reddit username, without <c>u/</c>.</summary>
    public string? RedditUsername { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}

internal sealed class ContributorConfiguration : IEntityTypeConfiguration<Contributor>
{
    public void Configure(EntityTypeBuilder<Contributor> builder)
    {
        // GitHub logins are at most 39 characters.
        builder.Property(c => c.GithubLogin).HasMaxLength(39);
        builder.Property(c => c.Email).HasMaxLength(254);
        builder.Property(c => c.DisplayName).HasMaxLength(100);
        builder.Property(c => c.WebsiteUrl).HasMaxLength(500);
        builder.Property(c => c.BlueskyHandle).HasMaxLength(253);
        builder.Property(c => c.MastodonHandle).HasMaxLength(100);
        builder.Property(c => c.LinkedInUrl).HasMaxLength(200).HasColumnName("linkedin_url");
        builder.Property(c => c.FacebookUrl).HasMaxLength(200);
        builder.Property(c => c.RedditUsername).HasMaxLength(20);

        builder.HasIndex(c => c.GithubUserId).IsUnique();
    }
}
