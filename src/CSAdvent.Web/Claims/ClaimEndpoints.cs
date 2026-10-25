using CSAdvent.Web.Auth;
using CSAdvent.Web.Manage;
using Microsoft.AspNetCore.Mvc;

namespace CSAdvent.Web.Claims;

/// <summary>
/// What the approve and decline buttons for other people's requests post: under "Settle the other requests" on a request's
/// page (#143), and on the requests list (#202). Plain forms, because there's one per request.
/// </summary>
public sealed class RequestDecisionForm
{
    public int RequestId { get; set; }

    /// <summary>The spot to approve it into, always chosen by the organizer. Zero means they haven't chosen one.</summary>
    public int SlotId { get; set; }

    /// <summary>The request whose page to go back to, or zero for the requests list.</summary>
    public int ReturnTo { get; set; }

    /// <summary>The season the requests list was showing.</summary>
    public int? Year { get; set; }
}

/// <summary>What a Withdraw button on My spots posts. A plain form, because there's one per request.</summary>
public sealed class WithdrawRequestForm
{
    public int RequestId { get; set; }
}

public static class ClaimEndpoints
{
    public static WebApplication MapClaims(this WebApplication app)
    {
        // Binding a form makes these check the antiforgery token.
        app.MapPost("/my-spots/requests/withdraw", async ([FromForm] WithdrawRequestForm form, HttpContext context, ClaimService claims, CancellationToken cancellationToken) =>
            {
                var withdrawn = await claims.WithdrawAsync(context.User.GetContributorId() ?? 0, form.RequestId, cancellationToken);
                return Results.LocalRedirect($"/my-spots?done={(withdrawn ? "withdrawn" : "not-withdrawn")}");
            })
            .RequireAuthorization();

        var manage = app.MapGroup("/manage/requests").RequireAuthorization(AdventPolicies.Admin);

        manage.MapPost("/approve", async ([FromForm] RequestDecisionForm form, ClaimReviewService reviews, CancellationToken cancellationToken) =>
        {
            if (form.SlotId == 0)
            {
                return Back(form, done: null, ReviewMessages.ChooseSpot);
            }

            var outcome = await reviews.ApproveAsync(form.RequestId, form.SlotId, cancellationToken);
            if (outcome is not ReviewOutcome.Approved)
            {
                return Back(form, done: null, ReviewMessages.Code(outcome));
            }

            // Approved from the list, but someone else asked for the spot since the list was shown: settle theirs on its page.
            return form.ReturnTo == 0 && await reviews.HasCompetingAsync(form.RequestId, cancellationToken)
                ? Results.LocalRedirect($"/manage/requests/{form.RequestId}?done=approved")
                : Back(form, "approved", error: null);
        });

        manage.MapPost("/decline", async ([FromForm] RequestDecisionForm form, ClaimReviewService reviews, CancellationToken cancellationToken) =>
        {
            var outcome = await reviews.DeclineAsync(form.RequestId, cancellationToken);
            return outcome is ReviewOutcome.Declined ? Back(form, "declined", error: null) : Back(form, done: null, ReviewMessages.Code(outcome));
        });

        // Every other request for an approved request's spot at once.
        manage.MapPost("/decline-competing", async ([FromForm] RequestDecisionForm form, ClaimReviewService reviews, CancellationToken cancellationToken) =>
        {
            await reviews.DeclineCompetingAsync(form.RequestId, cancellationToken);
            return Results.LocalRedirect($"/manage/requests/{form.RequestId}?settled=declined");
        });

        return app;
    }

    /// <summary>Back to the request's page the buttons were on, or to the requests list, saying what happened.</summary>
    private static IResult Back(RequestDecisionForm form, string? done, string? error)
    {
        if (form.ReturnTo > 0)
        {
            return Results.LocalRedirect($"/manage/requests/{form.ReturnTo}?{(done is null ? $"error={error}" : $"settled={done}")}");
        }

        var year = form.Year is { } shown ? $"year={shown}&" : "";
        return Results.LocalRedirect(done is null
            ? $"/manage/requests?{year}error={error}&request={form.RequestId}"
            : $"/manage/requests?{year}{done}={form.RequestId}");
    }
}
