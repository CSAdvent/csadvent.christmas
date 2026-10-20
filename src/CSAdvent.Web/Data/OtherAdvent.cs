using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>Another developer advent calendar listed on the Other Advents page for a season.</summary>
public sealed class OtherAdvent
{
    public int Id { get; private set; }

    public int SeasonYear { get; private set; }

    public Season Season { get; private set; } = null!;

    public required string Name { get; set; }

    public required string Url { get; set; }

    public string? Description { get; set; }

    public int SortOrder { get; set; }
}

internal sealed class OtherAdventConfiguration : IEntityTypeConfiguration<OtherAdvent>
{
    public void Configure(EntityTypeBuilder<OtherAdvent> builder)
    {
        builder.Property(a => a.Name).HasMaxLength(100);
        builder.Property(a => a.Url).HasMaxLength(500);
        builder.Property(a => a.Description).HasMaxLength(500);

        builder.HasIndex(a => new { a.SeasonYear, a.SortOrder });

        builder.HasOne(a => a.Season)
            .WithMany(s => s.OtherAdvents)
            .HasForeignKey(a => a.SeasonYear)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t => t.HasCheckConstraint("ck_other_advents_url", "url ~* '^https?://'"));
    }
}
