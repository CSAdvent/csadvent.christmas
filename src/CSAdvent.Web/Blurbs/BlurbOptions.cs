using System.ComponentModel.DataAnnotations;

namespace CSAdvent.Web.Blurbs;

/// <summary>The blurb check (#128): Claude Haiku through Anthropic's API, and how much of each post it reads.</summary>
public sealed class BlurbOptions
{
    public const string SectionName = "Blurbs";

    /// <summary>An Anthropic API key (a secret, never in source). Without one, posts aren't checked.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Claude model that checks posts.</summary>
    public string Model { get; set; } = "claude-haiku-5-5";

    /// <summary>
    /// Development and the tests: a stand-in that reads the text with simple rules and only logs, so the check can be tried
    /// without an API key. A real key wins.
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Development and the tests: posts are only read from the built-in sample pages (<see cref="SamplePosts"/>), never
    /// from the internet.
    /// </summary>
    public bool SamplePages { get; set; }

    /// <summary>How much of the post's text, from the top, is read and sent to Claude.</summary>
    [Range(500, 10_000)]
    public int OpeningCharacters { get; set; } = 2000;

    /// <summary>How long before its reveal a post is first looked at, in case its author publishes early.</summary>
    public TimeSpan CheckFrom { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// How long before its deadline a post whose page doesn't load, or doesn't seem to be the post, stops counting as not
    /// published yet, and goes to the organizers (#159). Many authors publish on the morning of their day.
    /// </summary>
    public TimeSpan PublishedBy { get; set; } = TimeSpan.FromHours(3);

    /// <summary>
    /// "Check my post" (#174): how many of the checks authors ask for can go to Claude in 30 days, across everyone, so they can't
    /// use up the Anthropic workspace's spend limit ($25 a month) that December's own checks need. A check costs about $0.0005.
    /// Checks that don't need Claude (the page doesn't load, has no link back, or hasn't changed since Claude last answered)
    /// don't count.
    /// </summary>
    [Range(0, 100_000)]
    public int SelfChecksPerMonth { get; set; } = 2000;

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Whether posts are checked at all: with an API key, or with the stand-in.</summary>
    public bool IsEnabled => HasApiKey || DryRun;
}
