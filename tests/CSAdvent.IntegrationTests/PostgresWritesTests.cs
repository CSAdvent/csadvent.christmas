using System.Net;
using Azure.Provisioning;
using Azure.Provisioning.PostgreSql;
using CSAdvent.AppHost;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// The order the deploy writes the database server's children in (<see cref="PostgresWrites"/>): a setting after the database
/// and firewall rule, never alongside them. Nothing here needs the AppHost or Docker.
/// </summary>
public sealed class PostgresWritesTests
{
    [Fact]
    public void ASettingIsWrittenAfterTheDatabaseAndTheFirewallRule()
    {
        var infra = new Infrastructure("postgres");
        var server = new PostgreSqlFlexibleServer("postgres");
        infra.Add(server);
        infra.Add(new PostgreSqlFlexibleServerFirewallRule("firewall") { Parent = server, Name = "AllowAllAzureIps", StartIPAddress = IPAddress.Any, EndIPAddress = IPAddress.Any });
        infra.Add(new PostgreSqlFlexibleServerDatabase("csadvent") { Parent = server, Name = "csadvent" });

        infra.Add(new PostgreSqlFlexibleServerConfiguration("maxConnections") { Parent = server, Name = "max_connections", Value = "429" }
            .AfterTheOtherChildren(infra.GetProvisionableResources()));

        var bicep = infra.Build().Compile().Values.Single().ReplaceLineEndings("\n");
        var start = bicep.IndexOf("resource maxConnections", StringComparison.Ordinal);
        var setting = bicep[start..(bicep.IndexOf("\n}", start, StringComparison.Ordinal) + 2)];
        Assert.EndsWith("  parent: postgres\n  dependsOn: [\n    firewall\n    csadvent\n  ]\n}", setting);
    }
}
