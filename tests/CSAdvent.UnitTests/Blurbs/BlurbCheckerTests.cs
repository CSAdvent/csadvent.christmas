using CSAdvent.Web.Blurbs;
using CSAdvent.Web.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CSAdvent.UnitTests.Blurbs;

/// <summary>The whole check, on the sample pages with the dry-run stand-in, as development and the integration tests see it.</summary>
public sealed class BlurbCheckerTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("with-blurb", BlurbStatus.Passed, true)]
    [InlineData("own-words", BlurbStatus.Passed, true)]
    [InlineData("wrong-year", BlurbStatus.Missing, true)]
    [InlineData("link-only", BlurbStatus.Missing, true)]
    [InlineData("without-blurb", BlurbStatus.Missing, false)]
    [InlineData("blurb-in-footer", BlurbStatus.Missing, false)]
    [InlineData("unsure", BlurbStatus.NeedsDecision, true)]
    [InlineData("talks-to-the-model", BlurbStatus.NeedsDecision, true)]
    [InlineData("javascript-shell", BlurbStatus.NeedsDecision, false)]
    [InlineData("not-found", BlurbStatus.NeedsDecision, false)]
    [InlineData("no-such-page", BlurbStatus.CouldNotCheck, false)]
    public async Task EachSampleGetsItsAnswer(string sample, BlurbStatus status, bool askedClaude)
    {
        var result = await Checker().CheckAsync(2026, Sample(sample), SamplePosts.Title, unchanged: null, CancellationToken);

        Assert.Equal(status, result.Outcome.Status);
        Assert.Equal(askedClaude, result.Answer is not null);
    }

    [Fact]
    public async Task APostWithoutALinkBackFailsWithoutAskingClaude()
    {
        var result = await Checker().CheckAsync(2026, Sample("without-blurb"), SamplePosts.Title, unchanged: null, CancellationToken);

        Assert.Equal("There's no link to csadvent.christmas near the top of the post.", result.Outcome.Detail);
        Assert.False(result.MaybeUnpublished);
    }

    [Theory]
    [InlineData("javascript-shell")]
    [InlineData("not-found")]
    public async Task APageThatIsntThePostNeedsAPersonRatherThanFailingTheAuthor(string sample)
    {
        var result = await Checker().CheckAsync(2026, Sample(sample), SamplePosts.Title, unchanged: null, CancellationToken);

        Assert.Equal(new CheckOutcome(BlurbStatus.NeedsDecision, BlurbChecker.NotThePost), result.Outcome);
        Assert.True(result.MaybeUnpublished);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("A different post altogether")]
    public async Task WithoutTheTitleOnThePageNoLinkBackIsntEnoughToFail(string? title)
    {
        var result = await Checker().CheckAsync(2026, Sample("without-blurb"), title, unchanged: null, CancellationToken);

        Assert.Equal(BlurbStatus.NeedsDecision, result.Outcome.Status);
    }

    [Fact]
    public async Task APageThatDoesntLoadMayNotBePublishedYet()
    {
        var result = await Checker().CheckAsync(2026, Sample("no-such-page"), SamplePosts.Title, unchanged: null, CancellationToken);

        Assert.True(result.MaybeUnpublished);
    }

    [Fact]
    public async Task TheWrongYearIsExplained()
    {
        var result = await Checker().CheckAsync(2026, Sample("wrong-year"), SamplePosts.Title, unchanged: null, CancellationToken);

        Assert.Equal("It doesn't say the post is part of the 2026 C# Advent calendar.", result.Outcome.Detail);
    }

    [Fact]
    public async Task AnUnchangedPostIsntSentToClaudeAgain()
    {
        var checker = Checker();
        var first = await checker.CheckAsync(2026, Sample("link-only"), SamplePosts.Title, unchanged: null, CancellationToken);

        var again = await checker.CheckAsync(2026, Sample("link-only"), SamplePosts.Title, first.Opening!.Hash, CancellationToken);

        Assert.True(again.Unchanged);
        Assert.Null(again.Answer);
    }

    [Fact]
    public async Task WithoutAnApiKeyNothingIsChecked()
    {
        var options = Options.Create(new BlurbOptions { SamplePages = true });

        var result = await new BlurbChecker(new SamplePostFetcher(), judge: null, options).CheckAsync(2026, Sample("with-blurb"), SamplePosts.Title, unchanged: null, CancellationToken);

        Assert.Equal(new CheckOutcome(BlurbStatus.CouldNotCheck, "There's no Anthropic API key, so posts can't be checked."), result.Outcome);
    }

    [Fact]
    public async Task SamplePagesNeverReachTheInternet()
    {
        var page = await new SamplePostFetcher().FetchAsync(new Uri("https://csadvent.christmas/"), CancellationToken);

        Assert.False(page.Loaded);
    }

    [Theory]
    [InlineData("Span<T> all the way down", true)]
    [InlineData("span<t>: All the Way Down!", true)]
    [InlineData("  Span<T>   all the way down ", true)]
    [InlineData("All the way", true)]
    [InlineData("Span all the way down", false)]
    [InlineData("Collection expressions everywhere", false)]
    [InlineData("🎄", false)]
    [InlineData("", false)]
    public void ThePostIsRecognizedByItsTitleWhateverTheCaseSpacingOrPunctuation(string title, bool isThePost)
    {
        var opening = new PostOpening("Span<T> lets you work with contiguous memory.", []) { Titles = ["Span<T> all the way down · My blog"] };

        Assert.Equal(isThePost, BlurbChecker.LooksLikeThePost(opening, title));
    }

    [Fact]
    public void TheTitleCanBeInTheOpeningItself()
    {
        var opening = new PostOpening("Records, revisited\nThis post looks at records.", []);

        Assert.True(BlurbChecker.LooksLikeThePost(opening, "Records, revisited"));
    }

    private static Uri Sample(string name) => new($"https://{SamplePostFetcher.Host}/{name}?year=2026");

    private static BlurbChecker Checker() =>
        new(new SamplePostFetcher(), new DryRunJudge(NullLogger<DryRunJudge>.Instance), Options.Create(new BlurbOptions { DryRun = true, SamplePages = true }));
}
