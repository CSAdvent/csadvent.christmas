using System.Collections.Concurrent;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using CSAdvent.Web.Email.Queue;
using CSAdvent.Web.Jobs;
using MailKit.Net.Smtp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// The email queue's worker (<see cref="EmailOutboxWorker"/>), the one background job with a loop of its own rather than
/// <see cref="PeriodicWorker"/>'s, run on a fake clock against databases of its own (#199): csadvent_worker_2624 and 2625.
/// The site's own worker never sees them, and an advisory lock only counts in its own database.
/// </summary>
public sealed class EmailQueueWorkerTests(AppHostFixture fixture)
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ItWaitsForTheMigrationsThenSendsWhatsQueued()
    {
        // A new revision starts before the deploy's migrations job has created the queue's table (#151).
        await using var database = await ThrowawayDatabase.CreateAsync(fixture, "csadvent_worker_2624");
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var queue = new WorkerUnderTest(database.ConnectionString, clock);
        await queue.StartAsync();

        // It says so once, as information rather than an error for the alert, and looks again every 15 seconds without
        // recording a run, so /manage doesn't show the step failing (#172).
        await WaitUntilAsync(() => queue.Logs.Any(e => e.Message.Contains("isn't there yet", StringComparison.Ordinal)), "the worker to notice the missing table");
        for (var look = 0; look < 3; look++)
        {
            clock.Advance(TimeSpan.FromSeconds(15));
            await Task.Delay(100, CancellationToken);
        }

        Assert.Equal([LogLevel.Information], queue.Logs.Select(e => e.Level));
        Assert.Empty(queue.Heartbeats.Saved);

        // The migrations run, and an email is queued (on another replica, so nothing wakes this one).
        await using (var db = database.CreateDbContext())
        {
            await db.Database.MigrateAsync(CancellationToken);
            db.EmailOutbox.Add(Queued("worker-2624@example.com", "Queued before the migrations finished", clock.GetUtcNow()));
            await db.SaveChangesAsync(CancellationToken);
        }

        // Its next look finds the table and sends it, and the run is recorded as a success.
        await AdvanceUntilAsync(clock, TimeSpan.FromSeconds(15), () => !queue.Transport.Attempts.IsEmpty, "the email to be sent");
        Assert.Equal("worker-2624@example.com", Assert.Single(queue.Transport.Attempts).To);
        await WaitUntilAsync(() => queue.Heartbeats.Saved.Any(), "the run to be recorded");
        Assert.NotNull(queue.Heartbeats.Saved.Last().LastSucceededAt);
        Assert.DoesNotContain(queue.Logs, e => e.Level >= LogLevel.Warning);
        await using (var db = database.CreateDbContext())
        {
            Assert.Equal(QueuedEmailStatus.Sent, (await db.EmailOutbox.SingleAsync(CancellationToken)).Status);
        }
    }

    [Fact]
    public async Task WhenTheEmailServiceThrottlesItWaitsAsLongAsItWasAskedWhateverWakesIt()
    {
        await using var database = await ThrowawayDatabase.CreateAsync(fixture, "csadvent_worker_2625");
        await using (var db = database.CreateDbContext())
        {
            await db.Database.MigrateAsync(CancellationToken);
        }

        // The email service refuses the first send with SMTP's 421, and doesn't say for how long: the queue's own pause.
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var pause = TimeSpan.FromMinutes(10);
        await using var queue = new WorkerUnderTest(database.ConnectionString, clock, new EmailQueueOptions { ThrottlePause = pause });
        queue.Transport.Refuse(1);
        await using (var db = database.CreateDbContext())
        {
            db.EmailOutbox.Add(Queued("worker-2625@example.com", "Sent once the email service lets us", clock.GetUtcNow()));
            await db.SaveChangesAsync(CancellationToken);
        }

        await queue.StartAsync();
        await WaitUntilAsync(() => queue.Transport.Attempts.Count == 1, "the first, throttled send");
        var throttledAt = queue.Transport.Attempts.Single().At;

        // While it's paused, neither the poll interval nor an email queued on this replica (the signal) starts a pass.
        for (var minute = 1; minute < pause.TotalMinutes; minute++)
        {
            queue.Signal.Notify();
            clock.Advance(TimeSpan.FromMinutes(1));
            await Task.Delay(100, CancellationToken);
        }

        // Once the pause is over it sends, and the throttled try didn't count against the email.
        await AdvanceUntilAsync(clock, TimeSpan.FromMinutes(1), () => queue.Transport.Attempts.Count == 2, "the send after the pause");
        var sentAt = queue.Transport.Attempts.Last().At;
        Assert.True(sentAt - throttledAt >= pause, $"It sent again {sentAt - throttledAt} after it was throttled, before the {pause} pause was over.");
        await using (var db = database.CreateDbContext())
        {
            var email = await db.EmailOutbox.SingleAsync(CancellationToken);
            Assert.Equal((QueuedEmailStatus.Sent, 0), (email.Status, email.Attempts));
        }
    }

    private static QueuedEmail Queued(string to, string subject, DateTimeOffset now) => new()
    {
        Kind = "claim-received",
        Priority = EmailPriority.Urgent,
        ToAddress = to,
        ReplyTo = "hello@csadvent.christmas",
        Subject = subject,
        Html = $"<p>{subject}</p>",
        Text = subject,
        Transactional = true,
        CreatedAt = now,
        Status = QueuedEmailStatus.Pending,
        NextAttemptAt = now,
    };

    /// <summary>Waits, in real time, for something the worker does on its own task.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        for (var attempt = 0; attempt < 600; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(100, CancellationToken);
        }

        Assert.Fail($"Gave up waiting for {what}.");
    }

    /// <summary>
    /// Moves the fake clock on a step at a time until the condition holds. A step taken before the worker starts waiting
    /// only makes the worker's wait end later on the fake clock, so this never depends on how fast the worker is.
    /// </summary>
    private static async Task AdvanceUntilAsync(FakeTimeProvider clock, TimeSpan step, Func<bool> condition, string what)
    {
        for (var attempt = 0; attempt < 600; attempt++)
        {
            if (condition())
            {
                return;
            }

            clock.Advance(step);
            await Task.Delay(100, CancellationToken);
        }

        Assert.Fail($"Gave up waiting for {what}.");
    }

    /// <summary>A database of the test's own on the test server, dropped afterwards.</summary>
    private sealed class ThrowawayDatabase : IAsyncDisposable
    {
        private readonly string _server;
        private readonly string _name;

        private ThrowawayDatabase(string server, string name)
        {
            _server = server;
            _name = name;
            ConnectionString = new NpgsqlConnectionStringBuilder(server) { Database = name }.ConnectionString;
        }

        public string ConnectionString { get; }

        public static async Task<ThrowawayDatabase> CreateAsync(AppHostFixture fixture, string name)
        {
            var server = new NpgsqlConnectionStringBuilder(await fixture.GetConnectionStringAsync()) { Database = "postgres" }.ConnectionString;
            await ExecuteAsync(server, $"DROP DATABASE IF EXISTS {name} WITH (FORCE)");
            await ExecuteAsync(server, $"CREATE DATABASE {name}");
            return new ThrowawayDatabase(server, name);
        }

        public AdventDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AdventDbContext>().UseNpgsql(ConnectionString).Options);

        public async ValueTask DisposeAsync()
        {
            await using (var connection = new NpgsqlConnection(ConnectionString))
            {
                NpgsqlConnection.ClearPool(connection);
            }

            await ExecuteAsync(_server, $"DROP DATABASE IF EXISTS {_name} WITH (FORCE)");
        }

        private static async Task ExecuteAsync(string connectionString, string sql)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// The worker with the services it uses in the app: the real outbox store and drain against the database, a transport
    /// that records (or refuses) each send, and its runs and logs kept for the test.
    /// </summary>
    private sealed class WorkerUnderTest : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly EmailOutboxWorker _worker;

        public WorkerUnderTest(string connectionString, FakeTimeProvider clock, EmailQueueOptions? options = null)
        {
            var settings = Options.Create(options ?? new EmailQueueOptions());
            Transport = new RecordingTransport(clock);
            _services = new ServiceCollection()
                .AddDbContext<AdventDbContext>(db => db.UseNpgsql(connectionString))
                .AddScoped<IEmailOutboxStore, EmailOutboxStore>()
                .AddKeyedSingleton<IEmailSender>(EmailRegistration.TransportKey, Transport)
                .AddScoped<EmailQueueDrain>()
                .AddSingleton(settings)
                .AddSingleton<TimeProvider>(clock)
                .AddLogging(logging => logging.AddProvider(new RecordingLoggerProvider(Logs)))
                .BuildServiceProvider();
            _worker = new EmailOutboxWorker(
                _services.GetRequiredService<IServiceScopeFactory>(),
                Signal,
                settings,
                new JobRuns(Heartbeats, clock, NullLogger<JobRuns>.Instance),
                clock,
                _services.GetRequiredService<ILogger<EmailOutboxWorker>>());
        }

        public EmailQueueSignal Signal { get; } = new();

        public RecordingTransport Transport { get; }

        public RecordingHeartbeats Heartbeats { get; } = new();

        /// <summary>What the worker itself logged (not the drain, the store or EF Core).</summary>
        public ConcurrentQueue<(LogLevel Level, string Message)> Logs { get; } = new();

        public Task StartAsync() => _worker.StartAsync(CancellationToken);

        public async ValueTask DisposeAsync()
        {
            await _worker.StopAsync(CancellationToken.None);
            _worker.Dispose();
            await _services.DisposeAsync();
        }
    }

    /// <summary>Records each send and when it was by the fake clock, refusing the ones it's told to with SMTP's 421.</summary>
    private sealed class RecordingTransport(TimeProvider clock) : IEmailSender
    {
        private int _refuseNext;

        public void Refuse(int sends) => _refuseNext = sends;

        public ConcurrentQueue<(string To, DateTimeOffset At)> Attempts { get; } = new();

        public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
        {
            Attempts.Enqueue((email.To.Address, clock.GetUtcNow()));
            return Interlocked.Decrement(ref _refuseNext) >= 0
                ? Task.FromException(new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.ServiceNotAvailable, "Too many messages, slow down"))
                : Task.CompletedTask;
        }
    }

    /// <summary><c>job_heartbeats</c>, in memory.</summary>
    private sealed class RecordingHeartbeats : IJobHeartbeatStore
    {
        public ConcurrentQueue<JobHeartbeat> Saved { get; } = new();

        public Task SaveAsync(string step, DateTimeOffset? lastSucceededAt, DateTimeOffset? lastFailedAt, CancellationToken cancellationToken)
        {
            Saved.Enqueue(new JobHeartbeat { Step = step, LastSucceededAt = lastSucceededAt, LastFailedAt = lastFailedAt });
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<JobHeartbeat>> LoadAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<JobHeartbeat>>([.. Saved]);
    }

    private sealed class RecordingLoggerProvider(ConcurrentQueue<(LogLevel Level, string Message)> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) =>
            categoryName == typeof(EmailOutboxWorker).FullName ? new Recorder(entries) : NullLogger.Instance;

        public void Dispose()
        {
        }

        private sealed class Recorder(ConcurrentQueue<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
