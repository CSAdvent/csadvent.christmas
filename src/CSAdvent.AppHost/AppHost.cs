using Aspire.Hosting.Azure;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Integration tests run the AppHost in the Testing environment: a throwaway database, no developer tools,
// and plain HTTP so they don't depend on a trusted development certificate.
var isTesting = builder.Environment.IsEnvironment("Testing");
var isPublish = builder.ExecutionContext.IsPublishMode;

// The site's domain. In production its DNS zone and email are part of the deploy too.
const string domainName = "csadvent.christmas";

// Production runs on Azure Container Apps. Locally (run mode) this isn't part of the app model.
var containerApps = builder.AddAzureContainerAppEnvironment("aca");

// Azure Database for PostgreSQL in production; locally the same "postgres" container as always (same user, password
// parameter and data volume).
var postgres = builder.AddAzurePostgresFlexibleServer("postgres")
    .ConfigureInfrastructure(infra =>
    {
        var server = infra.GetProvisionableResources().OfType<PostgreSqlFlexibleServer>().Single();

        // PostgreSQL 18, to match the local container. The default API version only allows up to 16.
        server.ResourceVersion = "2025-08-01";
        server.Version = new StringLiteralExpression("18");
        server.Sku = new PostgreSqlFlexibleServerSku { Name = "Standard_B1ms", Tier = PostgreSqlFlexibleServerSkuTier.Burstable };
        server.StorageSizeInGB = 32;
        server.Backup = new PostgreSqlFlexibleServerBackupProperties
        {
            // Every season since 2017 lives here. Geo-redundant backup can only be chosen when the server is created.
            BackupRetentionDays = 14,
            GeoRedundantBackup = PostgreSqlFlexibleServerGeoRedundantBackupEnum.Disabled,
        };
    })
    .RunAsContainer(container =>
    {
        if (!isTesting)
        {
            container.WithDataVolume().WithPgWeb();
        }
    });

if (isPublish)
{
    // Password authentication, so the EF migration bundle can connect with a plain connection string (Entra ID tokens
    // don't survive its --connection argument). The connection strings go into a Key Vault that Aspire adds. Publish
    // mode only: locally it would change the container's user and lock out the existing data volume.
    postgres.WithPasswordAuthentication(
        builder.AddParameter("pg-admin-username"),
        builder.AddParameter("pg-admin-password", secret: true));
}

var database = postgres.AddDatabase("csadvent");

// The site's custom domains (CustomDomains in appsettings.json). Azure only accepts a domain once DNS points at the app, and
// only issues its managed certificate after that, so each domain goes in in two steps (see the README): its name first,
// then its certificate's name. Kept in configuration rather than set by hand, because a deploy replaces whatever isn't in
// the model.
var customDomains = (builder.Configuration.GetSection("CustomDomains").Get<CustomDomain[]>() ?? [])
    .Select((domain, index) => (
        Name: builder.AddParameter($"custom-domain-{index}", domain.Name, publishValueAsDefault: true),
        Certificate: builder.AddParameter($"custom-domain-{index}-certificate", domain.Certificate ?? "", publishValueAsDefault: true)))
    .ToList();

var web = builder.AddProject<Projects.CSAdvent_Web>("web", launchProfileName: isTesting ? "http" : "https")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(database)
    .WaitFor(database)
    .PublishAsAzureContainerApp((_, app) =>
    {
        // Always one replica running (a managed certificate needs the app up to renew), and room to scale in December.
        app.Template.Scale.MinReplicas = 1;
        app.Template.Scale.MaxReplicas = 3;

        foreach (var (name, certificate) in customDomains)
        {
            app.ConfigureCustomDomain(name, certificate);
        }
    });

