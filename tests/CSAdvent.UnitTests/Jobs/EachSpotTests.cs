using CSAdvent.Web.Data;
using CSAdvent.Web.Jobs;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CSAdvent.UnitTests.Jobs;

/// <summary>One spot that throws doesn't stop a job step's other spots (#147).</summary>
public sealed class EachSpotTests
{
    // Tracking entities never opens a connection, so no database is needed.
    private static AdventDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AdventDbContext>().UseNpgsql("Host=localhost;Database=csadvent").Options);

    [Fact]
    public async Task ASpotThatThrowsIsSkippedAndWhatItLeftIsntSavedWithTheNext()
    {
        await using var db = CreateContext();
        var advanced = new List<int>();
        var failed = new List<(int SlotId, string Message)>();

        await EachSpot.RunAsync(db, [1, 2, 3], slotId =>
        {
            if (slotId == 2)
            {
                // Half done when it failed: nothing it changed may go out with spot 3's save.
                db.SentReminders.Add(new SentReminder { Kind = ReminderKind.ContributorDayOf, SlotId = 2 });
                throw new UriFormatException("Invalid URI: The hostname could not be parsed.");
            }

            Assert.Empty(db.ChangeTracker.Entries());
            advanced.Add(slotId);
            return Task.CompletedTask;
        }, (exception, slotId) => failed.Add((slotId, exception.Message)), TestContext.Current.CancellationToken);

        Assert.Equal([1, 3], advanced);
        Assert.Equal([(2, "Invalid URI: The hostname could not be parsed.")], failed);
    }

    [Fact]
    public async Task AMissingTableStopsTheLoopForTheWorkersWarning()
    {
        // Every spot would fail the same way until the deploy's migrations run, so the worker logs it once, as a warning.
        await using var db = CreateContext();
        var missing = new DbUpdateException("Saving failed.", new PostgresException("relation \"backup_rounds\" does not exist", "ERROR", "ERROR", PostgresErrorCodes.UndefinedTable));
        var advanced = new List<int>();

        await Assert.ThrowsAsync<DbUpdateException>(() => EachSpot.RunAsync(db, [1, 2], slotId =>
        {
            advanced.Add(slotId);
            throw missing;
        }, (_, _) => Assert.Fail("A missing table isn't one spot's failure."), TestContext.Current.CancellationToken));

        Assert.Equal([1], advanced);
    }

    [Fact]
    public async Task StoppingTheAppStopsTheLoop()
    {
        await using var db = CreateContext();
        using var stopping = new CancellationTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(() => EachSpot.RunAsync(db, [1, 2], _ =>
        {
            stopping.Cancel();
            throw new OperationCanceledException(stopping.Token);
        }, (_, _) => Assert.Fail("Stopping isn't a failure."), stopping.Token));
    }
}
