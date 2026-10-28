using CSAdvent.Web.Data;
using CSAdvent.Web.Snapshots;

namespace CSAdvent.UnitTests.Snapshots;

public sealed class SnapshotRulesTests
{
    // December 3, 2026 in Eastern time: revealed at 05:00 UTC, over at 05:00 UTC the next day.
    private static readonly DateTimeOffset Reveal = new(2026, 12, 3, 5, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DayEnd = Reveal.AddDays(1);
    private static readonly DateTimeOffset Now = new(2026, 12, 3, 15, 0, 0, TimeSpan.Zero);
    private static readonly SnapshotOptions Options = new();

    private static readonly SnapshotState Due = new(SnapshotStatus.Due, 0, Now, null, null, null, null, null);

    [Theory]
    [InlineData(-1, true, false)] // Before its reveal, even if it's up.
    [InlineData(1, false, false)] // Revealed, but not seen yet: it may not be published.
    [InlineData(1, true, true)] // Revealed, and the blurb check has seen it.
    [InlineData(24, false, true)] // Its day is over: whatever is there is the post.
    [InlineData(24 + 47, false, true)]
    [InlineData(24 + 48, true, false)] // Too long ago: it waits for the one after its season.
    public void APostIsDueItsLiveSnapshotOnceItsUpOnItsDay(int hoursAfterReveal, bool seen, bool due) =>
        Assert.Equal(due, SnapshotRules.IsLiveDue(Reveal, DayEnd, seen, Reveal.AddHours(hoursAfterReveal), Options.LiveWithin));

    [Theory]
    [InlineData(-1, null, false)] // The season isn't over.
    [InlineData(0, null, true)]
    [InlineData(0, LinkStatus.Unchecked, true)]
    [InlineData(0, LinkStatus.Working, true)]
    [InlineData(0, LinkStatus.Refused, true)] // The site turns our checks away, not necessarily the archive's.
    [InlineData(0, LinkStatus.Failing, false)]
    [InlineData(0, LinkStatus.Dead, false)]
    public void APostIsDueItsSnapshotAfterTheSeasonUnlessItsGone(int hoursAfterSeason, LinkStatus? link, bool due)
    {
        var seasonEnds = new DateTimeOffset(2026, 12, 26, 5, 0, 0, TimeSpan.Zero);
        Assert.Equal(due, SnapshotRules.IsAfterSeasonDue(seasonEnds, link, seasonEnds.AddHours(hoursAfterSeason)));
    }

    [Fact]
    public void AStartedCaptureIsCapturingFromWhenItWasAskedFor()
    {
        var after = SnapshotRules.After(Due, CaptureAnswer.Pending("spn2-1"), Now, Options);

        Assert.Equal(Due with { Status = SnapshotStatus.Capturing, JobId = "spn2-1", RequestedAt = Now }, after);
    }

    [Fact]
    public void ACaptureStillGoingKeepsItsJob()
    {
        var capturing = Due with { Status = SnapshotStatus.Capturing, JobId = "spn2-1", RequestedAt = Now };

        Assert.Equal(capturing, SnapshotRules.After(capturing, CaptureAnswer.Pending("spn2-1"), Now.AddMinutes(15), Options));
        Assert.Equal(capturing with { Detail = "The Wayback Machine answered 503." }, SnapshotRules.After(capturing, CaptureAnswer.NoAnswer("The Wayback Machine answered 503."), Now.AddMinutes(15), Options));
    }

    [Fact]
    public void ASavedCaptureIsDone()
    {
        var capturing = Due with { Status = SnapshotStatus.Capturing, JobId = "spn2-1", RequestedAt = Now, Attempts = 1, Detail = "Earlier" };
        var savedAt = Now.AddSeconds(20);

        var after = SnapshotRules.After(capturing, CaptureAnswer.Saved("https://web.archive.org/web/20261203150020/https://a.example/", savedAt), Now.AddMinutes(15), Options);

        Assert.Equal(capturing with { Status = SnapshotStatus.Saved, JobId = null, SnapshotUrl = "https://web.archive.org/web/20261203150020/https://a.example/", SavedAt = savedAt, Detail = null }, after);
    }

    [Fact]
    public void AFailedCaptureIsTriedAgainLaterAndLaterThenGivenUpOn()
    {
        var state = Due;
        var at = Now;
        var waits = new List<TimeSpan>();
        for (var attempt = 1; attempt < Options.MaxAttempts; attempt++)
        {
            state = SnapshotRules.After(state with { Status = SnapshotStatus.Capturing, JobId = "spn2-1", RequestedAt = at }, CaptureAnswer.Failed("Nope"), at, Options);
            Assert.Equal((SnapshotStatus.Due, attempt, (string?)null, "Nope"), (state.Status, state.Attempts, state.JobId, state.Detail));
            waits.Add(state.NextAttemptAt - at);
            at = state.NextAttemptAt;
        }

        Assert.Equal([TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.FromHours(4), TimeSpan.FromHours(8)], waits);
        var last = SnapshotRules.After(state, CaptureAnswer.Failed("Nope"), at, Options);
        Assert.Equal((SnapshotStatus.Failed, Options.MaxAttempts), (last.Status, last.Attempts));
    }

    [Fact]
    public void ARequestWithNoAnswerIsMadeAgainLaterWithoutCountingAsATry()
    {
        var after = SnapshotRules.After(Due, CaptureAnswer.NoAnswer("The Wayback Machine couldn't be reached."), Now, Options);

        Assert.Equal(Due with { NextAttemptAt = Now + Options.RetryAfter, Detail = "The Wayback Machine couldn't be reached." }, after);
    }

    [Fact]
    public void ACaptureThatNeverSaysItsDoneIsATry()
    {
        var capturing = Due with { Status = SnapshotStatus.Capturing, JobId = "spn2-1", RequestedAt = Now };
        var later = Now + Options.JobExpiry;

        foreach (var answer in new[] { CaptureAnswer.Pending("spn2-1"), CaptureAnswer.NoAnswer("Not found") })
        {
            var after = SnapshotRules.After(capturing, answer, later, Options);
            Assert.Equal((SnapshotStatus.Due, 1, (string?)null, later + Options.RetryAfter), (after.Status, after.Attempts, after.JobId, after.NextAttemptAt));
        }
    }
}
