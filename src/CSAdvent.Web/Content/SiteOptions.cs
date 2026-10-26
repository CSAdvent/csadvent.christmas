using System.ComponentModel.DataAnnotations;
using CSAdvent.Web.Email;

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

    /// <summary>The organizers' inbox (<see cref="ContactEmail"/>) as an email recipient, for everything the site tells them.</summary>
    public EmailRecipient OrganizersInbox => new(ContactEmail, "C# Advent organizers");

    /// <summary>The site's public address, for links and images in emails, which can't be relative.</summary>
    [Required(ErrorMessage = "Site:BaseUrl is required. Emails use it for their links.")]
    [Url(ErrorMessage = "Site:BaseUrl must be an absolute URL, such as https://csadvent.christmas.")]
    public string BaseUrl { get; set; } = "";

    /// <summary>Who runs C# Advent, for the About page.</summary>
    public List<Organizer> Organizers { get; set; } = [];
}

/// <summary>An organizer: their name, and their GitHub login for the photo and profile link. The About page lists them in order.</summary>
public sealed class Organizer
{
    public string Name { get; set; } = "";

    public string GitHub { get; set; } = "";

    /// <summary>A line about what they do, under their name.</summary>
    public string? Role { get; set; }

    /// <summary>Who started C# Advent, and still hosts it. The About page features them above everything else.</summary>
    public bool Founder { get; set; }
}
