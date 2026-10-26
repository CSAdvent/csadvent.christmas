using System.Threading.RateLimiting;
using CSAdvent.Web.Announcements;
using CSAdvent.Web.Auth;
using CSAdvent.Web.Authors;
using CSAdvent.Web.Backups;
using CSAdvent.Web.Blurbs;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Cards;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Components;
using CSAdvent.Web.Content;
using CSAdvent.Web.Contributors;
using CSAdvent.Web.Data;
using CSAdvent.Web.Data.Archive;
using CSAdvent.Web.DeadLinks;
using CSAdvent.Web.Email;
using CSAdvent.Web.Email.Queue;
using CSAdvent.Web.Entries;
using CSAdvent.Web.Feed;
using CSAdvent.Web.Jobs;
using CSAdvent.Web.Manage;
using CSAdvent.Web.Reminders;
using CSAdvent.Web.Social;
using CSAdvent.Web.Social.Posting;
using CSAdvent.Web.Retention;
using CSAdvent.Web.Retrospectives;
using CSAdvent.Web.SeasonTesting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetry.Instrumentation.Http;

var builder = WebApplication.CreateBuilder(args);
builder.AddSecurityHeaders();

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<AdventDbContext>(
    "csadvent",
    // In production the AppHost caps each replica's connection pool (#158). Every connection the app opens comes from here.
    configureSettings: settings => settings.ConnectionString = ConnectionPool.WithMaxPoolSize(
        settings.ConnectionString, builder.Configuration.GetValue<int?>(ConnectionPool.MaxPoolSizeSetting)),
    configureDbContextOptions: options => options
        // Runs after migrations are applied, and adds any archived season that isn't in the database yet.
        // EF Core's tools and migration bundles use the synchronous delegate.
        .UseSeeding((context, _) => ArchiveSeeder.Seed(context))
        .UseAsyncSeeding((context, _, cancellationToken) => ArchiveSeeder.SeedAsync(context, cancellationToken)));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<AdventOptions>()
    .BindConfiguration(AdventOptions.SectionName)
    .Validate(
        options => TimeZoneInfo.TryFindSystemTimeZoneById(options.TimeZone, out _),
        $"{AdventOptions.SectionName}:TimeZone must be a time zone ID, such as America/New_York.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.TimeZoneName),
        $"{AdventOptions.SectionName}:TimeZoneName must say what emails call the time zone, such as Eastern.")
    .ValidateOnStart();
