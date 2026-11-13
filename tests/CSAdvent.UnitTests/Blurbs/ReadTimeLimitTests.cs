using CSAdvent.Web.Blurbs;
using CSAdvent.Web.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CSAdvent.UnitTests.Blurbs;

/// <summary>
/// A page that takes longer to read than its time limit (<see cref="BlurbOptions.ReadTimeout"/>). On their own
/// (<see cref="NonParallel"/>), because the limit's timer needs a free thread-pool thread to fire while the page is parsed.
/// </summary>
[Collection(nameof(NonParallel))]
public sealed class ReadTimeLimitTests
{
    private static readonly Uri Page = new("https://blog.example.com/posts/span");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APageThatTakesLongerThanTheLimitToReadIsGivenUpOn()
    {
        var opening = await ArticleReader.ReadAsync(ArticleReaderTests.LongPage, Page, 2000, TimeSpan.FromMilliseconds(1), CancellationToken);

        Assert.Null(opening);
    }

    [Fact]
    public async Task APageThatTakesTooLongToReadIsLeftForAPerson()
    {
        var options = Options.Create(new BlurbOptions { DryRun = true, ReadTimeout = TimeSpan.FromMilliseconds(1) });
        var checker = new BlurbChecker(new OnePage(ArticleReaderTests.LongPage), new DryRunJudge(NullLogger<DryRunJudge>.Instance), options, NullLogger<BlurbChecker>.Instance);

        var result = await checker.CheckAsync(2026, Page, SamplePosts.Title, unchanged: null, CancellationToken);

        Assert.Equal(new CheckOutcome(BlurbStatus.CouldNotCheck, BlurbChecker.TookTooLong), result.Outcome);
        Assert.False(result.MaybeUnpublished);
        Assert.Null(result.Answer);
    }

    /// <summary>Serves the same page at every address.</summary>
    private sealed class OnePage(string html) : IPostFetcher
    {
        public Task<FetchedPost> FetchAsync(Uri url, CancellationToken cancellationToken) => Task.FromResult(new FetchedPost(html, url, null));
    }
}
