using System.Net.Http.Json;

namespace CSAdvent.IntegrationTests;

/// <summary>Reads what the site sent from MailPit's API. Every test run starts with an empty MailPit.</summary>
internal static class TestMailbox
{
    /// <summary>
    /// Waits for the email to <paramref name="to"/> whose subject contains <paramref name="subject"/>. Other tests send to
    /// the organizers too, so pick a subject only this test produces.
    /// </summary>
    public static async Task<MailPitMessage> WaitForEmailAsync(this AppHostFixture fixture, string to, string subject)
    {
        using var mailPit = new HttpClient { BaseAddress = fixture.App.GetEndpoint("email", "http") };
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = Uri.EscapeDataString($"to:\"{to}\"");

        for (var attempt = 0; attempt < 50; attempt++)
        {
            var search = await mailPit.GetFromJsonAsync<MailPitSearch>($"/api/v1/search?query={query}", cancellationToken);
            if (search!.Messages.FirstOrDefault(m => m.Subject.Contains(subject, StringComparison.Ordinal)) is { } found)
            {
                return (await mailPit.GetFromJsonAsync<MailPitMessage>($"/api/v1/message/{found.Id}", cancellationToken))!;
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new InvalidOperationException($"No email to {to} with \"{subject}\" in the subject arrived.");
    }
}

internal sealed record MailPitAddress(string Name, string Address);

internal sealed record MailPitSummary(string Id, string Subject);

internal sealed record MailPitSearch(List<MailPitSummary> Messages);

internal sealed record MailPitMessage(string Subject, MailPitAddress From, List<MailPitAddress> To, List<MailPitAddress> ReplyTo, string Html, string Text);
