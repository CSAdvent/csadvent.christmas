using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Logging;

namespace CSAdvent.AppHost;

/// <summary>
/// Starts a Container Apps job through Azure Resource Manager and waits for that execution to succeed (#151). It makes the
/// same two calls the deploy workflow made with <c>az containerapp job start</c> and <c>az containerapp job execution
/// show</c> before #151: a <c>POST</c> to the job's <c>start</c>, with no body so the job runs as deployed, and a
/// <c>GET</c> of the execution until it ends. Every wait has a deadline (<see cref="Timeout"/>), and every failure throws
/// a <see cref="DistributedApplicationException"/> whose message says what happened, so the deploy stops with it.
/// </summary>
internal sealed class ContainerAppJobRunner(HttpClient http, TokenCredential credential, TimeProvider time, ILogger logger)
{
    /// <summary>Azure Resource Manager. The bearer token is only ever sent here.</summary>
    public static readonly Uri ResourceManager = new("https://management.azure.com/");

    /// <summary>The API version Aspire's generated Bicep deploys the job with, and the one the Azure CLI uses.</summary>
    public const string ApiVersion = "2025-07-01";

    /// <summary>How long a request may take before it counts as failed: a hung request never holds up the deadline for long.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    /// <summary>How often to ask how the execution is doing.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long the execution may take, from the start request, before the deploy gives up on it. The migrations took about a
    /// minute in October 2026; the job's own limit (<c>replicaTimeout</c>) is 30 minutes.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);

    private AccessToken? _token;

    /// <summary>Starts the job, waits for the execution to succeed, and returns the execution's name.</summary>
    /// <param name="jobId">The job's Azure resource ID (<c>/subscriptions/…/providers/Microsoft.App/jobs/{name}</c>).</param>
    public async Task<string> RunAsync(string jobId, CancellationToken cancellationToken)
    {
        if (!IsJobId(jobId))
        {
            throw new DistributedApplicationException($"\"{jobId}\" isn't the resource ID of a Container Apps job, so the migrations job wasn't started.");
        }

        var deadline = time.GetUtcNow() + Timeout;
        var execution = await StartAsync(jobId, deadline, cancellationToken);
        logger.LogInformation("Started the migrations job's execution {Execution}. Waiting for it to finish.", execution);

        await WaitForSuccessAsync(jobId, execution, deadline, cancellationToken);
        logger.LogInformation("The migrations job's execution {Execution} succeeded.", execution);
        return execution;
    }

