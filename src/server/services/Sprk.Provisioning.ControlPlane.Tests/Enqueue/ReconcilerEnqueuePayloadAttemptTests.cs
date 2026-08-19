// -----------------------------------------------------------------------------
// ReconcilerEnqueuePayloadAttemptTests.cs
//
// L2 CONTROL-PLANE tests for the HandlerEnvelope.Attempt field + its
// participation in ServiceBusHandlerEnqueuer.ComputeMessageId (task 107 /
// DS-2 §4-L1).
//
// DEFECT THIS FIXES:
//   Once Service Bus level-1 duplicate-detection is enabled (task 108), the
//   §4C RetryableWithCleanup auto-retry path breaks: StateReconcilerService.
//   ApplyHandlerOutcomeAsync re-enqueues via BuildEnvelope, whose
//   ReconcilerEnqueuePayload is deliberately byte-stable (EnqueuedAt is NOT
//   in the hash) -- the retry message would carry the IDENTICAL MessageId as
//   the just-consumed original and SB dedup would silently drop it within
//   the 1h window. HandlerEnvelope.Attempt (default 0, incremented ONLY on
//   the re-enqueue path) fixes this: MessageId now hashes
//   SHA256(HandlerId|RunId|CustomerId|paramHash|attempt).
//
// TESTED BEHAVIORS (POML 107 acceptance criteria):
//   AC1  HandlerEnvelope serializes an 'attempt' field (camelCase) in its
//        JSON payload when non-zero; OMITTED when zero (first-enqueue byte-
//        stability contract preserved).
//   AC2  ComputeMessageId(envelope) with attempt=1 produces a DIFFERENT hash
//        than the same envelope with attempt=0, all other fields equal.
//   AC3  A simulated §4C RetryableWithCleanup retry re-enqueue (driven via
//        StateReconcilerService.ApplyHandlerOutcomeAsync) produces a
//        MessageId distinct from the original failed dispatch's.
//   AC4  Two consecutive reconciler ticks against an unchanged ready-set
//        produce IDENTICAL MessageIds (byte-stability preserved for the
//        tick-duplicate-suppression purpose -- the normal enqueue path never
//        reads ProvisioningRun.HandlerRetryAttempts).
//
// SEAM STRATEGY (docs/standards/TEST-ARCHITECTURE.md §5):
//   Hand-rolled in-memory test doubles for IFailureClassifier,
//   IProvisioningRunRepository, IHandlerEnqueuer, IActiveRunScanner --
//   mirrors the existing StateReconcilerServiceTests.cs convention (no Moq).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Reconciler;
using Sprk.Provisioning.ControlPlane.Repositories;
using Sprk.Provisioning.ControlPlane.Rollback;
using System.Text.Json;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Enqueue;

public sealed class ReconcilerEnqueuePayloadAttemptTests
{
    private const string TestCustomerId = "test-customer";
    private const string TestRunId = "00000000-0000-0000-0000-000000000001";

    // -----------------------------------------------------------------------
    // AC1 -- wire-format: attempt omitted at 0, present (camelCase) otherwise.
    // -----------------------------------------------------------------------

    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void Serialize_AttemptZero_OmitsAttemptField()
    {
        var envelope = MakeEnvelope(attempt: 0);

        var json = JsonSerializer.Serialize(envelope, CamelCase);

        json.Should().NotContain("\"attempt\"",
            "first-enqueue byte-stability (task 107) requires the wire payload to OMIT attempt at its default value.");
    }

    [Fact]
    public void Serialize_AttemptNonZero_IncludesCamelCaseAttemptField()
    {
        var envelope = MakeEnvelope(attempt: 2);

        var json = JsonSerializer.Serialize(envelope, CamelCase);

        json.Should().Contain("\"attempt\":2");
    }

    // -----------------------------------------------------------------------
    // AC2 -- ComputeMessageId hash includes attempt.
    // -----------------------------------------------------------------------

