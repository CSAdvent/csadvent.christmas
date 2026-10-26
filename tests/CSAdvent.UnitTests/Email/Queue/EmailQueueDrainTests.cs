using System.Diagnostics.CodeAnalysis;
using Azure;
using Azure.Core;
using CSAdvent.Web.Data;
using CSAdvent.Web.Email;
using CSAdvent.Web.Email.Queue;
using MailKit.Net.Smtp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace CSAdvent.UnitTests.Email.Queue;

/// <summary>The email queue's drain (#152) with a fake clock, an in-memory outbox and a fake transport.</summary>
public sealed class EmailQueueDrainTests
{
    private static readonly DateTimeOffset Start = new(2026, 12, 3, 14, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Start);
    private readonly InMemoryOutboxStore _store = new();
    private readonly FakeTransport _transport = new();
    private readonly RecordingUndeliveredHandler _handler = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private DateTimeOffset Now => _clock.GetUtcNow();

    private EmailQueueDrain Drain(EmailQueueOptions? options = null) => new(
        _store,
        _transport,
        new ServiceCollection().AddSingleton<IUndeliveredEmailHandler>(_handler).BuildServiceProvider(),
        Options.Create(options ?? new EmailQueueOptions()),
        _clock,
        NullLogger<EmailQueueDrain>.Instance);

    private void Queue(int count, EmailPriority priority = EmailPriority.Urgent)
    {
        for (var i = 0; i < count; i++)
        {
            _store.Add(Now, priority);
        }
    }

    [Fact]
    public async Task SendsNoMoreThanTheMinutesLimit()
    {
        Queue(30);

        Assert.Equal(25, (await Drain().RunAsync(CancellationToken)).Sent);
        Assert.Equal(0, (await Drain().RunAsync(CancellationToken)).Sent);

        // A minute after those went, there's room again.
        _clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(5, (await Drain().RunAsync(CancellationToken)).Sent);
        Assert.Equal(30, _transport.Sent.Count);
    }

