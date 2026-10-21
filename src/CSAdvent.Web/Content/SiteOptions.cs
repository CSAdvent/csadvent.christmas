using System.ComponentModel.DataAnnotations;

namespace CSAdvent.Web.Content;

public sealed class SiteOptions
{
    public const string SectionName = "Site";

    /// <summary>
    /// How people reach the organizers, shown on the privacy and terms pages. Required: a privacy policy has to say who to
    /// contact, so the app won't start without it.
    /// </summary>
    [Required(ErrorMessage = "Site:ContactEmail is required. It's shown on the privacy and terms pages.")]
    [EmailAddress(ErrorMessage = "Site:ContactEmail must be an email address.")]
    public string ContactEmail { get; set; } = "";
}
