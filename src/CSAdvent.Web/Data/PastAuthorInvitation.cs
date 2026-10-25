using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>
/// A past author's invitation to a season (#133), recorded before it's sent, so nobody gets it twice even across replicas.
/// Only a one-way fingerprint of the address is kept (<c>EmailOptOutLinks.Fingerprint</c>), never the address itself.
/// Deleted with the season's requests on February 1 after it (<c>ContactRetention</c>).
/// </summary>
public sealed class PastAuthorInvitation
{
    public int Id { get; private set; }

    public int SeasonYear { get; init; }

    public Season Season { get; private set; } = null!;

    /// <summary>The SHA-256 of the address in lowercase, as hex.</summary>
    public required string Fingerprint { get; init; }

    public DateTimeOffset SentAt { get; init; }
}

internal sealed class PastAuthorInvitationConfiguration : IEntityTypeConfiguration<PastAuthorInvitation>
{
    public void Configure(EntityTypeBuilder<PastAuthorInvitation> builder)
    {
        builder.Property(i => i.Fingerprint).HasMaxLength(64).IsFixedLength();

        builder.HasIndex(i => new { i.SeasonYear, i.Fingerprint }).IsUnique();
        builder.HasIndex(i => i.SentAt);

        builder.HasOne(i => i.Season)
            .WithMany()
            .HasForeignKey(i => i.SeasonYear)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
