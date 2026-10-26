using System.Threading.Channels;

namespace CSAdvent.Web.Email.Queue;

/// <summary>
/// Wakes <see cref="EmailOutboxWorker"/> when an email is queued in this process, so it goes out straight away rather than at
/// the next poll. Wake-ups between two passes count once.
/// </summary>
public sealed class EmailQueueSignal
{
    private readonly Channel<bool> _wakeUps = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Notify() => _wakeUps.Writer.TryWrite(true);

    /// <summary>Waits until something is queued here, or <paramref name="timeout"/> passes.</summary>
    public async Task WaitAsync(TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        using var timer = new CancellationTokenSource(timeout, timeProvider);
        using var either = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, cancellationToken);
        try
        {
            await _wakeUps.Reader.ReadAsync(either.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The time's up.
        }
    }
}
