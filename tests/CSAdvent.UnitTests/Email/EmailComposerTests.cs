using CSAdvent.Web.Content;
using CSAdvent.Web.Email;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static CSAdvent.Web.Email.EmailBlocks;

namespace CSAdvent.UnitTests.Email;

public sealed class EmailComposerTests
{
    private static readonly SiteOptions Site = new() { ContactEmail = "hello@csadvent.christmas", BaseUrl = "https://csadvent.christmas" };

    private static readonly EmailContent Content = new()
    {
        Subject = "Line one\r\nBcc: someone@example.com",
        Preview = "A preview line",
        Heading = "Hi <b>Ada</b> & friends",
        Body =
        [
            Paragraph("Read the ", Link("terms", "https://csadvent.christmas/terms"), ", then ", Link("https://example.com", "https://example.com"), "."),
            List(["First"], ["Second"]),
            Button("Review the request", "https://csadvent.christmas/manage/requests/42"),
            Details(new DetailRow("Name", "Ada <script>")),
            Paragraph("Thanks,", EmailSpan.LineBreak, "The organizers"),
        ],
        Reason = "You're getting this because of a test.",
    };

    private static readonly EmailOptOutLinks Links = new(new EphemeralDataProtectionProvider(), Options.Create(Site));

    private static EmailComposer CreateComposer(IEmailSender? sender = null, ILoggerFactory? loggerFactory = null) =>
        new(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            loggerFactory ?? NullLoggerFactory.Instance,
            Options.Create(Site),
            Links,
            sender ?? new SendingSender());

    [Fact]
    public async Task TheHtmlIsBrandedAndEncoded()
    {
        var html = await CreateComposer().RenderHtmlAsync(Content);

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("src=\"https://csadvent.christmas/images/csadvent-logo.png\"", html);
        Assert.Contains("background=\"https://csadvent.christmas/images/email-header.jpg\"", html);
        Assert.Contains("A preview line", html);
        Assert.Contains("Hi &lt;b&gt;Ada&lt;/b&gt; &amp; friends", html);
        Assert.Contains("Ada &lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("href=\"https://csadvent.christmas/manage/requests/42\"", html);
        Assert.Contains(">Review the request</a>", html);
        Assert.Contains("<li style=\"margin:0 0 8px;\">Second</li>", html);
        Assert.Contains("Thanks,<br>The organizers", html);
        Assert.Contains("href=\"https://csadvent.christmas/privacy\"", html);
        Assert.Contains("You&#x27;re getting this because of a test.", html);
    }

    [Fact]
    public void ThePlainTextAlternativeSaysTheSame()
    {
        var text = CreateComposer().RenderText(Content);

        Assert.StartsWith("Hi <b>Ada</b> & friends\n\n", text);
        Assert.Contains("Read the terms (https://csadvent.christmas/terms), then https://example.com.", text);
        Assert.Contains("- First\n- Second\n", text);
        Assert.Contains("Review the request: https://csadvent.christmas/manage/requests/42", text);
        Assert.Contains("Name: Ada <script>", text);
        Assert.Contains("Thanks,\nThe organizers", text);
        Assert.Contains("Privacy: https://csadvent.christmas/privacy", text);
        Assert.EndsWith("You're getting this because of a test.\n", text);
    }

    [Fact]
    public async Task RepliesGoToTheOrganizersUnlessSaidOtherwise()
    {
        var composer = CreateComposer();
        var to = new EmailRecipient("ada@example.com", "Ada");

        Assert.Equal("hello@csadvent.christmas", (await composer.ComposeAsync(Content, to)).ReplyTo);
        Assert.Equal("grace@example.com", (await composer.ComposeAsync(Content, to, replyTo: "grace@example.com")).ReplyTo);
    }

    [Fact]
    public async Task EmailsPeopleCanTurnOffCarryTheLinkInTheFooterAndHeaders()
    {
        var composer = CreateComposer();
        var content = Content with { UnsubscribeUrl = "https://csadvent.christmas/emails/daily/stop?token=abc" };

        var email = await composer.ComposeAsync(content, new EmailRecipient("ada@example.com"));

        Assert.Equal("https://csadvent.christmas/emails/daily/stop?token=abc", email.UnsubscribeUrl);
        Assert.Contains("<a href=\"https://csadvent.christmas/emails/daily/stop?token=abc\"", email.Html);
        Assert.Contains("Unsubscribe</a>", email.Html);
        Assert.Contains("Unsubscribe: https://csadvent.christmas/emails/daily/stop?token=abc", email.Text);
    }

    [Fact]
    public async Task EveryOtherEmailToAPersonCanUnsubscribeFromEverything()
    {
        var email = await CreateComposer().ComposeAsync(Content, new EmailRecipient("Ada@Example.com", "Ada"));

        Assert.StartsWith("https://csadvent.christmas/emails/stop?token=", email.UnsubscribeUrl);
        Assert.Contains($"<a href=\"{email.UnsubscribeUrl}\"", email.Html);
        Assert.Contains($"Unsubscribe: {email.UnsubscribeUrl}", email.Text);
        Assert.True(Links.TryReadToken(new Uri(email.UnsubscribeUrl!).Query.Split("token=")[1], out var address));
        Assert.Equal("ada@example.com", address);
    }

