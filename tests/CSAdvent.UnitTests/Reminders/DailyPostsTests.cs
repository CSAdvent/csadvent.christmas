using CSAdvent.Web.Reminders;
using CSAdvent.Web.Social.Posting;

namespace CSAdvent.UnitTests.Reminders;

public sealed class DailyPostsTests
{
    private static readonly TimeOnly SendAt = new(9, 0);
    private static readonly TimeOnly Cutoff = new(14, 0);

    [Theory]
    [InlineData(8, 59, 2, false)]
    [InlineData(9, 0, 2, true)]
    [InlineData(9, 0, 1, false)]
    [InlineData(13, 59, 1, false)]
    [InlineData(14, 0, 1, true)]
    [InlineData(18, 0, 1, true)]
    [InlineData(14, 0, 0, false)]
    [InlineData(23, 45, 0, false)]
    public void TheDailyEmailWaitsForEveryPostUntilTheCutoff(int hour, int minute, int ready, bool due) =>
        Assert.Equal(due, DailyPosts.IsDue(new TimeOnly(hour, minute), SendAt, Cutoff, ready, slotsPerDay: 2));

    [Fact]
    public void ADayWithOnePostGoesOutAsSoonAsItsReady() =>
        Assert.True(DailyPosts.IsDue(new TimeOnly(9, 0), SendAt, Cutoff, ready: 1, slotsPerDay: 1));

    [Fact]
    public void ACutoffBeforeTheSendTimeNeverSendsEarly()
    {
        Assert.False(DailyPosts.IsDue(new TimeOnly(8, 30), SendAt, new TimeOnly(8, 0), ready: 1, slotsPerDay: 2));
        Assert.True(DailyPosts.IsDue(new TimeOnly(9, 0), SendAt, new TimeOnly(8, 0), ready: 1, slotsPerDay: 2));
    }

    [Fact]
    public void TheCutoffIsTheDaysLastAnnouncementUnlessItsSet()
    {
        var posting = new SocialPostingOptions { PostAt = [new TimeOnly(10, 0), new TimeOnly(14, 0)] };

        Assert.Equal(new TimeOnly(14, 0), DailyPosts.CutoffFor(2, new ReminderOptions(), posting));
        Assert.Equal(new TimeOnly(10, 0), DailyPosts.CutoffFor(1, new ReminderOptions(), posting));
        Assert.Equal(new TimeOnly(12, 30), DailyPosts.CutoffFor(2, new ReminderOptions { DailyEmailCutoff = new TimeOnly(12, 30) }, posting));
    }
}
