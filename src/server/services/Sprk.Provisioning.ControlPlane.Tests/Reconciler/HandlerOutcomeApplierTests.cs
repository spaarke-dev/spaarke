// -----------------------------------------------------------------------------
// HandlerOutcomeApplierTests.cs
//
// L2 CONTROL-PLANE tests for the extracted HandlerOutcomeApplier (task 104,
// Phase C'' Wave G-1 -- extraction of StateReconcilerService's former
// internal ApplyHandlerOutcomeAsync per design-study-ds2-dispatcher-design.md
// §5, closing gap C2.1).
//
// TESTED BEHAVIORS (POML 104 acceptance criterion #4 + task 107's moved AC3):
//   Success-no-op          -- HandlerResult.Success writes nothing, enqueues
//                              nothing, releases nothing; returns the run's
//                              CURRENT status unchanged + FailureClass null.
//   Failure-transition +
//     quarantine            -- QuarantineRequired populates QuarantineInfo,
//                              transitions to RunStatus.Quarantined, does NOT
//                              re-enqueue, does NOT release the I5 guard
//                              (spec FR-24 SCOPE keeps quarantined customers
//                              blocked).
//   RetryableWithCleanup    -- auto-retries with an incremented
//                              HandlerEnvelope.Attempt (task 107) so the
//                              retry's MessageId differs from the original
//                              dispatch's; monotonic across repeated
//                              failures of the same handler. MOVED from
//                              ReconcilerEnqueuePayloadAttemptTests.cs (task
//                              104) -- now constructs HandlerOutcomeApplier
//                              directly instead of driving it through
//                              StateReconcilerService's delegating shim.
//   ETag-Conflict-tolerant  -- a concurrent writer's ETag conflict skips
//                              BOTH re-enqueue AND guard-release for OUR
//                              call (the winning writer owns its own
//                              transition's side effects); no exception.
//   SuccessfulButDrifted    -- task 104's gap closure: releases the I5
//                              same-customer guard (task 059
//                              ICustomerRunGuard) exactly once, is
//                              tolerant of a transient release failure
//                              (best-effort -- operator cancel/clear-
//                              quarantine remain available as a backstop).
//   Resumable               -- transitions to Failed, does NOT re-enqueue,
//                              does NOT release the guard (operator resumes
//                              explicitly).
//
// SEAM STRATEGY (docs/standards/TEST-ARCHITECTURE.md §5):
//   Hand-rolled in-memory test doubles for IFailureClassifier,
//   IProvisioningRunRepository, IHandlerEnqueuer, ICustomerRunGuard -- no Moq,
//   mirrors the existing StateReconcilerServiceTests.cs /
//   ReconcilerEnqueuePayloadAttemptTests.cs convention.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Concurrency;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Reconciler;
using Sprk.Provisioning.ControlPlane.Repositories;
using Sprk.Provisioning.ControlPlane.Rollback;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Reconciler;

public sealed class HandlerOutcomeApplierTests
{
    private const string TestCustomerId = "test-customer";
    private const string TestRunId = "00000000-0000-0000-0000-000000000001";

    // -----------------------------------------------------------------------
    // Success -- no-op (handlers own their own CompletedPhases + Cosmos write
    // on success; the applier must not double-write, enqueue, or release).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ApplyHandlerOutcomeAsync_Success_IsNoOp_WritesNothingEnqueuesNothingReleasesNothing()
    {
        var repository = new RecordingRunRepository();
        var enqueuer = new RecordingEnqueuer();
        var guard = new RecordingCustomerRunGuard();
        var sut = BuildSut(out _, repository: repository, enqueuer: enqueuer, guard: guard);
        var run = MakeRun(RunStatus.Running, "H0", "H1");

        var applied = await sut.ApplyHandlerOutcomeAsync(
            run, ifMatchEtag: "etag-1", outcome: new HandlerResult.Success("h2a-test-customer-idem"), handlerId: "H2a", CancellationToken.None);

        applied.TargetStatus.Should().Be(RunStatus.Running, "Success returns the run's CURRENT status unchanged.");
        applied.Reenqueued.Should().BeFalse();
        applied.FailureClass.Should().BeNull("no classification fires on the Success path.");

        repository.ReplaceCalls.Should().BeEmpty("handlers own the CompletedPhases append + Cosmos write on Success.");
        enqueuer.Envelopes.Should().BeEmpty();
        guard.ReleaseCalls.Should().BeEmpty(
            "Bucket B HIGH#6 SESSION 18: mid-DAG Success (run.Status = Running) MUST NOT release the guard — " +
            "release fires ONLY when run.Status is Completed (i.e., H13 terminal completion).");
    }

