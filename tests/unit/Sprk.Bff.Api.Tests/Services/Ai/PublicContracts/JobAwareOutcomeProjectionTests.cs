using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.PublicContracts;

/// <summary>
/// FR-A1-07 / NFR-12 (task AIR2-036) — contract-anchor tests for
/// <see cref="JobAwareOutcomeProjection"/>, the integration layer that layers
/// <c>JobAwareCompletionState v1</c> (task 014) onto the <see cref="OutcomeCard"/> (task 011).
///
/// These are MAINTAIN-class: they protect a concrete production behavior — a bare
/// <c>sprk_document</c> row whose downstream analysis/indexing has not finished must NEVER
/// render as a successful document operation (the R-2 ingestion-parity invariant). Deleting
/// any of them would let that regression ship silently.
///
/// The state under test is built with the REAL task-014 <see cref="JobAwareCompletionStateProjector"/>
/// over a REAL <see cref="JobContract"/> + <see cref="JobStatus"/>, proving the integration is a
/// projection over the EXISTING job pipeline — no new job model. Self-contained: no DI, no
/// WebApplicationFactory, no <c>Mock&lt;HttpMessageHandler&gt;</c>, no DI-registration assertions (ADR-038).
/// </summary>
public sealed class JobAwareOutcomeProjectionTests
{
    private static readonly DateTimeOffset ObservedAt = DateTimeOffset.Parse("2026-07-09T12:00:00Z");
    private const string LedgerKey = "binding-1@t1";

    private static JobContract NewJob(string jobType = "document-ingest") =>
        new() { JobId = Guid.NewGuid(), JobType = jobType };

    private static OutcomeSummary Summary(string s = "Creating the document…") => new(s);

    private static StoredStepSignal Completed(string step) =>
        new() { StepName = step, StoredStatus = JobStatus.Completed };

    private static StoredStepSignal Queued(string step) =>
        new() { StepName = step };

    private static StoredStepSignal Running(string step) =>
        new() { StepName = step, Started = true };

    private static JobAwareCompletionState Project(JobContract job, params StoredStepSignal[] steps) =>
        JobAwareCompletionStateProjector.Project(job, steps, ObservedAt);

    // ── Aggregate → OutcomeStatus: the ingestion-parity guard (NFR-12) ────────────

    [Fact]
    public void ToOutcomeStatus_WhenAggregateCompleted_ReturnsSucceeded()
    {
        JobAwareOutcomeProjection.ToOutcomeStatus(JobAwareState.Completed)
            .Should().Be(OutcomeStatus.Succeeded);
    }

    [Fact]
    public void ToOutcomeStatus_WhenAggregatePartial_ReturnsPartialNeverSucceeded()
    {
        // Partial = "record exists but downstream not finished" — the NFR-12 signal.
        JobAwareOutcomeProjection.ToOutcomeStatus(JobAwareState.Partial)
            .Should().Be(OutcomeStatus.Partial);
    }

    [Theory]
    [InlineData(JobAwareState.Queued)]
    [InlineData(JobAwareState.Running)]
    [InlineData(JobAwareState.Partial)]
    [InlineData(JobAwareState.RetryPending)]
    [InlineData(JobAwareState.UserActionRequired)]
    public void ToOutcomeStatus_ForEveryUnfinishedAggregate_IsNeverSucceeded(JobAwareState aggregate)
    {
        // The load-bearing invariant: an operation that has not durably finished is never Succeeded.
        JobAwareOutcomeProjection.ToOutcomeStatus(aggregate)
            .Should().NotBe(OutcomeStatus.Succeeded);
    }

    [Theory]
    [InlineData(JobAwareState.Failed)]
    [InlineData(JobAwareState.Poisoned)]
    [InlineData(JobAwareState.Cancelled)]
    public void ToOutcomeStatus_ForTerminalNonSuccess_ReturnsFailed(JobAwareState aggregate)
    {
        JobAwareOutcomeProjection.ToOutcomeStatus(aggregate)
            .Should().Be(OutcomeStatus.Failed);
    }

    // ── The canonical bare-row case: document-create with indexing still pending ──

    [Fact]
    public void ForJobAwareOutcome_WhenRecordExistsButIndexingPending_CardIsPartialNotSucceeded()
    {
        // Compose's declared steps: container → record → profile-analysis → indexing.
        // The record row EXISTS (record + container completed) but indexing is still queued.
        var state = Project(
            NewJob(),
            Completed("container"),
            Completed("record"),
            Running("profile-analysis"),
            Queued("indexing"));

        state.Aggregate.Should().Be(JobAwareState.Partial, "some steps done, not all → ingestion-parity signal");

        var card = JobAwareOutcomeProjection.ForJobAwareOutcome(LedgerKey, state, Summary());

        // NFR-12: a bare document row is NOT rendered as a successful document operation.
        card.Status.Should().Be(OutcomeStatus.Partial);
        card.Completion!.Mode.Should().Be(OutcomeCompletionMode.JobAware);

        // The user can still see the record step succeeded and downstream is in flight.
        var indexing = card.Completion.Steps.Single(s => s.Key == "indexing");
        var record = card.Completion.Steps.Single(s => s.Key == "record");
        record.Status.Should().Be("succeeded");
        indexing.Status.Should().Be("pending");
    }

    [Fact]
    public void ForJobAwareOutcome_WhenEveryStepCompleted_CardIsSucceeded()
    {
        var state = Project(
            NewJob(),
            Completed("container"),
            Completed("record"),
            Completed("profile-analysis"),
            Completed("indexing"));

        var card = JobAwareOutcomeProjection.ForJobAwareOutcome(LedgerKey, state, Summary("Document ready."));

        card.Status.Should().Be(OutcomeStatus.Succeeded);
        card.Completion!.Steps.Should().OnlyContain(s => s.Status == "succeeded");
    }

