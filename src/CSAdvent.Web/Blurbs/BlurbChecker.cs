using CSAdvent.Web.Data;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Blurbs;

/// <summary>What checking a post found: its status, why, what was read, and Claude's answer (if it was asked).</summary>
public sealed record BlurbCheckResult(CheckOutcome Outcome, PostOpening? Opening, JudgeAnswer? Answer)
{
    /// <summary>The opening is the same as last time, so Claude wasn't asked again and the last answer stands.</summary>
    public bool Unchanged { get; init; }
}

/// <summary>
/// Checks one post for the blurb, without saving anything: fetches it ourselves (Claude never fetches), reads its opening, fails
/// it straight away without a link back near the top, and otherwise asks Claude.
/// </summary>
public sealed class BlurbChecker(IPostFetcher fetcher, IBlurbJudge? judge, IOptions<BlurbOptions> options)
{
    /// <param name="unchanged">
    /// The hash of the opening Claude last answered about, if its answer should stand while it's the same: a post whose opening
    /// still has it isn't sent to Claude again.
    /// </param>
    public async Task<BlurbCheckResult> CheckAsync(int year, Uri url, string? unchanged, CancellationToken cancellationToken)
    {
        var page = await fetcher.FetchAsync(url, cancellationToken);
        if (!page.Loaded)
        {
            return new BlurbCheckResult(new CheckOutcome(BlurbStatus.CouldNotCheck, page.Problem ?? "The post's page didn't load."), null, null);
        }

        var opening = ArticleReader.Read(page.Html!, page.Url, options.Value.OpeningCharacters);
        if (opening.IsEmpty)
        {
            return new BlurbCheckResult(new CheckOutcome(BlurbStatus.CouldNotCheck, "The post's page has no text we could read."), opening, null);
        }

        if (!opening.Links.Any(link => BlurbRules.IsLinkBack(link.Url)))
        {
            return new BlurbCheckResult(new CheckOutcome(BlurbStatus.Missing, $"There's no link to {BlurbRules.Host} near the top of the post."), opening, null);
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
}
