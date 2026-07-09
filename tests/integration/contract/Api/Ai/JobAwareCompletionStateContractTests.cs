using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests;

/// <summary>
/// Contract test for <c>JobAwareCompletionState v1</c> (task AIR2-014, spec FR-A0-07).
///
/// KEEP-path integration/contract test per ADR-038: it locks the SHAPE of the seam
/// (nine states, version field, tolerant reader, terminal map), proves the projection
/// is over the EXISTING <see cref="JobContract"/>/<see cref="JobStatus"/> (no new job
/// model), asserts the NFR-12 ingestion-parity distinction, and proves the ordered step
/// set is consumer-declared (Compose's steps AND a different consumer's steps both render).
///
/// Deliberately SELF-CONTAINED: no DI, no WebApplicationFactory, no Mock&lt;HttpMessageHandler&gt;,
/// no DI-registration assertions (ADR-038 B1-B5). It exercises the contract types directly so
/// Compose r2 can consume the seam with zero local variant.
/// </summary>
public class JobAwareCompletionStateContractTests
{
    private static readonly DateTimeOffset ObservedAt = DateTimeOffset.Parse("2026-07-08T12:00:00Z");

    private static JobContract NewJob(string jobType = "document-ingest") =>
        new() { JobId = Guid.NewGuid(), JobType = jobType };

    // ── Shape: nine states, version, terminal map ────────────────────────────

    [Fact]
    public void JobAwareState_DefinesExactlyTheNineContractStates()
    {
        var names = Enum.GetNames<JobAwareState>();
        names.Should().BeEquivalentTo(new[]
        {
            "Queued", "Running", "Partial", "Completed", "Failed",
            "Poisoned", "Cancelled", "RetryPending", "UserActionRequired",
        });
    }

    [Fact]
    public void JobAwareCompletionState_VersionField_DefaultsToOne()
    {
        var state = new JobAwareCompletionState
        {
            JobId = Guid.NewGuid(),
            JobType = "x",
            Steps = Array.Empty<JobStepCompletion>(),
            Aggregate = JobAwareState.Queued,
        };

        state.Version.Should().Be(1);
        JobAwareCompletionState.CurrentVersion.Should().Be(1);
    }

    [Theory]
    [InlineData(JobAwareState.Completed, true)]
    [InlineData(JobAwareState.Failed, true)]
    [InlineData(JobAwareState.Poisoned, true)]
    [InlineData(JobAwareState.Cancelled, true)]
    [InlineData(JobAwareState.Queued, false)]
    [InlineData(JobAwareState.Running, false)]
    [InlineData(JobAwareState.Partial, false)]
    [InlineData(JobAwareState.RetryPending, false)]
    [InlineData(JobAwareState.UserActionRequired, false)]
    public void IsTerminal_ForEachState_MatchesTheTerminalMap(JobAwareState state, bool expectedTerminal)
    {
        JobAwareCompletionState.IsTerminal(state).Should().Be(expectedTerminal);
    }

    // ── Tolerant reader: unknown extra field is ignored ──────────────────────

    [Fact]
    public void TolerantReader_UnknownExtraField_IsIgnoredOnDeserialize()
    {
        // A v2-ish payload carrying a field a v1 reader does not know about.
        var json = """
        {
            "version": 1,
            "jobId": "11111111-1111-1111-1111-111111111111",
            "jobType": "document-ingest",
            "steps": [ { "stepName": "record", "state": "Completed" } ],
            "aggregate": "Completed",
            "observedAt": "2026-07-08T12:00:00+00:00",
            "someFutureV2Field": { "nested": true }
        }
        """;

        var state = JsonSerializer.Deserialize<JobAwareCompletionState>(json);

        state.Should().NotBeNull();
        state!.Version.Should().Be(1);
        state.JobType.Should().Be("document-ingest");
        state.Aggregate.Should().Be(JobAwareState.Completed);
        state.Steps.Should().ContainSingle().Which.State.Should().Be(JobAwareState.Completed);
    }

