using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>
/// When a background job step (<see cref="Jobs.JobStep"/>) last succeeded and last failed, on any replica (#172), for
/// /manage. Every replica writes the later of its own times and what's here, at most about once a minute per step
/// (<see cref="Jobs.JobRuns"/>). Nothing here is personal.
/// </summary>
public sealed class JobHeartbeat
{
    /// <summary>The step's <see cref="Jobs.JobStep.Key"/>.</summary>
    public required string Step { get; init; }

    public DateTimeOffset? LastSucceededAt { get; init; }

    public DateTimeOffset? LastFailedAt { get; init; }
}

internal sealed class JobHeartbeatConfiguration : IEntityTypeConfiguration<JobHeartbeat>
{
    public void Configure(EntityTypeBuilder<JobHeartbeat> builder)
    {
        builder.HasKey(h => h.Step);
        builder.Property(h => h.Step).HasMaxLength(50);
    }
}
