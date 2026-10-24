using CSAdvent.Web.Retrospectives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>
/// A finished season's look back, at <c>/archive/{year}/retrospective</c> once it's published: the organizers' post, in
/// Markdown, and the season's numbers for its charts. The draft is written from the numbers when it's generated, then it's
/// the organizers' to rewrite. Never for the test season.
/// </summary>
public sealed class Retrospective
{
    public int SeasonYear { get; init; }

    public Season Season { get; private set; } = null!;

    public required string Title { get; set; }

    /// <summary>The post, in Markdown.</summary>
    public required string Body { get; set; }

    /// <summary>Whether the page shows the charts from <see cref="Stats"/> after the post.</summary>
    public bool ShowCharts { get; set; } = true;

    /// <summary>The season's numbers when the draft was generated (stored as JSON).</summary>
    public required RetrospectiveStats Stats { get; set; }

    public DateTimeOffset StatsAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The organizer's GitHub login.</summary>
    public required string UpdatedBy { get; set; }

    /// <summary>When it went public. Empty while it's a draft.</summary>
    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>Concurrency token (PostgreSQL <c>xmin</c>), so two organizers' edits can't silently overwrite each other.</summary>
    public uint Version { get; private set; }

    public bool IsPublished => PublishedAt is not null;
}

internal sealed class RetrospectiveConfiguration : IEntityTypeConfiguration<Retrospective>
{
    public void Configure(EntityTypeBuilder<Retrospective> builder)
    {
        builder.HasKey(r => r.SeasonYear);
        builder.Property(r => r.SeasonYear).ValueGeneratedNever();
        builder.Property(r => r.Title).HasMaxLength(150);
        builder.Property(r => r.Body).HasMaxLength(20000);
        builder.Property(r => r.UpdatedBy).HasMaxLength(39);
        builder.Property(r => r.Version).IsRowVersion();
        builder.Property(r => r.Stats).HasConversion(stats => stats.ToJson(), json => RetrospectiveStats.FromJson(json));

        builder.HasOne(r => r.Season)
            .WithMany()
            .HasForeignKey(r => r.SeasonYear)
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t => t.HasCheckConstraint("ck_retrospectives_not_test", $"season_year <> {Season.TestYear}"));
    }
}
