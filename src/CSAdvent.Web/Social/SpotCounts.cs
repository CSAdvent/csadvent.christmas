using System.Globalization;

namespace CSAdvent.Web.Social;

/// <summary>
/// How a season's spots stand, for the "spots left" post, counted the way the home page counts them: a spot is taken if it
/// has an author, and left if it hasn't and its day hasn't been revealed, so it can still be claimed.
/// </summary>
/// <param name="Total">Every spot in the season.</param>
/// <param name="OpenDays">The days with a spot left, in order. A day with both its spots left is listed once.</param>
public sealed record SpotCounts(int Total, int Taken, int Left, IReadOnlyList<int> OpenDays)
{
    /// <summary>With this many spots left or fewer, the post says "only" and names their days.</summary>
    public const int Few = 5;

    public bool AreFew => Left <= Few;

    /// <summary>At least half the spots are taken.</summary>
    public bool AreFillingUp => Taken * 2 >= Total;

    /// <summary>The post in a few words, for its card: "Only 3 spots left", "38 of 50 spots left".</summary>
    public string Headline => this switch
    {
        { Taken: 0 } => "Sign-ups are open",
        { Left: 1 } => "Just one spot left",
        { AreFew: true } => string.Create(CultureInfo.InvariantCulture, $"Only {Left} spots left"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{Left} of {Total} spots left"),
    };

    /// <summary>The days with a spot left: "December 24", "December 3 and 24", "December 3, 9 and 24".</summary>
    public string Days => OpenDays.Count switch
    {
        0 => "",
        1 => string.Create(CultureInfo.InvariantCulture, $"December {OpenDays[0]}"),
        _ => string.Create(CultureInfo.InvariantCulture, $"December {string.Join(", ", OpenDays.Take(OpenDays.Count - 1))} and {OpenDays[^1]}"),
    };
}
