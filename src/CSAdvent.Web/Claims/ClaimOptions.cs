using System.ComponentModel.DataAnnotations;

namespace CSAdvent.Web.Claims;

/// <summary>
/// Limits on claim requests (#137). Every request emails the contributor and the organizers, so one person can't use up the
/// email service's hourly limit (100 an hour for a new custom domain) by asking for every open spot.
/// </summary>
public sealed class ClaimOptions
{
    public const string SectionName = "Claims";

    /// <summary>Requests one person can have waiting for the organizers in a season.</summary>
    [Range(1, 50)]
    public int MaxPendingPerSeason { get; set; } = 3;

    /// <summary>Requests one person can make in an hour, whatever happened to them, so withdrawing and asking again has a limit too.</summary>
    [Range(1, 100)]
    public int MaxPerHour { get; set; } = 5;
}
