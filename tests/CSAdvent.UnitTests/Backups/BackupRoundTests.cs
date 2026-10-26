using CSAdvent.Web.Backups;
using CSAdvent.Web.Data;

namespace CSAdvent.UnitTests.Backups;

/// <summary>
/// Rounds of backup asks (#153): everyone's asked once per round, and a spot that needs a backup for a new reason or a new
/// author starts a new round, so the backups are asked again.
/// </summary>
public sealed class BackupRoundTests
{
    private static readonly DateTimeOffset November5 = new(2026, 11, 5, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset December8 = new(2026, 12, 8, 23, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ADroppedSpotGivenToANewAuthorWhoseLinkIsMissingStartsANewRound()
    {
        // The scenario in #153: dropped on November 5, nobody takes it, an organizer gives it to a new author, and on the
        // evening before its day their link is missing.
        var slot = new Slot { SeasonYear = 2026, Day = 9, Part = 1, DroppedOutAt = November5 };
        var dropped = BackupRound.For(latest: null, slot, BackupReason.DroppedOut, highestOfferId: 0, November5);
        dropped.NobodyLeftAt = November5.AddHours(8);

        slot.AuthorName = "Nora New";
        var linkMissing = BackupRound.For(dropped, slot, BackupReason.LinkMissing, highestOfferId: 41, December8);

        Assert.NotSame(dropped, linkMissing);
        Assert.Equal((2, BackupReason.LinkMissing, 41, December8), (linkMissing.Number, linkMissing.Reason, linkMissing.OffersAfter, linkMissing.StartedAt));
        Assert.Null(linkMissing.NobodyLeftAt);
    }

    [Fact]
    public void TheSameNeedCarriesOnTheRound()
    {
        var slot = new Slot { SeasonYear = 2026, Day = 9, Part = 1, AuthorName = "Lu Late", ContributorId = 7 };
        var round = BackupRound.For(latest: null, slot, BackupReason.LinkMissing, highestOfferId: 0, December8);

        Assert.Same(round, BackupRound.For(round, slot, BackupReason.LinkMissing, highestOfferId: 12, December8.AddHours(4)));

        // A typo fixed in an unlinked author's name isn't a new author.
        var unlinked = new Slot { SeasonYear = 2026, Day = 10, Part = 1, AuthorName = "Lu  late" };
        var first = BackupRound.For(latest: null, unlinked, BackupReason.LinkMissing, highestOfferId: 0, December8);
        unlinked.AuthorName = "Lu Late";
        Assert.Same(first, BackupRound.For(first, unlinked, BackupReason.LinkMissing, highestOfferId: 3, December8));
    }

    [Fact]
    public void ANewAuthorOrANewReasonOrAnotherDropoutIsANewRound()
    {
        var slot = new Slot { SeasonYear = 2026, Day = 9, Part = 1, AuthorName = "Lu Late", ContributorId = 7 };
        var linkMissing = BackupRound.For(latest: null, slot, BackupReason.LinkMissing, highestOfferId: 0, December8);

        // Their post arrives, but without the blurb.
        var blurbMissing = BackupRound.For(linkMissing, slot, BackupReason.BlurbMissing, highestOfferId: 5, December8.AddHours(7));
        Assert.Equal(2, blurbMissing.Number);

        // A backup took it, then dropped out; another one did too.
        slot.AuthorName = null;
        slot.ContributorId = null;
        slot.DroppedOutAt = December8.AddHours(8);
        var dropped = BackupRound.For(blurbMissing, slot, BackupReason.DroppedOut, highestOfferId: 9, December8.AddHours(8));
        slot.DroppedOutAt = December8.AddHours(10);
        var droppedAgain = BackupRound.For(dropped, slot, BackupReason.DroppedOut, highestOfferId: 11, December8.AddHours(10));
        Assert.Equal((3, 4), (dropped.Number, droppedAgain.Number));

        // Rounds only go forward: the same need as an earlier round, after another one, is a new round too.
        slot.AuthorName = "Lu Late";
        slot.ContributorId = 7;
        Assert.Equal(5, BackupRound.For(droppedAgain, slot, BackupReason.LinkMissing, highestOfferId: 12, December8.AddHours(11)).Number);
    }

    [Fact]
    public void ADropoutIsTheSameWhetherItsReadFromTheDatabaseOrNot()
    {
        // .NET keeps 100-nanosecond ticks and PostgreSQL microseconds, so a dropout read back is a little earlier than the one
        // just saved. It mustn't look like another dropout, which would ask everyone again.
        var saved = new DateTimeOffset(2026, 11, 5, 15, 0, 7, TimeSpan.Zero).AddTicks(1_234_567);
        var readBack = saved.AddTicks(-(saved.Ticks % 10));

        Assert.Equal(
            BackupRound.HolderOf(new Slot { DroppedOutAt = saved }, BackupReason.DroppedOut),
            BackupRound.HolderOf(new Slot { DroppedOutAt = readBack }, BackupReason.DroppedOut));
    }
}
