namespace CSAdvent.Web.Backups;

public static class BackupEndpoints
{
    public static WebApplication MapBackups(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            // Development only: moves the backup asks along as if it were the given time, so the flow can be tried (and
            // tested) without waiting for December. Everything goes to MailPit.
            app.MapPost("/dev/backups/run", async (DateTimeOffset at, int? year, BackupService backups, CancellationToken cancellationToken) =>
                Results.Ok(await backups.RunAsync(at.ToUniversalTime(), year, cancellationToken)));
        }

        return app;
    }
}