if (isPublish)
{
    // Telemetry goes to Application Insights in production. Never added locally, where it would provision Azure resources.
    web.WithReference(builder.AddAzureApplicationInsights("appinsights"));

    // Email goes through Azure Communication Services. The web app sends with its own managed identity, so there's no key.
    var webIdentity = builder.AddAzureUserAssignedIdentity("web-identity");
    web.WithAzureUserAssignedIdentity(webIdentity);

    // The sending domain is connected by the first deploy after it's verified in DNS (see the README).
    var acs = builder.AddBicepTemplate("acs", "infra/acs.bicep")
        .WithParameter("domainName", domainName)
        .WithParameter("webPrincipalId", webIdentity.Resource.PrincipalId);
    web.WithReference(builder.AddConnectionString("email", ReferenceExpression.Create($"Endpoint={acs.GetOutput("endpoint")}")));

    // The DNS zone, with the site's records and the email records. Hover is switched to its name servers at cutover.
    builder.AddBicepTemplate("dns", "infra/dns.bicep")
        .WithParameter("domainName", domainName)
        .WithParameter("containerAppsEnvironmentName", containerApps.GetOutput("AZURE_CONTAINER_APPS_ENVIRONMENT_NAME"))
        .WithParameter("webAppName", web.Resource.Name)
        .WithParameter("emailDomainVerification", acs.GetOutput("domainVerification"))
        .WithParameter("emailSpf", acs.GetOutput("spf"))
        .WithParameter("emailDkim", acs.GetOutput("dkim"))
        .WithParameter("emailDkim2", acs.GetOutput("dkim2"));
}
else
{
    // Catches every email sent in development and in the tests, so nothing reaches real people.
    web.WithReference(builder.AddMailPit("email"));
}

// GitHub sign-in. Locally it's optional, because dev sign-in works without it; in production both values are required.
if (isPublish || builder.Configuration["Parameters:github-client-id"] is not null)
{
    web.WithEnvironment("Accounts__GitHub__ClientId", builder.AddParameter("github-client-id"))
        .WithEnvironment("Accounts__GitHub__ClientSecret", builder.AddParameter("github-client-secret", secret: true));
}

if (builder.ExecutionContext.IsRunMode)
{
    // Lets developers and the integration tests sign in as anyone without GitHub. The web app only honors it in Development.
    web.WithEnvironment("Accounts__DevSignIn", "true");
}

if (isTesting)
{
    // The organizer the integration tests sign in as (AppHostFixture.OrganizerGitHubId).
    web.WithEnvironment("Accounts__Admins__0", "9001");

    // RetentionTests and ReminderTests run these with a fake clock. The real-time background runs would race them.
    web.WithEnvironment("Retention__DeleteInBackground", "false");
    web.WithEnvironment("Reminders__SendInBackground", "false");

    // The tests never reach out to the Other Advents calendars' real sites.
    web.WithEnvironment("OtherAdvents__FetchLogosInBackground", "false");
}
else
{
    // Container Apps terminates TLS, so trust its X-Forwarded-* headers to keep sign-in redirects on https.
    web.WithEnvironment("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "true");
}

// Applies EF Core migrations, and seeds the 2017-2025 history, before the web app starts. Locally that's `dotnet ef`; in
// production a migration bundle runs as a manually started Container Apps job (`aspire deploy` doesn't start it; see the
// README). Keep the tool version in step with dotnet-tools.json.
var migrations = web.AddEFMigrations(
        "migrations",
        "CSAdvent.Web.Data.AdventDbContext",
        configureToolResource: tool => tool.WithToolVersion("11.0.0-rc.2.26504.105"))
    .WithMigrationOutputDirectory("Data/Migrations")
    .WithMigrationNamespace("CSAdvent.Web.Data.Migrations")
    .WithReference(database)
    .WaitFor(database)
    .RunDatabaseUpdateOnStart()
    // The bundle is framework-dependent and loads the web app's assembly, so it needs .NET 11 with ASP.NET Core. Left to
    // itself, Aspire picks runtime:10.0. Update the tag with the rest of the .NET 11 GA work (#55).
    .PublishAsMigrationBundle(publishContainer: true, baseImage: "mcr.microsoft.com/dotnet/aspnet:11.0")
    .PublishAsAzureContainerAppJob();

web.WaitForCompletion(migrations);

builder.Build().Run();

/// <summary>A custom domain for the site, and the name of its managed certificate once Azure has issued one.</summary>
internal sealed record CustomDomain(string Name, string? Certificate);
