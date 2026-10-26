using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using CSAdvent.Web.Content;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using CSAdvent.Web.Email.Queue;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// The email queue (#152) against the real database and MailPit, with the site's own worker sending. Seasons 2380-2399.
/// Queueing is made to fail, as it would if the database refused it, by a trigger only the throwaway test database gets
/// (<see cref="RejectQueueingAsync"/>).
/// </summary>
public sealed partial class EmailQueueTests(AppHostFixture fixture)
{
    private static readonly SemaphoreSlim TriggerCreated = new(1, 1);
    private static bool _triggerExists;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EmailsReachTheirInboxThroughTheQueue()
    {
        const string address = "queue-subscriber-2380@example.com";
        using var browser = fixture.CreateBrowser();
        var html = await browser.GetStringAsync("/announcements", CancellationToken);
        var subscribed = await browser.PostAsync("/announcements/subscribe", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(html),
            ["Email"] = address,
            ["Website"] = "",
            ["ReturnUrl"] = "announcements",
        }), CancellationToken);
        Assert.Equal("/announcements?done=check-inbox", TestBrowser.PathOf(subscribed.Headers.Location!));

        await fixture.WaitForEmailAsync(address, "Confirm your C# Advent announcements");
        await fixture.WaitForQueueAsync(address);
        await using var db = await fixture.CreateDbContextAsync();
        var queued = await db.EmailOutbox.AsNoTracking().SingleAsync(e => e.ToAddress == address && e.Kind == "announcements-confirm", CancellationToken);
        Assert.Equal((EmailPriority.Normal, QueuedEmailStatus.Sent), (queued.Priority, queued.Status));
        Assert.NotNull(queued.SentAt);
    }

    [Fact]
    public async Task TransactionalEmailGoesAheadOfListMail()
    {
        // Queued together, the invitation first: the request's confirmation still goes first.
        var now = DateTimeOffset.UtcNow;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            db.EmailOutbox.Add(Queued("queue-invited-2381@example.com", "Queue order 2381: list mail", "past-author-invitation", EmailPriority.Invitation, now));
            db.EmailOutbox.Add(Queued("queue-claimed-2381@example.com", "Queue order 2381: transactional", "claim-received", EmailPriority.Urgent, now));
            await db.SaveChangesAsync(CancellationToken);
        }

        await fixture.WaitForEmailAsync("queue-invited-2381@example.com", "Queue order 2381: list mail");
        await fixture.WaitForEmailAsync("queue-claimed-2381@example.com", "Queue order 2381: transactional");
        await fixture.WaitForQueueAsync("queue-invited-2381@example.com");

        var list = await SingleQueuedAsync("queue-invited-2381@example.com");
        var transactional = await SingleQueuedAsync("queue-claimed-2381@example.com");
        Assert.True(transactional.SentAt < list.SentAt, $"The transactional email went at {transactional.SentAt:O}, after the list mail at {list.SentAt:O}.");
    }

    [Fact]
    public async Task OrganizersCanRetryOrDiscardAFailedEmail()
    {
        int retryId, discardId;
        var now = DateTimeOffset.UtcNow;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var retry = Failed("queue-retry-2382@example.com", "Queue retry 2382", now);
            var discard = Failed("queue-discard-2382@example.com", "Queue discard 2382", now);
            db.EmailOutbox.AddRange(retry, discard);
            await db.SaveChangesAsync(CancellationToken);
            (retryId, discardId) = (retry.Id, discard.Id);
        }

        using var organizer = await fixture.SignInAsync("organizer", AppHostFixture.OrganizerGitHubId);
        var page = WebUtility.HtmlDecode(await organizer.GetPageAsync("/manage/emails/queue"));
        Assert.Contains("Request received to queue-retry-2382@example.com", page);
        Assert.Contains("The email service answered 503 (ServiceUnavailable), after 6 attempts", page);
        Assert.Contains("Open the email queue", await organizer.GetPageAsync("/manage/emails"));

        // Someone who isn't an organizer can't.
        using (var author = await fixture.SignInAsync("queue-author-2382", 6382))
        {
            var refused = await PostQueueFormAsync(author, "retry", retryId, Token(await author.GetStringAsync("/announcements", CancellationToken)));
            Assert.False(refused.IsSuccessStatusCode);
            Assert.False(refused.Headers.Location is { } location && TestBrowser.PathOf(location).StartsWith("/manage/emails/queue", StringComparison.Ordinal));
            Assert.Equal(QueuedEmailStatus.Failed, (await QueuedAsync(retryId))!.Status);
        }

        var token = Token(await organizer.GetPageAsync("/manage/emails/queue"));
        var retried = await PostQueueFormAsync(organizer, "retry", retryId, token);
        Assert.Equal("/manage/emails/queue?done=retried", TestBrowser.PathOf(retried.Headers.Location!));
        await fixture.WaitForEmailAsync("queue-retry-2382@example.com", "Queue retry 2382");

        var discarded = await PostQueueFormAsync(organizer, "discard", discardId, token);
        Assert.Equal("/manage/emails/queue?done=discarded", TestBrowser.PathOf(discarded.Headers.Location!));
        Assert.Null(await QueuedAsync(discardId));
        Assert.Equal(0, await fixture.CountEmailsAsync("queue-discard-2382@example.com", "Queue discard 2382"));

        // Pressing a button again, once it's done, says so.
        var again = await PostQueueFormAsync(organizer, "discard", discardId, token);
        Assert.Equal("/manage/emails/queue?done=gone", TestBrowser.PathOf(again.Headers.Location!));
        Assert.DoesNotContain("queue-discard-2382@example.com", await organizer.GetPageAsync("/manage/emails/queue"));
    }

    [Fact]
    public async Task OnlyOneReplicaDrainsAtATime()
    {
        using var services = Services(await fixture.App.GetConnectionStringAsync("csadvent", CancellationToken));
        var scopes = services.GetRequiredService<IServiceScopeFactory>();
        await using var scope = scopes.CreateAsyncScope();
        var store = new EmailOutboxStore(scope.ServiceProvider.GetRequiredService<AdventDbContext>(), scopes);

        // The site's worker only holds the lock for a moment at a time.
        var held = await TryLockAsync(store);
        try
        {
            Assert.Null(await store.TryLockAsync(CancellationToken));

            // While another replica (this test) drains, the site's worker leaves the queue alone.
            await using (var db = await fixture.CreateDbContextAsync())
            {
                db.EmailOutbox.Add(Queued("queue-lock-2383@example.com", "Queue lock 2383", "claim-received", EmailPriority.Urgent, DateTimeOffset.UtcNow));
                await db.SaveChangesAsync(CancellationToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken);
            Assert.Equal(QueuedEmailStatus.Pending, (await SingleQueuedAsync("queue-lock-2383@example.com")).Status);
        }
        finally
        {
            await held.DisposeAsync();
        }

        await fixture.WaitForEmailAsync("queue-lock-2383@example.com", "Queue lock 2383");
    }

    [Fact]
    public async Task ShortlyAfterADeployEmailsGoStraightOut()
    {
        // A database without the queue's table, as during the minute between a new revision starting and its migrations.
        var connectionString = new NpgsqlConnectionStringBuilder(await fixture.App.GetConnectionStringAsync("csadvent", CancellationToken)) { Database = "postgres" }.ConnectionString;
        const string database = "csadvent_queue_2384";
        await using (var server = new NpgsqlConnection(connectionString))
        {
            await server.OpenAsync(CancellationToken);
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", server);
            await drop.ExecuteNonQueryAsync(CancellationToken);
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", server);
            await create.ExecuteNonQueryAsync(CancellationToken);
        }

        try
        {
            using var services = Services(new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString);
            var transport = new RecordingTransport();
            var site = Options.Create(new SiteOptions { ContactEmail = "hello@csadvent.christmas", BaseUrl = "https://csadvent.christmas" });
            var sender = new QueueingEmailSender(
                transport,
                new EmailOptOutLinks(new EphemeralDataProtectionProvider(), site),
                new EmailQueueSignal(),
                services.GetRequiredService<IServiceScopeFactory>(),
                TimeProvider.System,
                NullLogger<QueueingEmailSender>.Instance);

            await sender.SendAsync(new OutgoingEmail(new EmailRecipient("queue-deploy-2384@example.com"), "hello@csadvent.christmas", "Sent straight out", "<p>Hi</p>", "Hi") { Kind = "claim-received" }, CancellationToken);
            Assert.Equal(["Sent straight out"], transport.Sent.Select(e => e.Subject));

            // Everything else that reads the queue copes until the table's there.
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AdventDbContext>();
            var outbox = new EmailOutbox(db, new EmailQueueSignal(), Options.Create(new EmailQueueOptions()), TimeProvider.System);
            Assert.True((await outbox.GetStatusAsync(CancellationToken)).TableMissing);
            Assert.Equal(0, await outbox.GetListRoomAsync(CancellationToken));
            Assert.Equal(0, await outbox.CancelAsync("queue-deploy-2384@example.com", "announcement:", CancellationToken));
            var drain = new EmailQueueDrain(
                new EmailOutboxStore(db, services.GetRequiredService<IServiceScopeFactory>()),
                transport,
                scope.ServiceProvider,
                Options.Create(new EmailQueueOptions()),
                TimeProvider.System,
                NullLogger<EmailQueueDrain>.Instance);
            var missing = await Assert.ThrowsAnyAsync<Exception>(() => drain.RunAsync(CancellationToken));
            Assert.True(MissingSchema.IsMissingTableOrColumn(missing));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var server = new NpgsqlConnection(connectionString);
            await server.OpenAsync(CancellationToken);
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", server);
            await drop.ExecuteNonQueryAsync(CancellationToken);
        }
    }

    [Fact]
    public async Task ABackupAskThatCantBeQueuedAsksTheSameBackupAgain()
    {
        const int year = 2385;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true, season =>
        {
            Drop(season, day: 19);
            season.Substitutes.Add(new Substitute { Name = "Ann Backup", Email = "queue-ann-2385@example.com", SignedUpAt = DateTimeOffset.UtcNow.AddDays(-2) });
            season.Substitutes.Add(new Substitute { Name = "Bob Backup", Email = "queue-bob-2385@example.com", SignedUpAt = DateTimeOffset.UtcNow.AddDays(-1) });
        });

        // Ann was never told, through no fault of hers (#149), so she isn't passed over.
        await using (await RejectQueueingAsync("queue-ann-2385@example.com"))
        {
            Assert.Equal(new BackupRun(0, 0, 0, 0), await RunBackupsAsync(year, "2385-12-18T12:00:00Z"));
            await using var db = await fixture.CreateDbContextAsync();
            Assert.False(await db.BackupOffers.AnyAsync(o => o.Slot.SeasonYear == year, CancellationToken));
        }

        Assert.Equal(new BackupRun(1, 0, 0, 0), await RunBackupsAsync(year, "2385-12-18T12:15:00Z"));
        await fixture.WaitForEmailAsync("queue-ann-2385@example.com", "Can you fill in on C# Advent, December 19?");
        Assert.Equal(0, await fixture.CountEmailsAsync("queue-bob-2385@example.com", "Can you fill in on C# Advent, December 19?"));
    }

    [Fact]
    public async Task ABackupAskTheQueueGivesUpOnAsksTheNextBackupStraightAway()
    {
        const int year = 2386;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true, season =>
        {
            Drop(season, day: 20);
            season.Substitutes.Add(new Substitute { Name = "Cy Backup", Email = "queue-cy-2386@example.com", SignedUpAt = DateTimeOffset.UtcNow.AddDays(-2) });
            season.Substitutes.Add(new Substitute { Name = "Di Backup", Email = "queue-di-2386@example.com", SignedUpAt = DateTimeOffset.UtcNow.AddDays(-1) });
        });

        Assert.Equal(new BackupRun(1, 0, 0, 0), await RunBackupsAsync(year, "2386-12-19T12:00:00Z"));
        await fixture.WaitForEmailAsync("queue-cy-2386@example.com", "Can you fill in on C# Advent, December 20?");
        await fixture.WaitForQueueAsync("queue-cy-2386@example.com");

        // Cy's ask goes unsent (#149): it's still waiting when its time runs out. Instead of the spot waiting out Cy's four
        // hours, Di is asked as soon as the queue gives up on it.
        await using (var db = await fixture.CreateDbContextAsync())
        {
            var ask = await db.EmailOutbox.SingleAsync(e => e.ToAddress == "queue-cy-2386@example.com", CancellationToken);
            await db.EmailOutbox.Where(e => e.Id == ask.Id).ExecuteUpdateAsync(
                e => e.SetProperty(x => x.Status, QueuedEmailStatus.Pending)
                    .SetProperty(x => x.SentAt, (DateTimeOffset?)null)
                    .SetProperty(x => x.FinishedAt, (DateTimeOffset?)null)
                    .SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow.AddHours(1))
                    .SetProperty(x => x.SendBefore, DateTimeOffset.UtcNow.AddSeconds(-1)),
                CancellationToken);
        }

        await fixture.WaitForEmailAsync("queue-di-2386@example.com", "Can you fill in on C# Advent, December 20?");
        await using (var db = await fixture.CreateDbContextAsync())
        {
            Assert.Equal(QueuedEmailStatus.Expired, (await db.EmailOutbox.SingleAsync(e => e.ToAddress == "queue-cy-2386@example.com", CancellationToken)).Status);
            var cy = await db.BackupOffers.SingleAsync(o => o.Substitute.Email == "queue-cy-2386@example.com", CancellationToken);
            Assert.True(cy.ExpiresAt <= DateTimeOffset.UtcNow, "Cy's time to answer should have ended when the ask was given up on.");
        }
    }

    [Fact]
    public async Task NoBackupsLeftIsToldAgainIfItCouldntBeQueued()
    {
        const int year = 2387;
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true, season => Drop(season, day: 21));
        int slotId;
        await using (var db = await fixture.CreateDbContextAsync())
        {
            slotId = await db.Slots.Where(s => s.SeasonYear == year && s.Day == 21 && s.Part == 1).Select(s => s.Id).SingleAsync(CancellationToken);
        }

        // The record of telling them goes when the email can't be queued (#149), so the next run tells them.
        await using (await RejectQueueingAsync("%December 21, 2387%"))
        {
            Assert.Equal(new BackupRun(0, 0, 0, 0), await RunBackupsAsync(year, "2387-12-20T12:00:00Z"));
            await using var db = await fixture.CreateDbContextAsync();
            Assert.False(await db.SentReminders.AnyAsync(r => r.SlotId == slotId && r.Kind == ReminderKind.AdminBackupsExhausted, CancellationToken));
        }

        Assert.Equal(new BackupRun(0, 0, 0, 1), await RunBackupsAsync(year, "2387-12-20T12:15:00Z"));
        var email = await fixture.WaitForEmailAsync("hello@csadvent.christmas", "No backup poster for C# Advent, December 21");
        Assert.Contains("December 21, 2387", email.Text);
    }

    [Fact]
    public async Task ABlurbAlertThatCouldntBeQueuedIsSentByALaterRun()
    {
        const int year = 2388;
        var postUrl = $"https://samples.csadvent.test/unsure?year={year}";
        await TestSeasons.EnsureAsync(fixture, year, claimsOpen: true, season =>
        {
            var slot = season.Slots.Single(s => s is { Day: 18, Part: 1 });
            slot.AuthorName = "Una Unsure";
            slot.PostTitle = "Una's post";
            slot.PostUrl = postUrl;
            slot.Contact = new SlotContact { Email = "queue-una-2388@example.com" };
        });

        // Claimed before it's queued; the claim is undone when it can't be (#149).
        await using (await RejectQueueingAsync($"%{postUrl}%"))
        {
            await RunBlurbsAsync(year, "2388-12-18T05:30:00Z");
            await using var db = await fixture.CreateDbContextAsync();
            var check = await db.BlurbChecks.AsNoTracking().SingleAsync(c => c.Slot.SeasonYear == year, CancellationToken);
            Assert.Equal(BlurbStatus.NeedsDecision, check.Status);
            Assert.Null(check.OrganizersEmailedAbout);
        }

        await RunBlurbsAsync(year, "2388-12-18T05:45:00Z");
        var email = await fixture.WaitForEmailAsync("hello@csadvent.christmas", "C# Advent, December 18: a post's blurb needs your decision");
        Assert.Contains(postUrl, email.Text);
    }

    private static void Drop(Season season, int day)
    {
        var slot = season.Slots.Single(s => s.Day == day && s.Part == 1);
        slot.DroppedOutAt = DateTimeOffset.UtcNow.AddDays(-1);
    }

    private static QueuedEmail Queued(string to, string subject, string kind, EmailPriority priority, DateTimeOffset now) => new()
    {
        Kind = kind,
        Priority = priority,
        ToAddress = to,
        ReplyTo = "hello@csadvent.christmas",
        Subject = subject,
        Html = $"<p>{subject}</p>",
        Text = subject,
        Transactional = priority is EmailPriority.Urgent,
        CreatedAt = now,
        Status = QueuedEmailStatus.Pending,
        NextAttemptAt = now,
    };

    private static QueuedEmail Failed(string to, string subject, DateTimeOffset now)
    {
        var email = Queued(to, subject, "claim-received", EmailPriority.Urgent, now - TimeSpan.FromHours(3));
        email.Status = QueuedEmailStatus.Failed;
        email.Attempts = 6;
        email.LastError = "The email service answered 503 (ServiceUnavailable)";
        email.FinishedAt = now;
        return email;
    }

    private async Task<QueuedEmail> SingleQueuedAsync(string to)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.EmailOutbox.AsNoTracking().SingleAsync(e => e.ToAddress == to, CancellationToken);
    }

    private async Task<QueuedEmail?> QueuedAsync(int id)
    {
        await using var db = await fixture.CreateDbContextAsync();
        return await db.EmailOutbox.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, CancellationToken);
    }

    private static Task<HttpResponseMessage> PostQueueFormAsync(HttpClient browser, string action, int id, string token) =>
        browser.PostAsync($"/manage/emails/queue/{action}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Id"] = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }), CancellationToken);

    private async Task<BackupRun> RunBackupsAsync(int year, string at)
    {
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/backups/run?year={year}&at={Uri.EscapeDataString(at)}", null, CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BackupRun>(CancellationToken))!;
    }

    private async Task RunBlurbsAsync(int year, string at)
    {
        using var client = fixture.CreateWebClient();
        var response = await client.PostAsync($"/dev/blurbs/run?year={year}&at={Uri.EscapeDataString(at)}", null, CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Makes queueing fail for emails whose address, subject or text is <c>LIKE</c> the pattern, the way it would if the
    /// database refused the insert, until it's disposed. The trigger is created once, in the throwaway test database only.
    /// </summary>
    private async Task<IAsyncDisposable> RejectQueueingAsync(string pattern)
    {
        await using var db = await fixture.CreateDbContextAsync();
        await TriggerCreated.WaitAsync(CancellationToken);
        try
        {
            if (!_triggerExists)
            {
                await db.Database.ExecuteSqlRawAsync(
                    """
                    CREATE TABLE IF NOT EXISTS test_outbox_rejections (pattern text PRIMARY KEY);
                    CREATE OR REPLACE FUNCTION test_reject_outbox() RETURNS trigger LANGUAGE plpgsql AS $$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM test_outbox_rejections r
                                   WHERE NEW.to_address LIKE r.pattern OR NEW.subject LIKE r.pattern OR NEW.text LIKE r.pattern) THEN
                            RAISE EXCEPTION 'Rejected by the integration tests';
                        END IF;
                        RETURN NEW;
                    END $$;
                    DROP TRIGGER IF EXISTS test_reject_outbox ON email_outbox;
                    CREATE TRIGGER test_reject_outbox BEFORE INSERT ON email_outbox FOR EACH ROW EXECUTE FUNCTION test_reject_outbox();
                    """,
                    CancellationToken);
                _triggerExists = true;
            }
        }
        finally
        {
            TriggerCreated.Release();
        }

        await db.Database.ExecuteSqlAsync($"INSERT INTO test_outbox_rejections (pattern) VALUES ({pattern}) ON CONFLICT DO NOTHING", CancellationToken);
        return new Rejection(fixture, pattern);
    }

    private sealed class Rejection(AppHostFixture fixture, string pattern) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await using var db = await fixture.CreateDbContextAsync();
            await db.Database.ExecuteSqlAsync($"DELETE FROM test_outbox_rejections WHERE pattern = {pattern}", CancellationToken.None);
        }
    }

    private static async Task<IAsyncDisposable> TryLockAsync(EmailOutboxStore store)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await store.TryLockAsync(CancellationToken) is { } held)
            {
                return held;
            }

            await Task.Delay(50, CancellationToken);
        }

        throw new InvalidOperationException("Couldn't take the email queue's lock.");
    }

    private static ServiceProvider Services(string? connectionString) =>
        new ServiceCollection().AddDbContext<AdventDbContext>(options => options.UseNpgsql(connectionString)).BuildServiceProvider();

    private static string Token(string page) => WebUtility.HtmlDecode(AntiforgeryToken().Match(page).Groups["token"].Value);

    [GeneratedRegex("<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    private sealed record BackupRun(int Asked, int Used, int Withdrawn, int Exhausted);

    private sealed class RecordingTransport : IEmailSender
    {
        public List<OutgoingEmail> Sent { get; } = [];

        public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
        {
            Sent.Add(email);
            return Task.CompletedTask;
        }
    }
}
