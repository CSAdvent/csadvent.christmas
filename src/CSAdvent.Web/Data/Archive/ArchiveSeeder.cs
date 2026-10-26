using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Data.Archive;

/// <summary>
/// Seeds the finished seasons from the JSON files in this folder: 2017-2025 were extracted from the legacy site's
/// hand-saved archive pages, and each later season is exported from the organizers' seasons page once it's finished
/// (<see cref="Serialize"/>, #182). It runs after migrations (EF Core's <c>UseSeeding</c>), so every new database gets the
/// history: locally, in tests, and in production through the migration bundle. It only adds seasons that don't exist
/// yet and never changes one that's already in the database.
/// </summary>
public static class ArchiveSeeder
{
    private const string ResourcePrefix = "archive/";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>How the files are written: indented, without empty fields, and with accents and quotes as they are.</summary>
    private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static IReadOnlyList<ArchivedSeason> LoadSeasons()
    {
        var assembly = typeof(ArchiveSeeder).Assembly;

        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                return Deserialize(stream) ?? throw new InvalidDataException($"Archive resource '{name}' is empty.");
            })
            .OrderBy(season => season.Year)
            .ToList();
    }

    /// <summary>A season's file, as the ones in this folder are written: save it here as <c>{year}.json</c>.</summary>
    public static byte[] Serialize(ArchivedSeason season) => JsonSerializer.SerializeToUtf8Bytes(season, WriteOptions);

    public static ArchivedSeason? Deserialize(Stream json) => JsonSerializer.Deserialize<ArchivedSeason>(json, JsonOptions);

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
    /// <summary>
    /// A season's calendar as it's shown: every spot's author, link, post and summary, in calendar order. Never contact
    /// details, contributors' accounts or anything else that isn't on the calendar.
    /// </summary>
    public static ArchivedSeason From(Season season) => new(
        season.Year,
        season.SlotsPerDay,
        season.Notes,
        [.. season.Slots
            .OrderBy(s => s.Day).ThenBy(s => s.Part)
            .Select(s => new ArchivedSlot(s.Day, s.Part, s.AuthorName, s.AuthorUrl, s.PostTitle, s.PostUrl, s.PostSummary))]);

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
            slot.PostSummary = archived.PostSummary;
        }

        return season;
    }
}

/// <summary>
/// One slot of a past season. A slot with no author was never claimed, or its author left; a slot with an author but
/// no post is one whose author never published. Summaries (#208) start with 2026.
/// </summary>
public sealed record ArchivedSlot(int Day, int Part, string? AuthorName, string? AuthorUrl, string? PostTitle, string? PostUrl, string? PostSummary = null);
