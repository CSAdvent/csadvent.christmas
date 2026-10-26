using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>
/// One "Check my post" an author asked for (#174): who, which spot, when, and whether it went to Claude. It's only there to
/// limit how often each person can ask and how many go to Claude (<c>BlurbSelfCheckService</c>); the result is the spot's
/// <see cref="BlurbCheck"/>. Deleted after 31 days (<c>ContactRetention</c>).
/// </summary>
public sealed class BlurbSelfCheck
{
    public int Id { get; private set; }

    public int SlotId { get; init; }

    public Slot Slot { get; private set; } = null!;

    public int ContributorId { get; init; }

    public Contributor Contributor { get; private set; } = null!;

    public DateTimeOffset CheckedAt { get; init; }

    /// <summary>Whether it went to Claude, which is what costs money.</summary>
    public bool AskedClaude { get; init; }
}

internal sealed class BlurbSelfCheckConfiguration : IEntityTypeConfiguration<BlurbSelfCheck>
{
    public void Configure(EntityTypeBuilder<BlurbSelfCheck> builder)
    {
        builder.HasIndex(c => new { c.ContributorId, c.CheckedAt });
        builder.HasIndex(c => c.CheckedAt);

        builder.HasOne(c => c.Slot)
            .WithMany()
            .HasForeignKey(c => c.SlotId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Contributor)
            .WithMany()
            .HasForeignKey(c => c.ContributorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
