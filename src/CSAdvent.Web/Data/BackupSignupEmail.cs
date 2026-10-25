using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>
/// The "you're on the backup list" email someone got for a season, recorded before it's sent (#137). Leaving the list deletes
/// their place on it, so this is what stops leaving and joining again from sending the email every time: it goes once per
/// person and season. Deleted with the season's backup list on February 1 after it (<c>ContactRetention</c>).
/// </summary>
public sealed class BackupSignupEmail
{
    public int Id { get; private set; }

    public int SeasonYear { get; init; }

    public Season Season { get; private set; } = null!;

    public int ContributorId { get; init; }

    public Contributor Contributor { get; private set; } = null!;

    public DateTimeOffset SentAt { get; init; }
}

internal sealed class BackupSignupEmailConfiguration : IEntityTypeConfiguration<BackupSignupEmail>
{
    public void Configure(EntityTypeBuilder<BackupSignupEmail> builder)
    {
        builder.HasIndex(e => new { e.SeasonYear, e.ContributorId }).IsUnique();

        builder.HasOne(e => e.Season)
            .WithMany()
            .HasForeignKey(e => e.SeasonYear)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Contributor)
            .WithMany()
            .HasForeignKey(e => e.ContributorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
