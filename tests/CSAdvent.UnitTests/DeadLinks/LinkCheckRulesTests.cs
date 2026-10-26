using CSAdvent.Web.Data;
using CSAdvent.Web.DeadLinks;

namespace CSAdvent.UnitTests.DeadLinks;

public sealed class LinkCheckRulesTests
{
    private static readonly DateTimeOffset Now = new(2027, 1, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly LinkCheckOptions Options = new();
    private static readonly LinkProbe NotFound = LinkProbe.Failed("The page answered 404 Not Found.");

    private static LinkCheckState New => new(LinkStatus.Unchecked, 0, null, null, null, null, Now);

    [Fact]
    public void APostThatLoadsIsCheckedAgainInAboutAMonth()
    {
        var early = LinkCheckRules.After(New, LinkProbe.Loaded(), Now, Options, jitter: 0);
        var late = LinkCheckRules.After(New, LinkProbe.Loaded(), Now, Options, jitter: 1);

        Assert.Equal(new LinkCheckState(LinkStatus.Working, 0, null, null, "The page loaded.", Now, Now.AddDays(27)), early);
        Assert.Equal(Now.AddDays(33), late.NextCheckAt);
    }

    [Fact]
    public void APostIsOnlyDeadAfterThreeFailuresDaysApart()
    {
        var first = LinkCheckRules.After(New, NotFound, Now, Options, 0.5);
        Assert.Equal(new LinkCheckState(LinkStatus.Failing, 1, Now, null, NotFound.Detail, Now, Now.AddDays(1)), first);

        var second = LinkCheckRules.After(first, NotFound, first.NextCheckAt, Options, 0.5);
        Assert.Equal((LinkStatus.Failing, 2, Now), (second.Status, second.Failures, second.FailingSince));
        Assert.Equal(Now.AddDays(3), second.NextCheckAt);

        var third = LinkCheckRules.After(second, NotFound, second.NextCheckAt, Options, 0.5);
        Assert.Equal((LinkStatus.Dead, 3, Now, Now.AddDays(3)), (third.Status, third.Failures, third.FailingSince, third.DeadSince));
        Assert.Equal(Now.AddDays(33), third.NextCheckAt);

        // Still dead a month later: it stays dead since it first was.
        var later = LinkCheckRules.After(third, NotFound, third.NextCheckAt, Options, 0.5);
        Assert.Equal((LinkStatus.Dead, Now.AddDays(3)), (later.Status, later.DeadSince));
    }

    [Fact]
    public void ADeadPostThatLoadsAgainIsWorking()
    {
        var dead = new LinkCheckState(LinkStatus.Dead, 3, Now.AddDays(-40), Now.AddDays(-37), NotFound.Detail, Now.AddDays(-30), Now);

        var back = LinkCheckRules.After(dead, LinkProbe.Loaded(), Now, Options, 0);

        Assert.Equal((LinkStatus.Working, 0, (DateTimeOffset?)null, (DateTimeOffset?)null), (back.Status, back.Failures, back.FailingSince, back.DeadSince));
    }

    [Fact]
    public void ALoadInBetweenStartsTheFailuresAgain()
    {
        var failing = LinkCheckRules.After(LinkCheckRules.After(New, NotFound, Now, Options, 0), NotFound, Now.AddDays(1), Options, 0);
        var loaded = LinkCheckRules.After(failing, LinkProbe.Loaded(), Now.AddDays(3), Options, 0);

        var again = LinkCheckRules.After(loaded, NotFound, Now.AddDays(30), Options, 0);

        Assert.Equal((LinkStatus.Failing, 1, Now.AddDays(30)), (again.Status, again.Failures, again.FailingSince));
    }

    [Fact]
    public void ASiteThatTurnsTheCheckAwayTellsUsNothing()
    {
        var refused = LinkProbe.Refused("The site turned the check away (403 Forbidden).");

        var unknown = LinkCheckRules.After(New, refused, Now, Options, 0);
        Assert.Equal((LinkStatus.Refused, 0, Now.AddDays(27)), (unknown.Status, unknown.Failures, unknown.NextCheckAt));

        // A dead post stays dead, with its copy: the site answering 403 now doesn't bring the post back.
        var dead = new LinkCheckState(LinkStatus.Dead, 3, Now.AddDays(-10), Now.AddDays(-7), NotFound.Detail, Now.AddDays(-7), Now);
        var stillDead = LinkCheckRules.After(dead, refused, Now, Options, 0);
        Assert.Equal((LinkStatus.Dead, Now.AddDays(-7), refused.Detail), (stillDead.Status, stillDead.DeadSince, stillDead.Detail));
    }

    [Fact]
    public void FewerFailuresCanBeEnough()
    {
        var options = new LinkCheckOptions { FailuresBeforeDead = 1 };

        Assert.Equal(LinkStatus.Dead, LinkCheckRules.After(New, NotFound, Now, options, 0).Status);
    }

    [Fact]
    public void AnArchiveThatDidntAnswerIsAskedAgainInHours()
    {
        Assert.Equal(Now, LinkCheckRules.ArchiveCheckedAt(ArchiveAnswer.NoCopy, Now, Options));

        // Stored as if it was asked long enough ago that it's due again 6 hours from now.
        var asked = LinkCheckRules.ArchiveCheckedAt(ArchiveAnswer.NoAnswer, Now, Options);
        Assert.Equal(Now.AddHours(6), asked + Options.ArchiveRecheckAfter);
    }
}
