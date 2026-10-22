using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CSAdvent.Web.Data;

/// <summary>
/// One year of the C# Advent. Every season since 2017 lives in the same tables, so creating a new one only
/// adds rows and never touches earlier years. A season's phase (claims open, in progress, over) comes from
/// <see cref="ClaimsOpenAt"/> and the reveal schedule, not from a status someone has to remember to change.
/// </summary>
public sealed class Season
{
    public const int FirstYear = 2017;
    public const int DaysInAdvent = 25;
    public const int MaxSlotsPerDay = 2;

    public int Year { get; private set; }

    /// <summary>Posts per day. The first season (2017) had one; every season since has had two.</summary>
    public int SlotsPerDay { get; private set; }

    /// <summary>When contributors can start requesting slots. Empty until the organizer schedules it.</summary>
    public DateTimeOffset? ClaimsOpenAt { get; set; }

    /// <summary>Organizer notes for the season, in Markdown. For example, where an early season was first hosted.</summary>
    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// While set, the season is being tested before its sign-ups open: only its testers and the organizers see its spots,
    /// and finishing testing deletes everything done in it (<see cref="SeasonTesting.SeasonTestingService"/>).
    /// </summary>
    public DateTimeOffset? TestingStartedAt { get; set; }

    /// <summary>While testing, the date that stands in for December 1, so reveals, reminders and backup asks can be tried early.</summary>
    public DateOnly? RehearsalStartsOn { get; set; }

    /// <summary>Who can take part while the season is being tested, besides the organizers.</summary>
    public List<SeasonTester> Testers { get; private set; } = [];

    public List<Slot> Slots { get; private set; } = [];

    public List<Substitute> Substitutes { get; private set; } = [];

    public List<OtherAdvent> OtherAdvents { get; private set; } = [];

    /// <summary>Creates a season with an open slot for every day and part.</summary>
    public static Season Create(int year, int slotsPerDay, DateTimeOffset createdAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, FirstYear);
        ArgumentOutOfRangeException.ThrowIfLessThan(slotsPerDay, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slotsPerDay, MaxSlotsPerDay);

        var season = new Season { Year = year, SlotsPerDay = slotsPerDay, CreatedAt = createdAt };

        for (var day = 1; day <= DaysInAdvent; day++)
        {
            for (var part = 1; part <= slotsPerDay; part++)
            {
                season.Slots.Add(new Slot { SeasonYear = year, Day = day, Part = part });
            }
        }

        return season;
    }
}

internal sealed class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    public void Configure(EntityTypeBuilder<Season> builder)
    {
        builder.HasKey(s => s.Year);
        builder.Property(s => s.Year).ValueGeneratedNever();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_seasons_year", $"year >= {Season.FirstYear}");
            t.HasCheckConstraint("ck_seasons_slots_per_day", $"slots_per_day BETWEEN 1 AND {Season.MaxSlotsPerDay}");
            t.HasCheckConstraint("ck_seasons_rehearsal", "(testing_started_at IS NULL) = (rehearsal_starts_on IS NULL)");
        });
    }
}
