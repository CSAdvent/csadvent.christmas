using System.ComponentModel.DataAnnotations;

namespace CSAdvent.Web.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// The sender. In production it has to be a sender address on the verified Azure Communication Services domain, whose
    /// sender username also sets the display name people see.
    /// </summary>
    [Required(ErrorMessage = "Email:FromAddress is required.")]
    [EmailAddress(ErrorMessage = "Email:FromAddress must be an email address.")]
    public string FromAddress { get; set; } = "";

    /// <summary>The sender's display name over SMTP (MailPit). Azure Communication Services uses the sender username's.</summary>
    public string FromName { get; set; } = "C# Advent";

    /// <summary>
    /// <c>ConnectionStrings:email</c> from the AppHost: MailPit's SMTP endpoint locally, an Azure Communication Services
    /// connection string in production.
    /// </summary>
    public string? ConnectionString { get; set; }
}
