using Bunit;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Components.Manage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CSAdvent.UnitTests.Manage;

public sealed class LocalTimeTests : BunitContext
{
    public LocalTimeTests() =>
        Services.AddSingleton(new AdventSchedule(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), TimeProvider.System));

    [Fact]
    public void SaysTheTimeIsEastern()
    {
        var time = Render<LocalTime>(p => p.Add(c => c.Value, new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero)));

        Assert.Equal("Nov 1, 2026, 12:00 AM Eastern", time.Find("time").TextContent);
        Assert.Equal("2026-11-01T04:00:00.0000000+00:00", time.Find("time").GetAttribute("datetime"));
    }
}