    [Fact]
    public void ComputeMessageId_AttemptOneVsAttemptZero_ProducesDifferentHash_AllOtherFieldsEqual()
    {
        var envelopeAttempt0 = MakeEnvelope(attempt: 0);
        var envelopeAttempt1 = envelopeAttempt0 with { Attempt = 1 };

        var messageId0 = ServiceBusHandlerEnqueuer.ComputeMessageId(envelopeAttempt0);
        var messageId1 = ServiceBusHandlerEnqueuer.ComputeMessageId(envelopeAttempt1);

        messageId0.Should().NotBe(messageId1,
            "spec.md MUST rule: MessageId = SHA256(HandlerId|RunId|CustomerId|paramHash|attempt) -- " +
            "attempt participates in the hash so a retry survives SB level-1 dedup.");
    }

    [Fact]
    public void ComputeMessageId_SameAttempt_IsDeterministic()
    {
        var envelopeA = MakeEnvelope(attempt: 3);
        var envelopeB = MakeEnvelope(attempt: 3);

        ServiceBusHandlerEnqueuer.ComputeMessageId(envelopeA)
            .Should().Be(ServiceBusHandlerEnqueuer.ComputeMessageId(envelopeB),
                "two envelopes with identical fields (including attempt) MUST hash to the same MessageId.");
    }

    // -----------------------------------------------------------------------
    // BuildEnvelope defaults + explicit attempt.
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildEnvelope_NoAttemptArgument_DefaultsToZero()
    {
        var sut = BuildSut(out _);
        var run = MakeRun(RunStatus.Running, "H0");

        var envelope = sut.BuildEnvelope("H1", run);

        envelope.Attempt.Should().Be(0,
            "the normal tick-driven ready-set enqueue path MUST default to attempt=0 (unchanged byte-stability).");
    }

    [Fact]
    public void BuildEnvelope_ExplicitAttempt_PropagatesToEnvelope()
    {
        var sut = BuildSut(out _);
        var run = MakeRun(RunStatus.Running, "H0");

        var envelope = sut.BuildEnvelope("H1", run, attempt: 5);

        envelope.Attempt.Should().Be(5);
    }

    // -----------------------------------------------------------------------
    // AC3 -- §4C RetryableWithCleanup re-enqueue produces a distinct MessageId
    //         from the original dispatch's.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ApplyHandlerOutcomeAsync_RetryableWithCleanup_ReenqueuesWithIncrementedAttempt_DistinctFromOriginalMessageId()
    {
        // Arrange
        var sut = BuildSut(out var enqueuer,
            classifier: new StubFailureClassifier(FailureClass.RetryableWithCleanup),
            repository: new EchoingRunRepository());
        var run = MakeRun(RunStatus.Running, "H0");

        // The "just-consumed original" dispatch -- what the reconciler sent
        // BEFORE this handler failed (attempt=0, the normal enqueue path).
        var originalEnvelope = sut.BuildEnvelope("H1", run);
        var originalMessageId = ServiceBusHandlerEnqueuer.ComputeMessageId(originalEnvelope);

        var failure = new HandlerResult.Failure(
            FailureClass.RetryableWithCleanup,
            RejectionCode: "transient-http-503",
            Diagnostic: "Downstream call returned 503; safe to retry.");

        // Act -- the reconciler classifies the failure + re-enqueues per §4C.
        var applied = await sut.ApplyHandlerOutcomeAsync(
            run, ifMatchEtag: "etag-1", outcome: failure, handlerId: "H1", CancellationToken.None);

        // Assert
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
            classifier: new StubFailureClassifier(FailureClass.RetryableWithCleanup),
            repository: new EchoingRunRepository());
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
    // AC4 -- normal tick-driven first-enqueue path is byte-stable across
    //         repeated ticks (unaffected by HandlerRetryAttempts contents).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Tick_TwoConsecutiveTicksUnchangedReadySet_ProducesIdenticalMessageId()
    {
        var run = MakeRun(RunStatus.Running, "H0");
        var scanner = new StubActiveRunScanner(new[] { run });
        var enqueuer = new RecordingEnqueuer();
        var sut = BuildSut(out _, scanner: scanner, sharedEnqueuer: enqueuer);

        await sut.RunTickAsync(CancellationToken.None);
        await sut.RunTickAsync(CancellationToken.None);

        enqueuer.Envelopes.Should().HaveCount(2, "no run-state change occurred between ticks, so both ticks dispatch H1 again.");
        enqueuer.Envelopes.Should().OnlyContain(e => e.Attempt == 0,
            "the normal tick-driven path never reads ProvisioningRun.HandlerRetryAttempts.");

        var messageId1 = ServiceBusHandlerEnqueuer.ComputeMessageId(enqueuer.Envelopes[0]);
        var messageId2 = ServiceBusHandlerEnqueuer.ComputeMessageId(enqueuer.Envelopes[1]);
        messageId1.Should().Be(messageId2,
            "first-enqueue byte-stability (the actual purpose of SB level-1 dedup) MUST be unaffected by task 107.");
    }

