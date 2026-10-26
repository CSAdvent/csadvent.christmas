using CSAdvent.Web.Data;
using CSAdvent.Web.Jobs;
using CSAdvent.Web.Reminders;
using CSAdvent.Web.Retention;
using CSAdvent.Web.SeasonTesting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace CSAdvent.UnitTests.Jobs;

/// <summary>
/// When each step last succeeded (#172): late after four runs and at least 30 minutes, which turns <c>/health</c> Degraded,
/// never Unhealthy; only steps whose worker runs here count; and saved for /manage without writing on every run.
/// </summary>
public sealed class JobRunsTests
{
    private static readonly DateTimeOffset Start = new(2026, 12, 9, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AStepIsLateAfterFourRunsAndAtLeastHalfAnHour()
    {
        Assert.Equal(TimeSpan.FromHours(1), ReminderWorker.Backups.LateAfter);
        Assert.Equal(TimeSpan.FromHours(4), ContactRetentionWorker.ContactDetails.LateAfter);
        Assert.Equal(TimeSpan.FromMinutes(30), TestSeasonsRefresher.Refresh.LateAfter);
        Assert.Equal(TimeSpan.FromMinutes(30), Web.Email.Queue.EmailOutboxWorker.Step.LateAfter);
    }

    [Fact]
    public void WhereAStepStands()
    {
        var step = ReminderWorker.Reminders;

        // Its worker doesn't run here (the Testing environment), however old its last run.
        Assert.Equal(JobHealth.Off, step.HealthAt(Start.AddDays(3), startedAt: null, lastSucceededAt: Start, lastFailedAt: null));

        // Started, no run finished yet, then late an hour after it started.
        Assert.Equal(JobHealth.Waiting, step.HealthAt(Start.AddMinutes(59), Start, null, null));
        Assert.Equal(JobHealth.Late, step.HealthAt(Start.AddMinutes(61), Start, null, null));

        Assert.Equal(JobHealth.Succeeding, step.HealthAt(Start.AddMinutes(30), Start, Start.AddMinutes(15), Start.AddMinutes(1)));
        Assert.Equal(JobHealth.Failing, step.HealthAt(Start.AddMinutes(31), Start, Start.AddMinutes(15), Start.AddMinutes(30)));
        Assert.Equal(JobHealth.Failing, step.HealthAt(Start.AddMinutes(31), Start, null, Start.AddMinutes(30)));

        // An hour since it last succeeded, failing or not.
        Assert.Equal(JobHealth.Late, step.HealthAt(Start.AddMinutes(76), Start, Start.AddMinutes(15), Start.AddMinutes(75)));

        // A replica that has just started isn't late because the last success, on the old revision, was a while ago.
        Assert.Equal(JobHealth.Succeeding, step.HealthAt(Start.AddMinutes(5), Start, Start.AddDays(-2), null));
    }

    [Fact]
    public void ManageSaysHowOftenEachStepRuns()
    {
        Assert.Equal("Every 15 minutes", ReminderWorker.Reminders.Every);
        Assert.Equal("Every hour", ContactRetentionWorker.ContactDetails.Every);
        Assert.Equal("Every 15 seconds", TestSeasonsRefresher.Refresh.Every);
        Assert.Equal("Every 2 seconds", Web.Email.Queue.EmailOutboxWorker.Step.Every);
        Assert.Equal("Every minute", Web.Content.OtherAdventLogoWorker.Logos.Every);
        Assert.Equal("4 hours", JobStep.Duration(TimeSpan.FromHours(4)));
    }

    [Fact]
    public async Task TheHealthCheckIsDegradedNeverUnhealthyWhenAStepIsLate()
    {
        var clock = new FakeTimeProvider(Start);
        var runs = new JobRuns(new FakeHeartbeatStore(), clock, NullLogger<JobRuns>.Instance);
        var check = new JobsHealthCheck(runs);
        runs.Started(ReminderWorker.Backups);
        await runs.RecordAsync(ReminderWorker.Backups, succeeded: true, TestContext.Current.CancellationToken);

        clock.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(HealthStatus.Healthy, (await CheckAsync(check)).Status);

        // It keeps failing, an hour after it last succeeded.
        await runs.RecordAsync(ReminderWorker.Backups, succeeded: false, TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(2));
        var late = await CheckAsync(check);
        Assert.Equal(HealthStatus.Degraded, late.Status);
        Assert.Contains("Backup asks", late.Description);

        await runs.RecordAsync(ReminderWorker.Backups, succeeded: true, TestContext.Current.CancellationToken);
        Assert.Equal(HealthStatus.Healthy, (await CheckAsync(check)).Status);
    }

    [Fact]
    public async Task StepsWhoseWorkerDoesntRunHereNeverCount()
    {
        // The Testing environment runs only the email queue's worker and the test season's: the rest never start.
        var clock = new FakeTimeProvider(Start);
        var runs = new JobRuns(new FakeHeartbeatStore(), clock, NullLogger<JobRuns>.Instance);
        runs.Started(Web.Email.Queue.EmailOutboxWorker.Step);
        await runs.RecordAsync(ReminderWorker.Reminders, succeeded: false, TestContext.Current.CancellationToken);

        for (var i = 0; i < 48; i++)
        {
            clock.Advance(TimeSpan.FromHours(1));
            await runs.RecordAsync(Web.Email.Queue.EmailOutboxWorker.Step, succeeded: true, TestContext.Current.CancellationToken);
        }

        Assert.Equal(HealthStatus.Healthy, (await CheckAsync(new JobsHealthCheck(runs))).Status);
        var report = await runs.GetStatusAsync(TestContext.Current.CancellationToken);
        Assert.Equal(JobHealth.Off, report.Steps.Single(s => s.Step == ReminderWorker.Reminders).Health);
        Assert.Equal(JobHealth.Succeeding, report.Steps.Single(s => s.Step == Web.Email.Queue.EmailOutboxWorker.Step).Health);
    }

    [Fact]
    public async Task RunsAreSavedWhenTheOutcomeChangesAndOtherwiseOnceAMinute()
    {
        var clock = new FakeTimeProvider(Start);
        var store = new FakeHeartbeatStore();
        var runs = new JobRuns(store, clock, NullLogger<JobRuns>.Instance);
        var step = Web.Email.Queue.EmailOutboxWorker.Step;

        await runs.RecordAsync(step, succeeded: true, TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(2));
        await runs.RecordAsync(step, succeeded: true, TestContext.Current.CancellationToken);
        Assert.Single(store.Saved);

        clock.Advance(TimeSpan.FromSeconds(2));
        await runs.RecordAsync(step, succeeded: false, TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(2));
        await runs.RecordAsync(step, succeeded: true, TestContext.Current.CancellationToken);
        Assert.Equal(3, store.Saved.Count);

        clock.Advance(TimeSpan.FromSeconds(58));
        await runs.RecordAsync(step, succeeded: true, TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(2));
        await runs.RecordAsync(step, succeeded: true, TestContext.Current.CancellationToken);
        Assert.Equal(4, store.Saved.Count);

        // Each save carries both times, so neither is lost.
        Assert.Equal((Start.AddSeconds(66), Start.AddSeconds(4)), (store.Saved[^1].LastSucceededAt, store.Saved[^1].LastFailedAt));
    }

    [Fact]
    public async Task ManageShowsTheLatestRunOnAnyReplica()
    {
        var clock = new FakeTimeProvider(Start);
        var store = new FakeHeartbeatStore();
        var runs = new JobRuns(store, clock, NullLogger<JobRuns>.Instance);
        runs.Started(ReminderWorker.Backups);
        await runs.RecordAsync(ReminderWorker.Backups, succeeded: true, TestContext.Current.CancellationToken);
        store.Add(new JobHeartbeat { Step = ReminderWorker.Backups.Key, LastSucceededAt = Start.AddMinutes(10), LastFailedAt = Start.AddMinutes(-5) });

        var backups = (await runs.GetStatusAsync(TestContext.Current.CancellationToken)).Steps.Single(s => s.Step == ReminderWorker.Backups);

        Assert.Equal((Start.AddMinutes(10), Start.AddMinutes(-5)), (backups.LastSucceededAt, backups.LastFailedAt));
        Assert.Equal(JobsSteps(), (await runs.GetStatusAsync(TestContext.Current.CancellationToken)).Steps.Select(s => s.Step.Key));
    }

    [Fact]
    public async Task BeforeTheMigrationsItsOnlyThisReplicasRuns()
    {
        // The deploy window (#225): the table isn't there yet. Recording doesn't throw, and /manage still shows this replica.
        var clock = new FakeTimeProvider(Start);
        var store = new FakeHeartbeatStore { Failure = new PostgresException("relation \"job_heartbeats\" does not exist", "ERROR", "ERROR", PostgresErrorCodes.UndefinedTable) };
        var runs = new JobRuns(store, clock, NullLogger<JobRuns>.Instance);
        runs.Started(ReminderWorker.Backups);

        await runs.RecordAsync(ReminderWorker.Backups, succeeded: true, TestContext.Current.CancellationToken);
        var report = await runs.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.True(report.NotMigrated);
        Assert.Equal(Start, report.Steps.Single(s => s.Step == ReminderWorker.Backups).LastSucceededAt);
    }

    [Fact]
    public async Task ASaveThatFailsNeverFailsTheStep()
    {
        var clock = new FakeTimeProvider(Start);
        var runs = new JobRuns(new FakeHeartbeatStore { Failure = new TimeoutException() }, clock, NullLogger<JobRuns>.Instance);
        runs.Started(ReminderWorker.Backups);

        await runs.RecordAsync(ReminderWorker.Backups, succeeded: true, TestContext.Current.CancellationToken);

        Assert.Equal(JobHealth.Succeeding, Assert.Single(runs.Running()).Health);
    }

    private static IEnumerable<string> JobsSteps() => JobSteps.All.Select(s => s.Key);

    private static Task<HealthCheckResult> CheckAsync(JobsHealthCheck check) =>
        check.CheckHealthAsync(new HealthCheckContext { Registration = new HealthCheckRegistration(JobsHealthCheck.Name, check, HealthStatus.Degraded, null) }, TestContext.Current.CancellationToken);
}
