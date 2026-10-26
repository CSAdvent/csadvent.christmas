using System.Diagnostics;
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
    /// How long to wait for an email, or for the queue to empty. Generous, because a busy machine (several test runs at once)
    /// slows the queue down, and a passing test never waits this long.
    /// </summary>
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(90);

    /// <summary>MailPit's search returns a page at a time (50 by default), newest first, so searches go through every page.</summary>
    private const int PageSize = 200;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// Waits for the email to <paramref name="to"/> whose subject contains <paramref name="subject"/>, and whose text contains
    /// <paramref name="containing"/> if it's given. Other tests send to the organizers too, so pick a subject, or text, only
    /// this test produces: the organizers' inbox gets the same subject for the same day of every season.
    /// </summary>
    public static async Task<MailPitMessage> WaitForEmailAsync(this AppHostFixture fixture, string to, string subject, string? containing = null)
    {
        using var mailPit = CreateClient(fixture);
        var waited = Stopwatch.StartNew();
        var seen = new HashSet<string>();
        while (true)
        {
            foreach (var found in await SearchAsync(mailPit, to, subject))
            {
                if (containing is null)
                {
                    return await GetMessageAsync(mailPit, found.Id);
                }

                // Each message is only read once: it either has the text or it doesn't.
                if (seen.Add(found.Id) && await GetMessageAsync(mailPit, found.Id) is { } message && message.Text.Contains(containing, StringComparison.Ordinal))
                {
                    return message;
                }
            }

            if (waited.Elapsed > WaitLimit)
            {
                var what = containing is null ? $"\"{subject}\" in the subject" : $"\"{subject}\" in the subject and \"{containing}\" in the text";
                throw new InvalidOperationException($"No email to {to} with {what} arrived in {WaitLimit.TotalSeconds:N0} seconds. {await DescribeQueueAsync(fixture, to)}");
            }

            await Task.Delay(100, CancellationToken);
        }
    }

    /// <summary>
    /// How many emails to <paramref name="to"/> have <paramref name="subject"/> in their subject, once the email queue has
    /// nothing left for them. Only use it after whatever queues them has finished, such as a completed reminder run. Never
    /// for the organizers' inbox, whose queue other tests keep filling: count what was queued about your own spot instead.
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
        var waited = Stopwatch.StartNew();
        while (await db.EmailOutbox.AnyAsync(e => e.Status == QueuedEmailStatus.Pending && e.ToAddress.ToLower() == address, CancellationToken))
        {
            if (waited.Elapsed > WaitLimit)
            {
                throw new InvalidOperationException($"Emails to {to} are still waiting in the email queue after {WaitLimit.TotalSeconds:N0} seconds. {await DescribeQueueAsync(fixture, to)}");
            }

            await Task.Delay(100, CancellationToken);
        }
    }

    public static async Task<Dictionary<string, string[]>> GetHeadersAsync(this AppHostFixture fixture, MailPitMessage message)
    {
        using var mailPit = CreateClient(fixture);
        return (await mailPit.GetFromJsonAsync<Dictionary<string, string[]>>($"/api/v1/message/{message.Id}/headers", CancellationToken))!;
    }

    private static HttpClient CreateClient(AppHostFixture fixture) => new() { BaseAddress = fixture.App.GetEndpoint("email", "http") };

    private static async Task<MailPitMessage> GetMessageAsync(HttpClient mailPit, string id) =>
        (await mailPit.GetFromJsonAsync<MailPitMessage>($"/api/v1/message/{id}", CancellationToken))!;

    /// <summary>
    /// The emails to <paramref name="to"/> whose subject contains <paramref name="subject"/>, newest first. MailPit is asked
    /// for the subject too (unless it has a quote in it, which its search syntax can't take), so the organizers' inbox, which
    /// every test shares, never pushes this test's email off the first page (#198); the subject is checked exactly here.
    /// </summary>
    private static async Task<List<MailPitSummary>> SearchAsync(HttpClient mailPit, string to, string subject)
    {
        var terms = subject.Contains('"', StringComparison.Ordinal) ? $"to:\"{to}\"" : $"to:\"{to}\" subject:\"{subject}\"";
        var query = Uri.EscapeDataString(terms);
        var found = new List<MailPitSummary>();
        for (var start = 0; ; start += PageSize)
        {
            var page = await mailPit.GetFromJsonAsync<MailPitSearch>($"/api/v1/search?query={query}&start={start}&limit={PageSize}", CancellationToken);
            found.AddRange(page!.Messages.Where(m => m.Subject.Contains(subject, StringComparison.Ordinal)));
            if (page.Messages.Count < PageSize)
            {
                return found;
            }
        }
    }

    /// <summary>What the email queue has for the address, for a failure message: was the email queued, and did it go?</summary>
    private static async Task<string> DescribeQueueAsync(AppHostFixture fixture, string to)
    {
        await using var db = await fixture.CreateDbContextAsync();
        var address = to.ToLowerInvariant();
        var statuses = await db.EmailOutbox
            .Where(e => e.ToAddress.ToLower() == address)
            .GroupBy(e => e.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(CancellationToken.None);
        var pending = await db.EmailOutbox.CountAsync(e => e.Status == QueuedEmailStatus.Pending, CancellationToken.None);
        return statuses.Count == 0
            ? $"The email queue has nothing for that address ({pending} waiting for anyone)."
            : $"The email queue has {string.Join(", ", statuses.Select(s => $"{s.Count} {s.Status}"))} for that address ({pending} waiting for anyone).";
    }
}

internal sealed record MailPitAddress(string Name, string Address);

internal sealed record MailPitSummary(string Id, string Subject);

internal sealed record MailPitSearch(List<MailPitSummary> Messages);

internal sealed record MailPitMessage(string Id, string Subject, MailPitAddress From, List<MailPitAddress> To, List<MailPitAddress> ReplyTo, string Html, string Text);
