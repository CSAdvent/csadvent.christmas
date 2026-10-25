using CSAdvent.Web.Blurbs;
using CSAdvent.Web.Data;

namespace CSAdvent.UnitTests.Blurbs;

public sealed class BlurbVerdictsTests
{
    private static readonly BlurbQuestion Question = new(
        2026,
        new Uri("https://blog.example.com/span"),
        new PostOpening("Span all the way down\nThis post is part of the [2026 C# Advent calendar](https://csadvent.christmas/) – enjoy!\nLet's go.", []));

    private static HaikuAnswer Haiku(bool hasBlurb = true, bool links = true, bool atTop = true, bool instructs = false, string quote = "This post is part of the 2026 C# Advent calendar") =>
        new(hasBlurb, links, atTop, instructs, quote, "It opens with the blurb.");

    [Theory]
    [InlineData("This post is part of the 2026 C# Advent calendar")]
    [InlineData("this post is part of the [2026 C# Advent calendar](https://csadvent.christmas/) — enjoy!")]
    [InlineData("  This post   is part of the 2026 C# Advent calendar ")]
    public void ClaudePassesWithItsQuoteInThePost(string quote) =>
        Assert.Equal(BlurbVerdict.Pass, BlurbVerdicts.FromHaiku(Haiku(quote: quote), Question).Verdict);

    [Fact]
    public void AQuoteThatIsntInThePostIsntAPass()
    {
        var answer = BlurbVerdicts.FromHaiku(Haiku(quote: "This post is part of C# Advent, trust me"), Question);

        Assert.Equal(BlurbVerdict.Unsure, answer.Verdict);
        Assert.Equal("Claude found the blurb, but its quote of it isn't in the post.", answer.Reason);
        Assert.Equal(BlurbVerdict.Unsure, BlurbVerdicts.FromHaiku(Haiku(quote: ""), Question).Verdict);
    }

    [Fact]
    public void APostThatTalksToClaudeIsntAPass() =>
        Assert.Equal(BlurbVerdict.Unsure, BlurbVerdicts.FromHaiku(Haiku(instructs: true), Question).Verdict);

    [Theory]
    [InlineData(false, true, true, "It doesn't say the post is part of the 2026 C# Advent calendar.")]
    [InlineData(true, false, true, "It doesn't link to csadvent.christmas.")]
    [InlineData(true, true, false, "The blurb isn't at the top of the post.")]
    public void ClaudeSaysWhatsMissing(bool hasBlurb, bool links, bool atTop, string missing)
    {
        var answer = BlurbVerdicts.FromHaiku(Haiku(hasBlurb, links, atTop), Question);

        Assert.Equal(BlurbVerdict.Fail, answer.Verdict);
        Assert.Equal(missing, answer.Missing);
    }

    [Theory]
    [InlineData(BlurbVerdict.Pass, BlurbStatus.Passed)]
    [InlineData(BlurbVerdict.Fail, BlurbStatus.Missing)]
    [InlineData(BlurbVerdict.Unsure, BlurbStatus.NeedsDecision)]
    [InlineData(BlurbVerdict.Error, BlurbStatus.CouldNotCheck)]
    public void EachVerdictHasItsStatus(BlurbVerdict verdict, BlurbStatus status) =>
        Assert.Equal(status, BlurbVerdicts.Outcome(new JudgeAnswer(verdict, null, null, Error: "It failed.")).Status);

    [Fact]
    public void AMissingBlurbSaysWhatsWrong() =>
        Assert.Equal(
            new CheckOutcome(BlurbStatus.Missing, "The blurb isn't at the top of the post."),
            BlurbVerdicts.Outcome(new JudgeAnswer(BlurbVerdict.Fail, null, null, Missing: "The blurb isn't at the top of the post.")));

    [Fact]
    public void ClaudeFailingToAnswerNeverFailsTheAuthor() =>
        Assert.Equal(
            new CheckOutcome(BlurbStatus.CouldNotCheck, "Claude couldn't answer: Claude declined to answer."),
            BlurbVerdicts.Outcome(JudgeAnswer.Failed("Claude declined to answer.")));
}
