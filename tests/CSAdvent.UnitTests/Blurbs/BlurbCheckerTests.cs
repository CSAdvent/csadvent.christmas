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
    [InlineData("no-such-page", BlurbStatus.CouldNotCheck, false)]
    public async Task EachSampleGetsItsAnswer(string sample, BlurbStatus status, bool askedClaude)
    {
        var result = await Checker().CheckAsync(2026, Sample(sample), unchanged: null, CancellationToken);

        Assert.Equal(status, result.Outcome.Status);
        Assert.Equal(askedClaude, result.Answer is not null);
    }

    [Fact]
    public async Task APostWithoutALinkBackFailsWithoutAskingClaude()
    {
        var result = await Checker().CheckAsync(2026, Sample("without-blurb"), unchanged: null, CancellationToken);

        Assert.Equal("There's no link to csadvent.christmas near the top of the post.", result.Outcome.Detail);
    }

    [Fact]
    public async Task TheWrongYearIsExplained()
    {
        var result = await Checker().CheckAsync(2026, Sample("wrong-year"), unchanged: null, CancellationToken);

        Assert.Equal("It doesn't say the post is part of the 2026 C# Advent calendar.", result.Outcome.Detail);
    }

    [Fact]
    public async Task AnUnchangedPostIsntSentToClaudeAgain()
    {
        var checker = Checker();
        var first = await checker.CheckAsync(2026, Sample("link-only"), unchanged: null, CancellationToken);

        var again = await checker.CheckAsync(2026, Sample("link-only"), first.Opening!.Hash, CancellationToken);

        Assert.True(again.Unchanged);
        Assert.Null(again.Answer);
    }

    [Fact]
    public async Task WithoutAnApiKeyNothingIsChecked()
    {
        var options = Options.Create(new BlurbOptions { SamplePages = true });

        var result = await new BlurbChecker(new SamplePostFetcher(), judge: null, options).CheckAsync(2026, Sample("with-blurb"), unchanged: null, CancellationToken);

        Assert.Equal(new CheckOutcome(BlurbStatus.CouldNotCheck, "There's no Anthropic API key, so posts can't be checked."), result.Outcome);
    }

    [Fact]
    public async Task SamplePagesNeverReachTheInternet()
    {
        var page = await new SamplePostFetcher().FetchAsync(new Uri("https://csadvent.christmas/"), CancellationToken);

        Assert.False(page.Loaded);
    }

    private static Uri Sample(string name) => new($"https://{SamplePostFetcher.Host}/{name}?year=2026");

    private static BlurbChecker Checker() =>
        new(new SamplePostFetcher(), new DryRunJudge(NullLogger<DryRunJudge>.Instance), Options.Create(new BlurbOptions { DryRun = true, SamplePages = true }));
}
