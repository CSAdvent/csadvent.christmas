using System.Text;
using CSAdvent.Web.Data;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Blurbs;

/// <summary>What checking a post found: its status, why, what was read, and Claude's answer (if it was asked).</summary>
public sealed record BlurbCheckResult(CheckOutcome Outcome, PostOpening? Opening, JudgeAnswer? Answer)
{
    /// <summary>The opening is the same as last time, so Claude wasn't asked again and the last answer stands.</summary>
    public bool Unchanged { get; init; }

    /// <summary>
    /// The page wasn't the post, as far as we could tell: it didn't load, had no text, or had neither a link back nor the post's
    /// title. That's what a post's address shows before it's published, so it isn't anyone's fault yet (#159).
    /// </summary>
    public bool MaybeUnpublished { get; init; }
}

/// <summary>
/// Checks one post for the blurb, without saving anything: fetches it ourselves (Claude never fetches), reads its opening, fails
/// it straight away without a link back near the top (if the page is clearly the post), and otherwise asks Claude.
/// </summary>
public sealed class BlurbChecker(IPostFetcher fetcher, IBlurbJudge? judge, IOptions<BlurbOptions> options)
{
    /// <summary>
    /// Why a page with no link back isn't failed: it may not be the post at all (#148). Pages that need JavaScript, sign-in,
    /// cookie and bot checks, and "not found" pages that answer 200 all look like that.
    /// </summary>
    public const string NotThePost =
        $"There's no link to {BlurbRules.Host} near the top, but the page doesn't seem to be the post: its title isn't on it. " +
        "The page may need JavaScript to show the post, or be a sign-in, cookie or bot check, or a \"not found\" page.";

    /// <param name="title">The post's title on the calendar, to tell the post from a page that isn't it.</param>
    /// <param name="unchanged">
    /// The hash of the opening Claude last answered about, if its answer should stand while it's the same: a post whose opening
    /// still has it isn't sent to Claude again.
    /// </param>
    public async Task<BlurbCheckResult> CheckAsync(int year, Uri url, string? title, string? unchanged, CancellationToken cancellationToken)
    {
        var page = await fetcher.FetchAsync(url, cancellationToken);
        if (!page.Loaded)
        {
            return new BlurbCheckResult(new CheckOutcome(BlurbStatus.CouldNotCheck, page.Problem ?? "The post's page didn't load."), null, null)
            {
                MaybeUnpublished = !page.NeverReadable,
            };
        }

        var opening = ArticleReader.Read(page.Html!, page.Url, options.Value.OpeningCharacters);
        if (opening.IsEmpty)
        {
            return new BlurbCheckResult(new CheckOutcome(BlurbStatus.CouldNotCheck, "The post's page has no text we could read."), opening, null)
            {
                MaybeUnpublished = true,
            };
        }

        if (!opening.Links.Any(link => BlurbRules.IsLinkBack(link.Url)))
        {
            // Failing without asking Claude is only fair when the page is clearly the post; otherwise a person looks.
            return LooksLikeThePost(opening, title)
                ? new BlurbCheckResult(new CheckOutcome(BlurbStatus.Missing, $"There's no link to {BlurbRules.Host} near the top of the post."), opening, null)
                : new BlurbCheckResult(new CheckOutcome(BlurbStatus.NeedsDecision, NotThePost), opening, null) { MaybeUnpublished = true };
        }

        if (opening.Hash == unchanged)
        {
            return new BlurbCheckResult(new CheckOutcome(BlurbStatus.Waiting, "Unchanged since the last check."), opening, null) { Unchanged = true };
        }

        if (judge is null)
        {
            return new BlurbCheckResult(new CheckOutcome(BlurbStatus.CouldNotCheck, "There's no Anthropic API key, so posts can't be checked."), opening, null);
        }

        var answer = await judge.JudgeAsync(new BlurbQuestion(year, page.Url, opening), cancellationToken);
        return new BlurbCheckResult(BlurbVerdicts.Outcome(answer), opening, answer);
    }

    /// <summary>
    /// Whether the page is clearly the post: its title on the calendar is in the page's opening, title or top heading, whatever
    /// the case, spacing or punctuation. Without a title we can't tell.
    /// </summary>
    public static bool LooksLikeThePost(PostOpening opening, string? title)
    {
        var wanted = Words(title ?? "");
        return wanted.Length >= 3
            && opening.Titles.Prepend(BlurbVerdicts.Plain(opening.Text)).Any(text => $" {Words(text)} ".Contains($" {wanted} ", StringComparison.Ordinal));
    }

    /// <summary>Only the letters and digits, lowercase, one space between words: "Span&lt;T&gt;: All the way!" is "span t all the way".</summary>
    private static string Words(string text)
    {
        var words = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                words.Append(char.ToLowerInvariant(c));
            }
            else if (words.Length > 0 && words[^1] != ' ')
            {
                words.Append(' ');
            }
        }

        return words.ToString().TrimEnd();
    }
}
