using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CSAdvent.UnitTests.Data;

/// <summary>The errors the catch filters expect from SaveChanges (#183): only the one they name, as SaveChanges wraps it.</summary>
public sealed class DatabaseErrorsTests
{
    [Fact]
    public void AUniqueViolationIsRecognized()
    {
        Assert.True(Saving(PostgresErrorCodes.UniqueViolation).IsUniqueViolation());
        Assert.False(Saving(PostgresErrorCodes.UniqueViolation).IsForeignKeyViolation());
    }

    [Fact]
    public void AForeignKeyViolationIsRecognized()
    {
        Assert.True(Saving(PostgresErrorCodes.ForeignKeyViolation).IsForeignKeyViolation());
        Assert.False(Saving(PostgresErrorCodes.ForeignKeyViolation).IsUniqueViolation());
    }

    [Fact]
    public void OtherFailuresAreNeither()
    {
        DbUpdateException[] others =
        [
            Saving(PostgresErrorCodes.CheckViolation),
            new DbUpdateConcurrencyException("Changed by someone else."),
            new DbUpdateException("Saving failed.", new TimeoutException()),
        ];

        Assert.All(others, exception => Assert.False(exception.IsUniqueViolation() || exception.IsForeignKeyViolation()));
    }

    private static DbUpdateException Saving(string sqlState) =>
        new("Saving failed.", new PostgresException("violates a constraint", "ERROR", "ERROR", sqlState));
}
