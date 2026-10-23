using System.ComponentModel.DataAnnotations;

namespace CSAdvent.Web.Claims;

/// <summary>What a contributor fills in to request a slot. The email comes from their verified GitHub address instead.</summary>
public sealed class ClaimForm
{
    [Required(ErrorMessage = "Enter the name to show on the calendar.")]
    [StringLength(50, ErrorMessage = "Keep your name to 50 characters or fewer.")]
    public string DisplayName { get; set; } = "";

    [StringLength(200, ErrorMessage = "Keep the link to 200 characters or fewer.")]
    [HttpUrl(ErrorMessage = "Enter a full web address that starts with https:// or http://.")]
    [NotSocialProfile]
    public string? Link { get; set; }

    [StringLength(200, ErrorMessage = "Keep comments to 200 characters or fewer.")]
    public string? Comments { get; set; }

    [SocialAccount(SocialPlatform.Bluesky, ErrorMessage = "Enter a Bluesky handle, such as you.bsky.social.")]
    public string? Bluesky { get; set; }

    [SocialAccount(SocialPlatform.Mastodon, ErrorMessage = "Enter a Mastodon handle, such as @you@mastodon.social.")]
    public string? Mastodon { get; set; }

    [SocialAccount(SocialPlatform.LinkedIn, ErrorMessage = "Enter your LinkedIn profile URL, such as https://www.linkedin.com/in/you.")]
    public string? LinkedIn { get; set; }

    [SocialAccount(SocialPlatform.Facebook, ErrorMessage = "Enter your Facebook profile or Page URL, such as https://www.facebook.com/you.")]
    public string? Facebook { get; set; }

    [SocialAccount(SocialPlatform.Reddit, ErrorMessage = "Enter a Reddit username, such as u/you.")]
    public string? Reddit { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "Agree to publish your post on your day to continue.")]
    public bool AgreeToTiming { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "Agree to the terms to continue.")]
    public bool AgreeToTerms { get; set; }
}

/// <summary>An absolute http or https URL. Empty is allowed; use [Required] when it isn't.</summary>
public sealed class HttpUrlAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) =>
        value is not string text
        || string.IsNullOrWhiteSpace(text)
        || (Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https");
}
