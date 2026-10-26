using Azure.Communication.Email;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using CSAdvent.Web.Email.Queue;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Email;

public static class EmailRegistration
{
    /// <summary>The AppHost's email resource: MailPit locally, Azure Communication Services in production.</summary>
    public const string ConnectionName = "email";

    /// <summary>
    /// The key of the sender that really sends: the transport, behind <see cref="OptOutAwareEmailSender"/>. Only the email
    /// queue uses it; everything else gets the queue (<see cref="QueueingEmailSender"/>) as its <see cref="IEmailSender"/>.
    /// </summary>
    public const string TransportKey = "transport";

    /// <summary>
    /// Registers the email composer, the transport for <c>ConnectionStrings:email</c>, and the email queue (#152) in front of
    /// it, with the worker that drains it. The connection string is checked when the app starts, not here, because EF Core's
    /// tools and the migration bundle build the host without it.
    /// </summary>
    public static IHostApplicationBuilder AddEmail(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<EmailOptions>()
            .BindConfiguration(EmailOptions.SectionName)
            .Configure<IConfiguration>((options, configuration) => options.ConnectionString = configuration.GetConnectionString(ConnectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => EmailEndpoint.TryParse(options.ConnectionString, out _),
                $"ConnectionStrings:{ConnectionName} must be an SMTP endpoint (such as MailPit's) or an Azure Communication Services endpoint.")
            .ValidateOnStart();

        builder.Services.AddSingleton<EmailOptOutLinks>();
        builder.Services.AddScoped<EmailOptOuts>();
        builder.Services.AddScoped<IEmailOptOutList>(services => services.GetRequiredService<EmailOptOuts>());
        builder.Services.AddSingleton<EmailComposer>();
        builder.Services.AddKeyedSingleton<IEmailSender>(TransportKey, (services, _) =>
        {
            var options = services.GetRequiredService<IOptions<EmailOptions>>();
            var endpoint = EmailEndpoint.Parse(options.Value.ConnectionString);
            IEmailSender transport = endpoint.Transport switch
            {
                EmailTransport.Smtp => new SmtpEmailSender(endpoint, options),
                _ => new AzureEmailSender(CreateEmailClient(endpoint), options),
            };

            // No optional email goes to an address that unsubscribed, whatever sends it.
            return new OptOutAwareEmailSender(
                transport,
                services.GetRequiredService<EmailOptOutLinks>(),
                services.GetRequiredService<IServiceScopeFactory>(),
                services.GetRequiredService<ILogger<OptOutAwareEmailSender>>());
        });

        // Everything the site sends is queued, and one worker per replica sends it within the email service's limits,
        // which every replica shares through the queue's own table.
        builder.Services.AddOptions<EmailQueueOptions>()
            .BindConfiguration(EmailQueueOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddSingleton<EmailQueueSignal>();
        builder.Services.AddSingleton<QueueingEmailSender>();
        builder.Services.AddSingleton<IEmailSender>(services => services.GetRequiredService<QueueingEmailSender>());
        builder.Services.AddScoped<EmailOutbox>();
        builder.Services.AddScoped<IEmailOutboxStore, EmailOutboxStore>();
        builder.Services.AddScoped<EmailQueueDrain>();
        builder.Services.AddHostedService<EmailOutboxWorker>();

        return builder;
    }

    /// <summary>
    /// In production there's no key: the app's managed identity, which the AppHost lets send, signs in. Aspire sets
    /// AZURE_CLIENT_ID and AZURE_TOKEN_CREDENTIALS so <see cref="DefaultAzureCredential"/> picks that identity.
    /// </summary>
    private static EmailClient CreateEmailClient(EmailEndpoint endpoint)
    {
        var options = new EmailClientOptions { RetryPolicy = new RetryExceptThrottling() };
        return endpoint.HasAccessKey
            ? new EmailClient(endpoint.ConnectionString, options)
            : new EmailClient(endpoint.Uri, new DefaultAzureCredential(), options);
    }

    /// <summary>
    /// Azure's usual retries, except on 429: the email queue handles throttling itself (it stops, and honors Retry-After), so
    /// the client shouldn't sit on one email for minutes while the queue waits.
    /// </summary>
    private sealed class RetryExceptThrottling() : RetryPolicy(maxRetries: 3)
    {
        protected override bool ShouldRetry(HttpMessage message, Exception? exception) =>
            !IsThrottled(message) && base.ShouldRetry(message, exception);

        protected override ValueTask<bool> ShouldRetryAsync(HttpMessage message, Exception? exception) =>
            IsThrottled(message) ? ValueTask.FromResult(false) : base.ShouldRetryAsync(message, exception);

        private static bool IsThrottled(HttpMessage message) => message.HasResponse && message.Response.Status == 429;
    }
}
