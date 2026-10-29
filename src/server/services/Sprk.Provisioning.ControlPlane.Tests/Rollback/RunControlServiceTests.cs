// -----------------------------------------------------------------------------
// RunControlServiceTests.cs
//
// L2 CONTROL-PLANE tests for <see cref="RunControlService"/> — the operator
// resume / cancel / gate-advance transitions. Before this service the three
// endpoints only enqueued control-tag envelopes ("Resume", "Cancel",
// "GateAdvance") that no keyed IProvisioningHandler serves, so the Worker
// dead-lettered them as NoHandler and nothing changed.
//
// TESTED BEHAVIORS:
//   Resume   - Failed -> Running (ErrorDetail / CompletedOn cleared), attempt
//              bumped + persisted, guard re-acquired, the run's REAL current
//              handler enqueued with the bumped attempt AFTER the write
//              (incl. entry points H0 / H0.5); every wrong-state / guard /
//              ETag / enqueue-failure branch.
//   Cancel   - non-final -> Cancelled + CompletedOn + guard released;
//              Completed / Cancelled / Quarantined -> Conflict, no release.
//   Advance  - Pending gate's verifier enqueued with a bumped attempt; every
//              refusal branch.
//
// SEAM STRATEGY (docs/standards/TEST-ARCHITECTURE.md §5): hand-rolled doubles,
// same style as QuarantineClearServiceTests. No Moq.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Concurrency;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Sprk.Provisioning.ControlPlane.Rollback;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Rollback;

public sealed class RunControlServiceTests
{
    private const string TestCustomerId = "testcust";
    private const string TestRunId = "00000000-0000-0000-0000-000000000001";
    private const string TestActorOid = "22222222-2222-2222-2222-222222222222";
    private const string PriorError = "[h4-transient] Dataverse 503";

    private static readonly DateTimeOffset FrozenNow = DateTimeOffset.Parse("2026-10-15T12:00:00Z");

    private sealed class Rig
    {
        public InMemoryRunRepository Repo { get; } = new();
        public FakeCustomerRunGuard Guard { get; } = new();
        public RecordingEnqueuer Enqueuer { get; }
        public RunControlService Sut { get; }

        public Rig()
        {
            Enqueuer = new RecordingEnqueuer(Repo);
            Sut = new RunControlService(
                Repo, Guard, Enqueuer, new FrozenTimeProvider(FrozenNow), NullLogger<RunControlService>.Instance);
        }
    }

