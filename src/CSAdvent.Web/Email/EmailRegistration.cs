using Azure.Communication.Email;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace CSAdvent.Web.Email;

public static class EmailRegistration
{
    /// <summary>The AppHost's email resource: MailPit locally, Azure Communication Services in production.</summary>
    public const string ConnectionName = "email";

    /// <summary>
    /// Registers the email composer and the sender for <c>ConnectionStrings:email</c>. The connection string is checked
    /// when the app starts, not here, because EF Core's tools and the migration bundle build the host without it.
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
        builder.Services.AddSingleton<IEmailSender>(services =>
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

        return builder;
    }

    /// <summary>
    /// In production there's no key: the app's managed identity, which the AppHost lets send, signs in. Aspire sets
    /// AZURE_CLIENT_ID and AZURE_TOKEN_CREDENTIALS so <see cref="DefaultAzureCredential"/> picks that identity.
    /// </summary>
    private static EmailClient CreateEmailClient(EmailEndpoint endpoint) =>
        endpoint.HasAccessKey
            ? new EmailClient(endpoint.ConnectionString)
            : new EmailClient(endpoint.Uri, new DefaultAzureCredential());
}
