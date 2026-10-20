using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Data.Archive;

/// <summary>
/// Seeds the 2017-2025 seasons from the JSON files in this folder, which were extracted from the legacy site's
/// hand-saved archive pages. It runs after migrations (EF Core's <c>UseSeeding</c>), so every new database gets the
/// history: locally, in tests, and in production through the migration bundle. It only adds seasons that don't exist
/// yet and never changes one that's already in the database.
/// </summary>
public static class ArchiveSeeder
{
    private const string ResourcePrefix = "archive/";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<ArchivedSeason> LoadSeasons()
    {
        var assembly = typeof(ArchiveSeeder).Assembly;

        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                return JsonSerializer.Deserialize<ArchivedSeason>(stream, JsonOptions)
                    ?? throw new InvalidDataException($"Archive resource '{name}' is empty.");
            })
            .OrderBy(season => season.Year)
            .ToList();
    }

    public static void Seed(DbContext context)
    {
        var existingYears = context.Set<Season>().Select(s => s.Year).ToHashSet();
        var missing = CreateMissingSeasons(existingYears);

        if (missing.Count > 0)
        {
            context.AddRange(missing);
            context.SaveChanges();
        }
    }

    public static async Task SeedAsync(DbContext context, CancellationToken cancellationToken)
    {
        var existingYears = (await context.Set<Season>().Select(s => s.Year).ToListAsync(cancellationToken)).ToHashSet();
        var missing = CreateMissingSeasons(existingYears);

        if (missing.Count > 0)
        {
            context.AddRange(missing);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static List<Season> CreateMissingSeasons(HashSet<int> existingYears) =>
        LoadSeasons()
            .Where(archived => !existingYears.Contains(archived.Year))
            .Select(archived => archived.ToSeason(DateTimeOffset.UtcNow))
            .ToList();
}

/// <summary>A past season as stored in the archive JSON.</summary>
public sealed record ArchivedSeason(int Year, int SlotsPerDay, string? Notes, IReadOnlyList<ArchivedSlot> Slots)
{
    public Season ToSeason(DateTimeOffset createdAt)
    {
        var season = Season.Create(Year, SlotsPerDay, createdAt);
        season.Notes = Notes;

        foreach (var archived in Slots)
        {
            var slot = season.Slots.Single(s => s.Day == archived.Day && s.Part == archived.Part);
            slot.AuthorName = archived.AuthorName;
            slot.AuthorUrl = archived.AuthorUrl;
            slot.PostTitle = archived.PostTitle;
            slot.PostUrl = archived.PostUrl;
        }

        return season;
    }
}

/// <summary>
/// One slot of a past season. A slot with no author was never claimed, or its author left; a slot with an author but
/// no post is one whose author never published.
/// </summary>
public sealed record ArchivedSlot(int Day, int Part, string? AuthorName, string? AuthorUrl, string? PostTitle, string? PostUrl);
