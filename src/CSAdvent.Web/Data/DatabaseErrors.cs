using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CSAdvent.Web.Data;

/// <summary>
/// Recognizes the PostgreSQL errors the code expects from <c>SaveChanges</c>, so each catch filter says what it means rather
/// than repeat the SqlState test. EF Core still logs the failed command first (#157), so a run that can look first should.
/// </summary>
public static class DatabaseErrors
{
    /// <summary>Whether saving failed on a unique index: another run, replica or request saved the same row first.</summary>
    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    /// <summary>
    /// Whether a statement failed on a unique index. <c>ExecuteUpdate</c> and <c>ExecuteDelete</c> throw PostgreSQL's own
    /// exception, not a <see cref="DbUpdateException"/>.
    /// </summary>
    public static bool IsUniqueViolation(this PostgresException exception) => exception.SqlState == PostgresErrorCodes.UniqueViolation;

    /// <summary>Whether saving failed on a foreign key: the row it refers to was deleted first (an unsubscribe, say).</summary>
    public static bool IsForeignKeyViolation(this DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };
}
