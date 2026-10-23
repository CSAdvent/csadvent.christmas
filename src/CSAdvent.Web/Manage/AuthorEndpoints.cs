using CSAdvent.Web.Auth;
using Microsoft.AspNetCore.Mvc;

namespace CSAdvent.Web.Manage;

/// <summary>What the Link button on an unlinked author's row posts. A plain form, because there's one per author.</summary>
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
        app.MapPost("/manage/authors/link", async ([FromForm] AuthorLinkForm form, AuthorService authors, CancellationToken cancellationToken) =>
            {
                var (outcome, _) = await authors.LinkAsync(form.Key, form.Login ?? "", cancellationToken);
                return Back(outcome switch
                {
                    AuthorLinkOutcome.Linked => "linked",
                    AuthorLinkOutcome.NoSuchAccount => "no-account",
                    AuthorLinkOutcome.GitHubUnavailable => "github-unavailable",
                    _ => "no-author",
                });
            })
            .RequireAuthorization(AdventPolicies.Admin);

        return app;
    }

    private static IResult Back(string done) => Results.LocalRedirect($"/manage/authors?done={done}");
}