    [Fact]
    public async Task ApplyHandlerOutcomeAsync_Success_WithCompletedStatus_ReleasesCustomerGuardExactlyOnce_BucketB_HIGH6()
    {
        // Bucket B HIGH#6 SESSION 18 (customer-provisioning-orchestration-r1
        // adversarial e2e verify workflow wepdcb8we): the happy-path terminal
        // completion (H13 writes Cosmos RunStatus.Completed + returns
        // HandlerResult.Success) MUST release the I5 concurrency guard
        // explicitly from THIS applier — the policy layer that already owns
        // Failure-branch releases via ShouldReleaseCustomerGuard. Prior to
        // this test the release path depended IMPLICITLY on H13's registry
        // Ready PATCH also clearing sprk_currentrunid as a side effect, which
        // was structurally unsafe (unconditional PATCH vs ETag-safe
        // ICustomerRunGuard.ReleaseAsync — see Bucket B HIGH#7). Any future
        // refactor that moves H13's Ready-writer or drops ClearCurrentRunId
        // would silently lock the customer forever on every successful
        // terminal completion.
        var repository = new RecordingRunRepository();
        var enqueuer = new RecordingEnqueuer();
        var guard = new RecordingCustomerRunGuard();
        var sut = BuildSut(out _, repository: repository, enqueuer: enqueuer, guard: guard);
        // Terminal H13 completion — run.Status is RunStatus.Completed BEFORE
        // the applier is called (H13 has already written the terminal
        // transition to Cosmos).
        var run = MakeRun(RunStatus.Completed, "H0", "H1", "H2a", "H2b", "H3", "H4", "H5", "H6", "H7", "H8", "H9", "H10", "H11", "H12", "H13");

        var applied = await sut.ApplyHandlerOutcomeAsync(
            run, ifMatchEtag: "etag-completed", outcome: new HandlerResult.Success("h13-idem"), handlerId: "H13", CancellationToken.None);

        applied.TargetStatus.Should().Be(RunStatus.Completed);
        applied.Reenqueued.Should().BeFalse();
        applied.FailureClass.Should().BeNull();

        repository.ReplaceCalls.Should().BeEmpty("H13 owns the Cosmos-Completed write; applier does not double-write.");
        enqueuer.Envelopes.Should().BeEmpty("terminal Completed does not re-enqueue.");
        guard.ReleaseCalls.Should().ContainSingle(
            "Bucket B HIGH#6 SESSION 18: terminal-Completed Success MUST fire ICustomerRunGuard.ReleaseAsync " +
            "exactly once so the release path does not depend on H13's registry Ready PATCH side effect.")
            .Which.Should().Be((TestCustomerId, TestRunId));
    }

