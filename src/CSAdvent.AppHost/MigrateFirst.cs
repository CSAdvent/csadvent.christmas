#pragma warning disable ASPIREPIPELINES001 // Aspire's deployment pipeline API is still marked experimental.

using Aspire.Hosting.Azure;
using Aspire.Hosting.Pipelines;
using Microsoft.Extensions.DependencyInjection;

namespace CSAdvent.AppHost;

/// <summary>
/// Makes <c>aspire deploy</c> run the migrations job, and wait for it to succeed, before it updates the web app (#151), so
/// a new revision never meets an older schema. Left to itself, Aspire provisions the job and the web app side by side and
/// never starts the job, so every deploy used to run new code on the old schema until the workflow started the job.
/// <para>
/// The <see cref="StepName"/> step comes after the step that provisions the job (with the new migration bundle's image) and
/// before the one that provisions the web app, which is the only step that changes it. When the migrations fail, or don't
/// finish in time, the step throws, Aspire doesn't run the steps after it, and the old revision keeps serving.
/// </para>
/// <para>
/// A readiness check on the schema wouldn't do instead: Azure only finishes updating a container app once its new revision
/// is ready, and Aspire waits for that before running any later step, so the job would never get to run.
/// </para>
/// </summary>
internal static class MigrateFirst
{
    /// <summary>The pipeline step. The deploy workflow checks for this file to know a commit has it (rolling back, #179).</summary>
    public const string StepName = "run-migrations";

    /// <summary>The job's Bicep output with its resource ID, set in AppHost.cs.</summary>
    public const string JobIdOutput = "id";

    /// <summary>Adds the step, and orders it between the job and the web app on every pipeline run that deploys them.</summary>
    public static void AddMigrateFirst(this IDistributedApplicationBuilder builder, IResource web, IResource migrations)
    {
        var wiring = new Wiring();

        builder.Pipeline.AddStep(new PipelineStep
        {
            Name = StepName,
            Description = "Runs the migrations job, and waits for it to succeed, before the web app is updated.",
            Action = context => RunAsync(context, migrations, wiring),
            RequiredBySteps = [WellKnownPipelineSteps.Deploy],
        });

        // Configuration runs each time the pipeline's steps are worked out: as the AppHost starts, for --list-steps, and for
        // the run itself, which is the one that counts.
        builder.Pipeline.AddPipelineConfiguration(context =>
        {
            wiring.IsWired = Wire(context, web, migrations);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Puts the step after the job's provisioning and before the web app's. Returns false, changing nothing, while neither has
    /// a deployment target yet: Aspire only prepares them once the AppHost has started (never for --list-steps), and without
    /// one nothing is deployed. Throws when they're there but the steps aren't, rather than let the two go side by side.
    /// </summary>
    internal static bool Wire(PipelineConfigurationContext context, IResource web, IResource migrations)
    {
        var step = context.Steps.Single(s => s.Name == StepName);
        var jobTarget = migrations.GetDeploymentTargetAnnotation()?.DeploymentTarget;
        var webTarget = web.GetDeploymentTargetAnnotation()?.DeploymentTarget;
        if (jobTarget is null && webTarget is null)
        {
            return false;
        }

        foreach (var jobStep in ProvisionSteps(context, jobTarget, migrations))
        {
            step.DependsOn(jobStep);
        }

        foreach (var webStep in ProvisionSteps(context, webTarget, web))
        {
            webStep.DependsOn(step);
        }

        return true;
    }

    private static List<PipelineStep> ProvisionSteps(PipelineConfigurationContext context, IResource? target, IResource resource)
    {
        var steps = target is null ? [] : context.GetSteps(target, WellKnownPipelineTags.ProvisionInfrastructure).ToList();
        if (steps.Count == 0)
        {
            throw new InvalidOperationException(
                $"The {StepName} step can't find the step that provisions '{resource.Name}' in Azure (tagged " +
                $"'{WellKnownPipelineTags.ProvisionInfrastructure}'), so it can't run the migrations before the web app is updated. " +
                "Did an Aspire update change the deployment pipeline? See MigrateFirst.cs.");
        }

        return steps;
    }

    private static async Task RunAsync(PipelineStepContext context, IResource migrations, Wiring wiring)
    {
        if (!wiring.IsWired)
        {
            throw new DistributedApplicationException(
                $"The {StepName} step isn't between the migrations job and the web app in this deploy, so it didn't start the job.");
        }

        var target = migrations.GetDeploymentTargetAnnotation()?.DeploymentTarget as AzureBicepResource
            ?? throw new DistributedApplicationException($"'{migrations.Name}' isn't deployed as an Azure resource, so its job can't be started.");

        // The job's provisioning step has finished by now, so its outputs are there. The limit is only so that can never hang.
        string? jobId;
        using (var outputTimeout = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken))
        {
            outputTimeout.CancelAfter(TimeSpan.FromMinutes(1));
            try
            {
                jobId = await new BicepOutputReference(JobIdOutput, target).GetValueAsync(outputTimeout.Token);
            }
            catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested)
            {
                throw new DistributedApplicationException(
                    "The migrations job's resource ID wasn't there a minute after it was provisioned, so the job wasn't started and the " +
                    "web app wasn't updated.");
            }
        }

        var credential = context.Services.GetRequiredService<ITokenCredentialProvider>().TokenCredential;
        using var http = new HttpClient { Timeout = ContainerAppJobRunner.RequestTimeout };
        var runner = new ContainerAppJobRunner(http, credential, TimeProvider.System, context.Logger);
        var execution = await runner.RunAsync(jobId ?? "", context.CancellationToken);

        await context.ReportingStep.CompleteAsync(
            new MarkdownString($"The migrations succeeded (execution **{execution}**)"),
            CompletionState.Completed,
            context.CancellationToken);
    }

    /// <summary>Whether the latest working out of the pipeline put the step in place.</summary>
    private sealed class Wiring
    {
        public bool IsWired { get; set; }
    }
}
