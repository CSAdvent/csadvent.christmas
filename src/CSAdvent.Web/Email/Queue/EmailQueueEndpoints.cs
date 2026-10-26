using CSAdvent.Web.Auth;
using Microsoft.AspNetCore.Mvc;

namespace CSAdvent.Web.Email.Queue;

/// <summary>What the failed emails' buttons on the queue page post. Plain forms, because there's one per email.</summary>
public sealed class EmailQueueForm
{
    public int Id { get; set; }
}

public static class EmailQueueEndpoints
{
    /// <summary>The organizers' view of the queue (<c>EmailQueue.razor</c>).</summary>
    public const string Page = "/manage/emails/queue";

    public static WebApplication MapEmailQueue(this WebApplication app)
    {
        // Binding a form makes these check the antiforgery token.
        var group = app.MapGroup(Page).RequireAuthorization(AdventPolicies.Admin);

        group.MapPost("/retry", async ([FromForm] EmailQueueForm form, EmailOutbox outbox, CancellationToken cancellationToken) =>
            Results.LocalRedirect($"{Page}?done={(await outbox.RetryAsync(form.Id, cancellationToken) ? "retried" : "gone")}"));

        group.MapPost("/discard", async ([FromForm] EmailQueueForm form, EmailOutbox outbox, CancellationToken cancellationToken) =>
            Results.LocalRedirect($"{Page}?done={(await outbox.DiscardAsync(form.Id, cancellationToken) ? "discarded" : "gone")}"));

        // After an outage, say: every failed email goes again.
        group.MapPost("/retry-all", async ([FromForm] EmailQueueForm form, EmailOutbox outbox, CancellationToken cancellationToken) =>
        {
            await outbox.RetryAllFailedAsync(cancellationToken);
            return Results.LocalRedirect($"{Page}?done=retried-all");
        });

        return app;
    }
}
