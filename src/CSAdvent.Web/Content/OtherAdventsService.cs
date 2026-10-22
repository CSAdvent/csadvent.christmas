using System.ComponentModel.DataAnnotations;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Content;

public sealed record OtherAdventItem(int? Id, string Name, string Url, string? Description);

/// <summary>The list to show: whose season it's from, if it's a season's own list.</summary>
public sealed record OtherAdventsList(int? Year, IReadOnlyList<OtherAdventItem> Items);

/// <summary>
/// Other developer advent calendars, kept per season so the list can change each year without code. The page shows the
/// current season's list, or the most recent season that has one, or the suggestions below.
/// </summary>
public sealed class OtherAdventsService(AdventDbContext db)
{
    /// <summary>
    /// A starting list, from the legacy site's, without links that no longer work (golang.christmas, pwadvent.dev) or
    /// one-off calendars from past years.
    /// </summary>
    public static IReadOnlyList<OtherAdventItem> Suggestions { get; } =
    [
        new(null, "F# Advent Calendar", "https://sergeytihon.com/fsadvent/", "The calendar that inspired C# Advent."),
        new(null, ".NET Advent Calendar", "https://dotnet.christmas/", null),
        new(null, "Festive Tech Calendar", "https://festivetechcalendar.com/", null),
        new(null, "Advent of Code", "https://adventofcode.com/", "Programming puzzles, one a day."),
        new(null, "Advent of JavaScript", "https://www.adventofjs.com/", null),
        new(null, "Advent of CSS", "https://www.adventofcss.com/", null),
        new(null, "HTMHell Advent Calendar", "https://www.htmhell.dev/adventcalendar/", null),
        new(null, "24 Days in December", "https://24daysindecember.net/", "PHP."),
        new(null, "Java Advent Calendar", "https://www.javaadvent.com/calendar", null),
        new(null, "Web Performance Calendar", "https://calendar.perfplanet.com/", null),
        new(null, "WordPress Advent Calendar", "https://wpmarmite.com/en/wordpress-advent-calendar/", null),
        new(null, "24 Pull Requests", "https://24pullrequests.com/", "Give back to open source in December."),
    ];

    /// <summary>The list for the public page: <paramref name="year"/>'s, else the latest season's, else the suggestions.</summary>
    public async Task<OtherAdventsList> GetForPageAsync(int? year, CancellationToken cancellationToken = default)
    {
        var lists = await db.OtherAdvents
            .AsNoTracking()
            .Where(a => year == null || a.SeasonYear <= year)
            .OrderByDescending(a => a.SeasonYear).ThenBy(a => a.SortOrder)
            .ToListAsync(cancellationToken);
        if (lists.Count == 0)
        {
            return new OtherAdventsList(null, Suggestions);
        }

        var latest = lists[0].SeasonYear;
        return new OtherAdventsList(latest, [.. lists.Where(a => a.SeasonYear == latest).Select(ToItem)]);
    }

    public async Task<List<OtherAdventItem>> ListAsync(int year, CancellationToken cancellationToken = default) =>
        [.. (await db.OtherAdvents.AsNoTracking().Where(a => a.SeasonYear == year).OrderBy(a => a.SortOrder).ToListAsync(cancellationToken)).Select(ToItem)];

    public async Task AddAsync(int year, OtherAdventForm form, CancellationToken cancellationToken = default)
    {
        var season = await db.Seasons.SingleAsync(s => s.Year == year, cancellationToken);
        var next = await db.OtherAdvents.Where(a => a.SeasonYear == year).MaxAsync(a => (int?)a.SortOrder, cancellationToken) ?? 0;
        season.OtherAdvents.Add(new OtherAdvent
        {
            Name = form.Name.Trim(),
            Url = form.Url.Trim(),
            Description = string.IsNullOrWhiteSpace(form.Description) ? null : form.Description.Trim(),
            SortOrder = next + 1,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task RemoveAsync(int year, int id, CancellationToken cancellationToken = default) =>
        db.OtherAdvents.Where(a => a.SeasonYear == year && a.Id == id).ExecuteDeleteAsync(cancellationToken);

    /// <summary>Starts an empty season's list from the previous season's, or from the suggestions if there isn't one.</summary>
    public async Task CopyIntoAsync(int year, CancellationToken cancellationToken = default)
    {
        if (await db.OtherAdvents.AnyAsync(a => a.SeasonYear == year, cancellationToken))
        {
            return;
        }

        var source = (await GetForPageAsync(year - 1, cancellationToken)).Items;
        var season = await db.Seasons.SingleAsync(s => s.Year == year, cancellationToken);
        var order = 0;
        foreach (var item in source)
        {
            season.OtherAdvents.Add(new OtherAdvent { Name = item.Name, Url = item.Url, Description = item.Description, SortOrder = ++order });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static OtherAdventItem ToItem(OtherAdvent advent) => new(advent.Id, advent.Name, advent.Url, advent.Description);
}

public sealed class OtherAdventForm
{
    [Required(ErrorMessage = "Enter the calendar's name.")]
    [StringLength(100, ErrorMessage = "Keep the name to 100 characters or fewer.")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Enter the calendar's address.")]
    [StringLength(500, ErrorMessage = "Keep the address to 500 characters or fewer.")]
    [HttpUrl(ErrorMessage = "Enter a full web address that starts with https:// or http://.")]
    public string Url { get; set; } = "";

    [StringLength(500, ErrorMessage = "Keep the description to 500 characters or fewer.")]
    public string? Description { get; set; }
}