    /// <summary>Whether the ID names a Container Apps job.</summary>
    public static bool IsJobId(string? jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId) || !jobId.StartsWith("/subscriptions/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var id = new ResourceIdentifier(jobId);
            return id.ResourceType == "Microsoft.App/jobs" && !string.IsNullOrEmpty(id.SubscriptionId) && !string.IsNullOrEmpty(id.ResourceGroupName);
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            return false;
        }
    }

    private async Task<string> StartAsync(string jobId, DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        // Never retried: a second start would run the migrations twice at once. A failure here stops the deploy, and the next
        // deploy starts it again.
        HttpResponseMessage response;
        try
        {
            response = await SendAsync(HttpMethod.Post, Address($"{jobId}/start"), cancellationToken);
        }
        catch (Exception e) when (IsTransient(e, cancellationToken))
        {
            throw new DistributedApplicationException(
                $"Azure didn't answer the request to start the migrations job ({e.Message}), so it may or may not have started. The " +
                "web app wasn't updated. Check the job's execution history in the Azure portal, then deploy again.", e);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new DistributedApplicationException(
                    "Azure refused to start the migrations job, so the web app wasn't updated. Azure said: " +
                    await DescribeAsync(response, cancellationToken));
            }

            // Azure answers with the new execution's name and ID, which is what az containerapp job start prints.
            if (await ReadExecutionNameAsync(response, cancellationToken) is { } name)
            {
                return name;
            }

            // The API's documentation only promises a Location to ask for the name, so follow it when there's no name.
            if (response.StatusCode == HttpStatusCode.Accepted && response.Headers.Location is { } location)
            {
                return await WaitForExecutionNameAsync(location, deadline, cancellationToken);
            }

            throw new DistributedApplicationException(
                $"Azure started the migrations job (HTTP {(int)response.StatusCode}) without saying which execution it started, so the " +
                "deploy can't tell whether it succeeded. The web app wasn't updated. Check the job's execution history in the Azure " +
                "portal, then deploy again.");
        }
    }

    private async Task<string> WaitForExecutionNameAsync(Uri location, DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        var address = location.IsAbsoluteUri ? location : new Uri(ResourceManager, location);
        if (address.Scheme != Uri.UriSchemeHttps || !string.Equals(address.Host, ResourceManager.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new DistributedApplicationException(
                $"Azure answered the migrations job's start with a Location on {address.Host}, not Azure Resource Manager, so the " +
                "deploy didn't follow it. The web app wasn't updated. Check the job's execution history in the Azure portal.");
        }

        while (true)
        {
            await PauseUnlessPastAsync(
                deadline,
                minutes => $"Azure didn't say which execution of the migrations job it started within {minutes} minutes.",
                cancellationToken);
            try
            {
                using var response = await SendAsync(HttpMethod.Get, address, cancellationToken);
                if (response.StatusCode == HttpStatusCode.Accepted || IsTransient(response.StatusCode))
                {
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new DistributedApplicationException(
                        "Couldn't find out which execution of the migrations job started, so the web app wasn't updated. Check the " +
                        "job's execution history in the Azure portal. Azure said: " + await DescribeAsync(response, cancellationToken));
                }

                return await ReadExecutionNameAsync(response, cancellationToken)
                    ?? throw new DistributedApplicationException(
                        "Azure finished starting the migrations job without saying which execution it started. The web app wasn't " +
                        "updated. Check the job's execution history in the Azure portal.");
            }
            catch (Exception e) when (IsTransient(e, cancellationToken))
            {
                logger.LogWarning("Couldn't ask Azure which execution of the migrations job started ({Error}). Trying again.", e.Message);
            }
        }
    }

    private async Task WaitForSuccessAsync(string jobId, string execution, DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        var address = Address($"{jobId}/executions/{Uri.EscapeDataString(execution)}");
        string? lastStatus = null;
        while (true)
        {
            var status = await GetStatusAsync(address, execution, cancellationToken);
            switch (status)
            {
                case "Succeeded":
                    return;
                case "Failed" or "Stopped" or "Degraded":
                    throw new DistributedApplicationException(
                        $"The migrations job's execution {execution} ended as {status}, so the web app wasn't updated: the site still " +
                        "runs the previous version, on the database as the migrations left it. The execution's logs are in the Azure " +
                        "portal, under the migrations job's execution history.");
            }

            if (status is not null && status != lastStatus)
            {
                logger.LogInformation("The migrations job's execution {Execution} is {Status}.", execution, status);
                lastStatus = status;
            }

            await PauseUnlessPastAsync(
                deadline,
                minutes => $"The migrations job's execution {execution} didn't finish within {minutes} minutes (it was " +
                    $"{lastStatus ?? "not found"}), and may still be running.",
                cancellationToken);
        }
    }

    /// <summary>The execution's status, or null when Azure couldn't say this time (which the next poll asks again).</summary>
    private async Task<string?> GetStatusAsync(Uri address, string execution, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, address, cancellationToken);

            // An execution can take a moment to show up after the start.
            if (response.StatusCode == HttpStatusCode.NotFound || IsTransient(response.StatusCode))
            {
                logger.LogWarning("Azure couldn't say how the migrations job's execution {Execution} is doing (HTTP {Status}). Asking again.", execution, (int)response.StatusCode);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new DistributedApplicationException(
                    $"Couldn't find out how the migrations job's execution {execution} is doing, so the web app wasn't updated. Check " +
                    "the job's execution history in the Azure portal. Azure said: " + await DescribeAsync(response, cancellationToken));
            }

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("properties", out var properties)
                && properties.ValueKind == JsonValueKind.Object
                && properties.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.String
                    ? status.GetString()
                    : null;
        }
        catch (JsonException e)
        {
            logger.LogWarning("Azure's answer about the migrations job's execution {Execution} wasn't JSON ({Error}). Asking again.", execution, e.Message);
            return null;
        }
        catch (Exception e) when (IsTransient(e, cancellationToken))
        {
            logger.LogWarning("Couldn't ask Azure how the migrations job's execution {Execution} is doing ({Error}). Asking again.", execution, e.Message);
            return null;
        }
    }

    /// <summary>Waits for the next poll, or throws once the deadline has passed, saying what didn't happen in time.</summary>
    private async Task PauseUnlessPastAsync(DateTimeOffset deadline, Func<string, string> whatDidNotHappen, CancellationToken cancellationToken)
    {
        if (time.GetUtcNow() >= deadline)
        {
            throw new DistributedApplicationException(
                $"{whatDidNotHappen(Timeout.TotalMinutes.ToString("0.#", CultureInfo.InvariantCulture))} The deploy stopped before " +
                "updating the web app, so the site still runs the previous version. Check the migrations job's execution history " +
                "in the Azure portal.");
        }

        await Task.Delay(PollInterval, time, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri address, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, address);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await http.SendAsync(request, cancellationToken);
    }

    /// <summary>A token for Resource Manager, kept until five minutes before it expires: in CI each new one runs <c>az</c>.</summary>
    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_token is not { } token || token.ExpiresOn - time.GetUtcNow() < TimeSpan.FromMinutes(5))
        {
            token = await credential.GetTokenAsync(new TokenRequestContext([$"{ResourceManager}.default"]), cancellationToken);
            _token = token;
        }

        return token.Token;
    }

    private static Uri Address(string path) => new(ResourceManager, $"{path.TrimStart('/')}?api-version={ApiVersion}");

    private static async Task<string?> ReadExecutionNameAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("name", out var name)
                && name.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(name.GetString())
                    ? name.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The status code with Azure's error code and message, when it sent one.</summary>
    private static async Task<string> DescribeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var description = $"HTTP {(int)response.StatusCode}";
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object)
            {
                var code = error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                var message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                var withCode = string.IsNullOrEmpty(code) ? description : $"{description} {code}";
                return string.IsNullOrEmpty(message) ? withCode : $"{withCode}: {message}";
            }
        }
        catch (JsonException)
        {
        }

        return body.Length == 0 ? description : $"{description}: {(body.Length > 300 ? body[..300] + "…" : body)}";
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;

    /// <summary>A failed request (no answer, or a request timeout), as opposed to the deploy being cancelled.</summary>
    private static bool IsTransient(Exception e, CancellationToken cancellationToken) =>
        e is HttpRequestException || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested);
}
