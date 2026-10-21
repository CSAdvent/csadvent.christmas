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

    /// <summary>The site's public address, for links and images in emails, which can't be relative.</summary>
    [Required(ErrorMessage = "Site:BaseUrl is required. Emails use it for their links.")]
    [Url(ErrorMessage = "Site:BaseUrl must be an absolute URL, such as https://csadvent.christmas.")]
    public string BaseUrl { get; set; } = "";
}
