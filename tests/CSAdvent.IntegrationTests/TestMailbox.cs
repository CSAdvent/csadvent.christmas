using System.Net.Http.Json;
using CSAdvent.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// Reads what the site sent from MailPit's API. Every test run starts with an empty MailPit. The site queues its emails
/// (#152), and the queue's worker sends them a moment later, so these wait for it.
/// </summary>
internal static class TestMailbox
{
    /// <summary>
    /// Waits for the email to <paramref name="to"/> whose subject contains <paramref name="subject"/>. Other tests send to
    /// the organizers too, so pick a subject only this test produces.
    /// </summary>
    public static async Task<MailPitMessage> WaitForEmailAsync(this AppHostFixture fixture, string to, string subject)
    {
        using var mailPit = CreateClient(fixture);

        for (var attempt = 0; attempt < 200; attempt++)
        {
            if ((await SearchAsync(mailPit, to, subject)).FirstOrDefault() is { } found)
            {
                return (await mailPit.GetFromJsonAsync<MailPitMessage>($"/api/v1/message/{found.Id}", TestContext.Current.CancellationToken))!;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        throw new InvalidOperationException($"No email to {to} with \"{subject}\" in the subject arrived.");
    }

    /// <summary>
    /// How many emails to <paramref name="to"/> have <paramref name="subject"/> in their subject, once the email queue has
    /// nothing left for them. Only use it after whatever queues them has finished, such as a completed reminder run.
    /// </summary>
    public static async Task<int> CountEmailsAsync(this AppHostFixture fixture, string to, string subject)
    {
        await fixture.WaitForQueueAsync(to);
        using var mailPit = CreateClient(fixture);
        return (await SearchAsync(mailPit, to, subject)).Count;
    }

    /// <summary>Waits until no email to <paramref name="to"/> is waiting in the email queue.</summary>
    public static async Task WaitForQueueAsync(this AppHostFixture fixture, string to)
    {
        await using var db = await fixture.CreateDbContextAsync();
        var address = to.ToLowerInvariant();
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (!await db.EmailOutbox.AnyAsync(e => e.Status == QueuedEmailStatus.Pending && e.ToAddress.ToLower() == address, TestContext.Current.CancellationToken))
            {
                return;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        throw new InvalidOperationException($"Emails to {to} are still waiting in the email queue.");
    }

    public static async Task<Dictionary<string, string[]>> GetHeadersAsync(this AppHostFixture fixture, MailPitMessage message)
    {
        using var mailPit = CreateClient(fixture);
        return (await mailPit.GetFromJsonAsync<Dictionary<string, string[]>>($"/api/v1/message/{message.Id}/headers", TestContext.Current.CancellationToken))!;
    }

    private static HttpClient CreateClient(AppHostFixture fixture) => new() { BaseAddress = fixture.App.GetEndpoint("email", "http") };

    private static async Task<List<MailPitSummary>> SearchAsync(HttpClient mailPit, string to, string subject)
    {
        var query = Uri.EscapeDataString($"to:\"{to}\"");
        var search = await mailPit.GetFromJsonAsync<MailPitSearch>($"/api/v1/search?query={query}", TestContext.Current.CancellationToken);
        return [.. search!.Messages.Where(m => m.Subject.Contains(subject, StringComparison.Ordinal))];
    }
}

internal sealed record MailPitAddress(string Name, string Address);

internal sealed record MailPitSummary(string Id, string Subject);

internal sealed record MailPitSearch(List<MailPitSummary> Messages);

internal sealed record MailPitMessage(string Id, string Subject, MailPitAddress From, List<MailPitAddress> To, List<MailPitAddress> ReplyTo, string Html, string Text);
