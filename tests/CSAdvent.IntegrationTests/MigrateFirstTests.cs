#pragma warning disable ASPIREPIPELINES001 // Aspire's deployment pipeline API is still marked experimental.

using System.Text;
using Aspire.Hosting.Pipelines;
using Azure.Core;
using CSAdvent.AppHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace CSAdvent.IntegrationTests;

/// <summary>
/// The deploy step that runs the migrations job before the web app is updated (#151): where it goes in Aspire's pipeline
/// (<see cref="MigrateFirst"/>), and how it starts the job and waits for it (<see cref="ContainerAppJobRunner"/>), against
/// a fake Azure Resource Manager. Nothing here needs the AppHost or Docker.
/// </summary>
public sealed class MigrateFirstTests
{
    private const string JobId = "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/rg-csadvent/providers/Microsoft.App/jobs/migrations";
    private const string Start = $"https://management.azure.com{JobId}/start?api-version=2025-07-01";
    private const string Execution = $"https://management.azure.com{JobId}/executions/migrations-abc123?api-version=2025-07-01";
    private const string Started = """{ "name": "migrations-abc123", "id": "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/rg-csadvent/providers/Microsoft.App/jobs/migrations/executions/migrations-abc123" }""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static string Status(string status) => $$"""{ "name": "migrations-abc123", "properties": { "status": "{{status}}" } }""";

    private static ContainerAppJobRunner Runner(FakeResourceManager arm, FakeTimeProvider? time = null, FakeCredential? credential = null) =>
        new(new HttpClient(arm), credential ?? new FakeCredential(), time ?? new FakeTimeProvider(), NullLogger.Instance) { PollInterval = TimeSpan.Zero };

    [Fact]
    public async Task StartsTheJobAsDeployedAndWaitsForItToSucceed()
    {
        var arm = new FakeResourceManager(
            _ => Answer(HttpStatusCode.Accepted, Started),
            _ => Answer(HttpStatusCode.OK, Status("Running")),
            _ => Answer(HttpStatusCode.OK, Status("Succeeded")));

        var execution = await Runner(arm).RunAsync(JobId, CancellationToken);

        Assert.Equal("migrations-abc123", execution);
        Assert.Collection(
            arm.Requests,
            start =>
            {
                Assert.Equal((HttpMethod.Post, Start, "Bearer token-1"), (start.Method, start.Url, start.Authorization));
                // No body: a body would replace the job's template for this run.
                Assert.Null(start.Body);
            },
            poll => Assert.Equal((HttpMethod.Get, Execution, "Bearer token-1"), (poll.Method, poll.Url, poll.Authorization)),
            poll => Assert.Equal((HttpMethod.Get, Execution), (poll.Method, poll.Url)));
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Stopped")]
    [InlineData("Degraded")]
    public async Task AnExecutionThatDoesNotSucceedStopsTheDeploy(string status)
    {
        var arm = new FakeResourceManager(
            _ => Answer(HttpStatusCode.Accepted, Started),
            _ => Answer(HttpStatusCode.OK, Status("Running")),
            _ => Answer(HttpStatusCode.OK, Status(status)));

        var failure = await Assert.ThrowsAsync<DistributedApplicationException>(() => Runner(arm).RunAsync(JobId, CancellationToken));

        Assert.StartsWith($"The migrations job's execution migrations-abc123 ended as {status}, so the web app wasn't updated", failure.Message);
        Assert.Equal(3, arm.Requests.Count);
    }

    [Fact]
    public async Task ARefusedStartSaysWhatAzureSaidAndIsNotRetried()
    {
        var arm = new FakeResourceManager(_ => Answer(HttpStatusCode.Forbidden,
            """{ "error": { "code": "AuthorizationFailed", "message": "The client does not have authorization to perform action 'Microsoft.App/jobs/start/action'." } }"""));

        var failure = await Assert.ThrowsAsync<DistributedApplicationException>(() => Runner(arm).RunAsync(JobId, CancellationToken));

        Assert.Equal(
            "Azure refused to start the migrations job, so the web app wasn't updated. Azure said: HTTP 403 AuthorizationFailed: The " +
            "client does not have authorization to perform action 'Microsoft.App/jobs/start/action'.",
            failure.Message);
        Assert.Single(arm.Requests);
    }

    [Fact]
    public async Task AStartWithNoAnswerIsNotRetried()
    {
        // A second start could run the migrations twice at once.
        var arm = new FakeResourceManager(_ => throw new HttpRequestException("Connection reset by peer"));

        var failure = await Assert.ThrowsAsync<DistributedApplicationException>(() => Runner(arm).RunAsync(JobId, CancellationToken));

        Assert.Contains("may or may not have started", failure.Message);
        Assert.Single(arm.Requests);
    }

    [Fact]
    public async Task FollowsTheLocationWhenTheStartDoesNotNameTheExecution()
    {
        const string location = "https://management.azure.com/subscriptions/00000000-0000-0000-0000-000000000001/providers/Microsoft.App/locations/centralus/jobOperationResults/42?api-version=2025-07-01";
        var arm = new FakeResourceManager(
            _ => Answer(HttpStatusCode.Accepted, "", location),
            _ => Answer(HttpStatusCode.Accepted, "", location),
            _ => Answer(HttpStatusCode.OK, Started),
            _ => Answer(HttpStatusCode.OK, Status("Succeeded")));

        var execution = await Runner(arm).RunAsync(JobId, CancellationToken);

        Assert.Equal("migrations-abc123", execution);
        Assert.Equal([Start, location, location, Execution], arm.Requests.Select(r => r.Url));
    }

    [Fact]
    public async Task NeverSendsTheTokenAnywhereButResourceManager()
    {
        var arm = new FakeResourceManager(_ => Answer(HttpStatusCode.Accepted, "", "https://example.com/operations/42"));

        var failure = await Assert.ThrowsAsync<DistributedApplicationException>(() => Runner(arm).RunAsync(JobId, CancellationToken));

        Assert.Contains("a Location on example.com", failure.Message);
        Assert.Equal([Start], arm.Requests.Select(r => r.Url));
    }

    [Fact]
    public async Task AStartWithNoWayToFollowTheExecutionStopsTheDeploy()
    {
        var arm = new FakeResourceManager(_ => Answer(HttpStatusCode.OK, "{}"));

        var failure = await Assert.ThrowsAsync<DistributedApplicationException>(() => Runner(arm).RunAsync(JobId, CancellationToken));

        Assert.StartsWith("Azure started the migrations job (HTTP 200) without saying which execution it started", failure.Message);
    }

    [Fact]
    public async Task KeepsAskingThroughErrorsThatPass()
    {
        var arm = new FakeResourceManager(
            _ => Answer(HttpStatusCode.Accepted, Started),
            // The execution can take a moment to show up.
            _ => Answer(HttpStatusCode.NotFound, """{ "error": { "code": "ResourceNotFound" } }"""),
            _ => Answer(HttpStatusCode.ServiceUnavailable, ""),
            _ => throw new HttpRequestException("Name or service not known"),
            _ => Answer(HttpStatusCode.TooManyRequests, ""),
            _ => Answer(HttpStatusCode.OK, "not JSON"),
            _ => Answer(HttpStatusCode.OK, Status("Running")),
            _ => Answer(HttpStatusCode.OK, Status("Succeeded")));

        Assert.Equal("migrations-abc123", await Runner(arm).RunAsync(JobId, CancellationToken));
        Assert.Equal(8, arm.Requests.Count);
    }

    [Fact]
    public async Task AnErrorThatWillNotPassStopsTheDeploy()
    {
        var arm = new FakeResourceManager(
            _ => Answer(HttpStatusCode.Accepted, Started),
            _ => Answer(HttpStatusCode.Unauthorized, """{ "error": { "code": "ExpiredAuthenticationToken", "message": "The token expired." } }"""));

        var failure = await Assert.ThrowsAsync<DistributedApplicationException>(() => Runner(arm).RunAsync(JobId, CancellationToken));

        Assert.Equal(
            "Couldn't find out how the migrations job's execution migrations-abc123 is doing, so the web app wasn't updated. Check the " +
            "job's execution history in the Azure portal. Azure said: HTTP 401 ExpiredAuthenticationToken: The token expired.",
            failure.Message);
    }

    [Fact]
    public async Task GivesUpWhenTheExecutionTakesTooLong()
    {
        var time = new FakeTimeProvider();
        var arm = new FakeResourceManager(
            _ => Answer(HttpStatusCode.Accepted, Started),
            _ =>
            {
                time.Advance(TimeSpan.FromMinutes(1));
                return Answer(HttpStatusCode.OK, Status("Running"));
            });

        var failure = await Assert.ThrowsAsync<DistributedApplicationException>(() => Runner(arm, time).RunAsync(JobId, CancellationToken));

        Assert.Equal(
            "The migrations job's execution migrations-abc123 didn't finish within 10 minutes (it was Running), and may still be " +
            "running. The deploy stopped before updating the web app, so the site still runs the previous version. Check the " +
            "migrations job's execution history in the Azure portal.",
            failure.Message);
        Assert.Equal(11, arm.Requests.Count);
    }

    [Fact]
    public async Task GivesUpWhenAzureNeverNamesTheExecution()
    {
        var time = new FakeTimeProvider();
        const string location = "https://management.azure.com/operations/42";
        var arm = new FakeResourceManager(
            _ => Answer(HttpStatusCode.Accepted, "", location),
            _ =>
            {
                time.Advance(TimeSpan.FromMinutes(1));
                return Answer(HttpStatusCode.Accepted, "", location);
            });

        var failure = await Assert.ThrowsAsync<DistributedApplicationException>(() => Runner(arm, time).RunAsync(JobId, CancellationToken));

        Assert.StartsWith("Azure didn't say which execution of the migrations job it started within 10 minutes.", failure.Message);
    }

    [Fact]
    public async Task KeepsItsTokenUntilItIsAboutToExpire()
    {
        var time = new FakeTimeProvider();
        var credential = new FakeCredential(time, lifetime: TimeSpan.FromMinutes(7));
        var arm = new FakeResourceManager(
            _ => Answer(HttpStatusCode.Accepted, Started),
            _ =>
            {
                time.Advance(TimeSpan.FromMinutes(1));
                return Answer(HttpStatusCode.OK, Status("Running"));
            },
            _ =>
            {
                time.Advance(TimeSpan.FromMinutes(1));
                return Answer(HttpStatusCode.OK, Status("Running"));
            },
            _ =>
            {
                time.Advance(TimeSpan.FromMinutes(1));
                return Answer(HttpStatusCode.OK, Status("Running"));
            },
            _ => Answer(HttpStatusCode.OK, Status("Succeeded")));

        await Runner(arm, time, credential).RunAsync(JobId, CancellationToken);

        // Each new token runs az in CI. This one lasts 7 minutes, so it's replaced once less than 5 are left.
        Assert.Equal(["Bearer token-1", "Bearer token-1", "Bearer token-1", "Bearer token-1", "Bearer token-2"], arm.Requests.Select(r => r.Authorization));
        Assert.Equal(["https://management.azure.com/.default"], credential.Scopes.Distinct());
    }

    [Fact]
    public async Task ACancelledDeployIsNotTakenForAnAzureError()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var arm = new FakeResourceManager(
            _ => Answer(HttpStatusCode.Accepted, Started),
            _ =>
            {
                cancellation.Cancel();
                throw new TaskCanceledException();
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runner(arm).RunAsync(JobId, cancellation.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("migrations")]
    [InlineData("/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/rg-csadvent/providers/Microsoft.App/containerApps/web")]
    [InlineData("https://management.azure.com/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/rg-csadvent/providers/Microsoft.App/jobs/migrations")]
    public async Task OnlyStartsAContainerAppsJob(string id)
    {
        var arm = new FakeResourceManager(_ => Answer(HttpStatusCode.Accepted, Started));

        await Assert.ThrowsAsync<DistributedApplicationException>(() => Runner(arm).RunAsync(id, CancellationToken));

        Assert.Empty(arm.Requests);
    }

    [Fact]
    public void PutsTheMigrationsBetweenTheJobAndTheWebApp()
    {
        var (web, migrations, webTarget, jobTarget) = DeployedResources();
        var steps = new[]
        {
            Step(MigrateFirst.StepName),
            Step("provision-web-containerapp", webTarget, WellKnownPipelineTags.ProvisionInfrastructure),
            Step("provision-migrations-containerapp", jobTarget, WellKnownPipelineTags.ProvisionInfrastructure),
            Step("print-web-summary", webTarget, "print-summary"),
        };

        Assert.True(MigrateFirst.Wire(Context(steps), web, migrations));

        Assert.Equal(["provision-migrations-containerapp"], steps[0].DependsOnSteps);
        Assert.Equal([MigrateFirst.StepName], steps[1].DependsOnSteps);
        Assert.Empty(steps[2].DependsOnSteps);
        Assert.Empty(steps[3].DependsOnSteps);
    }

    [Fact]
    public void ChangesNothingBeforeTheContainerAppsArePrepared()
    {
        // As the AppHost starts, and for --list-steps, there are no deployment targets yet, so nothing gets deployed.
        var steps = new[] { Step(MigrateFirst.StepName) };

        Assert.False(MigrateFirst.Wire(Context(steps), new ContainerResource("web"), new ContainerResource("migrations")));

        Assert.Empty(steps[0].DependsOnSteps);
    }

    [Fact]
    public void RefusesToDeployWhenItCannotFindTheStepThatUpdatesTheWebApp()
    {
        // If an Aspire update changed how the web app is provisioned, the two would otherwise go side by side again.
        var (web, migrations, _, jobTarget) = DeployedResources();
        var steps = new[]
        {
            Step(MigrateFirst.StepName),
            Step("provision-migrations-containerapp", jobTarget, WellKnownPipelineTags.ProvisionInfrastructure),
            Step("deploy-web-somehow", null, WellKnownPipelineTags.ProvisionInfrastructure),
        };

        var failure = Assert.Throws<InvalidOperationException>(() => MigrateFirst.Wire(Context(steps), web, migrations));

        Assert.StartsWith("The run-migrations step can't find the step that provisions 'web' in Azure", failure.Message);
    }

    private static (IResource Web, IResource Migrations, IResource WebTarget, IResource JobTarget) DeployedResources()
    {
        var web = new ContainerResource("web");
        var migrations = new ContainerResource("migrations");
        var webTarget = new ContainerResource("web-containerapp");
        var jobTarget = new ContainerResource("migrations-containerapp");
        web.Annotations.Add(new DeploymentTargetAnnotation(webTarget));
        migrations.Annotations.Add(new DeploymentTargetAnnotation(jobTarget));
        return (web, migrations, webTarget, jobTarget);
    }

    private static PipelineStep Step(string name, IResource? resource = null, params string[] tags) =>
        new() { Name = name, Action = _ => Task.CompletedTask, Resource = resource, Tags = [.. tags] };

    private static PipelineConfigurationContext Context(IReadOnlyList<PipelineStep> steps) => new()
    {
        Services = new ServiceCollection().BuildServiceProvider(),
        Steps = steps,
        Model = new DistributedApplicationModel(new Resources()),
    };

    private sealed class Resources : List<IResource>, IResourceCollection;

    private static HttpResponseMessage Answer(HttpStatusCode status, string body, string? location = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (location is not null)
        {
            response.Headers.Location = new Uri(location);
        }

        return response;
    }

    /// <summary>Answers each request with the next response, repeating the last one.</summary>
    private sealed class FakeResourceManager(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new SentRequest(
                request.Method,
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return responses[Math.Min(Requests.Count, responses.Length) - 1](request);
        }
    }

    private sealed record SentRequest(HttpMethod Method, string Url, string? Authorization, string? Body);

    /// <summary>Hands out token-1, token-2…, each lasting the given time (an hour, as az's do, by default).</summary>
    private sealed class FakeCredential(TimeProvider? time = null, TimeSpan? lifetime = null) : TokenCredential
    {
        private int _issued;

        public List<string> Scopes { get; } = [];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Scopes.AddRange(requestContext.Scopes);
            _issued++;
            return new AccessToken($"token-{_issued}", (time ?? TimeProvider.System).GetUtcNow() + (lifetime ?? TimeSpan.FromHours(1)));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
