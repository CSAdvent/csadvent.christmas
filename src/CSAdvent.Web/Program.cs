using CSAdvent.Web.Auth;
using CSAdvent.Web.Backups;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Cards;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Components;
using CSAdvent.Web.Content;
using CSAdvent.Web.Contributors;
using CSAdvent.Web.Data;
using CSAdvent.Web.Data.Archive;
using CSAdvent.Web.Email;
using CSAdvent.Web.Entries;
using CSAdvent.Web.Feed;
using CSAdvent.Web.Manage;
using CSAdvent.Web.Reminders;
using CSAdvent.Web.Social;
using CSAdvent.Web.Retention;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<AdventDbContext>("csadvent", configureDbContextOptions: options => options
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
    .ValidateOnStart();
builder.Services.AddOptions<SiteOptions>()
    .BindConfiguration(SiteOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<AdventSchedule>();
builder.Services.AddScoped<ArchiveService>();
builder.Services.AddScoped<HomeService>();
builder.Services.AddScoped<ClaimService>();
builder.Services.AddScoped<SeasonAdminService>();
builder.Services.AddScoped<ClaimReviewService>();
builder.Services.AddScoped<EntryAdminService>();
builder.Services.AddScoped<MySpotsService>();
builder.Services.AddScoped<EntryService>();
builder.Services.AddSingleton<CardRenderer>();
builder.Services.AddScoped<RssFeed>();
builder.Services.AddScoped<SocialService>();
builder.Services.AddOptions<SocialAccountsOptions>().BindConfiguration(SocialAccountsOptions.SectionName);
builder.AddEmail();
builder.Services.AddScoped<IClaimNotifier, EmailClaimNotifier>();

// Post link reminders and the daily "today's posts" email, sent each morning in the reveal time zone.
builder.Services.AddOptions<ReminderOptions>()
    .BindConfiguration(ReminderOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddScoped<DailyEmailOptOut>();
builder.Services.AddOptions<BackupOptions>().BindConfiguration(BackupOptions.SectionName);
builder.Services.AddScoped<BackupService>();
builder.Services.AddScoped<BackupListService>();
builder.Services.AddScoped<ReminderService>();
if (builder.Configuration.GetValue("Reminders:SendInBackground", defaultValue: true))
{
    builder.Services.AddHostedService<ReminderWorker>();
}

// Deletes each season's contact details on February 1 after it, as the privacy page promises.
builder.Services.AddScoped<ContactRetention>();
if (builder.Configuration.GetValue("Retention:DeleteInBackground", defaultValue: true))
{
    builder.Services.AddHostedService<ContactRetentionWorker>();
}

builder.AddAccounts();

// Keys for sign-in cookies and antiforgery tokens live in the database, so a restart or a second replica doesn't sign
// everyone out or reject their forms.
builder.Services.AddDataProtection()
    .SetApplicationName("CSAdvent")
    .PersistKeysToDbContext<AdventDbContext>();

// Pages render statically on the server by default. Components opt in to interactivity where they need it.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// www.csadvent.christmas (and links to it) go to csadvent.christmas.
app.UseCanonicalHost();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapAccounts();
app.MapEmailPreviews();
app.MapReminders();
app.MapBackups();
app.MapEntries();
app.MapCards();
app.MapFeeds();
app.MapSocial();
app.MapLegacyRedirects();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