    // -----------------------------------------------------------------------
    // QuarantineRequired -- transitions to Quarantined, populates
    // QuarantineInfo, does NOT re-enqueue, does NOT release the guard.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ApplyHandlerOutcomeAsync_QuarantineRequired_TransitionsAndPopulatesQuarantineInfo_DoesNotReenqueueOrReleaseGuard()
    {
        var frozenNow = DateTimeOffset.Parse("2026-08-19T15:00:00Z");
        var repository = new RecordingRunRepository();
        var enqueuer = new RecordingEnqueuer();
        var guard = new RecordingCustomerRunGuard();
        var sut = BuildSut(out _,
            classifier: new StubFailureClassifier(FailureClass.QuarantineRequired),
            repository: repository,
            enqueuer: enqueuer,
            guard: guard,
            timeProvider: new TestTimeProvider(frozenNow));
        var run = MakeRun(RunStatus.Running, "H0", "H1", "H2a");

        var failure = new HandlerResult.Failure(
            FailureClass.QuarantineRequired, "orphaned-resources", "H2a Bicep failed after 12 of 16 resources.");

        var applied = await sut.ApplyHandlerOutcomeAsync(
            run, ifMatchEtag: "etag-1", outcome: failure, handlerId: "H2a", CancellationToken.None);

        applied.TargetStatus.Should().Be(RunStatus.Quarantined);
        applied.Reenqueued.Should().BeFalse("QuarantineRequired requires operator clear-quarantine, not auto-retry.");
        applied.FailureClass.Should().Be(FailureClass.QuarantineRequired);

        run.Status.Should().Be(RunStatus.Quarantined);
        run.Quarantine.Should().NotBeNull();
        run.Quarantine!.State.Should().Be(QuarantineState.Quarantined);
        run.Quarantine.Reason.Should().Be(failure.Diagnostic);
        run.Quarantine.QuarantinedByHandler.Should().Be("H2a");
        run.Quarantine.QuarantinedAt.Should().Be(frozenNow);
        run.CompletedOn.Should().Be(frozenNow, "Quarantined is a terminal transition for auditability.");

        enqueuer.Envelopes.Should().BeEmpty();
        guard.ReleaseCalls.Should().BeEmpty(
            "spec FR-24 SCOPE: Quarantined runs BLOCK new runs against the same customerId until cleared.");
    }

    // -----------------------------------------------------------------------
    // RetryableWithCleanup -- auto-retry with incremented Attempt (task 107).
    // MOVED from ReconcilerEnqueuePayloadAttemptTests.cs by task 104.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ApplyHandlerOutcomeAsync_RetryableWithCleanup_ReenqueuesWithIncrementedAttempt_DistinctFromOriginalMessageId()
    {
        var sut = BuildSut(out var enqueuer,
            classifier: new StubFailureClassifier(FailureClass.RetryableWithCleanup));
        var run = MakeRun(RunStatus.Running, "H0");

        // The "just-consumed original" dispatch -- what the reconciler sent
        // BEFORE this handler failed (attempt=0, the normal enqueue path).
        // ReconcilerEnvelopeBuilder is internal but visible here via
        // InternalsVisibleTo (Core -> Tests) -- reusing the REAL production
        // builder (not a hand-copy) so this assertion can never silently
        // drift from the actual envelope shape.
        var originalEnvelope = ReconcilerEnvelopeBuilder.Build("H1", run, TimeProvider.System);
        var originalMessageId = ServiceBusHandlerEnqueuer.ComputeMessageId(originalEnvelope);

        var failure = new HandlerResult.Failure(
            FailureClass.RetryableWithCleanup,
            RejectionCode: "transient-http-503",
            Diagnostic: "Downstream call returned 503; safe to retry.");

        var applied = await sut.ApplyHandlerOutcomeAsync(
            run, ifMatchEtag: "etag-1", outcome: failure, handlerId: "H1", CancellationToken.None);

        applied.Reenqueued.Should().BeTrue("RetryableWithCleanup MUST auto-retry per §4C.");
        enqueuer.Envelopes.Should().ContainSingle();

        var retryEnvelope = enqueuer.Envelopes[0];
        retryEnvelope.Attempt.Should().Be(1, "the FIRST auto-retry increments attempt from 0 -> 1.");

        var retryMessageId = ServiceBusHandlerEnqueuer.ComputeMessageId(retryEnvelope);
        retryMessageId.Should().NotBe(originalMessageId,
            "task 107 / DS-2 §4-L1: without the attempt-field fix, this retry would carry the IDENTICAL " +
            "MessageId as the original dispatch and Service Bus level-1 dedup would silently drop it.");

        run.HandlerRetryAttempts.Should().ContainKey("H1").WhoseValue.Should().Be(1,
            "the per-handler retry counter is persisted on the run doc alongside the failure transition.");
    }

