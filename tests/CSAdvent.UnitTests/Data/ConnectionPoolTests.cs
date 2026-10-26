using CSAdvent.Web.Data;
using Npgsql;

namespace CSAdvent.UnitTests.Data;

public sealed class ConnectionPoolTests
{
    // The form Aspire gives the web app in production, with the password authentication the AppHost sets up.
    private const string AzureConnectionString =
        "Host=postgres-abc.postgres.database.azure.com;Username=admin;Password=secret;Database=csadvent;SSL Mode=Require";

    [Fact]
    public void SetsTheMaximumPoolSize()
    {
        var limited = new NpgsqlConnectionStringBuilder(ConnectionPool.WithMaxPoolSize(AzureConnectionString, 8));

        Assert.Equal(8, limited.MaxPoolSize);
        Assert.Equal("postgres-abc.postgres.database.azure.com", limited.Host);
        Assert.Equal("admin", limited.Username);
        Assert.Equal("secret", limited.Password);
        Assert.Equal("csadvent", limited.Database);
        Assert.Equal(SslMode.Require, limited.SslMode);
    }

    [Fact]
    public void ReplacesAPoolSizeAlreadyInTheConnectionString()
    {
        var limited = ConnectionPool.WithMaxPoolSize(AzureConnectionString + ";Maximum Pool Size=100", 8);

        Assert.Equal(8, new NpgsqlConnectionStringBuilder(limited).MaxPoolSize);
    }

    [Fact]
    public void LeavesTheConnectionStringAloneWithoutASize() =>
        Assert.Equal(AzureConnectionString, ConnectionPool.WithMaxPoolSize(AzureConnectionString, null));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void LeavesAMissingConnectionStringForAspireToReport(string? connectionString) =>
        Assert.Equal(connectionString, ConnectionPool.WithMaxPoolSize(connectionString, 8));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RefusesAPoolWithNoConnections(int maxPoolSize) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ConnectionPool.WithMaxPoolSize(AzureConnectionString, maxPoolSize));
}
