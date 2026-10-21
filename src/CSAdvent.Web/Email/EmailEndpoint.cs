using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace CSAdvent.Web.Email;

public enum EmailTransport
{
    /// <summary>SMTP, such as MailPit in development (<c>Endpoint=smtp://localhost:1025</c>).</summary>
    Smtp,

    /// <summary>Azure Communication Services (<c>endpoint=https://….communication.azure.com/;accesskey=…</c>).</summary>
    AzureCommunicationServices,
}

/// <summary>Where email goes, read from the <c>email</c> connection string.</summary>
public sealed record EmailEndpoint(EmailTransport Transport, Uri Uri, NetworkCredential? Credentials, string ConnectionString)
{
    public static bool TryParse(string? connectionString, [NotNullWhen(true)] out EmailEndpoint? endpoint)
    {
        endpoint = null;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        var builder = new DbConnectionStringBuilder();
        try
        {
            builder.ConnectionString = connectionString;
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!builder.TryGetValue("Endpoint", out var value) || !Uri.TryCreate(value as string, UriKind.Absolute, out var uri))
        {
            return false;
        }

        EmailTransport? transport = uri.Scheme switch
        {
            "smtp" or "smtps" => EmailTransport.Smtp,
            "https" when builder.ContainsKey("AccessKey") => EmailTransport.AzureCommunicationServices,
            _ => null,
        };
        if (transport is null)
        {
            return false;
        }

        var credentials = builder.TryGetValue("Username", out var username) && username is string { Length: > 0 } user
            ? new NetworkCredential(user, builder.TryGetValue("Password", out var password) ? password as string : null)
            : null;

        endpoint = new EmailEndpoint(transport.Value, uri, credentials, connectionString);
        return true;
    }

    public static EmailEndpoint Parse(string? connectionString) =>
        TryParse(connectionString, out var endpoint)
            ? endpoint
            : throw new FormatException("The email connection string must be an SMTP endpoint or an Azure Communication Services connection string.");
}