    [Fact]
    public async Task EmailsAboutWhatSomeoneSignedUpForHaveNoUnsubscribeLink()
    {
        var email = await CreateComposer().ComposeAsync(Content with { Transactional = true }, new EmailRecipient("ada@example.com"));

        Assert.Null(email.UnsubscribeUrl);
        Assert.True(email.Transactional);
        Assert.DoesNotContain("Unsubscribe", email.Html);
        Assert.DoesNotContain("Unsubscribe", email.Text);
    }

    [Fact]
    public async Task TheOrganizersInboxGetsNoUnsubscribeLink()
    {
        var email = await CreateComposer().ComposeAsync(Content, new EmailRecipient("Hello@CSAdvent.christmas"));

        Assert.Null(email.UnsubscribeUrl);
        Assert.DoesNotContain("Unsubscribe", email.Html);
    }

    [Fact]
    public void UnsubscribeTokensCantBeForgedAndTheListKeepsOnlyAFingerprint()
    {
        Assert.False(Links.TryReadToken("not-a-token", out _));
        Assert.False(Links.TryReadToken(null, out _));
        Assert.True(Links.TryReadToken(Links.CreateToken(" Grace@Example.com "), out var address));
        Assert.Equal("grace@example.com", address);

        var fingerprint = EmailOptOutLinks.Fingerprint("Grace@Example.com ");
        Assert.Equal(EmailOptOutLinks.Fingerprint("grace@example.com"), fingerprint);
        Assert.Equal(64, fingerprint.Length);
        Assert.DoesNotContain("grace", fingerprint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubjectsAreASingleLine()
    {
        var email = await CreateComposer().ComposeAsync(Content, new EmailRecipient("ada@example.com"));

        Assert.Equal("Line oneBcc: someone@example.com", email.Subject);
    }

    [Fact]
    public async Task TrySendQueuesTheComposedEmail()
    {
        var sender = new SendingSender();

        var sent = await CreateComposer(sender).TrySendAsync(Content with { Kind = "claim-received" }, new EmailRecipient("ada@example.com", "Ada"), "claim request 7", TestContext.Current.CancellationToken);

        Assert.True(sent);
        var email = Assert.Single(sender.Sent);
        Assert.Equal(("ada@example.com", "claim-received", "hello@csadvent.christmas"), (email.To.Address, email.Kind, email.ReplyTo));
    }

    [Fact]
    public async Task TrySendCanSendRepliesElsewhere()
    {
        var sender = new SendingSender();

        await CreateComposer(sender).TrySendAsync(Content, Site.OrganizersInbox, "grace@example.com", "post claim request 3", TestContext.Current.CancellationToken);

        var email = Assert.Single(sender.Sent);
        Assert.Equal(("hello@csadvent.christmas", "C# Advent organizers", "grace@example.com"), (email.To.Address, email.To.Name, email.ReplyTo));
    }

    [Fact]
    public async Task AnEmailThatCantBeQueuedIsLoggedWithoutTheAddress()
    {
        var logs = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));

        var sent = await CreateComposer(new FailingSender(), loggerFactory)
            .TrySendAsync(Content with { Kind = "backup-ask" }, new EmailRecipient("ada@example.com", "Ada"), "slot 12", TestContext.Current.CancellationToken);

        Assert.False(sent);
        var entry = Assert.Single(logs.Composer);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("Couldn't queue the backup-ask email (slot 12)", entry.Message);
        Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.DoesNotContain("ada@example.com", entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnEmailWithoutAKindIsLoggedAsOther()
    {
        var logs = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));

        await CreateComposer(new FailingSender(), loggerFactory).TrySendAsync(Content, new EmailRecipient("ada@example.com"), "a sample", TestContext.Current.CancellationToken);

        Assert.Equal("Couldn't queue the other email (a sample)", Assert.Single(logs.Composer).Message);
    }

    [Fact]
    public async Task ACancelledSendIsntSwallowed()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateComposer(new CancelledSender()).TrySendAsync(Content, new EmailRecipient("ada@example.com"), "slot 12", cancelled.Token));
    }

    [Fact]
    public void TheOrganizersInboxIsTheContactAddress() =>
        Assert.Equal(new EmailRecipient("hello@csadvent.christmas", "C# Advent organizers"), Site.OrganizersInbox);

    private sealed class SendingSender : IEmailSender
    {
        public List<OutgoingEmail> Sent { get; } = [];

        public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
        {
            Sent.Add(email);
            return Task.CompletedTask;
        }
    }

    /// <summary>As the queue does when the database fails: an exception whose message could quote anything.</summary>
    private sealed class FailingSender : IEmailSender
    {
        public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The database is down.");
    }

    private sealed class CancelledSender : IEmailSender
    {
        public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default) =>
            Task.FromCanceled(cancellationToken);
    }

    private sealed record LogEntry(string Category, LogLevel Level, string Message, Exception? Exception);

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<LogEntry> Entries { get; } = [];

        /// <summary>What the composer logged (the renderer logs too, at Debug).</summary>
        public IEnumerable<LogEntry> Composer => Entries.Where(e => e.Category == typeof(EmailComposer).FullName);

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, Entries);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, List<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Add(new LogEntry(category, logLevel, formatter(state, exception), exception));
        }
    }
}
