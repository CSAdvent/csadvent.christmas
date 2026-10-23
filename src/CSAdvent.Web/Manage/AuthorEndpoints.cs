using CSAdvent.Web.Auth;
using Microsoft.AspNetCore.Mvc;

namespace CSAdvent.Web.Manage;

/// <summary>What the Link and Unlink buttons on the authors page post. Plain forms, because there's one per author.</summary>
public sealed class AuthorLinkForm
{
    public string Key { get; set; } = "";

    public string? Login { get; set; }
}

public static class AuthorEndpoints
{
    public static WebApplication MapAuthors(this WebApplication app)
    {
        // Binding a form makes these check the antiforgery token.
        app.MapPost("/manage/authors/link", async ([FromForm] AuthorLinkForm form, AuthorLinkService authors, CancellationToken cancellationToken) =>
            {
                var outcome = await authors.LinkAsync(form.Key, form.Login ?? "", cancellationToken);
                return Back(outcome switch
                {
                    AuthorLinkOutcome.Linked => "linked",
                    AuthorLinkOutcome.NoSuchAccount => "no-account",
                    AuthorLinkOutcome.GitHubUnavailable => "github-unavailable",
                    _ => "no-author",
                });
            })
            .RequireAuthorization(AdventPolicies.Admin);

        app.MapPost("/manage/authors/unlink", async ([FromForm] AuthorLinkForm form, AuthorLinkService authors, CancellationToken cancellationToken) =>
            {
                await authors.UnlinkAsync(form.Key, cancellationToken);
                return Back("unlinked");
            })
            .RequireAuthorization(AdventPolicies.Admin);

        return app;
    }

    private static IResult Back(string done) => Results.LocalRedirect($"/manage/authors?done={done}");
}