    [Fact]
    public async Task SendsNoMoreThanTheHoursLimit()
    {
        Queue(200);

        var sent = 0;
        for (var minute = 0; minute < 10; minute++)
        {
            sent += (await Drain().RunAsync(CancellationToken)).Sent;
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(90, sent);

        // An hour after the first sends, the oldest have aged out.
        _clock.Advance(TimeSpan.FromMinutes(51));
        Assert.Equal(25, (await Drain().RunAsync(CancellationToken)).Sent);
    }

    [Fact]
    public async Task TheLimitsCountWhatEveryReplicaSent()
    {
        // Sent by another replica 30 seconds ago: the same table counts them.
        for (var i = 0; i < 20; i++)
        {
            var email = _store.Add(Now - TimeSpan.FromSeconds(30));
            await _store.MarkSentAsync(email.Id, Now - TimeSpan.FromSeconds(30), CancellationToken);
        }

        Queue(10);

        Assert.Equal(5, (await Drain().RunAsync(CancellationToken)).Sent);
    }

    [Fact]
    public async Task SendsInPriorityOrderAndOldestFirst()
    {
        var invitation = _store.Add(Now, EmailPriority.Invitation);
        var announcement = _store.Add(Now, EmailPriority.Announcement);
        var readers = _store.Add(Now, EmailPriority.ReadersDailyPosts);
        var daily = _store.Add(Now, EmailPriority.Normal);
        var claim = _store.Add(Now, EmailPriority.Urgent);
        var laterClaim = _store.Add(Now, EmailPriority.Urgent);

        // List mail goes a minute at a time.
        for (var minute = 0; minute < 3; minute++)
        {
            await Drain().RunAsync(CancellationToken);
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(
            [claim.Subject, laterClaim.Subject, daily.Subject, readers.Subject, announcement.Subject, invitation.Subject],
            _transport.Sent.Select(e => e.Subject));
    }

    [Fact]
    public async Task ListMailOnlyGetsWhatsLeftUnderItsShareOfTheHour()
    {
        var options = new EmailQueueOptions { PerMinute = 90, PerHour = 90, ListPerHour = 50 };
        Queue(60, EmailPriority.Announcement);
        Queue(10, EmailPriority.Urgent);

        await Drain(options).RunAsync(CancellationToken);
        Assert.Equal(10, _transport.Sent.Count(e => e.Priority == EmailPriority.Urgent));

        // A minute at a time, list mail fills the hour up to the ceiling, which the urgent ones count towards too.
        for (var minute = 0; minute < 60; minute++)
        {
            _clock.Advance(TimeSpan.FromMinutes(1));
            await Drain(options).RunAsync(CancellationToken);
            if (_transport.Sent.Count >= 50)
            {
                break;
            }
        }

        Assert.Equal(40, _transport.Sent.Count(e => e.Priority == EmailPriority.Announcement));
        Assert.Equal(0, (await Drain(options).RunAsync(CancellationToken)).Sent);

        // With list mail held back, the rest of the hour is still there for emails people are waiting for.
        Queue(30, EmailPriority.Urgent);
        await Drain(options).RunAsync(CancellationToken);
        Assert.Equal(40, _transport.Sent.Count(e => e.Priority == EmailPriority.Urgent));
        Assert.Equal(40, _transport.Sent.Count(e => e.Priority == EmailPriority.Announcement));
    }

    [Fact]
    public async Task ListMailIsSpreadAcrossTheHourSoItYieldsToABurstOfClaims()
    {
        // Sign-up day: the announcement is queued for everyone at 9 AM, and the claims it brings start a few minutes later.
        Queue(200, EmailPriority.Announcement);
        for (var minute = 0; minute < 5; minute++)
        {
            await Drain().RunAsync(CancellationToken);
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(5, _transport.Sent.Count);

        // 30 claims (60 emails) over the next half hour all go straight away. List mail carries on a minute at a time until the
        // hour reaches the ceiling, then waits. Sent in a burst at 9 AM instead, it would have left room for only 40 of them.
        for (var minute = 0; minute < 30; minute++)
        {
            Queue(2, EmailPriority.Urgent);
            await Drain().RunAsync(CancellationToken);
            Assert.DoesNotContain(_store.Emails, e => e.Priority == EmailPriority.Urgent && e.Status == QueuedEmailStatus.Pending);
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(60, _transport.Sent.Count(e => e.Priority == EmailPriority.Urgent));
        Assert.Equal(20, _transport.Sent.Count(e => e.Priority == EmailPriority.Announcement));
    }

    [Fact]
    public async Task EmailsPastTheirDeadlineExpireUnsentAndTheirSendersHear()
    {
        var late = _store.Add(Now, EmailPriority.ReadersDailyPosts, kind: "daily-posts-readers", sendBefore: Now - TimeSpan.FromSeconds(1), reference: "daily-posts:2026:3");
        var onTime = _store.Add(Now, EmailPriority.ReadersDailyPosts, sendBefore: Now + TimeSpan.FromHours(1));

        var pass = await Drain().RunAsync(CancellationToken);

        Assert.Equal(1, pass.Expired);
        Assert.Equal(QueuedEmailStatus.Expired, late.Status);
        Assert.Equal([onTime.Subject], _transport.Sent.Select(e => e.Subject));
        Assert.Equal([new UndeliveredEmail(late.Id, "daily-posts-readers", "daily-posts:2026:3", QueuedEmailStatus.Expired)], _handler.Told);
    }

    [Fact]
    public async Task AFailedSendIsTriedAgainWithBackoffThenGivenUp()
    {
        var failing = _store.Add(Now, kind: "backup-ask-dropped", reference: "backup-offer:7");
        _transport.Fail = email => email.Subject == failing.Subject ? new IOException("Connection reset") : null;

        var waits = new List<TimeSpan>();
        for (var attempt = 1; attempt < 6; attempt++)
        {
            var pass = await Drain().RunAsync(CancellationToken);
            Assert.Equal(1, pass.Retrying);
            Assert.Equal(attempt, failing.Attempts);
            Assert.Equal("Sending failed (IOException)", failing.LastError);
            waits.Add(failing.NextAttemptAt - Now);

            // Not before its time.
            Assert.Equal(EmailQueuePass.Nothing, await Drain().RunAsync(CancellationToken));
            _clock.Advance(failing.NextAttemptAt - Now);
        }

        Assert.Equal([TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), TimeSpan.FromHours(1)], waits);

        // The sixth attempt is the last.
        Assert.Equal(1, (await Drain().RunAsync(CancellationToken)).Failed);
        Assert.Equal(QueuedEmailStatus.Failed, failing.Status);
        Assert.Equal(6, failing.Attempts);
        Assert.Equal([new UndeliveredEmail(failing.Id, "backup-ask-dropped", "backup-offer:7", QueuedEmailStatus.Failed)], _handler.Told);
        Assert.Equal(6, _transport.Attempts.Count);
    }

    [Fact]
    public async Task AFailureEndsThePassAndLeavesTheRestForTheNext()
    {
        var first = _store.Add(Now);
        var second = _store.Add(Now);
        var third = _store.Add(Now);
        _transport.Fail = email => email.Subject == first.Subject ? new TimeoutException() : null;

        await Drain().RunAsync(CancellationToken);
        Assert.Empty(_transport.Sent);
        Assert.Null(second.LockedUntil);
        Assert.Equal(0, second.Attempts);

        await Drain().RunAsync(CancellationToken);
        Assert.Equal([second.Subject, third.Subject], _transport.Sent.Select(e => e.Subject));
    }

    [Fact]
    public async Task ThrottlingPausesTheQueueForAsLongAsItsAsked()
    {
        var first = _store.Add(Now);
        var throttled = _store.Add(Now);
        var third = _store.Add(Now);
        _transport.Fail = email => email.Subject == throttled.Subject ? new RequestFailedException(new FakeResponse(429, ("Retry-After", "30"))) : null;

        var pass = await Drain().RunAsync(CancellationToken);

        Assert.Equal(1, pass.Sent);
        Assert.Equal(Now + TimeSpan.FromSeconds(30), pass.PausedUntil);

        // Throttling isn't the email's fault: it isn't counted against it, and nothing is held.
        Assert.Equal(QueuedEmailStatus.Sent, first.Status);
        Assert.All([throttled, third], email =>
        {
            Assert.Equal(QueuedEmailStatus.Pending, email.Status);
            Assert.Equal(0, email.Attempts);
            Assert.Null(email.LockedUntil);
        });
    }

    [Fact]
    public async Task ThrottlingWithoutRetryAfterPausesForTheDefault()
    {
        _store.Add(Now);
        _transport.Fail = _ => new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.ServiceNotAvailable, "Too many messages");

        var pass = await Drain(new EmailQueueOptions { ThrottlePause = TimeSpan.FromMinutes(2) }).RunAsync(CancellationToken);

        Assert.Equal(Now + TimeSpan.FromMinutes(2), pass.PausedUntil);
    }

    [Fact]
    public async Task OnlyOneDrainSendsAtATime()
    {
        Queue(3);
        var holdUp = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        _transport.Before = async _ =>
        {
            started.TrySetResult();
            await holdUp.Task;
        };

        var first = Drain().RunAsync(CancellationToken);
        await started.Task;

        // Queued while the first pass is sending: the second pass finds it due, but can't take the lock.
        Queue(1);
        var second = await Drain().RunAsync(CancellationToken);
        holdUp.SetResult();

        Assert.True(second.Busy);
        Assert.Equal(3, (await first).Sent);
        Assert.Equal(1, (await Drain().RunAsync(CancellationToken)).Sent);
        Assert.Equal(4, _transport.Sent.Select(e => e.Subject).Distinct().Count());
    }

    [Fact]
    public async Task ASlowPassStopsStartingSendsHalfwayThroughItsLease()
    {
        Queue(3);
        _transport.Before = _ =>
        {
            _clock.Advance(TimeSpan.FromMinutes(3));
            return Task.CompletedTask;
        };

        var pass = await Drain(new EmailQueueOptions { Lease = TimeSpan.FromMinutes(5) }).RunAsync(CancellationToken);

        Assert.Equal(1, pass.Sent);
        Assert.All(_store.Emails.Where(e => e.Status == QueuedEmailStatus.Pending), email => Assert.Null(email.LockedUntil));
    }

    [Fact]
    public async Task AnEmailHeldByAPassThatDiedGoesOnceItsLeaseEnds()
    {
        var held = _store.Add(Now);
        held.LockedUntil = Now + TimeSpan.FromMinutes(5);

        Assert.Equal(0, (await Drain().RunAsync(CancellationToken)).Sent);

        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, (await Drain().RunAsync(CancellationToken)).Sent);
    }

    [Fact]
    public async Task TheEmailGoesToTheTransportAsItWasQueued()
    {
        _store.Emails.Add(new QueuedEmail
        {
            Id = 42,
            Kind = "announcement",
            Priority = EmailPriority.Announcement,
            Reference = "announcement:3",
            ToAddress = "ada@example.com",
            ToName = "Ada",
            ReplyTo = "hello@csadvent.christmas",
            Subject = "News",
            Html = "<p>News</p>",
            Text = "News",
            UnsubscribeUrl = "https://csadvent.christmas/emails/announcements/stop?token=abc",
            CreatedAt = Now,
            Status = QueuedEmailStatus.Pending,
            NextAttemptAt = Now,
        });

        await Drain().RunAsync(CancellationToken);

        var sent = Assert.Single(_transport.Sent);
        Assert.Equal(new EmailRecipient("ada@example.com", "Ada"), sent.To);
        Assert.Equal("https://csadvent.christmas/emails/announcements/stop?token=abc", sent.UnsubscribeUrl);
        Assert.False(sent.Transactional);
        Assert.Equal(("hello@csadvent.christmas", "News", "<p>News</p>", "News"), (sent.ReplyTo, sent.Subject, sent.Html, sent.Text));
    }

    [Theory]
    [InlineData(0, 0, 0, 25, 1)]
    [InlineData(10, 10, 0, 15, 1)]
    [InlineData(10, 10, 1, 15, 0)]
    [InlineData(0, 49, 0, 25, 1)]
    [InlineData(0, 50, 0, 25, 0)]
    [InlineData(5, 88, 0, 2, 0)]
    [InlineData(30, 30, 0, 0, 0)]
    [InlineData(0, 95, 0, 0, 0)]
    public void TheBudgetIsWhatsLeftOfTheMinuteAndTheHour(int lastMinute, int lastHour, int listLastMinute, int total, int list)
    {
        var budget = EmailBudget.From(new EmailQueueOptions(), new RecentSends(lastMinute, lastHour, listLastMinute));

        Assert.Equal(total, budget.Total);
        Assert.Equal(list, budget.ListRoom(0));
    }

    [Fact]
    public void ListMailIsSpreadOverTheHour()
    {
        Assert.Equal(1, new EmailQueueOptions { ListPerHour = 50 }.ListPerMinute);
        Assert.Equal(2, new EmailQueueOptions { ListPerHour = 61 }.ListPerMinute);
        Assert.Equal(0, new EmailQueueOptions { ListPerHour = 0 }.ListPerMinute);
        Assert.Equal(1667, new EmailQueueOptions { PerHour = 100_000, ListPerHour = 100_000 }.ListPerMinute);
    }

    /// <summary>An Azure response, so a test can throw what the email service would.</summary>
    private sealed class FakeResponse(int status, params (string Name, string Value)[] headers) : Response
    {
        public override int Status => status;

        public override string ReasonPhrase => status == 429 ? "Too Many Requests" : "Error";

        public override Stream? ContentStream { get; set; }

        public override string ClientRequestId { get; set; } = "test";

        public override void Dispose()
        {
        }

        protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string? value)
        {
            value = headers.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
            return value is not null;
        }

        protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values)
        {
            values = TryGetHeader(name, out var value) ? [value] : null;
            return values is not null;
        }

        protected override bool ContainsHeader(string name) => TryGetHeader(name, out _);

        protected override IEnumerable<HttpHeader> EnumerateHeaders() => headers.Select(h => new HttpHeader(h.Name, h.Value));
    }
}
