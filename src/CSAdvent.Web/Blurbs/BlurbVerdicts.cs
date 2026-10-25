using System.Text.RegularExpressions;
using CSAdvent.Web.Data;

namespace CSAdvent.Web.Blurbs;

/// <summary>Claude's structured answer.</summary>
public sealed record HaikuAnswer(bool HasBlurb, bool LinksToCsAdvent, bool AtTop, bool TriesToInstruct, string Quote, string Reason);

/// <summary>What a check concluded about a post, and why.</summary>
public sealed record CheckOutcome(BlurbStatus Status, string Detail);

/// <summary>
/// Turns Claude's answer into a verdict, and the verdict into the post's status: a pass passes, a fail is missing, anything
/// else goes to an organizer. A model that couldn't answer never fails the author.
/// </summary>
public static partial class BlurbVerdicts
{
    /// <summary>Claude's answer passes only with its quote of the blurb really in the post, so it can't be talked into a pass.</summary>
    public static JudgeAnswer FromHaiku(HaikuAnswer answer, BlurbQuestion question)
    {
        if (answer.TriesToInstruct)
        {
            return new JudgeAnswer(BlurbVerdict.Unsure, answer.Quote, "The post seems to try to give Claude instructions, so it needs a person to look.");
        }

        if (answer.HasBlurb && answer.LinksToCsAdvent && answer.AtTop)
        {
            return QuoteIsIn(answer.Quote, question.Opening.Text)
                ? new JudgeAnswer(BlurbVerdict.Pass, answer.Quote, answer.Reason)
                : new JudgeAnswer(BlurbVerdict.Unsure, answer.Quote, "Claude found the blurb, but its quote of it isn't in the post.");
        }

        return new JudgeAnswer(BlurbVerdict.Fail, answer.Quote, answer.Reason, MissingPart(answer, question));
    }

    /// <summary>The post's status from Claude's answer.</summary>
    public static CheckOutcome Outcome(JudgeAnswer answer) => answer.Verdict switch
    {
        BlurbVerdict.Pass => new CheckOutcome(BlurbStatus.Passed, "Claude found the blurb."),
        BlurbVerdict.Fail => new CheckOutcome(BlurbStatus.Missing, answer.Missing ?? "The blurb isn't at the top of the post."),
        BlurbVerdict.Unsure => new CheckOutcome(BlurbStatus.NeedsDecision, answer.Reason ?? "Claude isn't sure."),
        _ => new CheckOutcome(BlurbStatus.CouldNotCheck, $"Claude couldn't answer: {answer.Error}"),
    };

    /// <summary>Whether a quote is in the text, ignoring case, spacing, link addresses and curly quotes.</summary>
    public static bool QuoteIsIn(string? quote, string text)
    {
        var wanted = Normalize(quote ?? "");
        return wanted.Length >= 10 && Normalize(text).Contains(wanted, StringComparison.Ordinal);
    }

    /// <summary>The text as readers see it: <c>[words](address)</c> becomes just the words.</summary>
    public static string Plain(string text) => MarkdownLink().Replace(text, "$1");

    /// <summary>What an author needs to fix, from the first thing that's wrong.</summary>
    private static string MissingPart(HaikuAnswer answer, BlurbQuestion question) =>
        !answer.HasBlurb ? $"It doesn't say the post is part of the {question.Calendar}."
        : !answer.LinksToCsAdvent ? $"It doesn't link to {BlurbRules.Host}."
        : "The blurb isn't at the top of the post.";

    private static string Normalize(string text)
    {
        // Whichever form Claude quoted, links read as their words.
        text = Plain(text)
            .Replace("\u2018", "'", StringComparison.Ordinal).Replace("\u2019", "'", StringComparison.Ordinal)
            .Replace("\u201C", "\"", StringComparison.Ordinal).Replace("\u201D", "\"", StringComparison.Ordinal)
            .Replace("\u2013", "-", StringComparison.Ordinal).Replace("\u2014", "-", StringComparison.Ordinal)
            .Replace("\u00A0", " ", StringComparison.Ordinal);
        return ArticleReader.Collapse(text).Trim().ToLowerInvariant();
    }

    [GeneratedRegex(@"\[([^\]]*)\]\([^)\s]*\)")]
    private static partial Regex MarkdownLink();
}