    [Fact]
    public void ForJobAwareOutcome_WhenNothingStarted_CardIsPartialWithAllStepsPending()
    {
        var state = Project(NewJob(), Queued("container"), Queued("record"), Queued("indexing"));

        var card = JobAwareOutcomeProjection.ForJobAwareOutcome(LedgerKey, state, Summary());

        card.Status.Should().Be(OutcomeStatus.Partial);
        card.Completion!.Steps.Should().OnlyContain(s => s.Status == "pending");
    }

    // ── Honest failure / retry / poisoned surfacing (not collapsed to success) ────

    [Fact]
    public void ForJobAwareOutcome_WhenAStepPoisoned_CardIsFailedAndRecoveryEscalates()
    {
        var state = Project(
            NewJob(),
            Completed("record"),
            new StoredStepSignal { StepName = "indexing", StoredStatus = JobStatus.Poisoned });

        var card = JobAwareOutcomeProjection.ForJobAwareOutcome(LedgerKey, state, Summary());

        card.Status.Should().Be(OutcomeStatus.Failed);
        card.Completion!.Steps.Single(s => s.Key == "indexing").Status.Should().Be("failed");

        // The honest recovery affordance is preserved on the underlying state.
        new JobAwareCompletionStateView(state).Recovery!.Kind.Should().Be("escalate");
    }

    [Theory]
    [InlineData(JobAwareState.RetryPending, "running")]
    [InlineData(JobAwareState.UserActionRequired, "running")]
    [InlineData(JobAwareState.Queued, "pending")]
    [InlineData(JobAwareState.Running, "running")]
    [InlineData(JobAwareState.Completed, "succeeded")]
    [InlineData(JobAwareState.Poisoned, "failed")]
    public void ToStepStatus_MapsEachJobStateToTheCardRenderVocab(JobAwareState state, string expected)
    {
        JobAwareOutcomeProjection.ToStepStatus(state).Should().Be(expected);
    }

    // ── EnsureIngestionParity: fail-loud guard for the hand-assembled path ─────────

    [Fact]
    public void EnsureIngestionParity_WhenSucceededDeclaredButAggregateNotCompleted_Throws()
    {
        var state = Project(NewJob(), Completed("record"), Queued("indexing")); // aggregate == Partial

        var act = () => JobAwareOutcomeProjection.EnsureIngestionParity(OutcomeStatus.Succeeded, state);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*NFR-12*");
    }

    [Fact]
    public void EnsureIngestionParity_WhenSucceededDeclaredAndAggregateCompleted_DoesNotThrow()
    {
        var state = Project(NewJob(), Completed("record"), Completed("indexing"));

        var act = () => JobAwareOutcomeProjection.EnsureIngestionParity(OutcomeStatus.Succeeded, state);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureIngestionParity_WhenPartialDeclaredForUnfinishedOperation_DoesNotThrow()
    {
        var state = Project(NewJob(), Completed("record"), Queued("indexing"));

        var act = () => JobAwareOutcomeProjection.EnsureIngestionParity(OutcomeStatus.Partial, state);

        act.Should().NotThrow();
    }

    // ── Store-before-render inherited from OutcomeCard.ForStoredOutcome (ADR-040) ──

    [Fact]
    public void ForJobAwareOutcome_WhenLedgerOutputKeyBlank_Throws()
    {
        var state = Project(NewJob(), Completed("record"));

        var act = () => JobAwareOutcomeProjection.ForJobAwareOutcome("  ", state, Summary());

        act.Should().Throw<ArgumentException>();
    }

    // ── Consumer-declared step set: not hardcoded to Compose's steps ──────────────

    [Fact]
    public void ToOutcomeCompletion_PreservesADifferentConsumersDeclaredStepOrderAndKeys()
    {
        // A NON-Compose consumer (e.g. an outbound draft) declares draft → review → send.
        var state = Project(
            NewJob("email-send"),
            Completed("draft"),
            Running("review"),
            Queued("send"));

        var completion = JobAwareOutcomeProjection.ToOutcomeCompletion(state);

        completion.Steps.Select(s => s.Key).Should().ContainInOrder("draft", "review", "send");
        completion.Steps.Single(s => s.Key == "draft").Status.Should().Be("succeeded");
        completion.Steps.Single(s => s.Key == "send").Status.Should().Be("pending");
    }

    [Fact]
    public void ToOutcomeCompletion_WhenNoLabelSupplied_HumanizesTheStepKey()
    {
        var state = Project(NewJob(), Running("profile-analysis"));

        var completion = JobAwareOutcomeProjection.ToOutcomeCompletion(state);

        completion.Steps.Single().Label.Should().Be("Profile analysis");
    }

    [Fact]
    public void ToOutcomeCompletion_WhenLabelSupplied_UsesTheSuppliedLabel()
    {
        var state = Project(NewJob(), Running("indexing"));

        var completion = JobAwareOutcomeProjection.ToOutcomeCompletion(
            state,
            stepLabels: new Dictionary<string, string> { ["indexing"] = "Indexing for search" });

        completion.Steps.Single().Label.Should().Be("Indexing for search");
    }

    [Fact]
    public void RecordExistsButOperationUnfinished_WhenRecordDoneButIndexingPending_IsTrue()
    {
        var state = Project(NewJob(), Completed("record"), Queued("indexing"));

        JobAwareOutcomeProjection.RecordExistsButOperationUnfinished(state, "record").Should().BeTrue();
    }
}
