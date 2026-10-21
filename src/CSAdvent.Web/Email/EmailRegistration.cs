using Azure.Communication.Email;
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
                $"ConnectionStrings:{ConnectionName} must be an SMTP endpoint (such as MailPit's) or an Azure Communication Services connection string.")
            .ValidateOnStart();

        builder.Services.AddSingleton<EmailComposer>();
        builder.Services.AddSingleton<IEmailSender>(services =>
        {
            var options = services.GetRequiredService<IOptions<EmailOptions>>();
            var endpoint = EmailEndpoint.Parse(options.Value.ConnectionString);
            return endpoint.Transport switch
            {
                EmailTransport.Smtp => new SmtpEmailSender(endpoint, options),
                _ => new AzureEmailSender(new EmailClient(endpoint.ConnectionString), options),
            };
        });

        return builder;
    }
}
