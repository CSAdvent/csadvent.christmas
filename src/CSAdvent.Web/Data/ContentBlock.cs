using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>Site copy the organizer edits without a redeploy, such as the home page intro and the About page.</summary>
public sealed class ContentBlock
{
    public required string Key { get; init; }

    /// <summary>The current copy, in Markdown.</summary>
    public required string Body { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }

    public Contributor? UpdatedBy { get; set; }

    /// <summary>Every saved version, including the current one.</summary>
    public List<ContentBlockRevision> Revisions { get; private set; } = [];
}

public sealed class ContentBlockRevision
{
    public int Id { get; private set; }

    public string ContentBlockKey { get; private set; } = null!;

    public required string Body { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public int? CreatedById { get; init; }

    public Contributor? CreatedBy { get; private set; }
}

internal sealed class ContentBlockConfiguration : IEntityTypeConfiguration<ContentBlock>
{
    public void Configure(EntityTypeBuilder<ContentBlock> builder)
    {
        builder.HasKey(b => b.Key);
        builder.Property(b => b.Key).HasMaxLength(50);

        builder.HasOne(b => b.UpdatedBy)
            .WithMany()
            .HasForeignKey(b => b.UpdatedById)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(b => b.Revisions)
            .WithOne()
            .HasForeignKey(r => r.ContentBlockKey)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ContentBlockRevisionConfiguration : IEntityTypeConfiguration<ContentBlockRevision>
{
    public void Configure(EntityTypeBuilder<ContentBlockRevision> builder)
    {
        builder.HasIndex(r => new { r.ContentBlockKey, r.CreatedAt });

        builder.HasOne(r => r.CreatedBy)
            .WithMany()
            .HasForeignKey(r => r.CreatedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
