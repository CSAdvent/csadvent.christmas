namespace CSAdvent.Web.Auth;

public sealed class AccountOptions
{
    public const string SectionName = "Accounts";

    /// <summary>GitHub user IDs of the organizers. IDs never change, unlike logins.</summary>
    public long[] Admins { get; set; } = [];

    /// <summary>The GitHub OAuth app. Required in production; optional in development, where dev sign-in works without it.</summary>
    public GitHubAppOptions GitHub { get; set; } = new();

    /// <summary>
    /// Lets anyone sign in as any GitHub user without GitHub, for local development and the integration tests. The AppHost
    /// turns it on when running locally, and it's ignored outside the Development environment.
    /// </summary>
    public bool DevSignIn { get; set; }
}

public sealed class GitHubAppOptions
{
    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public bool IsConfigured => !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret);
}
