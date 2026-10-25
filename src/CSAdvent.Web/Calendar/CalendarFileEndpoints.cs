using System.Globalization;
using System.Text;

namespace CSAdvent.Web.Calendar;

public static class CalendarFileEndpoints
{
    public const string ContentType = "text/calendar; charset=utf-8";

    public static WebApplication MapCalendarFiles(this WebApplication app)
    {
        // Add-to-calendar files (#207), linked from the page between seasons and the announcements page.
        app.MapGet("/calendar/{year:int}/sign-ups.ics", async (int year, CalendarFileService files, HttpResponse response, CancellationToken cancellationToken) =>
            Respond(response, year, "sign-ups", await files.SignUpsAsync(year, cancellationToken)));

        app.MapGet("/calendar/{year:int}/starts.ics", async (int year, CalendarFileService files, HttpResponse response, CancellationToken cancellationToken) =>
            Respond(response, year, "starts", await files.StartsAsync(year, cancellationToken)));

        return app;
    }

    private static IResult Respond(HttpResponse response, int year, string name, string? calendar)
    {
        if (calendar is null)
        {
            return Results.NotFound();
        }

        response.Headers.CacheControl = "public, max-age=3600";
        return Results.File(Encoding.UTF8.GetBytes(calendar), ContentType, string.Create(CultureInfo.InvariantCulture, $"csadvent-{year}-{name}.ics"));
    }
}