    // -----------------------------------------------------------------------
    // Helpers + test doubles
    // -----------------------------------------------------------------------

    private static HandlerEnvelope MakeEnvelope(int attempt) => new()
    {
        HandlerId = "H1",
        RunId = TestRunId,
        CustomerId = TestCustomerId,
        ParametersJson = "{\"customerId\":\"test-customer\",\"runId\":\"00000000-0000-0000-0000-000000000001\",\"action\":\"reconciler-advance\",\"handlerId\":\"H1\"}",
        EnqueuedAt = DateTimeOffset.Parse("2026-08-19T12:00:00Z"),
        Attempt = attempt,
    };

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

    private static StateReconcilerService BuildSut(
        out RecordingEnqueuer enqueuer,
        IFailureClassifier? classifier = null,
        IProvisioningRunRepository? repository = null,
        IActiveRunScanner? scanner = null,
        RecordingEnqueuer? sharedEnqueuer = null)
    {
        enqueuer = sharedEnqueuer ?? new RecordingEnqueuer();

        var services = new ServiceCollection();
        services.AddSingleton<IActiveRunScanner>(scanner ?? new StubActiveRunScanner(Array.Empty<ProvisioningRun>()));
        services.AddSingleton<IHandlerEnqueuer>(enqueuer);
        services.AddSingleton<IDagAdvancer, DagAdvancer>();
        services.AddSingleton(classifier ?? new StubFailureClassifier(FailureClass.RetryableWithCleanup));
        services.AddSingleton(repository ?? (IProvisioningRunRepository)new EchoingRunRepository());
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new StateReconcilerService(
            scopeFactory,
            Options.Create(new ReconcilerOptions()),
            TimeProvider.System,
            NullLogger<StateReconcilerService>.Instance);
    }

    private sealed class StubActiveRunScanner : IActiveRunScanner
    {
        private readonly IReadOnlyList<ProvisioningRun> _runs;
        public StubActiveRunScanner(IEnumerable<ProvisioningRun> runs) => _runs = runs.ToList();
        public Task<IReadOnlyList<ProvisioningRun>> QueryActiveRunsAsync(CancellationToken ct)
            => Task.FromResult(_runs);
    }

    /// <summary>Records every enqueued envelope in call order -- no dedup (unlike StateReconcilerServiceTests' DedupingRecordingEnqueuer) so tests can inspect each individual dispatch.</summary>
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

    private sealed class StubFailureClassifier : IFailureClassifier
    {
        private readonly FailureClass _class;
        public StubFailureClassifier(FailureClass @class) => _class = @class;
        public FailureClass Classify(HandlerResult.Failure failure) => _class;
        public FailureClass ClassifyException(Exception exception) => _class;
    }

    /// <summary>Always succeeds, echoing back the (already-mutated) run with a fresh synthetic ETag -- mirrors real Cosmos replace-success shape without a live/emulated account.</summary>
    private sealed class EchoingRunRepository : IProvisioningRunRepository
    {
        public Task<ProvisioningRunReadResult?> ReadRunAsync(string customerId, string runId, CancellationToken ct)
            => Task.FromResult<ProvisioningRunReadResult?>(null);

        public Task<ProvisioningRunReadResult> CreateRunAsync(ProvisioningRun run, CancellationToken ct)
            => Task.FromResult(new ProvisioningRunReadResult(run, "etag-created"));

        public Task<ReplaceRunResult> ReplaceRunAsync(ProvisioningRun run, string ifMatchEtag, CancellationToken ct)
            => Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.Success(run, $"etag-{Guid.NewGuid()}"));
    }
}