    // -----------------------------------------------------------------------
    // Resume — happy paths
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Resume_FailedRun_GoesRunning_BumpsAttempt_AndEnqueuesTheCurrentHandler()
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeRun(RunStatus.Failed, currentPhase: "H4"), "\"etag-1\"");

        var result = await rig.Sut.ResumeAsync(TestCustomerId, TestRunId, TestActorOid, default);

        var success = result.Should().BeOfType<RunResumeResult.Success>().Subject;
        success.HandlerId.Should().Be("H4");
        success.Attempt.Should().Be(1);
        success.Run.Status.Should().Be(RunStatus.Running);
        success.Run.ErrorDetail.Should().BeNull();
        success.Run.CompletedOn.Should().BeNull();
        success.Run.HandlerRetryAttempts["H4"].Should().Be(1, "the bumped attempt is persisted in the same write");
        rig.Repo.ReplaceCalls.Should().Be(1);

        var envelope = rig.Enqueuer.Enqueued.Should().ContainSingle().Subject;
        envelope.HandlerId.Should().Be("H4");
        envelope.RunId.Should().Be(TestRunId);
        envelope.CustomerId.Should().Be(TestCustomerId);
        envelope.Attempt.Should().Be(1, "a fresh attempt gives a fresh MessageId that Service Bus duplicate detection keeps");
        HandlerIds.Dispatchable.Should().Contain(envelope.HandlerId);

        rig.Guard.AcquireCalls.Should().ContainSingle().Which.Should().Be((TestCustomerId, TestRunId));
    }

    [Fact]
    public async Task Resume_AlsoReDispatches_EveryOtherFailedHandler_WithAFreshAttempt()
    {
        // T186 L41: H8 failed, then H6 failed and became the current phase. Resume used to re-run H6 only; H8 never
        // ran again and the run stalled Running.
        var rig = new Rig();
        var run = MakeRun(RunStatus.Failed, currentPhase: "H6");
        run.FailedHandlers.Add("H8");
        run.FailedHandlers.Add("H6");
        run.HandlerRetryAttempts["H6"] = 1;
        run.HandlerAutoRetries["H8"] = 3; // T186 L44: auto-retries exhausted
        rig.Repo.Seed(run, "\"etag-1\"");

        var result = await rig.Sut.ResumeAsync(TestCustomerId, TestRunId, TestActorOid, default);

        var success = result.Should().BeOfType<RunResumeResult.Success>().Subject;
        success.Run.HandlerAutoRetries.Should().NotContainKey("H8", "a resume restores the auto-retry budget (T186 L44)");
        success.HandlerId.Should().Be("H6");
        success.Attempt.Should().Be(2);
        rig.Enqueuer.Enqueued.Select(e => (e.HandlerId, e.Attempt))
            .Should().Equal([("H6", 2), ("H8", 1)], "the current phase first, then every other failed handler");
        success.Run.HandlerRetryAttempts["H8"].Should().Be(1, "H8's new attempt is persisted with the Running write");
        rig.Repo.ReplaceCalls.Should().Be(1);
        rig.Enqueuer.StatusAtEnqueue.Should().AllBeEquivalentTo(RunStatus.Running);
    }

    [Fact]
    public async Task Resume_WritesTheRunRunning_BeforeItEnqueues()
    {
        // An envelope that reaches the dispatcher while the run is still Failed is dropped as stale.
        var rig = new Rig();
        rig.Repo.Seed(MakeRun(RunStatus.Failed, currentPhase: "H4"), "\"etag-1\"");

        await rig.Sut.ResumeAsync(TestCustomerId, TestRunId, TestActorOid, default);

        rig.Enqueuer.StatusAtEnqueue.Should().ContainSingle().Which.Should().Be(RunStatus.Running);
    }

    [Fact]
    public async Task Resume_PriorAttempts_AreContinued_CaseInsensitively()
    {
        var rig = new Rig();
        var run = MakeRun(RunStatus.Failed, currentPhase: "H9");
        run.HandlerRetryAttempts["h9"] = 2; // a run persisted before the verbatim-key fix
        rig.Repo.Seed(run, "\"etag-1\"");

        var result = await rig.Sut.ResumeAsync(TestCustomerId, TestRunId, TestActorOid, default);

        result.Should().BeOfType<RunResumeResult.Success>().Which.Attempt.Should().Be(3);
        rig.Enqueuer.Enqueued.Should().ContainSingle().Which.Attempt.Should().Be(3);
    }

    [Theory]
    [InlineData("H0")]
    [InlineData("H0.5")]
    public async Task Resume_EntryPointHandler_IsEnqueued_BecauseTheReconcilerNeverDispatchesIt(string entryPoint)
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeRun(RunStatus.Failed, currentPhase: entryPoint), "\"etag-1\"");

        var result = await rig.Sut.ResumeAsync(TestCustomerId, TestRunId, TestActorOid, default);

        result.Should().BeOfType<RunResumeResult.Success>();
        rig.Enqueuer.Enqueued.Should().ContainSingle().Which.HandlerId.Should().Be(entryPoint);
    }

    // -----------------------------------------------------------------------
    // Resume — refusals
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Resume_UnknownRun_IsNotFound()
    {
        var rig = new Rig();

        var result = await rig.Sut.ResumeAsync(TestCustomerId, TestRunId, TestActorOid, default);

        result.Should().BeOfType<RunResumeResult.NotFound>();
        rig.Enqueuer.Enqueued.Should().BeEmpty();
    }

    [Theory]
    [InlineData(RunStatus.NotStarted)]
    [InlineData(RunStatus.Running)]
    [InlineData(RunStatus.WaitingOnGate)]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.Cancelled)]
    [InlineData(RunStatus.Quarantined)]
    public async Task Resume_NonFailedRun_IsConflict_AndNothingIsEnqueued(RunStatus status)
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeRun(status, currentPhase: "H4"), "\"etag-1\"");

        var result = await rig.Sut.ResumeAsync(TestCustomerId, TestRunId, TestActorOid, default);

        result.Should().BeOfType<RunResumeResult.Conflict>().Which.CurrentStatus.Should().Be(status);
        rig.Enqueuer.Enqueued.Should().BeEmpty();
        rig.Repo.ReplaceCalls.Should().Be(0);
        rig.Guard.AcquireCalls.Should().BeEmpty("a refused resume must not touch the customer guard");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Resume")]
    [InlineData("H14a")] // in-process sub-step, never independently dispatchable
    public async Task Resume_FailedRun_WithoutADispatchablePhase_IsConflict(string? currentPhase)
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeRun(RunStatus.Failed, currentPhase), "\"etag-1\"");

        var result = await rig.Sut.ResumeAsync(TestCustomerId, TestRunId, TestActorOid, default);

        result.Should().BeOfType<RunResumeResult.Conflict>();
        rig.Enqueuer.Enqueued.Should().BeEmpty();
        rig.Repo.ReplaceCalls.Should().Be(0);
    }

    [Fact]
    public async Task Resume_GuardHeldByAnotherRun_IsGuardConflict_AndTheRunIsUnchanged()
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeRun(RunStatus.Failed, currentPhase: "H4"), "\"etag-1\"");
        rig.Guard.AcquireOutcome = new AcquireResult.Conflict(
            TestCustomerId, "winning-run", AcquireConflictReasonCodes.AlreadyInFlight);

        var result = await rig.Sut.ResumeAsync(TestCustomerId, TestRunId, TestActorOid, default);

        var conflict = result.Should().BeOfType<RunResumeResult.GuardConflict>().Subject;
        conflict.WinningRunId.Should().Be("winning-run");
        conflict.ReasonCode.Should().Be(AcquireConflictReasonCodes.AlreadyInFlight);
        rig.Repo.ReplaceCalls.Should().Be(0);
        rig.Enqueuer.Enqueued.Should().BeEmpty();
        var stored = (await rig.Repo.ReadRunAsync(TestCustomerId, TestRunId, default))!.Run;
        stored.Status.Should().Be(RunStatus.Failed);
        stored.ErrorDetail.Should().Be(PriorError);
        stored.HandlerRetryAttempts.Should().BeEmpty();
    }

    [Fact]
    public async Task Resume_GuardTransientFailure_IsGuardUnavailable()
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeRun(RunStatus.Failed, currentPhase: "H4"), "\"etag-1\"");
        rig.Guard.AcquireOutcome = new AcquireResult.TransientFailure(TestCustomerId, TestRunId, "dataverse 503");

        var result = await rig.Sut.ResumeAsync(TestCustomerId, TestRunId, TestActorOid, default);

        result.Should().BeOfType<RunResumeResult.GuardUnavailable>().Which.Diagnostic.Should().Be("dataverse 503");
        rig.Repo.ReplaceCalls.Should().Be(0);
        rig.Enqueuer.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task Resume_EtagRace_IsConcurrencyConflict_AndNothingIsEnqueued()
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeRun(RunStatus.Failed, currentPhase: "H4"), "\"etag-1\"");
        rig.Repo.ForceNextReplaceConflict = true;

        var result = await rig.Sut.ResumeAsync(TestCustomerId, TestRunId, TestActorOid, default);

        result.Should().BeOfType<RunResumeResult.ConcurrencyConflict>();
        rig.Enqueuer.Enqueued.Should().BeEmpty();
    }

    [Theory]
    [InlineData(RunStatus.Cancelled, true)]
    [InlineData(RunStatus.Failed, true)]
    [InlineData(RunStatus.Running, false)]
    [InlineData(RunStatus.WaitingOnGate, false)]
    public async Task Resume_EtagRace_ReleasesTheGuardItTook_OnlyWhenTheWinnerLeftTheRunInactive(RunStatus winner, bool released)
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeRun(RunStatus.Failed, currentPhase: "H4"), "\"etag-1\"");
        rig.Repo.ForceNextReplaceConflict = true;
        rig.Repo.ConflictWinner = MakeRun(winner, currentPhase: "H4");

        var result = await rig.Sut.ResumeAsync(TestCustomerId, TestRunId, TestActorOid, default);

        result.Should().BeOfType<RunResumeResult.ConcurrencyConflict>();
        if (released)
        {
            rig.Guard.ReleaseCalls.Should().ContainSingle(
                "an inactive run must not keep the guard resume re-acquired, or every later POST /api/runs is refused");
        }
        else
        {
            rig.Guard.ReleaseCalls.Should().BeEmpty("an active winner owns the guard");
        }
    }

    [Fact]
    public async Task Resume_EnqueueThrows_RevertsTheRunToFailed_AndReportsEnqueueFailed()
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeRun(RunStatus.Failed, currentPhase: "H4"), "\"etag-1\"");
        rig.Enqueuer.Failure = new InvalidOperationException("service bus unavailable");

        var result = await rig.Sut.ResumeAsync(TestCustomerId, TestRunId, TestActorOid, default);

        result.Should().BeOfType<RunResumeResult.EnqueueFailed>().Which.Diagnostic.Should().Contain("service bus unavailable");
        rig.Repo.ReplaceCalls.Should().Be(2, "one write to Running, one revert");
        var stored = (await rig.Repo.ReadRunAsync(TestCustomerId, TestRunId, default))!.Run;
        stored.Status.Should().Be(RunStatus.Failed);
        stored.ErrorDetail.Should().Be(PriorError, "the original failure detail is restored");
        stored.CompletedOn.Should().Be(FrozenNow);
        stored.HandlerRetryAttempts["H4"].Should().Be(1, "the counter stays bumped so the retry uses a fresh MessageId");
        rig.Guard.ReleaseCalls.Should().ContainSingle("back to Failed, the run gives back the guard resume took");
    }

    [Fact]
    public async Task Resume_EnqueueCancelled_PropagatesTheCancellation()
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeRun(RunStatus.Failed, currentPhase: "H4"), "\"etag-1\"");
        using var cts = new CancellationTokenSource();
        rig.Enqueuer.OnEnqueue = () =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        };

        var act = () => rig.Sut.ResumeAsync(TestCustomerId, TestRunId, TestActorOid, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // -----------------------------------------------------------------------
    // Cancel
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(RunStatus.NotStarted)]
    [InlineData(RunStatus.Running)]
    [InlineData(RunStatus.WaitingOnGate)]
    [InlineData(RunStatus.Failed)]
    public async Task Cancel_NonFinalRun_BecomesCancelled_AndReleasesTheGuard(RunStatus status)
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeRun(status, currentPhase: "H4"), "\"etag-1\"");

        var result = await rig.Sut.CancelAsync(TestCustomerId, TestRunId, TestActorOid, default);

        var success = result.Should().BeOfType<RunCancelResult.Success>().Subject;
        success.Run.Status.Should().Be(RunStatus.Cancelled);
        success.Run.CompletedOn.Should().Be(FrozenNow);
        rig.Repo.ReplaceCalls.Should().Be(1);
        rig.Guard.ReleaseCalls.Should().ContainSingle().Which.Should().Be((TestCustomerId, TestRunId));
        rig.Enqueuer.Enqueued.Should().BeEmpty("cancel enqueues nothing — the Cosmos write is the whole action");
    }

    [Theory]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.Cancelled)]
    [InlineData(RunStatus.Quarantined)]
    public async Task Cancel_FinalOrQuarantinedRun_IsConflict_WithoutWritingOrReleasing(RunStatus status)
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeRun(status, currentPhase: "H4"), "\"etag-1\"");

        var result = await rig.Sut.CancelAsync(TestCustomerId, TestRunId, TestActorOid, default);

        result.Should().BeOfType<RunCancelResult.Conflict>().Which.CurrentStatus.Should().Be(status);
        rig.Repo.ReplaceCalls.Should().Be(0);
        rig.Guard.ReleaseCalls.Should().BeEmpty("a Quarantined run keeps its guard until clear-quarantine");
    }

    [Fact]
    public async Task Cancel_UnknownRun_IsNotFound()
    {
        var rig = new Rig();

        var result = await rig.Sut.CancelAsync(TestCustomerId, TestRunId, TestActorOid, default);

        result.Should().BeOfType<RunCancelResult.NotFound>();
        rig.Guard.ReleaseCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancel_EtagRace_IsConcurrencyConflict_AndTheGuardIsNotReleased()
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeRun(RunStatus.Running, currentPhase: "H4"), "\"etag-1\"");
        rig.Repo.ForceNextReplaceConflict = true;

        var result = await rig.Sut.CancelAsync(TestCustomerId, TestRunId, TestActorOid, default);

        result.Should().BeOfType<RunCancelResult.ConcurrencyConflict>();
        rig.Guard.ReleaseCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancel_GuardReleaseTransientFailure_StillSucceeds()
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeRun(RunStatus.Running, currentPhase: "H4"), "\"etag-1\"");
        rig.Guard.ReleaseOutcome = new ReleaseResult.TransientFailure(TestCustomerId, TestRunId, "dataverse 503");

        var result = await rig.Sut.CancelAsync(TestCustomerId, TestRunId, TestActorOid, default);

        result.Should().BeOfType<RunCancelResult.Success>("the Cosmos write already landed; a release failure is log-only");
        rig.Guard.ReleaseCalls.Should().ContainSingle();
    }

    // -----------------------------------------------------------------------
    // AdvanceGate
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AdvanceGate_PendingGate_EnqueuesTheVerifier_WithABumpedAttempt()
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeGatedRun("admin-consent", GateState.Pending, "H0.5"), "\"etag-1\"");

        var result = await rig.Sut.AdvanceGateAsync(TestCustomerId, TestRunId, "admin-consent", default);

        var success = result.Should().BeOfType<GateAdvanceResult.Success>().Subject;
        success.VerifierHandler.Should().Be("H0.5");
        success.Attempt.Should().Be(1);
        success.Run.Status.Should().Be(RunStatus.WaitingOnGate, "the verifier — not this service — transitions the run");
        success.Run.HandlerRetryAttempts["H0.5"].Should().Be(1);
        rig.Repo.ReplaceCalls.Should().Be(1);

        var envelope = rig.Enqueuer.Enqueued.Should().ContainSingle().Subject;
        envelope.HandlerId.Should().Be("H0.5");
        envelope.Attempt.Should().Be(1);
        rig.Guard.AcquireCalls.Should().BeEmpty();
        rig.Guard.ReleaseCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task AdvanceGate_RepeatedRequests_UseIncreasingAttempts()
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeGatedRun("admin-consent", GateState.Pending, "H0.5"), "\"etag-1\"");

        await rig.Sut.AdvanceGateAsync(TestCustomerId, TestRunId, "admin-consent", default);
        await rig.Sut.AdvanceGateAsync(TestCustomerId, TestRunId, "admin-consent", default);

        rig.Enqueuer.Enqueued.Select(e => e.Attempt).Should().Equal(1, 2);
    }

    [Fact]
    public async Task AdvanceGate_UnknownRun_IsNotFound()
    {
        var rig = new Rig();

        var result = await rig.Sut.AdvanceGateAsync(TestCustomerId, TestRunId, "admin-consent", default);

        result.Should().BeOfType<GateAdvanceResult.NotFound>();
    }

    [Fact]
    public async Task AdvanceGate_RunningRunWithAPendingGate_ReDispatchesTheVerifier()
    {
        // T186 L37: a sibling's success overwrote the gated handler's WaitingOnGate with Running; the run then had no
        // way forward (the reconciler's re-dispatch of the verifier reused its MessageId and was dropped).
        var rig = new Rig();
        var run = MakeGatedRun("admin-consent", GateState.Pending, "H0.5");
        run.Status = RunStatus.Running;
        rig.Repo.Seed(run, "\"etag-1\"");

        var result = await rig.Sut.AdvanceGateAsync(TestCustomerId, TestRunId, "admin-consent", default);

        result.Should().BeOfType<GateAdvanceResult.Success>().Which.VerifierHandler.Should().Be("H0.5");
        rig.Enqueuer.Enqueued.Should().ContainSingle().Which.Attempt.Should().Be(1);
    }

    [Theory]
    [InlineData(RunStatus.Failed)]
    [InlineData(RunStatus.Quarantined)]
    [InlineData(RunStatus.Cancelled)]
    [InlineData(RunStatus.Completed)]
    public async Task AdvanceGate_RunNotWaitingOnGate_IsConflict(RunStatus status)
    {
        var rig = new Rig();
        var run = MakeGatedRun("admin-consent", GateState.Pending, "H0.5");
        run.Status = status;
        rig.Repo.Seed(run, "\"etag-1\"");

        var result = await rig.Sut.AdvanceGateAsync(TestCustomerId, TestRunId, "admin-consent", default);

        result.Should().BeOfType<GateAdvanceResult.Conflict>().Which.CurrentStatus.Should().Be(status);
        rig.Enqueuer.Enqueued.Should().BeEmpty();
        rig.Repo.ReplaceCalls.Should().Be(0);
    }

    [Fact]
    public async Task AdvanceGate_UnknownGate_IsGateNotFound()
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeGatedRun("admin-consent", GateState.Pending, "H0.5"), "\"etag-1\"");

        var result = await rig.Sut.AdvanceGateAsync(TestCustomerId, TestRunId, "no-such-gate", default);

        result.Should().BeOfType<GateAdvanceResult.GateNotFound>();
        rig.Enqueuer.Enqueued.Should().BeEmpty();
    }

    [Theory]
    [InlineData(GateState.Verified)]
    [InlineData(GateState.Cleared)]
    public async Task AdvanceGate_GateNotPending_IsConflict(GateState state)
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeGatedRun("admin-consent", state, "H0.5"), "\"etag-1\"");

        var result = await rig.Sut.AdvanceGateAsync(TestCustomerId, TestRunId, "admin-consent", default);

        result.Should().BeOfType<GateAdvanceResult.Conflict>();
        rig.Enqueuer.Enqueued.Should().BeEmpty();
        rig.Repo.ReplaceCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("GateAdvance")]
    [InlineData("H14a")]
    public async Task AdvanceGate_NoDispatchableVerifier_IsConflict(string? verifier)
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeGatedRun("admin-consent", GateState.Pending, verifier), "\"etag-1\"");

        var result = await rig.Sut.AdvanceGateAsync(TestCustomerId, TestRunId, "admin-consent", default);

        result.Should().BeOfType<GateAdvanceResult.Conflict>();
        rig.Enqueuer.Enqueued.Should().BeEmpty();
        rig.Repo.ReplaceCalls.Should().Be(0);
    }

    [Fact]
    public async Task AdvanceGate_EtagRace_IsConcurrencyConflict_AndNothingIsEnqueued()
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeGatedRun("admin-consent", GateState.Pending, "H0.5"), "\"etag-1\"");
        rig.Repo.ForceNextReplaceConflict = true;

        var result = await rig.Sut.AdvanceGateAsync(TestCustomerId, TestRunId, "admin-consent", default);

        result.Should().BeOfType<GateAdvanceResult.ConcurrencyConflict>();
        rig.Enqueuer.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task AdvanceGate_EnqueueThrows_IsEnqueueFailed()
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeGatedRun("admin-consent", GateState.Pending, "H0.5"), "\"etag-1\"");
        rig.Enqueuer.Failure = new InvalidOperationException("service bus unavailable");

        var result = await rig.Sut.AdvanceGateAsync(TestCustomerId, TestRunId, "admin-consent", default);

        result.Should().BeOfType<GateAdvanceResult.EnqueueFailed>().Which.Diagnostic.Should().Contain("service bus unavailable");
    }

    // -----------------------------------------------------------------------
    // Helpers + doubles
    // -----------------------------------------------------------------------

    private static ProvisioningRun MakeRun(RunStatus status, string? currentPhase) => new()
    {
        RunId = TestRunId,
        CustomerId = TestCustomerId,
        EnvironmentId = "env-1",
        TenancyModel = "Model1",
        Profile = "spaarke-hosted-model2",
        Status = status,
        CurrentPhase = currentPhase,
        ErrorDetail = status == RunStatus.Failed ? PriorError : null,
        CompletedOn = status is RunStatus.Failed or RunStatus.Completed or RunStatus.Cancelled or RunStatus.Quarantined
            ? DateTimeOffset.Parse("2026-10-15T10:00:00Z")
            : null,
    };

    // T186 L55: H11 done, H12a quarantined the run; H12b (dispatched with H12a) arrived while it was Quarantined and
    // the dispatcher completed it without running it.
    private static ProvisioningRun MakeClearedQuarantineRun(DateTimeOffset? resumedAt)
    {
        var run = MakeRun(RunStatus.Failed, currentPhase: "H12a");
        foreach (var done in new[] { "H0", "H1", "H2a", "H2b", "H3", "H4", "H4b", "H5", "H6", "H7", "H7b", "H7c", "H8", "H9", "H10", "H11" })
        {
            run.CompletedPhases.Add(new CompletedPhase { Phase = done, CompletedAt = FrozenNow, IdempotencyKey = done, JobId = done });
        }
        run.FailedHandlers.Add("H12a");
        run.HandlerRetryAttempts["H12a"] = 1;
        run.Quarantine = new QuarantineInfo
        {
            State = QuarantineState.Cleared,
            Reason = "seed write failed",
            QuarantinedByHandler = "H12a",
            QuarantinedAt = FrozenNow.AddMinutes(-30),
            ClearedBy = TestActorOid,
            ClearedAt = FrozenNow.AddMinutes(-1),
            ResumedAt = resumedAt,
        };
        return run;
    }

    [Fact]
    public async Task T186_L55_FirstResumeAfterAClearedQuarantine_AlsoReDispatchesTheReadySiblingWithAFreshAttempt()
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeClearedQuarantineRun(resumedAt: null), "\"etag-1\"");

        var result = await rig.Sut.ResumeAsync(TestCustomerId, TestRunId, TestActorOid, default);

        var success = result.Should().BeOfType<RunResumeResult.Success>().Subject;
        rig.Enqueuer.Enqueued.Select(e => (e.HandlerId, e.Attempt))
            .Should().Equal([("H12a", 2), ("H12b", 1)], "H12b's attempt 0 was swallowed; its re-send needs a MessageId Service Bus has not seen");
        success.Run.HandlerRetryAttempts["H12b"].Should().Be(1);
        success.Run.Quarantine!.ResumedAt.Should().Be(FrozenNow);
        rig.Repo.ReplaceCalls.Should().Be(1);
    }

    [Fact]
    public async Task T186_L55_LaterResume_AndAPlainFailedResume_DoNotReDispatchReadyHandlers()
    {
        // A ready handler may still be running (a long H6 import); only the post-quarantine resume re-sends them.
        var already = new Rig();
        already.Repo.Seed(MakeClearedQuarantineRun(resumedAt: FrozenNow.AddMinutes(-5)), "\"etag-1\"");
        await already.Sut.ResumeAsync(TestCustomerId, TestRunId, TestActorOid, default);
        already.Enqueuer.Enqueued.Select(e => e.HandlerId).Should().Equal("H12a");

        var plain = new Rig();
        var run = MakeClearedQuarantineRun(resumedAt: null);
        run.Quarantine = null;
        plain.Repo.Seed(run, "\"etag-1\"");
        await plain.Sut.ResumeAsync(TestCustomerId, TestRunId, TestActorOid, default);
        plain.Enqueuer.Enqueued.Select(e => e.HandlerId).Should().Equal("H12a");
    }

    [Fact]
    public async Task T186_L55_EnqueueThrows_TheRevertKeepsTheReadyHandlersOwedToTheNextResume()
    {
        var rig = new Rig();
        rig.Repo.Seed(MakeClearedQuarantineRun(resumedAt: null), "\"etag-1\"");
        rig.Enqueuer.Failure = new InvalidOperationException("service bus unavailable");

        await rig.Sut.ResumeAsync(TestCustomerId, TestRunId, TestActorOid, default);

        var stored = (await rig.Repo.ReadRunAsync(TestCustomerId, TestRunId, default))!.Run;
        stored.Status.Should().Be(RunStatus.Failed);
        stored.Quarantine!.ResumedAt.Should().BeNull();
    }

    private static ProvisioningRun MakeGatedRun(string gateId, GateState gateState, string? verifier)
    {
        var run = MakeRun(RunStatus.WaitingOnGate, currentPhase: "H0.5");
        run.GateStates[gateId] = new GateEntry { Status = gateState, VerifierHandler = verifier };
        return run;
    }

    private sealed class FakeCustomerRunGuard : ICustomerRunGuard
    {
        public List<(string CustomerId, string RunId)> AcquireCalls { get; } = new();
        public List<(string CustomerId, string RunId)> ReleaseCalls { get; } = new();
        public AcquireResult? AcquireOutcome { get; set; }
        public ReleaseResult? ReleaseOutcome { get; set; }

        public Task<AcquireResult> TryAcquireAsync(string customerId, string runId, CancellationToken cancellationToken)
        {
            AcquireCalls.Add((customerId, runId));
            return Task.FromResult(AcquireOutcome ?? new AcquireResult.Success(customerId, runId));
        }

        public Task<ReleaseResult> ReleaseAsync(string customerId, string runId, CancellationToken cancellationToken)
        {
            ReleaseCalls.Add((customerId, runId));
            return Task.FromResult(ReleaseOutcome ?? new ReleaseResult.Released(customerId, runId));
        }
    }

    private sealed class RecordingEnqueuer : IHandlerEnqueuer
    {
        private readonly InMemoryRunRepository _repo;
        public RecordingEnqueuer(InMemoryRunRepository repo) => _repo = repo;

        public List<HandlerEnvelope> Enqueued { get; } = new();
        public List<RunStatus> StatusAtEnqueue { get; } = new();
        public Exception? Failure { get; set; }
        public Action? OnEnqueue { get; set; }

        public async Task EnqueueAsync(HandlerEnvelope envelope, CancellationToken cancellationToken)
        {
            OnEnqueue?.Invoke();
            if (Failure is not null)
            {
                throw Failure;
            }
            var stored = await _repo.ReadRunAsync(envelope.CustomerId, envelope.RunId, cancellationToken);
            StatusAtEnqueue.Add(stored!.Run.Status);
            Enqueued.Add(envelope);
        }
    }

    // Same shape as the double in QuarantineClearServiceTests.
    private sealed class InMemoryRunRepository : IProvisioningRunRepository
    {
        private readonly Dictionary<string, (ProvisioningRun Run, string ETag)> _store = new();
        public int ReplaceCalls { get; private set; }
        public bool ForceNextReplaceConflict { get; set; }

        /// <summary>When set with <see cref="ForceNextReplaceConflict"/>, the concurrent winner's run state.</summary>
        public ProvisioningRun? ConflictWinner { get; set; }

        public void Seed(ProvisioningRun run, string etag) => _store[Key(run.CustomerId, run.RunId)] = (run, etag);

        public Task<ProvisioningRunReadResult?> ReadRunAsync(string customerId, string runId, CancellationToken cancellationToken)
        {
            if (_store.TryGetValue(Key(customerId, runId), out var stored))
            {
                return Task.FromResult<ProvisioningRunReadResult?>(new ProvisioningRunReadResult(stored.Run, stored.ETag));
            }
            return Task.FromResult<ProvisioningRunReadResult?>(null);
        }

        public Task<ProvisioningRunReadResult> CreateRunAsync(ProvisioningRun run, CancellationToken cancellationToken)
        {
            var etag = "\"etag-new\"";
            _store[Key(run.CustomerId, run.RunId)] = (run, etag);
            return Task.FromResult(new ProvisioningRunReadResult(run, etag));
        }

        public Task<ReplaceRunResult> ReplaceRunAsync(ProvisioningRun run, string ifMatchEtag, CancellationToken cancellationToken)
        {
            ReplaceCalls++;
            var key = Key(run.CustomerId, run.RunId);

            if (ForceNextReplaceConflict)
            {
                ForceNextReplaceConflict = false;
                var stored = _store[key];
                return Task.FromResult<ReplaceRunResult>(
                    new ReplaceRunResult.Conflict(new ProvisioningRunReadResult(ConflictWinner ?? stored.Run, stored.ETag)));
            }
            if (!_store.TryGetValue(key, out var current))
            {
                return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.NotFound());
            }
            if (!string.Equals(current.ETag, ifMatchEtag, StringComparison.Ordinal))
            {
                return Task.FromResult<ReplaceRunResult>(
                    new ReplaceRunResult.Conflict(new ProvisioningRunReadResult(current.Run, current.ETag)));
            }
            var newEtag = $"\"etag-{Guid.NewGuid():N}\"";
            _store[key] = (run, newEtag);
            return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.Success(run, newEtag));
        }

        private static string Key(string customerId, string runId) => $"{customerId}::{runId}";
    }

    private sealed class FrozenTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FrozenTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
