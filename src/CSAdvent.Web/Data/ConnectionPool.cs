using Npgsql;

namespace CSAdvent.Web.Data;

/// <summary>
/// Caps the database connection pool (#158). Npgsql allows 100 connections per pool, and the production server a few hundred
/// (Burstable B2s: 414 for us), shared by every replica, the old revision during a deploy, and the migrations job. So in production
/// the AppHost sets <c>Database:MaxPoolSize</c>, and every connection the app opens (its DbContexts, and through them the
/// health check, data protection and the email queue's advisory lock) shares that one pool. Locally and in the tests it isn't
/// set, and the connection string is left as it is.
/// </summary>
internal static class ConnectionPool
{
    /// <summary>The setting with the most connections one replica may open.</summary>
    public const string MaxPoolSizeSetting = "Database:MaxPoolSize";

    /// <summary>
    /// <paramref name="connectionString"/> with Npgsql's <c>Maximum Pool Size</c> set to <paramref name="maxPoolSize"/>, or
    /// unchanged when there's no size (or no connection string, which the Aspire integration reports itself).
    /// </summary>
    public static string? WithMaxPoolSize(string? connectionString, int? maxPoolSize)
    {
        if (string.IsNullOrEmpty(connectionString) || maxPoolSize is not { } size)
        {
            return connectionString;
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1, nameof(maxPoolSize));
        return new NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = size }.ConnectionString;
    }
}