    [Fact]
    public void Contract_SerializesStatesAsStableStrings_NotOrdinals()
    {
        var state = JobAwareCompletionStateProjector.Project(
            NewJob(),
            new[] { new StoredStepSignal { StepName = "record", StoredStatus = JobStatus.Completed } },
            ObservedAt);

        var json = JsonSerializer.Serialize(state);

        json.Should().Contain("\"Completed\"");
        json.Should().Contain("\"aggregate\"");
        json.Should().Contain("\"version\":1");
    }

    // ── Projection over the EXISTING job model (producer → consumer round-trip) ─

    [Fact]
    public void Producer_ProjectsRealJobContract_AndConsumerRendersIt()
    {
        var job = NewJob("compose-save-back");

        var state = JobAwareCompletionStateProjector.Project(
            job,
            new[]
            {
                new StoredStepSignal { StepName = "container", StoredStatus = JobStatus.Completed },
                new StoredStepSignal { StepName = "record", StoredStatus = JobStatus.Completed },
                new StoredStepSignal { StepName = "profile-analysis", Started = true },
                new StoredStepSignal { StepName = "indexing" },
            },
            ObservedAt);

        // Projection carries the REAL JobContract identity — proof it is a projection, not a fork.
        state.JobId.Should().Be(job.JobId);
        state.JobType.Should().Be("compose-save-back");

        var view = new JobAwareCompletionStateView(state);
        view.TotalStepCount.Should().Be(4);
        view.CompletedStepCount.Should().Be(2);
        view.ProgressLabel.Should().Be("2 of 4 steps complete");
    }

    [Fact]
    public void Producer_FailedWithAttemptsRemaining_MapsToRetryPending()
    {
        // Mirrors ServiceBusJobProcessor: Failed + attempts remaining → abandon/redeliver.
        var state = JobAwareCompletionStateProjector.Project(
            NewJob(),
            new[]
            {
                new StoredStepSignal
                {
                    StepName = "indexing",
                    StoredStatus = JobStatus.Failed,
                    Attempt = 1,
                    MaxAttempts = 3,
                },
            },
            ObservedAt);

        state.Steps.Single().State.Should().Be(JobAwareState.RetryPending);
        JobAwareCompletionState.IsTerminal(state.Aggregate).Should().BeFalse();
    }

    [Fact]
    public void Producer_PoisonedStatus_MapsToPoisonedTerminal()
    {
        var state = JobAwareCompletionStateProjector.Project(
            NewJob(),
            new[] { new StoredStepSignal { StepName = "indexing", StoredStatus = JobStatus.Poisoned } },
            ObservedAt);

        state.Steps.Single().State.Should().Be(JobAwareState.Poisoned);
        state.Aggregate.Should().Be(JobAwareState.Poisoned);
        JobAwareCompletionState.IsTerminal(state.Aggregate).Should().BeTrue();
    }

    // ── NFR-12 ingestion parity: record-exists ≠ downstream-finished ─────────

    [Fact]
    public void IngestionParity_RecordCompletedButIndexingRunning_AggregateIsPartialNotCompleted()
    {
        var state = JobAwareCompletionStateProjector.Project(
            NewJob(),
            new[]
            {
                new StoredStepSignal { StepName = "record", StoredStatus = JobStatus.Completed },
                new StoredStepSignal { StepName = "indexing", Started = true },
            },
            ObservedAt);

        // The invariant: a bare record row is NOT a successful document operation.
        state.Aggregate.Should().Be(JobAwareState.Partial);
        state.Aggregate.Should().NotBe(JobAwareState.Completed);

        var view = new JobAwareCompletionStateView(state);
        view.RecordExists("record").Should().BeTrue();
        view.DownstreamFinished.Should().BeFalse();
        view.RecordCreatedButDownstreamPending("record").Should().BeTrue();
    }

    [Fact]
    public void IngestionParity_AllStepsCompleted_AggregateIsCompleted()
    {
        var state = JobAwareCompletionStateProjector.Project(
            NewJob(),
            new[]
            {
                new StoredStepSignal { StepName = "record", StoredStatus = JobStatus.Completed },
                new StoredStepSignal { StepName = "indexing", StoredStatus = JobStatus.Completed },
            },
            ObservedAt);

        state.Aggregate.Should().Be(JobAwareState.Completed);
        new JobAwareCompletionStateView(state).DownstreamFinished.Should().BeTrue();
    }

