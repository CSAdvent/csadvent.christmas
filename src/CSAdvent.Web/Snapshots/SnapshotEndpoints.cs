namespace CSAdvent.Web.Snapshots;

public static class SnapshotEndpoints
{
    public static WebApplication MapSnapshots(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            // Development only: runs the snapshots as if it were the given time. With the stand-in, nothing reaches the
            // Internet Archive.
            app.MapPost("/dev/snapshots/run", async (DateTimeOffset at, int? year, SnapshotService snapshots, CancellationToken cancellationToken) =>
                Results.Ok(await snapshots.RunAsync(at.ToUniversalTime(), year, cancellationToken)));
        }

        return app;
    }
}
