using System.Globalization;
using Aspire.Hosting.Azure;
using Azure.Provisioning.AppContainers;
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

        // Azure's monthly patching restarts the server for a few minutes (#158). Without a window of our own it can land on
        // any night between 11 PM and 7 AM, midnight's reveal included. Sundays from 08:00 UTC is 3 AM Eastern in winter
        // (4 AM in summer): hours after the reveal, and before the morning's emails and announcements. Day 0 is Sunday.
        server.MaintenanceWindow = new PostgreSqlFlexibleServerMaintenanceWindow
        {
            CustomWindow = "Enabled",
            DayOfWeek = 0,
            StartHour = 8,
            StartMinute = 0,
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

// The web app's replicas in production (#158). The minimum is Web:MinReplicas in appsettings.json: 1 most of the year (a
// managed certificate needs the app up to renew), and 2 from December 1 to 26, so one replica restarting never takes the site
// down (the runbook's December and After Advent sections say when to change it). The maximum stays at 3, because the
// database's connections are shared out between them (connectionsPerReplica).
const int maxReplicas = 3;
var minReplicas = builder.Configuration.GetValue("Web:MinReplicas", 1);
if (minReplicas is < 1 or > maxReplicas)
{
    throw new InvalidOperationException($"Web:MinReplicas must be from 1 to {maxReplicas}, not {minReplicas}.");
}

// Each replica's database connection pool stops at 8 (Npgsql's default is 100), counting everything the web app opens: its
// DbContexts, so the health check, data protection and the email queue's advisory lock too. The server ran out of connections
// on October 24-25 (#158). Burstable B1ms allows 50 and keeps 15 for Azure, leaving 35. At most: 3 busy replicas, 24 (during
// a deploy, the old revision's); the new revision's replicas, which start at the minimum and only run their background jobs
// until they take traffic, about 3 each; and the migrations job's 1. That's 31. Change it only with the server's Sku or
// maxReplicas, and keep the connections alert in step.
const int connectionsPerReplica = 8;

var web = builder.AddProject<Projects.CSAdvent_Web>("web", launchProfileName: isTesting ? "http" : "https")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(database)
    .WaitFor(database)
    .PublishAsAzureContainerApp((_, app) =>
    {
        app.Template.Scale.MinReplicas = minReplicas;
        app.Template.Scale.MaxReplicas = maxReplicas;

        // Plain http reaches the app, which sends it to https://csadvent.christmas in one permanent redirect (#165). Left to
        // the ingress, http://www. took two: to https://www., then to the canonical host.
        app.Configuration.Ingress.AllowInsecure = true;

        // A new revision only takes traffic once it's started and can reach the database (startup and readiness ask
        // /health, every check), and a replica that's stopped answering is restarted (liveness asks /alive, only the app
        // itself, so a database outage doesn't restart every replica). Port 8080 is the container's HTTP_PORTS.
        var container = app.Template.Containers.Single().Value!;
        container.Probes.Add(Probe(ContainerAppProbeType.Startup, "/health", periodSeconds: 10, failureThreshold: 10));
        container.Probes.Add(Probe(ContainerAppProbeType.Readiness, "/health", periodSeconds: 10, failureThreshold: 3));
        container.Probes.Add(Probe(ContainerAppProbeType.Liveness, "/alive", periodSeconds: 30, failureThreshold: 3));

        foreach (var (name, certificate) in customDomains)
        {
            app.ConfigureCustomDomain(name, certificate);
        }
    });

if (isPublish)
{
    // Telemetry goes to Application Insights in production. Never added locally, where it would provision Azure resources.
    var appInsights = builder.AddAzureApplicationInsights("appinsights");
    web.WithReference(appInsights);

    // Caps each replica's connection pool (connectionsPerReplica, above). Locally and in the tests the pool keeps its default.
    web.WithEnvironment("Database__MaxPoolSize", connectionsPerReplica.ToString(CultureInfo.InvariantCulture));

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

    // Alerts (#139): errors in the logs, the database short of storage, CPU credits or connections, and Azure's own outages,
    // emailed to the organizers by Azure Monitor. Only with somewhere to send them (CI's ALERT_EMAILS), so a missing value
    // never breaks a deploy. A secret, because the addresses are personal and a variable would show in the deploy's log.
    if (builder.Configuration["Parameters:alert-emails"] is { Length: > 0 })
    {
        builder.AddBicepTemplate("alerts", "infra/alerts.bicep")
            .WithParameter("alertEmails", builder.AddParameter("alert-emails", secret: true))
            .WithParameter("appInsightsId", appInsights.GetOutput("id"))
            .WithParameter("postgresId", postgres.GetOutput("id"));
    }
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

// Automatic social posting (#61), and the Anthropic API key the blurb check (#128) asks Claude with. Each is optional: a
// platform without credentials stays copy and paste, and without the key posts aren't checked. Only published, so nothing
// run locally can post to C# Advent's real accounts or spend on the API (development uses dry-run modes instead).
if (isPublish)
{
    foreach (var (parameter, setting, secret) in new[]
    {
        ("mastodon-access-token", "SocialPosting__Mastodon__AccessToken", true),
        ("bluesky-app-password", "SocialPosting__Bluesky__AppPassword", true),
        ("facebook-page-id", "SocialPosting__Facebook__PageId", false),
        ("facebook-page-token", "SocialPosting__Facebook__PageToken", true),
        ("x-api-key", "SocialPosting__X__ApiKey", true),
        ("x-api-key-secret", "SocialPosting__X__ApiKeySecret", true),
        ("x-access-token", "SocialPosting__X__AccessToken", true),
        ("x-access-token-secret", "SocialPosting__X__AccessTokenSecret", true),
        ("anthropic-api-key", "Blurbs__ApiKey", true),
    })
    {
        if (builder.Configuration[$"Parameters:{parameter}"] is { Length: > 0 })
        {
            web.WithEnvironment(setting, builder.AddParameter(parameter, secret: secret));
        }
    }
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

    // RetentionTests and ReminderTests run these with a fake clock. The real-time background runs would race them. The email
    // queue's worker stays on, unlike these: it's what delivers the tests' emails to MailPit (its generous limits are in
    // the web app's appsettings.Development.json).
    web.WithEnvironment("Retention__DeleteInBackground", "false");
    web.WithEnvironment("Reminders__SendInBackground", "false");
    web.WithEnvironment("SocialPosting__PostInBackground", "false");

    // Every test signs up from the same address, so the announcements form's rate limit would trip. The tests deliver
    // announcements themselves, through the development endpoint.
    web.WithEnvironment("Announcements__SubscribeLimit", "1000");
    web.WithEnvironment("Announcements__SendInBackground", "false");

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
    // itself, Aspire picks runtime:10.0. Pinned by digest (#180) to the runtime of global.json's SDK, so a deploy never picks
    // up a different image by itself (the tag is only a label). Update both with the .NET 11 GA work (#55): the digest is
    // the Docker-Content-Digest of https://mcr.microsoft.com/v2/dotnet/aspnet/manifests/<tag>, asking for a manifest list.
    .PublishAsMigrationBundle(
        publishContainer: true,
        baseImage: "mcr.microsoft.com/dotnet/aspnet:11.0.0-rc.2@sha256:9a52cade70623cb66cad79baeea5ae9995b42b38d169ccc2a95b4f6b041e9874")
    .PublishAsAzureContainerAppJob();

web.WaitForCompletion(migrations);

builder.Build().Run();

// An HTTP probe on the web container. Container Apps allows at most 10 failures, so startup gets 10 tries 10 seconds apart.
static ContainerAppProbe Probe(ContainerAppProbeType type, string path, int periodSeconds, int failureThreshold) => new()
{
    ProbeType = type,
    HttpGet = new ContainerAppHttpRequestInfo { Path = path, Port = 8080 },
    PeriodSeconds = periodSeconds,
    FailureThreshold = failureThreshold,
    TimeoutSeconds = 5,
};

/// <summary>A custom domain for the site, and the name of its managed certificate once Azure has issued one.</summary>
internal sealed record CustomDomain(string Name, string? Certificate);
