using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>
/// One post in the calendar: a season, a day (1-25) and a part. Everything here is public. The author's email
/// lives in <see cref="SlotContact"/>, so it can be purged after the season without touching the calendar.
/// </summary>
/// <remarks>
/// A slot is open while it has no author, and claimed once it has one. Its post is revealed at midnight on its day
/// in the configured time zone. A slot whose author never published keeps the author but has no post.
/// </remarks>
public sealed class Slot
{
    /// <summary>How long a post's one-line summary can be: about what a search result or a link preview shows.</summary>
    public const int PostSummaryMaxLength = 160;

    public int Id { get; private set; }

    public int SeasonYear { get; init; }

    public Season Season { get; private set; } = null!;

    public int Day { get; init; }

    public int Part { get; init; }

    /// <summary>The author's display name. Empty while the slot is open.</summary>
    public string? AuthorName { get; set; }

    /// <summary>The author's own site or profile, not the post.</summary>
    public string? AuthorUrl { get; set; }

    public string? PostTitle { get; set; }

    public string? PostUrl { get; set; }

    /// <summary>
    /// The post in one line, from its author or an organizer (#208). Optional. Like the title, it's only shown once the post is
    /// revealed: on its entry page, in the feeds and in our social posts.
    /// </summary>
    public string? PostSummary { get; set; }

    /// <summary>
    /// The signed-in contributor who owns the slot. Empty for imported history and for authors the organizer adds by hand, and
    /// always empty while the slot has no author (<c>ck_slots_contributor_needs_author</c>).
    /// </summary>
    public int? ContributorId { get; set; }

    public Contributor? Contributor { get; set; }

    public SlotContact? Contact { get; set; }

    /// <summary>
    /// When the spot's author dropped out (or an organizer released it). While the spot stays open, the backup posters are
    /// asked to fill it. Says nothing about who dropped out: they're never named. Cleared whenever the spot gets an author
    /// again (#188), and always empty while it has one (<c>ck_slots_author_not_dropped_out</c>, #245).
    /// </summary>
    public DateTimeOffset? DroppedOutAt { get; set; }

    /// <summary>Concurrency token (PostgreSQL <c>xmin</c>), so an approval and an edit can't silently overwrite each other.</summary>
    public uint Version { get; private set; }

    public bool IsClaimed => AuthorName is not null;
}

internal sealed class SlotConfiguration : IEntityTypeConfiguration<Slot>
{
    public void Configure(EntityTypeBuilder<Slot> builder)
    {
        builder.Property(s => s.AuthorName).HasMaxLength(100);
        builder.Property(s => s.AuthorUrl).HasMaxLength(500);
        builder.Property(s => s.PostTitle).HasMaxLength(300);
        builder.Property(s => s.PostUrl).HasMaxLength(500);
        builder.Property(s => s.PostSummary).HasMaxLength(Slot.PostSummaryMaxLength);
        builder.Property(s => s.Version).IsRowVersion();

        builder.HasIndex(s => new { s.SeasonYear, s.Day, s.Part }).IsUnique();

        // History is precious: a season with slots can't be deleted by accident.
        builder.HasOne(s => s.Season)
            .WithMany(s => s.Slots)
            .HasForeignKey(s => s.SeasonYear)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Contributor)
            .WithMany()
            .HasForeignKey(s => s.ContributorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_slots_day", $"day BETWEEN 1 AND {Season.DaysInAdvent}");
            t.HasCheckConstraint("ck_slots_part", $"part BETWEEN 1 AND {Season.MaxSlotsPerDay}");
            t.HasCheckConstraint("ck_slots_author_url", "author_url ~* '^https?://'");
            t.HasCheckConstraint("ck_slots_post_url", "post_url ~* '^https?://'");
            t.HasCheckConstraint("ck_slots_post_requires_author", "author_name IS NOT NULL OR (post_title IS NULL AND post_url IS NULL)");

            // A spot without an author isn't anyone's (#188), and a spot with one isn't dropped out (#245). Everything that gives
            // a spot an author clears dropped_out_at, which the code before #188 didn't, so this waited until nothing older was
            // left to serve during a deploy or come back with a rollback.
            t.HasCheckConstraint("ck_slots_contributor_needs_author", "contributor_id IS NULL OR author_name IS NOT NULL");
            t.HasCheckConstraint("ck_slots_author_not_dropped_out", "author_name IS NULL OR dropped_out_at IS NULL");
        });
    }
}
