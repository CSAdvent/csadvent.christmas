using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Integration tests run the AppHost in the Testing environment: a throwaway database, no developer tools,
// and plain HTTP so they don't depend on a trusted development certificate.
var isTesting = builder.Environment.IsEnvironment("Testing");

var postgres = builder.AddPostgres("postgres");

if (!isTesting)
{
    postgres
        .WithDataVolume()
        .WithPgWeb();
}

var database = postgres.AddDatabase("csadvent");

// Catches every email sent in development, so nothing reaches real people.
var mailpit = builder.AddMailPit("mailpit");

var web = builder.AddProject<Projects.CSAdvent_Web>("web", launchProfileName: isTesting ? "http" : "https")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(database)
    .WithReference(mailpit)
    .WaitFor(database);

// Applies EF Core migrations with `dotnet ef` before the web app starts. Publishing produces a migration bundle instead.
// Keep the tool version in step with dotnet-tools.json.
var migrations = web.AddEFMigrations(
        "migrations",
        "CSAdvent.Web.Data.AdventDbContext",
        configureToolResource: tool => tool.WithToolVersion("11.0.0-rc.2.26504.105"))
    .WithMigrationOutputDirectory("Data/Migrations")
    .WithMigrationNamespace("CSAdvent.Web.Data.Migrations")
    .WithReference(database)
    .WaitFor(database)
    .RunDatabaseUpdateOnStart()
    .PublishAsMigrationBundle(publishContainer: true);

web.WaitForCompletion(migrations);

builder.Build().Run();
