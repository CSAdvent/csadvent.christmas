using System.Collections.Concurrent;
using CSAdvent.Web.Auth;
using CSAdvent.Web.Backups;
using CSAdvent.Web.Blurbs;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Content;
using CSAdvent.Web.Contributors;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using CSAdvent.Web.Entries;
using CSAdvent.Web.SeasonTesting;
using CSAdvent.Web.Snapshots;
using CSAdvent.Web.Social.Posting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Services on a fake clock against the test database, for what pages can't show on the real one, long before the test
/// seasons: a season's own December. Emails are kept in <see cref="Sent"/> instead of queued.
/// </summary>
internal sealed class ServicesOnAClock(AdventDbContext db, DateTimeOffset now)
{
    private static readonly IOptions<SiteOptions> Site = Options.Create(new SiteOptions { ContactEmail = "hello@csadvent.christmas", BaseUrl = "https://localhost" });

    // As in appsettings.json: post 1 is announced at 10 AM, post 2 at 2 PM.
    private static readonly IOptions<SocialPostingOptions> Posting = Options.Create(new SocialPostingOptions { PostAt = [new(10, 0), new(14, 0)] });

    // In UTC, as the system clock gives it: Npgsql only writes UTC times.
    public FakeTimeProvider Clock { get; } = new(now.ToUniversalTime());

    public RecordedEmails Sent { get; } = new();

    public AdventSchedule Schedule => new(Options.Create(new AdventOptions { TimeZone = "America/New_York" }), Clock);

    public PostLinkService PostLinks() => new(db, Schedule, Clock, Composer, Site);

    /// <summary>Backups that can't check a blurb, which nothing here needs.</summary>
    public BackupService Backups() =>
        new(db, Schedule, Composer, null!, Options.Create(new BackupOptions()), Posting, Site, NullLogger<BackupService>.Instance);

    /// <summary>Entry pages and the redirect to each post, for nobody signed in (no request, so the test season is hidden).</summary>
    public EntryService Entries() => new(
        db,
        Schedule,
        Clock,
        new SeasonAccess(new CSAdvent.Web.SeasonTesting.TestSeasons(EmptyServices), new HttpContextAccessor(), new OptionsMonitor<AccountOptions>(new OptionsFactory<AccountOptions>([], []), [], new OptionsCache<AccountOptions>())),
        NullLogger<EntryService>.Instance);

    private static IServiceScopeFactory EmptyServices => new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

    /// <summary>The Wayback Machine snapshots, on the stand-in archive.</summary>
    public SnapshotService Snapshots() =>
        new(db, Schedule, new SampleSnapshotSaver(), Options.Create(new SnapshotOptions { SamplePages = true }), NullLogger<SnapshotService>.Instance);

    public MySpotsService MySpots() =>
        new(db, Schedule, Clock, Composer, Backups(), PostLinks(), Site, Posting, Options.Create(new BlurbOptions()));

    private EmailComposer Composer => new(
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        NullLoggerFactory.Instance,
        Site,
        new EmailOptOutLinks(new EphemeralDataProtectionProvider(), Site),
        Sent);
}

internal sealed class RecordedEmails : IEmailSender
{
    private readonly ConcurrentQueue<OutgoingEmail> _sent = new();

    public IReadOnlyList<OutgoingEmail> All => [.. _sent];

    public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(email);
        return Task.CompletedTask;
    }
}
