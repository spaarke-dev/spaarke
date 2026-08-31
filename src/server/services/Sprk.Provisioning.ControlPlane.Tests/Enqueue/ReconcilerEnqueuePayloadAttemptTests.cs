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
//   AC3  A simulated §4C RetryableWithCleanup retry re-enqueue produces a
//        MessageId distinct from the original failed dispatch's. MOVED to
//        HandlerOutcomeApplierTests.cs by task 104 (Phase C'' Wave G-1):
//        ApplyHandlerOutcomeAsync's full §4C logic was extracted out of
//        StateReconcilerService into HandlerOutcomeApplier, so this
//        behavior is now exercised by constructing HandlerOutcomeApplier
//        directly rather than driving it through StateReconcilerService's
//        thin delegating shim.
//   AC4  Two consecutive reconciler ticks against an unchanged ready-set
//        produce IDENTICAL MessageIds (byte-stability preserved for the
//        tick-duplicate-suppression purpose -- the normal enqueue path never
//        reads ProvisioningRun.HandlerRetryAttempts).
//
// SEAM STRATEGY (docs/standards/TEST-ARCHITECTURE.md §5):
//   Hand-rolled in-memory test doubles for IHandlerEnqueuer, IActiveRunScanner
//   -- mirrors the existing StateReconcilerServiceTests.cs convention (no
//   Moq). Task 104 moved the IFailureClassifier / IProvisioningRunRepository
//   doubles out to HandlerOutcomeApplierTests.cs alongside the tests that
//   need them (this file's remaining tests never exercise
//   ApplyHandlerOutcomeAsync).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Reconciler;
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
    // AC3 -- MOVED to HandlerOutcomeApplierTests.cs by task 104 (Phase C''
    //         Wave G-1): the §4C RetryableWithCleanup re-enqueue behavior
    //         previously driven via StateReconcilerService.ApplyHandlerOutcomeAsync
    //         is now exercised directly against the extracted HandlerOutcomeApplier.
    //         See HandlerOutcomeApplierTests.RetryableWithCleanup_*.
    // -----------------------------------------------------------------------

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
        IActiveRunScanner? scanner = null,
        RecordingEnqueuer? sharedEnqueuer = null)
    {
        enqueuer = sharedEnqueuer ?? new RecordingEnqueuer();

        var services = new ServiceCollection();
        services.AddSingleton<IActiveRunScanner>(scanner ?? new StubActiveRunScanner(Array.Empty<ProvisioningRun>()));
        services.AddSingleton<IHandlerEnqueuer>(enqueuer);
        services.AddSingleton<IDagAdvancer, DagAdvancer>();
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
}
