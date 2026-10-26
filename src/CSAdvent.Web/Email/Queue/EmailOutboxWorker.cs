using CSAdvent.Web.Data;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Email.Queue;

/// <summary>
/// Drains the email queue (#152) on every replica, in every environment (the tests need their emails delivered too): a pass
/// (<see cref="EmailQueueDrain"/>) every <see cref="EmailQueueOptions.PollInterval"/>, or straight away when this replica queues
/// an email (<see cref="EmailQueueSignal"/>). The advisory lock means only one replica's pass sends at a time. When the email
/// service throttles, it waits as long as it was asked to.
/// </summary>
public sealed partial class EmailOutboxWorker(
    IServiceScopeFactory scopeFactory,
    EmailQueueSignal signal,
    IOptions<EmailQueueOptions> options,
    TimeProvider timeProvider,
    ILogger<EmailOutboxWorker> logger)
    : BackgroundService
{
    /// <summary>The longest wait between passes while they keep failing (the database is down, say).</summary>
    private static readonly TimeSpan LongestWait = TimeSpan.FromMinutes(1);

    /// <summary>How often to look for the table while the deploy's migrations haven't created it.</summary>
    private static readonly TimeSpan TableCheckInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        var tableExists = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = options.Value.PollInterval;
            var wakeable = true;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();

                // A new revision starts before the deploy runs the migrations (#151). Until the table's there,
                // QueueingEmailSender sends straight through instead, and this waits without failing queries.
                if (!tableExists && !(tableExists = await EmailOutbox.TableExistsAsync(scope.ServiceProvider.GetRequiredService<AdventDbContext>(), stoppingToken)))
                {
                    if (failures++ == 0)
                    {
                        LogNoTableYet();
                    }

                    wait = TableCheckInterval;
                    await Task.Delay(wait, timeProvider, stoppingToken);
                    continue;
                }

                var pass = await scope.ServiceProvider.GetRequiredService<EmailQueueDrain>().RunAsync(stoppingToken);
                failures = 0;
                if (pass.PausedUntil is { } pausedUntil)
                {
                    wait = pausedUntil - timeProvider.GetUtcNow();
                    wakeable = false;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (EmailOutbox.IsMissingTable(exception))
            {
                // Dropped from under us (a database restored from before the queue, say): check again before the next pass.
                tableExists = false;
                wait = TableCheckInterval;
            }
            catch (Exception exception)
            {
                // An unhandled exception would stop the whole app. Logged as an error once, then quietly, until a pass works.
                if (failures++ == 0)
                {
                    LogPassFailed(exception);
                }

                wait = TimeSpan.FromTicks(Math.Min(LongestWait.Ticks, options.Value.PollInterval.Ticks << Math.Min(failures, 10)));
            }

            try
            {
                if (wakeable)
                {
                    await signal.WaitAsync(wait, timeProvider, stoppingToken);
                }
                else if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, timeProvider, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The email queue's table isn't there yet (the migrations haven't run); emails are sent straight away until it is")]
    private partial void LogNoTableYet();

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending queued emails failed. Trying again shortly")]
    private partial void LogPassFailed(Exception exception);
}
