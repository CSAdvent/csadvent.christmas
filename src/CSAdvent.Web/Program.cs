using CSAdvent.Web.Auth;
using CSAdvent.Web.Calendar;
using CSAdvent.Web.Claims;
using CSAdvent.Web.Components;
using CSAdvent.Web.Data;
using CSAdvent.Web.Data.Archive;
using CSAdvent.Web.Manage;
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
builder.Services.AddSingleton<AdventSchedule>();
builder.Services.AddScoped<ArchiveService>();
builder.Services.AddScoped<HomeService>();
builder.Services.AddScoped<ClaimService>();
builder.Services.AddScoped<SeasonAdminService>();
builder.Services.AddScoped<ClaimReviewService>();
builder.Services.AddScoped<EntryAdminService>();
builder.Services.AddSingleton<IClaimNotifier, LoggingClaimNotifier>();

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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
