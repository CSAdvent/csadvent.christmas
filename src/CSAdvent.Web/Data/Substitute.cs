using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>Someone on standby to fill a slot when an author drops out, contacted in sign-up order. Everything here is private.</summary>
public sealed class Substitute
{
    public int Id { get; private set; }

    public int SeasonYear { get; private set; }

    public Season Season { get; private set; } = null!;

    /// <summary>The signed-in contributor, if they signed up while signed in.</summary>
    public int? ContributorId { get; init; }

    public Contributor? Contributor { get; private set; }

    public required string Name { get; set; }

    public required string Email { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset SignedUpAt { get; init; }

    /// <summary>The slot the organizer moved them into. Substitutes who couldn't step in stay on the list for the next dropout.</summary>
    public int? AssignedSlotId { get; set; }

    public Slot? AssignedSlot { get; set; }
}

internal sealed class SubstituteConfiguration : IEntityTypeConfiguration<Substitute>
{
    public void Configure(EntityTypeBuilder<Substitute> builder)
    {
        builder.Property(s => s.Name).HasMaxLength(100);
        builder.Property(s => s.Email).HasMaxLength(254);
        builder.Property(s => s.Notes).HasMaxLength(1000);

        builder.HasIndex(s => new { s.SeasonYear, s.SignedUpAt });
        builder.HasIndex(s => new { s.SeasonYear, s.ContributorId })
            .IsUnique()
            .HasFilter("contributor_id IS NOT NULL");

        builder.HasOne(s => s.Season)
            .WithMany(s => s.Substitutes)
            .HasForeignKey(s => s.SeasonYear)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Contributor)
            .WithMany()
            .HasForeignKey(s => s.ContributorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.AssignedSlot)
            .WithMany()
            .HasForeignKey(s => s.AssignedSlotId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
