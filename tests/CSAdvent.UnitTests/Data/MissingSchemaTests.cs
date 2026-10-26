using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CSAdvent.UnitTests.Data;

/// <summary>
/// The errors that mean the deploy's migrations haven't run yet (#225): a missing table or column, however deep it's wrapped.
/// Anything else is a real error, and stays one.
/// </summary>
public sealed class MissingSchemaTests
{
    private static readonly PostgresException MissingTable = Postgres(PostgresErrorCodes.UndefinedTable, "relation \"email_outbox\" does not exist");
    private static readonly PostgresException MissingColumn = Postgres(PostgresErrorCodes.UndefinedColumn, "column s.post_summary does not exist");

    [Fact]
    public void AMissingTableOrColumnIsNotMigratedYet()
    {
        Assert.True(MissingSchema.IsMissingTableOrColumn(MissingTable));
        Assert.True(MissingSchema.IsMissingTableOrColumn(MissingColumn));
    }

    [Fact]
    public void ItsFoundWhereverItsWrapped()
    {
        // SaveChanges wraps it once; a caller can wrap that again; Task.WhenAll gathers several.
        Assert.True(MissingSchema.IsMissingTableOrColumn(new DbUpdateException("Saving failed.", MissingColumn)));
        Assert.True(MissingSchema.IsMissingTableOrColumn(new InvalidOperationException("Outer.", new DbUpdateException("Saving failed.", MissingTable))));
        Assert.True(MissingSchema.IsMissingTableOrColumn(new AggregateException(new TimeoutException(), new DbUpdateException("Saving failed.", MissingTable))));
    }

    [Fact]
    public void OtherDatabaseErrorsAreRealErrors()
    {
        Assert.False(MissingSchema.IsMissingTableOrColumn(Postgres(PostgresErrorCodes.UniqueViolation, "duplicate key value violates unique constraint")));
        Assert.False(MissingSchema.IsMissingTableOrColumn(Postgres(PostgresErrorCodes.TooManyConnections, "sorry, too many clients already")));
        Assert.False(MissingSchema.IsMissingTableOrColumn(new DbUpdateException("Saving failed.", Postgres(PostgresErrorCodes.ForeignKeyViolation, "violates foreign key constraint"))));
        Assert.False(MissingSchema.IsMissingTableOrColumn(new TimeoutException()));
        Assert.False(MissingSchema.IsMissingTableOrColumn(new AggregateException()));
    }

    [Fact]
    public void OnlyATableCountsWhereAMissingColumnWouldMeanSomethingElse()
    {
        // The do-not-email list: nobody can have unsubscribed before the table existed, but they can have before a column did.
        Assert.True(MissingSchema.IsMissingTable(new DbUpdateException("Saving failed.", MissingTable)));
        Assert.False(MissingSchema.IsMissingTable(MissingColumn));
        Assert.False(MissingSchema.IsMissingTable(new DbUpdateException("Saving failed.", MissingColumn)));
    }

    private static PostgresException Postgres(string sqlState, string message) => new(message, "ERROR", "ERROR", sqlState);
}
