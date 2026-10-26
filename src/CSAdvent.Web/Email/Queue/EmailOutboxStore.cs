using System.Data.Common;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.Web.Email.Queue;

/// <summary>The outbox in PostgreSQL (<c>email_outbox</c>).</summary>
public sealed class EmailOutboxStore(AdventDbContext db, IServiceScopeFactory scopeFactory) : IEmailOutboxStore
{
    /// <summary>The advisory lock every replica's drain takes: "CSAdvent" in ASCII.</summary>
    public const long LockKey = 0x4353_4164_7665_6E74;

    public Task<bool> AnyDueAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        db.EmailOutbox.Due(now).Select(e => e.Id)
            .Concat(db.EmailOutbox.PastSendBefore(now).Select(e => e.Id))
            .AnyAsync(cancellationToken);

    public async Task<IAsyncDisposable?> TryLockAsync(CancellationToken cancellationToken)
    {
        // A connection and transaction of their own hold the lock for the whole pass, while the pass's changes commit one by
        // one on the context's connection. Plain ADO.NET, because EF Core's retrying execution strategy only allows a
        // transaction that can be replayed as a whole. If this replica dies, its connection closes and the lock goes with it.
        var scope = scopeFactory.CreateAsyncScope();
        var connection = scope.ServiceProvider.GetRequiredService<AdventDbContext>().Database.GetDbConnection();
        DbTransaction? transaction = null;
        try
        {
            await connection.OpenAsync(cancellationToken);
            transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT pg_try_advisory_xact_lock(@key)";
            var key = command.CreateParameter();
            key.ParameterName = "key";
            key.Value = LockKey;
            command.Parameters.Add(key);
            if (await command.ExecuteScalarAsync(cancellationToken) is true)
            {
                return new HeldLock(scope, connection, transaction);
            }
        }
        catch
        {
            await HeldLock.LetGoAsync(scope, connection, transaction);
            throw;
        }

        await HeldLock.LetGoAsync(scope, connection, transaction);
        return null;
    }

    public async Task<IReadOnlyList<UndeliveredEmail>> ExpireAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var expired = await db.EmailOutbox
            .PastSendBefore(now)
            .Select(e => new UndeliveredEmail(e.Id, e.Kind, e.Reference, QueuedEmailStatus.Expired))
            .ToListAsync(cancellationToken);
        if (expired.Count > 0)
        {
            var ids = expired.Select(e => e.Id).ToList();
            await db.EmailOutbox
                .Where(e => ids.Contains(e.Id) && e.Status == QueuedEmailStatus.Pending)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(e => e.Status, QueuedEmailStatus.Expired)
                        .SetProperty(e => e.FinishedAt, now)
                        .SetProperty(e => e.LockedUntil, (DateTimeOffset?)null),
                    cancellationToken);
        }

        return expired;
    }

    public Task<RecentSends> CountSentAsync(DateTimeOffset now, CancellationToken cancellationToken) => CountSentAsync(db, now, cancellationToken);

    /// <summary>What every replica has sent in the minute and the hour up to <paramref name="now"/>.</summary>
    public static async Task<RecentSends> CountSentAsync(AdventDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var minuteAgo = now - TimeSpan.FromMinutes(1);
        var hourAgo = now - TimeSpan.FromHours(1);
        return new(
            await db.EmailOutbox.CountAsync(e => e.SentAt > minuteAgo, cancellationToken),
            await db.EmailOutbox.CountAsync(e => e.SentAt > hourAgo, cancellationToken),
            await db.EmailOutbox.CountAsync(e => e.SentAt > minuteAgo && e.Priority >= EmailPriorities.FirstListMail, cancellationToken));
    }

    public async Task<IReadOnlyList<QueuedEmail>> ClaimAsync(DateTimeOffset now, int count, bool listMail, DateTimeOffset lockedUntil, CancellationToken cancellationToken)
    {
        if (count <= 0)
        {
            return [];
        }

        // Only the replica holding the advisory lock gets here, so nothing else can claim these in between.
        var ids = await db.EmailOutbox.Due(now).ListMail(listMail).InSendingOrder().Select(e => e.Id).Take(count).ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return [];
        }

        await db.EmailOutbox
            .Where(e => ids.Contains(e.Id) && e.Status == QueuedEmailStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.LockedUntil, lockedUntil), cancellationToken);
        return await db.EmailOutbox
            .AsNoTracking()
            .Where(e => ids.Contains(e.Id) && e.Status == QueuedEmailStatus.Pending)
            .InSendingOrder()
            .ToListAsync(cancellationToken);
    }

    public Task MarkSentAsync(int id, DateTimeOffset at, CancellationToken cancellationToken) =>
        db.EmailOutbox
            .Where(e => e.Id == id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(e => e.Status, QueuedEmailStatus.Sent)
                    .SetProperty(e => e.SentAt, at)
                    .SetProperty(e => e.FinishedAt, at)
                    .SetProperty(e => e.LockedUntil, (DateTimeOffset?)null),
                cancellationToken);

    public Task MarkRetryAsync(int id, int attempts, DateTimeOffset nextAttemptAt, string error, CancellationToken cancellationToken) =>
        db.EmailOutbox
            .Where(e => e.Id == id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(e => e.Attempts, attempts)
                    .SetProperty(e => e.NextAttemptAt, nextAttemptAt)
                    .SetProperty(e => e.LastError, error)
                    .SetProperty(e => e.LockedUntil, (DateTimeOffset?)null),
                cancellationToken);

    public Task MarkFailedAsync(int id, int attempts, DateTimeOffset at, string error, CancellationToken cancellationToken) =>
        db.EmailOutbox
            .Where(e => e.Id == id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(e => e.Status, QueuedEmailStatus.Failed)
                    .SetProperty(e => e.Attempts, attempts)
                    .SetProperty(e => e.FinishedAt, at)
                    .SetProperty(e => e.LastError, error)
                    .SetProperty(e => e.LockedUntil, (DateTimeOffset?)null),
                cancellationToken);

    public async Task ReleaseAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken)
    {
        if (ids.Count > 0)
        {
            await db.EmailOutbox
                .Where(e => ids.Contains(e.Id) && e.Status == QueuedEmailStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.LockedUntil, (DateTimeOffset?)null), cancellationToken);
        }
    }

    private sealed class HeldLock(AsyncServiceScope scope, DbConnection connection, DbTransaction transaction) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => new(LetGoAsync(scope, connection, transaction));

        /// <summary>Ends the transaction, which lets go of the lock, and closes the connection it was on.</summary>
        public static async Task LetGoAsync(AsyncServiceScope scope, DbConnection connection, DbTransaction? transaction)
        {
            try
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync();
                    await transaction.DisposeAsync();
                }
            }
            catch (DbException)
            {
                // The connection's already gone, and the lock with it.
            }
            finally
            {
                await connection.CloseAsync();
                await scope.DisposeAsync();
            }
        }
    }
}
