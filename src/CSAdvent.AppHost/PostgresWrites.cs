using Azure.Provisioning.PostgreSql;
using Azure.Provisioning.Primitives;

namespace CSAdvent.AppHost;

/// <summary>
/// A PostgreSQL flexible server takes one operation at a time, and refuses to change a setting while another is running
/// (<c>ServerIsBusy</c>). Azure Resource Manager writes a server's children all at once unless one depends on another, so a
/// deploy whose setting met the database's write failed (on November 13, 2026, after racing every deploy before it). So a
/// setting waits for the server's databases and firewall rules.
/// </summary>
internal static class PostgresWrites
{
    /// <summary>Makes <paramref name="setting"/> wait for every database and firewall rule in <paramref name="resources"/>.</summary>
    public static PostgreSqlFlexibleServerConfiguration AfterTheOtherChildren(this PostgreSqlFlexibleServerConfiguration setting, IEnumerable<Provisionable> resources)
    {
        foreach (var child in resources.OfType<ProvisionableResource>().Where(r => r is PostgreSqlFlexibleServerDatabase or PostgreSqlFlexibleServerFirewallRule))
        {
            setting.DependsOn.Add(child);
        }

        return setting;
    }
}
