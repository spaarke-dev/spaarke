using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Intake;
using Sprk.Bff.Api.Services.Intake.Harnesses;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Intake;

/// <summary>
/// Resume after a crash (spaarke-intake-pipeline-r1 task 013, spec FR-07a, SC-04): a delivery that dies right after a stage
/// stored its result is redelivered, and the run continues from the stored state with zero repeated AI calls, harness steps,
/// persists or door returns. Also the external-step wait (task 014's seam): a dispatched engine step is never dispatched
/// twice, and its recorded result resumes the run at the next step.
/// </summary>
/// <remarks>Maintain-class (ADR-038): the "never pay for the AI twice, never return twice" guarantee of the run row.</remarks>
public sealed class IntakeRunResumeTests
{
    public static TheoryData<string> CrashPoints() => new()
    {
        "stage:Receive",
        "stage:Store",
        "stage:Classify",
        "stage:Route",
        "step:segment",
        "step:edit-plan",
        "step:redline",
        "stage:Persist",
        "marker:approve",
    };

    [Theory]
    [MemberData(nameof(CrashPoints))]
    public async Task ProcessAsync_CrashRightAfterAStageStoredItsResult_ResumesWithNoRepeatedAiOrHarnessWork(string crashPoint)
    {
        var bed = new IntakeRunTestBed();
        var runId = await bed.CreateRunAsync();
        bed.Dataverse.CrashAfter(Condition(crashPoint));
        var jobId = Guid.NewGuid();

        var crashed = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId, jobId, deliveryCount: 1), CancellationToken.None);
        var redelivered = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId, jobId, deliveryCount: 2), CancellationToken.None);

        crashed.Status.Should().Be(JobStatus.Failed, "the crash is retried, not dead-lettered");
        redelivered.Status.Should().Be(JobStatus.Completed);
        bed.RequestStage().Should().Be(IntakeRequestStages.AwaitingApproval);
        bed.ClassifyCalls.Should().Be(1, "a stored classification is never asked for again");
        bed.Harness.StepCalls.Should().BeEquivalentTo(new Dictionary<string, int> { ["segment"] = 1, ["edit-plan"] = 1, ["redline"] = 1 },
            "a stored step result (segments, the edit plan, the redline) is never produced again");
        bed.Harness.PersistCalls.Should().Be(1);
        bed.Run(runId).State.Completed.Should().Equal("Receive", "Store", "Classify", "Route", "Analyse", "Build", "Persist");

        bed.Approve(runId);
        await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);
        bed.Door.Keys.Should().ContainSingle();
        bed.RunStatus(runId).Should().Be((int)ProcessingJobStatus.Completed);
    }

    [Fact]
    public async Task ProcessAsync_CrashRightAfterTheReturnWasSent_DoesNotReturnTwice()
    {
        var bed = new IntakeRunTestBed();
        var runId = await bed.CreateRunAsync();
        await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);
        bed.Approve(runId);
        bed.Dataverse.CrashAfter(s => s.IsCompleted(IntakeRunStage.Return));

        var crashed = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);
        var redelivered = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId, deliveryCount: 2), CancellationToken.None);

        crashed.Status.Should().Be(JobStatus.Failed);
        redelivered.Status.Should().Be(JobStatus.Completed);
        bed.Door.Keys.Should().Equal($"intake-return:{runId:N}");
        bed.RunStatus(runId).Should().Be((int)ProcessingJobStatus.Completed);
        bed.RequestStage().Should().Be(IntakeRequestStages.Returned);
    }

    // ── External engine steps (task 014 plugs in here) ────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_ExternalStepDispatched_WaitsWithoutRedispatchingAndResumesAtTheNextStepOnItsResult()
    {
        var bed = new IntakeRunTestBed { Harness = new RecordingHarness("nda", externalEngineSteps: true) };
        var runId = await bed.CreateRunAsync();

        var dispatched = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);

        dispatched.Status.Should().Be(JobStatus.Completed, "a dispatched engine job is a wait, not a failure");
        var run = bed.Run(runId);
        run.CurrentStage.Should().Be(nameof(IntakeRunStage.Analyse));
        run.State.Awaiting.Should().BeEquivalentTo(new { Step = "segment", Dispatched = true });
        run.State.Claim.Should().BeNull();
        bed.Dataverse.Get(IntakeRunStore.RunEntity, runId).GetAttributeValue<string>(IntakeRunStore.RunEngineExecution).Should().Be("exec-segment");

        // A duplicate delivery while the engine runs does not dispatch it again.
        var duplicate = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);
        duplicate.Status.Should().Be(JobStatus.Completed);
        bed.Harness.StepCalls["segment"].Should().Be(1);

        // The engine completion (task 014) records the result; a result for a step the run is not awaiting is refused.
        (await bed.Store.RecordExternalStepResultAsync(runId, "redline", RecordingHarness.Reference("redline", runId), CancellationToken.None))
            .Should().BeFalse();
        (await bed.Store.RecordExternalStepResultAsync(runId, "segment", RecordingHarness.Reference("segment", runId), CancellationToken.None))
            .Should().BeTrue();
        (await bed.Store.RecordExternalStepResultAsync(runId, "segment", RecordingHarness.Reference("segment", runId), CancellationToken.None))
            .Should().BeFalse("a repeated completion records nothing");

        var resumed = await bed.CreateHandler().ProcessAsync(IntakeRunTestBed.Job(runId), CancellationToken.None);

        resumed.Status.Should().Be(JobStatus.Completed);
        bed.Harness.StepCalls.Should().BeEquivalentTo(new Dictionary<string, int> { ["segment"] = 1, ["edit-plan"] = 1, ["redline"] = 1 });
        bed.Run(runId).State.Awaiting!.Step.Should().Be("redline", "the run continued past segment and edit-plan to the next engine step");
        bed.ClassifyCalls.Should().Be(1);
    }

    private static Func<IntakeRunState, bool> Condition(string crashPoint)
    {
        var (kind, name) = (crashPoint.Split(':')[0], crashPoint.Split(':')[1]);
        return kind switch
        {
            "stage" => s => s.IsCompleted(Enum.Parse<IntakeRunStage>(name)),
            "step" => s => s.Steps.ContainsKey(name),
            "marker" => s => s.Markers.ContainsKey(name),
            _ => throw new ArgumentOutOfRangeException(nameof(crashPoint)),
        };
    }
}
