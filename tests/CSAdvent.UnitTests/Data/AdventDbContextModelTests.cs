using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.UnitTests.Data;

public sealed class AdventDbContextModelTests
{
    // Building the model never opens a connection, so no database is needed.
    private static AdventDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AdventDbContext>().UseNpgsql("Host=localhost;Database=csadvent").Options);

    [Fact]
    public void MigrationsMatchTheModel()
    {
        using var context = CreateContext();

        Assert.False(
            context.Database.HasPendingModelChanges(),
            "The model has changes that no migration covers. Add one with `dotnet ef migrations add <Name>`.");
    }

    [Fact]
    public void EveryDatabaseNameIsSnakeCase()
    {
        using var context = CreateContext();

        var names = context.Model.GetEntityTypes().SelectMany(entityType =>
            new[] { entityType.GetTableName()! }
                .Concat(entityType.GetProperties().Select(p => p.GetColumnName()))
                .Concat(entityType.GetKeys().Select(k => k.GetName()!))
                .Concat(entityType.GetForeignKeys().Select(fk => fk.GetConstraintName()!))
                .Concat(entityType.GetIndexes().Select(i => i.GetDatabaseName()!)));

        Assert.All(names, name => Assert.Matches("^[a-z][a-z0-9_]*$", name));
    }

    [Fact]
    public void SlotsHoldNoContactDetails()
    {
        using var context = CreateContext();

        var columns = context.Model.FindEntityType(typeof(Slot))!.GetProperties().Select(p => p.GetColumnName());

        Assert.DoesNotContain(columns, column => column.Contains("email", StringComparison.Ordinal));
    }
}