builder.Services.AddOptions<SiteOptions>()
    .BindConfiguration(SiteOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// When each background job step last succeeded (#172), for /manage, and for /health, which turns Degraded (never
// Unhealthy, since readiness asks it too) when one of this replica's steps hasn't succeeded for too long.
builder.Services.AddSingleton<IJobHeartbeatStore, JobHeartbeatStore>();
builder.Services.AddSingleton<JobRuns>();
builder.Services.AddHealthChecks().AddCheck<JobsHealthCheck>(JobsHealthCheck.Name, failureStatus: HealthStatus.Degraded);

// Seasons being tested follow a rehearsal schedule, and only their testers see what's happening in them.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<TestSeasons>();
builder.Services.AddHostedService<TestSeasonsRefresher>();
builder.Services.AddScoped<SeasonAccess>();
builder.Services.AddScoped<SeasonTestingService>();
builder.Services.AddScoped<TesterInvitations>();
builder.Services.AddSingleton<AdventSchedule>();
builder.Services.AddScoped<ArchiveService>();
builder.Services.AddScoped<HomeService>();
builder.Services.AddScoped<CalendarFileService>();
builder.Services.AddScoped<ClaimService>();
builder.Services.AddOptions<ClaimOptions>()
    .BindConfiguration(ClaimOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddScoped<SeasonAdminService>();
builder.Services.AddScoped<SeasonArchiveExport>();
builder.Services.AddScoped<ClaimReviewService>();
builder.Services.AddScoped<EntryAdminService>();
builder.Services.AddScoped<OrganizerAttention>();
builder.Services.AddScoped<MySpotsService>();
builder.Services.AddScoped<PostClaimService>();
builder.Services.AddScoped<PostLinkService>();
builder.Services.AddScoped<ProfileService>();
builder.Services.AddScoped<EntryService>();
builder.Services.AddScoped<RevealedPosts>();
builder.Services.AddScoped<AuthorPageService>();
builder.Services.AddSingleton<CardRenderer>();
builder.Services.AddScoped<RssFeed>();
builder.Services.AddScoped<RetrospectiveService>();
builder.Services.AddScoped<SocialService>();
builder.Services.AddScoped<ContentService>();
builder.Services.AddScoped<OtherAdventsService>();

// The organizers' photos on the About page, fetched from GitHub by the site rather than by visitors' browsers (#168).
builder.Services.AddSingleton(services => new OrganizerPhotos(
    OrganizerPhotos.CreateClient(), services.GetRequiredService<IOptionsMonitor<SiteOptions>>(), services.GetRequiredService<TimeProvider>()));

// Other Advents logos, fetched from each calendar's site. The fetcher has its own client rather than one from the factory,
// so it skips the default retries: a missing icon is normal, and the worker asks again the next day.
builder.Services.AddSingleton(services => new LogoFetcher(LogoFetcher.CreateClient(), services.GetRequiredService<ILogger<LogoFetcher>>()));
builder.Services.AddScoped<OtherAdventLogoService>();
if (builder.Configuration.GetValue("OtherAdvents:FetchLogosInBackground", defaultValue: true))
{
    builder.Services.AddHostedService<OtherAdventLogoWorker>();
}

// The dead-link check (#201): each finished season's posts, now and then, and the Wayback Machine's copy of any that have gone.
// Both clients are the app's own (public addresses only, no factory retries); development and the tests use stand-ins, so
// they never reach authors' sites or the Internet Archive.
builder.Services.AddOptions<LinkCheckOptions>()
    .BindConfiguration(LinkCheckOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
var sampleLinks = builder.Configuration.GetValue<bool>($"{LinkCheckOptions.SectionName}:{nameof(LinkCheckOptions.SamplePages)}");
builder.Services.AddSingleton<ILinkProber>(services => sampleLinks
    ? new SampleLinkProber()
    : new WebLinkProber(WebLinkProber.CreateClient(), services.GetRequiredService<ILogger<WebLinkProber>>()));
builder.Services.AddSingleton<IArchiveLookup>(services => sampleLinks
    ? new SampleArchiveLookup()
    : new WaybackArchiveLookup(WaybackArchiveLookup.CreateClient(), services.GetRequiredService<ILogger<WaybackArchiveLookup>>()));
builder.Services.AddScoped<LinkCheckService>();
if (builder.Configuration.GetValue("LinkChecks:CheckInBackground", defaultValue: true))
{
    builder.Services.AddHostedService<LinkCheckWorker>();
}
builder.Services.AddOptions<SocialAccountsOptions>()
    .BindConfiguration(SocialAccountsOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Posts each day's announcements on the platforms in Automatic mode (#61). Without credentials a platform stays copy and
// paste, and in development and the tests dry-run mode only logs what it would post.
builder.Services.AddOptions<SocialPostingOptions>()
    .BindConfiguration(SocialPostingOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddKeyedSingleton(SocialPublishers.ClientKey, (_, _) => SocialPublishers.CreateClient());
builder.Services.AddScoped<SocialPublishers>();
builder.Services.AddScoped<SocialPlatformModes>();
builder.Services.AddScoped<SocialPostingAlerts>();
builder.Services.AddScoped<SocialPostingService>();
builder.Services.AddScoped<SocialCredentialCheck>();
builder.Services.AddScoped<SeasonSocialService>();
if (builder.Configuration.GetValue("SocialPosting:PostInBackground", defaultValue: true))
{
    builder.Services.AddHostedService<SocialPostingWorker>();
}
builder.AddEmail();
builder.Services.AddScoped<IClaimNotifier, EmailClaimNotifier>();
builder.Services.AddScoped<SampleEmailService>();

// GitHub logins are in these requests' addresses, so they're kept out of the telemetry (#168): the client's request logs
// ("Sending HTTP request GET https://api.github.com/users/…") are off, and the tracing redacts its spans' addresses.
builder.Services.AddHttpClient<GitHubUsers>(GitHubUsers.Configure).RemoveAllLoggers();
builder.Services.Configure<HttpClientTraceInstrumentationOptions>(GitHubTelemetry.Configure);
builder.Services.AddScoped<AuthorService>();

// Post link reminders and the daily "today's posts" email, sent each morning in the reveal time zone.
builder.Services.AddOptions<ReminderOptions>()
    .BindConfiguration(ReminderOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddScoped<DailyEmailOptOut>();
builder.Services.AddScoped<DailyPosts>();

// The blurb check (#128): every post opens by saying it's part of that year's C# Advent calendar. Posts are fetched by our
// own client (public addresses only) and judged by Claude Haiku through Anthropic's API. Without an API key the check is off;
// development and the tests use a stand-in and the built-in sample pages, so they never reach Anthropic or the internet.
builder.Services.AddOptions<BlurbOptions>()
    .BindConfiguration(BlurbOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IPostFetcher>(services =>
    services.GetRequiredService<IOptions<BlurbOptions>>().Value.SamplePages
        ? new SamplePostFetcher()
        : new WebPostFetcher(WebPostFetcher.CreateClient(), services.GetRequiredService<ILogger<WebPostFetcher>>()));
builder.Services.AddSingleton(services => new BlurbChecker(
    services.GetRequiredService<IPostFetcher>(),
    BlurbJudges.Create(services),
    services.GetRequiredService<IOptions<BlurbOptions>>()));
builder.Services.AddScoped<BlurbCheckService>();
builder.Services.AddScoped<BlurbSelfCheckService>();
builder.Services.AddScoped<BlurbQueries>();

builder.Services.AddOptions<BackupOptions>()
    .BindConfiguration(BackupOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddScoped<BackupService>();
builder.Services.AddScoped<IUndeliveredEmailHandler, BackupAskUndelivered>();
builder.Services.AddScoped<BackupListService>();
builder.Services.AddScoped<ReminderService>();

// The organizers' Today panel on /manage, and their morning digest, a step of the reminders' worker (#171).
builder.Services.AddScoped<TodayService>();
builder.Services.AddScoped<MorningDigest>();
if (builder.Configuration.GetValue("Reminders:SendInBackground", defaultValue: true))
{
    builder.Services.AddHostedService<ReminderWorker>();
}

// The announcements list (#49). Signing up is rate limited per visitor, so the form can't be used to send confirmation
// emails to a lot of addresses (and use up the email service's hourly limit).
builder.Services.AddScoped<AnnouncementSubscriptions>();
builder.Services.AddScoped<PastAuthorInvitationService>();
builder.Services.AddScoped<ReaderDailyEmailService>();
builder.Services.AddScoped<AnnouncementService>();
if (builder.Configuration.GetValue("Announcements:SendInBackground", defaultValue: true))
{
    builder.Services.AddHostedService<AnnouncementWorker>();
}
var subscribeLimit = builder.Configuration.GetValue("Announcements:SubscribeLimit", defaultValue: 5);
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(AnnouncementEndpoints.RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        AnnouncementEndpoints.RateLimitKey(context),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = subscribeLimit, Window = TimeSpan.FromMinutes(10) }));
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Redirect($"{AnnouncementEndpoints.Page}?error=busy");
        return ValueTask.CompletedTask;
    };
});

// Deletes each season's requests, backup list and non-authors' emails on February 1 after it, as the privacy page promises.
builder.Services.AddScoped<ContactRetention>();
if (builder.Configuration.GetValue("Retention:DeleteInBackground", defaultValue: true))
{
    builder.Services.AddHostedService<ContactRetentionWorker>();
}

builder.AddAccounts();

// Keys for sign-in cookies, antiforgery tokens and email links live in the database, so a restart or a second replica doesn't
// sign everyone out or reject their forms. In production they're encrypted with a Key Vault key first (#193), and the app
// won't start if it can't use that key.
builder.AddDataProtectionKeys();

// Every page renders statically on the server, and every form is a plain HTML form, so there's no interactive render mode
// and no Blazor script (#185). That also means antiforgery tokens (and their cookie and no-store) are only on pages with a
// form, so the other pages can be cached (#186) and compressed (#164).
builder.Services.AddRazorComponents();
builder.Services.AddCompression();

var app = builder.Build();

// Container Apps' probes ask /health and /alive. First, so nothing redirects them.
app.UseDefaultHealthChecks();

// Security headers on every response, static files and redirects included (#165).
app.UseSecurityHeaders();

// www.csadvent.christmas (and links to it) go to csadvent.christmas.
app.UseCanonicalHost();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseResponseCompression();
app.UsePageCaching();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Readers following an entry page's link go straight to the post, and link previews and organizers get the page. After
// sign-in, so organizers are known, and ahead of the page.
app.UseEntryRedirects();

app.MapStaticAssets();
app.MapAccounts();
app.MapEmailPreviews();
app.MapEmailQueue();
app.MapReminders();
app.MapEmailOptOuts();
app.MapBackups();
app.MapClaims();
app.MapBlurbs();
app.MapEntries();
app.MapLinkChecks();
app.MapCards();
app.MapFeeds();
app.MapCalendarFiles();
app.MapSearch();
app.MapAnnouncements();
app.MapSocial();
app.MapLegacyRedirects();
app.MapContentAdmin();
app.MapOrganizerPhotos();
app.MapSeasonArchive();
app.MapOtherAdventLogos();
app.MapSeasonTesting();
app.MapAuthors();
app.MapRazorComponents<App>();

app.Run();
