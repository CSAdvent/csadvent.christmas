namespace CSAdvent.Web.Blurbs;

/// <summary>
/// Development and the tests (<see cref="BlurbOptions.DryRun"/>): stands in for Claude without an API key, so the check can be
/// tried end to end. It reads the text with simple rules (a sentence near the top saying it's part of the year's C# Advent, with
/// a link back) and only logs. A sample page can ask it to be unsure, to try an organizer's decision.
/// </summary>
public sealed partial class DryRunJudge(ILogger<DryRunJudge> logger) : IBlurbJudge
{
    /// <summary>The blurb has to start within this many characters to be at the top.</summary>
    private const int TopCharacters = 600;

    public Task<JudgeAnswer> JudgeAsync(BlurbQuestion question, CancellationToken cancellationToken)
    {
        var text = BlurbVerdicts.Plain(question.Opening.Text);
        var sentences = text.Split(['.', '!', '?', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var blurb = sentences.FirstOrDefault(s =>
            s.Contains("part of", StringComparison.OrdinalIgnoreCase)
            && s.Contains("C# Advent", StringComparison.OrdinalIgnoreCase)
            && s.Contains(question.Year.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
        var start = blurb is null ? -1 : text.IndexOf(blurb, StringComparison.Ordinal);
        var links = question.Opening.Links.Any(l => BlurbRules.IsLinkBack(l.Url) && l.Position <= TopCharacters + (blurb?.Length ?? 0));

        var answer = text.Contains("Dry run: unsure", StringComparison.Ordinal)
            ? new JudgeAnswer(BlurbVerdict.Unsure, blurb?.Trim(), "Dry run: this sample asks for a person to decide.")
            : BlurbVerdicts.FromHaiku(
                new HaikuAnswer(
                    blurb is not null,
                    links,
                    start is >= 0 and <= TopCharacters,
                    text.Contains("ignore previous instructions", StringComparison.OrdinalIgnoreCase),
                    blurb?.Trim() ?? "",
                    "Dry run: read with simple rules."),
                question);

        LogDryRun(question.Url, answer.Verdict);
        return Task.FromResult(answer);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Dry run: the stand-in for Claude says {Verdict} for {Url}")]
    private partial void LogDryRun(Uri url, BlurbVerdict verdict);
}