    // ── NEGATIVE: store-before-render (ADR-040) ──────────────────────────────

    [Fact]
    public void StoreBeforeRender_ModelClaimsCompletionWithoutStoredStatus_DoesNotRenderCompleted()
    {
        // A caller "claims" the step is done (Started + a detail string) but NO stored
        // JobStatus.Completed backs it. The projector MUST NOT manufacture Completed.
        var state = JobAwareCompletionStateProjector.Project(
            NewJob(),
            new[]
            {
                new StoredStepSignal
                {
                    StepName = "record",
                    StoredStatus = null, // nothing durably stored
                    Started = true,
                    Detail = "model says this finished",
                },
            },
            ObservedAt);

        var step = state.Steps.Single();
        step.State.Should().NotBe(JobAwareState.Completed);
        step.State.Should().Be(JobAwareState.Running);
        state.Aggregate.Should().NotBe(JobAwareState.Completed);
        new JobAwareCompletionStateView(state).DownstreamFinished.Should().BeFalse();
    }

    // ── Consumer-declared step set: Compose's steps AND a different set ───────

    [Fact]
    public void ConsumerDeclaredSteps_ComposeStepSet_RendersInDeclaredOrder()
    {
        // Compose declares: container → record → profile-analysis → indexing.
        var state = JobAwareCompletionStateProjector.Project(
            NewJob("compose-save-back"),
            new[]
            {
                new StoredStepSignal { StepName = "container", StoredStatus = JobStatus.Completed },
                new StoredStepSignal { StepName = "record", StoredStatus = JobStatus.Completed },
                new StoredStepSignal { StepName = "profile-analysis", StoredStatus = JobStatus.Completed },
                new StoredStepSignal { StepName = "indexing", StoredStatus = JobStatus.Completed },
            },
            ObservedAt);

        state.Steps.Select(s => s.StepName).Should()
            .ContainInOrder("container", "record", "profile-analysis", "indexing");
        state.Aggregate.Should().Be(JobAwareState.Completed);
    }

    [Fact]
    public void ConsumerDeclaredSteps_DifferentStepSet_StillRendersCorrectly()
    {
        // A DIFFERENT consumer with unrelated steps — proves the contract does not
        // assume or hardcode Compose's step names.
        var state = JobAwareCompletionStateProjector.Project(
            NewJob("correspondence-draft"),
            new[]
            {
                new StoredStepSignal { StepName = "draft", StoredStatus = JobStatus.Completed },
                new StoredStepSignal { StepName = "review", AwaitingUserAction = true },
                new StoredStepSignal { StepName = "send" },
            },
            ObservedAt);

        state.Steps.Select(s => s.StepName).Should().ContainInOrder("draft", "review", "send");
        state.Steps[0].State.Should().Be(JobAwareState.Completed);
        state.Steps[1].State.Should().Be(JobAwareState.UserActionRequired);
        state.Steps[2].State.Should().Be(JobAwareState.Queued);

        // Aggregate surfaces the user-action gate (attention-requiring) over the partial progress.
        state.Aggregate.Should().Be(JobAwareState.UserActionRequired);

        var view = new JobAwareCompletionStateView(state);
        view.RecordExists("draft").Should().BeTrue();
        view.DownstreamFinished.Should().BeFalse();
    }

    [Fact]
    public void Cancellation_BeforeCompletion_MapsToCancelledTerminal()
    {
        var state = JobAwareCompletionStateProjector.Project(
            NewJob(),
            new[]
            {
                new StoredStepSignal { StepName = "record", StoredStatus = JobStatus.Completed },
                new StoredStepSignal { StepName = "indexing", Started = true, CancellationRequested = true },
            },
            ObservedAt);

        state.Steps[1].State.Should().Be(JobAwareState.Cancelled);
        state.Aggregate.Should().Be(JobAwareState.Cancelled);
        JobAwareCompletionState.IsTerminal(state.Aggregate).Should().BeTrue();
    }
}
