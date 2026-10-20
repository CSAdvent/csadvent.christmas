using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace CSAdvent.Web.Data;

/// <summary>
/// Names every table, column, key, foreign key and index in snake_case, the PostgreSQL convention, so hand-written SQL
/// never needs quoted identifiers. EFCore.NamingConventions does the same, but has no EF Core 11 release yet.
/// </summary>
internal sealed partial class SnakeCaseNamingConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            if (entityType.GetTableName() is { } tableName)
            {
                entityType.SetTableName(ToSnakeCase(tableName));
            }

            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));
            }

            // Default key, foreign key and index names are derived from the table and column names renamed above.
            foreach (var key in entityType.GetKeys())
            {
                if (key.GetName() is { } keyName)
                {
                    key.SetName(ToSnakeCase(keyName));
                }
            }

            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                if (foreignKey.GetConstraintName() is { } constraintName)
                {
                    foreignKey.SetConstraintName(ToSnakeCase(constraintName));
                }
            }

            foreach (var index in entityType.GetIndexes())
            {
                if (index.GetDatabaseName() is { } indexName)
                {
                    index.SetDatabaseName(ToSnakeCase(indexName));
                }
            }
        }
    }

    public static string ToSnakeCase(string name) => WordBoundary().Replace(name, "_").ToLowerInvariant();

    // Between a lowercase letter or digit and an uppercase letter ("postUrl"), or at the end of an acronym ("HTMLPage").
    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")]
    private static partial Regex WordBoundary();
}
