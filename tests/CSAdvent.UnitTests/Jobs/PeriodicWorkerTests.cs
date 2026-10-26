using CSAdvent.Web.Backups;
using CSAdvent.Web.Blurbs;
using CSAdvent.Web.Jobs;
using CSAdvent.Web.Reminders;
using CSAdvent.Web.Social.Posting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace CSAdvent.UnitTests.Jobs;

/// <summary>
/// Background jobs run each step in a scope and a try/catch of its own (#147), and record each step's outcome (#172), so one
/// step that throws never stops the others.
/// </summary>
public sealed class PeriodicWorkerTests
{
    private static readonly DateTimeOffset Now = new(2026, 12, 9, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AStepThatThrowsDoesntStopTheStepsAfterIt()
    {
        var ran = new List<(string Step, IServiceProvider Services)>();
        var clock = new FakeTimeProvider(Now);
        var (runs, store) = Runs(clock);
        var logger = new ListLogger();
        using var worker = new TestWorker(Services(), runs, clock, logger,
        [
            new(First, (services, _, _) => { ran.Add(("first", services)); throw new InvalidOperationException("One bad post."); }),
            new(Second, (services, _, _) => { ran.Add(("second", services)); return Task.CompletedTask; }),
            new(Third, (services, _, _) => { ran.Add(("third", services)); return Task.CompletedTask; }),
        ]);

        await worker.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["first", "second", "third"], ran.Select(r => r.Step));
        Assert.Equal(3, ran.Select(r => r.Services).Distinct().Count());
        Assert.Equal((LogLevel.Error, "First step"), Assert.Single(logger.Entries));

        // Each step's outcome is its own.
        Assert.Equal(
            [("first", (DateTimeOffset?)null, (DateTimeOffset?)Now), ("second", Now, null), ("third", Now, null)],
            store.Saved.Select(s => (s.Step, s.LastSucceededAt, s.LastFailedAt)));
    }

    [Fact]
    public async Task AMissingTableIsAWarningAndAnythingElseAnError()
    {
        var clock = new FakeTimeProvider(Now);
        var logger = new ListLogger();
        using var worker = new TestWorker(Services(), Runs(clock).Runs, clock, logger,
        [
            new(First, (_, _, _) => throw new InvalidOperationException("Outer.", new PostgresException("relation \"backup_rounds\" does not exist", "ERROR", "ERROR", PostgresErrorCodes.UndefinedTable))),
            new(Second, (_, _, _) => throw new TimeoutException()),
        ]);

        await worker.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal([(LogLevel.Warning, "First step"), (LogLevel.Error, "Second step")], logger.Entries.Select(e => (e.Level, e.Step)));
    }

    [Fact]
    public async Task ItRunsWhenItStartsThenEveryInterval()
    {
        var clock = new FakeTimeProvider(Now);
        var runs = Runs(clock).Runs;
        var ticks = new SemaphoreSlim(0);
        using var worker = new TestWorker(Services(), runs, clock, NullLogger.Instance,
            [new(First, (_, _, _) => { ticks.Release(); return Task.CompletedTask; })]);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.True(await ticks.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), "It didn't run when it started.");
            Assert.Equal(JobHealth.Succeeding, Assert.Single(runs.Running()).Health);

            clock.Advance(TestWorker.Interval);
            Assert.True(await ticks.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), "It didn't run after an interval.");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task WhenTheBlurbCheckThrowsTheRemindersAndBackupsStillRun()
    {
        // The steps' services fail as they're made, after saying they were asked for: enough to see each step runs.
        var asked = new List<string>();
        var services = new ServiceCollection()
            .AddScoped<BlurbCheckService>(_ => { asked.Add("blurb check"); throw new InvalidOperationException("A page the reader chokes on."); })
            .AddScoped<ReminderService>(_ => { asked.Add("reminders"); throw new InvalidOperationException("Stand-in."); })
            .AddScoped<BackupService>(_ => { asked.Add("backups"); throw new InvalidOperationException("Stand-in."); })
            .BuildServiceProvider();
        var clock = new FakeTimeProvider(Now);
        var (runs, store) = Runs(clock);
        using var worker = new ReminderWorker(services.GetRequiredService<IServiceScopeFactory>(), runs, clock, NullLogger<ReminderWorker>.Instance);

        await worker.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["blurb check", "reminders", "backups"], asked);
        Assert.Equal(["blurb-check", "reminders", "backups"], store.Saved.Select(s => s.Step));
    }

    [Fact]
    public async Task WhenTheCredentialCheckThrowsTheAnnouncementsStillGoOut()
    {
        var asked = new List<string>();
        var services = new ServiceCollection()
            .AddScoped<SocialCredentialCheck>(_ => { asked.Add("credentials"); throw new InvalidOperationException("Stand-in."); })
            .AddScoped<SocialPostingService>(_ => { asked.Add("announcements"); throw new InvalidOperationException("Stand-in."); })
            .AddScoped<SeasonSocialService>(_ => { asked.Add("season posts"); throw new InvalidOperationException("Stand-in."); })
            .BuildServiceProvider();
        var clock = new FakeTimeProvider(Now);
        using var worker = new SocialPostingWorker(services.GetRequiredService<IServiceScopeFactory>(), Runs(clock).Runs, clock, NullLogger<SocialPostingWorker>.Instance);

        await worker.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["credentials", "announcements", "season posts"], asked);
    }

    [Fact]
    public void EveryStepIsListedForManageOnce()
    {
        Assert.Equal(JobSteps.All.Count, JobSteps.All.Select(s => s.Key).Distinct().Count());
        Assert.Contains(ReminderWorker.BlurbCheck, JobSteps.All);
        Assert.Contains(ReminderWorker.Reminders, JobSteps.All);
        Assert.Contains(ReminderWorker.Backups, JobSteps.All);
        Assert.All(JobSteps.All, s => Assert.InRange(s.Key.Length, 1, 50));
    }

    private static readonly JobStep First = new("first", "First step", TestWorker.Interval);
    private static readonly JobStep Second = new("second", "Second step", TestWorker.Interval);
    private static readonly JobStep Third = new("third", "Third step", TestWorker.Interval);

    private static IServiceScopeFactory Services() => new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

    private static (JobRuns Runs, FakeHeartbeatStore Store) Runs(TimeProvider clock)
    {
        var store = new FakeHeartbeatStore();
        return (new JobRuns(store, clock, NullLogger<JobRuns>.Instance), store);
    }

    private sealed class TestWorker(IServiceScopeFactory scopeFactory, JobRuns runs, TimeProvider timeProvider, ILogger logger, IReadOnlyList<PeriodicStep> steps)
        : PeriodicWorker(scopeFactory, runs, timeProvider, logger, Interval)
    {
        public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

        protected override IReadOnlyList<PeriodicStep> Steps => steps;
    }

    /// <summary>Records what was logged, and which step it was about.</summary>
    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string? Step)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, (state as IReadOnlyList<KeyValuePair<string, object?>>)?.FirstOrDefault(p => p.Key == "Step").Value as string));
    }
}