    [Fact]
    public async Task ApplyHandlerOutcomeAsync_RetryableWithCleanup_CalledTwiceForSameHandler_MonotonicallyIncrementsAttempt_EachRetryDistinct()
    {
        var sut = BuildSut(out var enqueuer,
            classifier: new StubFailureClassifier(FailureClass.RetryableWithCleanup));
        var run = MakeRun(RunStatus.Running, "H0");

        var failure = new HandlerResult.Failure(
            FailureClass.RetryableWithCleanup, "transient-http-503", "Retry #1 diagnostic.");

        // First failure -> first retry (attempt 0 -> 1).
        await sut.ApplyHandlerOutcomeAsync(run, "etag-1", failure, "H1", CancellationToken.None);

        // Second failure of the SAME handler (the retry itself failed again)
        // -> second retry (attempt 1 -> 2).
        await sut.ApplyHandlerOutcomeAsync(run, "etag-2", failure, "H1", CancellationToken.None);

        enqueuer.Envelopes.Should().HaveCount(2);
        enqueuer.Envelopes[0].Attempt.Should().Be(1);
        enqueuer.Envelopes[1].Attempt.Should().Be(2);

        var messageId1 = ServiceBusHandlerEnqueuer.ComputeMessageId(enqueuer.Envelopes[0]);
        var messageId2 = ServiceBusHandlerEnqueuer.ComputeMessageId(enqueuer.Envelopes[1]);
        messageId1.Should().NotBe(messageId2,
            "each successive §4C retry MUST produce a fresh MessageId so it is never absorbed by the prior retry's dedup window.");

        run.HandlerRetryAttempts["H1"].Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // ETag-Conflict-tolerant -- a concurrent writer already landed a
    // transition; OUR call must not re-enqueue or release the guard.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ApplyHandlerOutcomeAsync_EtagConflict_RetryableWithCleanup_DoesNotReenqueue()
    {
        var enqueuer = new RecordingEnqueuer();
        var sut = BuildSut(out _,
            classifier: new StubFailureClassifier(FailureClass.RetryableWithCleanup),
            repository: new ConflictRunRepository(),
            enqueuer: enqueuer);
        var run = MakeRun(RunStatus.Running, "H0");

        var failure = new HandlerResult.Failure(
            FailureClass.RetryableWithCleanup, "transient-http-503", "Downstream 503.");

        var applied = await sut.ApplyHandlerOutcomeAsync(
            run, ifMatchEtag: "etag-1", outcome: failure, handlerId: "H1", CancellationToken.None);

        applied.Reenqueued.Should().BeFalse(
            "a concurrent writer already landed the transition -- the next reconciler tick reconciles, per design.md §4C.");
        applied.TargetStatus.Should().Be(RunStatus.Failed);
        enqueuer.Envelopes.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyHandlerOutcomeAsync_EtagConflict_SuccessfulButDrifted_DoesNotReleaseGuard()
    {
        var guard = new RecordingCustomerRunGuard();
        var sut = BuildSut(out _,
            classifier: new StubFailureClassifier(FailureClass.SuccessfulButDrifted),
            repository: new ConflictRunRepository(),
            guard: guard);
        var run = MakeRun(RunStatus.Running, "H0");

        var failure = new HandlerResult.Failure(
            FailureClass.SuccessfulButDrifted, "drift-detected", "H13 detected drift in 2 resources.");

        var applied = await sut.ApplyHandlerOutcomeAsync(
            run, ifMatchEtag: "etag-1", outcome: failure, handlerId: "H13", CancellationToken.None);

        applied.TargetStatus.Should().Be(RunStatus.Completed);
        // Bucket B MED#11 SESSION 18 (customer-provisioning-orchestration-r1
        // adversarial e2e verify workflow wepdcb8we) INVERTS the prior test's
        // expectation: on Conflict where winningStatus == targetStatus AND
        // ShouldReleaseCustomerGuard(failureClass) is true, fire the release
        // as stale-value-safe belt-and-suspenders. Prior behavior "skip release
        // on Conflict" was correct for the common case (both writers race to
        // the same terminal state), but left a rare hole where a partial-replay
        // winner never reached its own applier release call (mid-flow host
        // crash, ServiceBus lock loss). ICustomerRunGuard.ReleaseAsync is
        // stale-value-safe by contract (Mismatched = no-op), so this
        // additional release is safe under all winner-ordering permutations.
        guard.ReleaseCalls.Should().ContainSingle(
            because: "Bucket B MED#11 SESSION 18: Conflict.Current.Run.Status == targetStatus (both Completed) " +
                     "AND SuccessfulButDrifted requires guard release per ShouldReleaseCustomerGuard — fire " +
                     "stale-value-safe belt-and-suspenders release. The concurrent winner's own applier will " +
                     "also attempt release; ICustomerRunGuard's LookupAsync-equality check ensures one clears " +
                     "and the other returns Mismatched (no-op).")
            .Which.Should().Be((TestCustomerId, TestRunId));
    }

    // -----------------------------------------------------------------------
    // SuccessfulButDrifted -- task 104 gap closure: releases the I5 guard.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ApplyHandlerOutcomeAsync_SuccessfulButDrifted_ReleasesCustomerGuardExactlyOnce()
    {
        var guard = new RecordingCustomerRunGuard();
        var sut = BuildSut(out var enqueuer,
            classifier: new StubFailureClassifier(FailureClass.SuccessfulButDrifted),
            guard: guard);
        var run = MakeRun(RunStatus.Running, "H0", "H1", "H2a", "H13");

        var failure = new HandlerResult.Failure(
            FailureClass.SuccessfulButDrifted, "drift-detected", "H13 detected drift in 2 resources.");

        var applied = await sut.ApplyHandlerOutcomeAsync(
            run, ifMatchEtag: "etag-1", outcome: failure, handlerId: "H13", CancellationToken.None);

        applied.TargetStatus.Should().Be(RunStatus.Completed);
        applied.Reenqueued.Should().BeFalse("SuccessfulButDrifted requires operator resume with resumeFromPhase, not auto-retry.");
        enqueuer.Envelopes.Should().BeEmpty();

        guard.ReleaseCalls.Should().ContainSingle().Which.Should().Be((run.CustomerId, run.RunId),
            "task 104 gap closure (DS-2 §5): the customer may start a new run once drift is Completed.");
    }

    [Fact]
    public async Task ApplyHandlerOutcomeAsync_SuccessfulButDrifted_GuardReleaseTransientFailure_DoesNotThrow_OutcomeStillApplied()
    {
        var guard = new RecordingCustomerRunGuard { ReturnTransientFailure = true };
        var sut = BuildSut(out _,
            classifier: new StubFailureClassifier(FailureClass.SuccessfulButDrifted),
            guard: guard);
        var run = MakeRun(RunStatus.Running, "H0");

        var failure = new HandlerResult.Failure(
            FailureClass.SuccessfulButDrifted, "drift-detected", "Diagnostic.");

        var act = async () => await sut.ApplyHandlerOutcomeAsync(
            run, "etag-1", failure, "H13", CancellationToken.None);

        var applied = await act.Should().NotThrowAsync(
            "guard release is best-effort -- a transient failure must not fail the outcome application; " +
            "the operator's cancel/clear-quarantine endpoints remain available as a backstop.");
        applied.Subject.TargetStatus.Should().Be(RunStatus.Completed);
        guard.ReleaseCalls.Should().ContainSingle();
    }

    // -----------------------------------------------------------------------
    // Resumable -- Failed, no re-enqueue, no guard release.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ApplyHandlerOutcomeAsync_Resumable_TransitionsToFailed_ReleasesGuardForFreshRun()
    {
        // EXEC-07 (customer-provisioning-orchestration-r1 Wave 2 B24 punchlist,
        // 2026-08-27): Resumable now RELEASES the guard so a Failed run does
        // NOT permanently poison the customerId. Prior semantic (guard held
        // "so the operator's resume targets the SAME run") caused a single
        // Failed run to 409 forever on the next POST /api/runs until an
        // operator manually PATCHed sprk_currentrunid via pac data. The
        // resume path (/api/runs/{id}/resume) does not check the guard;
        // trade-off documented in RollbackTransitions.ShouldReleaseCustomerGuard.
        var enqueuer = new RecordingEnqueuer();
        var guard = new RecordingCustomerRunGuard();
        var sut = BuildSut(out _,
            classifier: new StubFailureClassifier(FailureClass.Resumable),
            enqueuer: enqueuer,
            guard: guard);
        var run = MakeRun(RunStatus.Running, "H0", "H1");

        var failure = new HandlerResult.Failure(
            FailureClass.Resumable, "missing-precondition", "Operator must grant subscription access first.");

        var applied = await sut.ApplyHandlerOutcomeAsync(
            run, ifMatchEtag: "etag-1", outcome: failure, handlerId: "H2a", CancellationToken.None);

        applied.TargetStatus.Should().Be(RunStatus.Failed);
        applied.Reenqueued.Should().BeFalse("Resumable requires operator POST /api/runs/{id}/resume.");
        enqueuer.Envelopes.Should().BeEmpty();

        guard.ReleaseCalls.Should().ContainSingle().Which.Should().Be((run.CustomerId, run.RunId),
            because: "EXEC-07 — a Failed run must release the guard so a fresh POST /api/runs succeeds " +
                     "(the resume path still targets the SAME failed runId without a guard check).");
    }

    // -----------------------------------------------------------------------
    // Helpers + test doubles
    // -----------------------------------------------------------------------

    private static HandlerOutcomeApplier BuildSut(
        out RecordingEnqueuer enqueuerOut,
        IFailureClassifier? classifier = null,
        IProvisioningRunRepository? repository = null,
        RecordingEnqueuer? enqueuer = null,
        ICustomerRunGuard? guard = null,
        TimeProvider? timeProvider = null)
    {
        enqueuerOut = enqueuer ?? new RecordingEnqueuer();

        return new HandlerOutcomeApplier(
            classifier ?? new StubFailureClassifier(FailureClass.Resumable),
            repository ?? new RecordingRunRepository(),
            enqueuerOut,
            guard ?? new RecordingCustomerRunGuard(),
            timeProvider ?? TimeProvider.System,
            NullLogger<HandlerOutcomeApplier>.Instance);
    }

    private static ProvisioningRun MakeRun(RunStatus status, params string[] completedPhases)
    {
        var run = new ProvisioningRun
        {
            RunId = TestRunId,
            CustomerId = TestCustomerId,
            EnvironmentId = "env-1",
            TenancyModel = "Model2Dedicated",
            Profile = "spaarke-hosted-model2",
            Status = status,
        };
        var now = DateTimeOffset.UtcNow;
        foreach (var phase in completedPhases)
        {
            run.CompletedPhases.Add(new CompletedPhase
            {
                Phase = phase,
                StartedAt = now,
                CompletedAt = now,
                IdempotencyKey = $"{phase.ToLowerInvariant()}-{TestCustomerId}-test",
                JobId = TestRunId,
            });
        }
        return run;
    }

    private sealed class StubFailureClassifier : IFailureClassifier
    {
        private readonly FailureClass _class;
        public StubFailureClassifier(FailureClass @class) => _class = @class;
        public FailureClass Classify(HandlerResult.Failure failure) => _class;
        public FailureClass ClassifyException(Exception exception) => _class;
    }

    /// <summary>Records every ReplaceRunAsync call; always succeeds by default -- mirrors real Cosmos replace-success shape without a live/emulated account.</summary>
    private sealed class RecordingRunRepository : IProvisioningRunRepository
    {
        public List<(ProvisioningRun Run, string ETag)> ReplaceCalls { get; } = new();

        public Task<ProvisioningRunReadResult?> ReadRunAsync(string customerId, string runId, CancellationToken ct)
            => Task.FromResult<ProvisioningRunReadResult?>(null);

        public Task<ProvisioningRunReadResult> CreateRunAsync(ProvisioningRun run, CancellationToken ct)
            => Task.FromResult(new ProvisioningRunReadResult(run, "etag-created"));

        public Task<ReplaceRunResult> ReplaceRunAsync(ProvisioningRun run, string ifMatchEtag, CancellationToken ct)
        {
            ReplaceCalls.Add((run, ifMatchEtag));
            return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.Success(run, $"etag-{Guid.NewGuid()}"));
        }
    }

    /// <summary>Always returns Conflict -- simulates a concurrent writer having already advanced the document's ETag.</summary>
    private sealed class ConflictRunRepository : IProvisioningRunRepository
    {
        public Task<ProvisioningRunReadResult?> ReadRunAsync(string customerId, string runId, CancellationToken ct)
            => Task.FromResult<ProvisioningRunReadResult?>(null);

        public Task<ProvisioningRunReadResult> CreateRunAsync(ProvisioningRun run, CancellationToken ct)
            => Task.FromResult(new ProvisioningRunReadResult(run, "etag-created"));

        public Task<ReplaceRunResult> ReplaceRunAsync(ProvisioningRun run, string ifMatchEtag, CancellationToken ct)
            => Task.FromResult<ReplaceRunResult>(
                new ReplaceRunResult.Conflict(new ProvisioningRunReadResult(run, "etag-winner")));
    }

    /// <summary>Records every enqueued envelope in call order -- no dedup, so tests can inspect each individual dispatch.</summary>
    private sealed class RecordingEnqueuer : IHandlerEnqueuer
    {
        private readonly List<HandlerEnvelope> _envelopes = new();
        public IReadOnlyList<HandlerEnvelope> Envelopes => _envelopes;

        public Task EnqueueAsync(HandlerEnvelope envelope, CancellationToken ct)
        {
            _envelopes.Add(envelope);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Records every ReleaseAsync call. TryAcquireAsync is NEVER called by
    /// HandlerOutcomeApplier -- throws NotSupportedException if invoked so a
    /// future regression that starts calling it fails loudly instead of
    /// silently returning a default.
    /// </summary>
    private sealed class RecordingCustomerRunGuard : ICustomerRunGuard
    {
        public List<(string CustomerId, string RunId)> ReleaseCalls { get; } = new();
        public bool ReturnTransientFailure { get; init; }

        public Task<AcquireResult> TryAcquireAsync(string customerId, string runId, CancellationToken cancellationToken)
            => throw new NotSupportedException("HandlerOutcomeApplier never calls ICustomerRunGuard.TryAcquireAsync.");

        public Task<ReleaseResult> ReleaseAsync(string customerId, string runId, CancellationToken cancellationToken)
        {
            ReleaseCalls.Add((customerId, runId));
            ReleaseResult result = ReturnTransientFailure
                ? new ReleaseResult.TransientFailure(customerId, runId, "simulated transient guard failure")
                : new ReleaseResult.Released(customerId, runId);
            return Task.FromResult(result);
        }
    }

    /// <summary>Minimal TimeProvider double -- avoids adding Microsoft.Extensions.TimeProvider.Testing as a new package dep (per tests/CLAUDE.md guidance).</summary>
    private sealed class TestTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public TestTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
