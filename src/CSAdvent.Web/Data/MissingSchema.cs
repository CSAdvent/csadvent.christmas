using Npgsql;

namespace CSAdvent.Web.Data;

/// <summary>
/// Recognizes PostgreSQL saying a table or column isn't there (SqlState 42P01 or 42703). A deploy starts the new revision
/// before its migrations job runs (#151), so for a few minutes the new code can ask for a table or column the migrations
/// haven't added yet. Background jobs take that as "not migrated yet": a warning, and they try again on their next run
/// (#225). The <see cref="PostgresException"/> can be wrapped (SaveChanges wraps it in a <c>DbUpdateException</c>, say), so
/// every inner exception is searched.
/// </summary>
public static class MissingSchema
{
    /// <summary>Whether the exception, or one inside it, is a missing table or column: the database isn't migrated to this version yet.</summary>
    public static bool IsMissingTableOrColumn(Exception exception) =>
        Contains(exception, e => e.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn);

    /// <summary>
    /// Whether the exception, or one inside it, is a missing table. For code that can take a table that isn't there yet as
    /// empty, where a missing column wouldn't mean that: nobody can have unsubscribed before the do-not-email list existed,
    /// but they can have before one of its columns did.
    /// </summary>
    public static bool IsMissingTable(Exception exception) =>
        Contains(exception, e => e.SqlState is PostgresErrorCodes.UndefinedTable);

    private static bool Contains(Exception? exception, Func<PostgresException, bool> matches) => exception switch
    {
        null => false,
        PostgresException postgres when matches(postgres) => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(inner => Contains(inner, matches)),
        _ => Contains(exception.InnerException, matches),
    };
}
