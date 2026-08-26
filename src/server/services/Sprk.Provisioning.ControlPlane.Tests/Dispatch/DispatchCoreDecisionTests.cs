// -----------------------------------------------------------------------------
// DispatchCoreDecisionTests.cs
//
// L2 CONTROL-PLANE table-driven unit-test suite over
// ProvisioningHandlerDispatcher.DispatchCoreAsync -- the pure decision
// function task 102 extracted specifically so this suite could run without a
// live Service Bus / Cosmos / Redis connection (task 105, Phase C'' Wave G-1,
// closing DS-2 §6.1).
//
// SCOPE NOTE -- "bad JSON" row (DS-2 §6.1 table row 1) is NOT unit-tested here:
//   DispatchCoreAsync's own doc comment states it covers "steps 2-8" of the
//   DS-2 §2.5 flow -- envelope DESERIALIZATION (step 1 / the "bad JSON" row)
//   is completed by the CALLER (OnSessionMessageAsync) before DispatchCoreAsync
//   is ever invoked; DispatchCoreAsync's signature takes an
//   already-deserialized HandlerEnvelope. That split is intentional (see
//   ProvisioningHandlerDispatcher.cs's own header: extracting the pure
//   decision function is what makes this whole test class possible).
//
//   The ONLY code that performs deserialization (TryDeserializeEnvelope) is
//   a PRIVATE static method reached solely from another PRIVATE method
//   (OnSessionMessageAsync), which is itself reachable only via a live
//   ServiceBusSessionProcessor message event. Testing it via reflection --
//   the initial draft of this suite did exactly that -- is ADR-038 §7 ban
//   B8 ("Internal/private method tests via InternalsVisibleTo or
//   reflection"): it locks an implementation detail that should stay free
//   to refactor, and the correct fix per B8's own guidance is "test through
//   the public surface" -- which for this row means a LIVE (or SDK-model-
//   factory-backed) Service Bus message flow, out of this pure-unit-test
//   class's scope. This is the DispatchCoreDecisionTests row that is simply
//   NOT covered at this layer; the class instead covers 14 real
//   DispatchCoreAsync-exercised cases (>= the 11 required by the POML
//   acceptance criterion) with zero reflection into private members. The
//   3-line null-check-then-dead-letter block in OnSessionMessageAsync that
//   would consume a null TryDeserializeEnvelope result is straightforward
//   enough to be covered by code review; a live/seam-level exercise of the
//   full message path is task 118's territory (Tests/Seam/**), not this task's.
//
// FAKES (no Moq -- hand-rolled per docs/standards/TEST-ARCHITECTURE.md §5 /
// existing project convention -- StateReconcilerServiceTests,
// HandlerOutcomeApplierTests, ReconcilerEnqueuePayloadAttemptTests):
//   FakeProvisioningHandler        -- IProvisioningHandler; configurable
//                                     Success / Failure / throw behavior;
//                                     records whether HandleAsync was invoked.
//   FakeDispatchIdempotencyService -- IDispatchIdempotencyService; configurable
//                                     IsProcessed / lock-acquired outcomes;
//                                     records every call.
//   FakeProvisioningRunRepository  -- IProvisioningRunRepository; returns a
//                                     configurable sequence of ReadRunAsync
//                                     results (step 4's initial read + step
//                                     6's fresh re-read can differ).
//   RecordingHandlerOutcomeApplier -- IHandlerOutcomeApplier; records every
//                                     call; can be configured to throw.
//   FailureClassifier (REAL)       -- the actual production classifier
//                                     (Rollback/FailureClassifier.cs) is used
//                                     as-is; it is a pure, dependency-free
//                                     policy class with no reason to fake.
//
// DISPATCHER CONSTRUCTION:
//   ProvisioningHandlerDispatcher.DispatchCoreAsync is an INSTANCE method
//   (internal, InternalsVisibleTo("...Tests") on the .Worker csproj), so a
//   real dispatcher instance is constructed via its public constructor. The
//   ServiceBusClient + IServiceScopeFactory ctor params are NEVER touched by
//   DispatchCoreAsync itself (only by ExecuteAsync / OnSessionMessageAsync,
//   neither of which this suite calls) -- they are satisfied with offline
//   placeholders (a ServiceBusClient constructed against a fake token
//   credential that is never invoked; a throwaway IServiceScopeFactory) so
//   no live Azure connection is opened.
// -----------------------------------------------------------------------------

extern alias WorkerHost;

using Azure.Core;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Dispatch;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Modules;
using Sprk.Provisioning.ControlPlane.Reconciler;
using Sprk.Provisioning.ControlPlane.Repositories;
using Sprk.Provisioning.ControlPlane.Rollback;
using Xunit;
using DispatcherType = WorkerHost::Sprk.Provisioning.ControlPlane.Worker.Dispatch.ProvisioningHandlerDispatcher;

namespace Sprk.Provisioning.ControlPlane.Tests.Dispatch;

public sealed class DispatchCoreDecisionTests
{
    private const string TestCustomerId = "test-customer";
    private const string TestRunId = "00000000-0000-0000-0000-000000000001";
    private const string TestHandlerId = "H1";

    // =====================================================================
    // Row 1 ("bad JSON" -> DeadLetter(InvalidFormat)) -- NOT covered in this
    // class. See file-header SCOPE NOTE for the ADR-038 B8 rationale.
    // =====================================================================

    // =====================================================================
    // Row 2 -- unknown id -> DeadLetter(NoHandler).
    // =====================================================================

    [Fact]
    public async Task Row2_UnknownHandlerId_ReturnsDeadLetter_NoHandler()
    {
        var handler = new FakeProvisioningHandler(TestHandlerId);
        var idempotency = new FakeDispatchIdempotencyService();
        var provider = BuildScopedProvider(handler, keyedAs: TestHandlerId, idempotency: idempotency);
        var envelope = MakeEnvelope(handlerId: "NOT-A-REGISTERED-HANDLER");
        var dispatcher = BuildDispatcher();

        var decision = await dispatcher.DispatchCoreAsync(envelope, deliveryCount: 1, provider, CancellationToken.None);

        var deadLetter = decision.Should().BeOfType<DispatchDecision.DeadLetter>().Subject;
        deadLetter.Reason.Should().Be(DeadLetterReasons.NoHandler);
        idempotency.Calls.Should().BeEmpty(
            "handler resolution happens BEFORE the Level-2 idempotency gate (step 2 precedes step 3) -- " +
            "an unresolvable handler must never touch the idempotency cache.");
    }

    // =====================================================================
    // Bonus -- DI construction throws during keyed resolution ->
    // DeadLetter(HandlerResolutionFailed). Not a distinct DS-2 §6.1 table
    // row but the sibling dead-letter reason STEP 2's code distinguishes
    // from NoHandler; included for completeness of DeadLetterReasons coverage.
    // =====================================================================

    [Fact]
    public async Task Bonus_HandlerDiConstructionThrows_ReturnsDeadLetter_HandlerResolutionFailed()
    {
        var idempotency = new FakeDispatchIdempotencyService();
        var provider = BuildScopedProviderWithThrowingHandlerFactory(TestHandlerId, idempotency);
        var envelope = MakeEnvelope(handlerId: TestHandlerId);
        var dispatcher = BuildDispatcher();

        var decision = await dispatcher.DispatchCoreAsync(envelope, deliveryCount: 1, provider, CancellationToken.None);

        var deadLetter = decision.Should().BeOfType<DispatchDecision.DeadLetter>().Subject;
        deadLetter.Reason.Should().Be(DeadLetterReasons.HandlerResolutionFailed);
        idempotency.Calls.Should().BeEmpty("a DI construction fault is also detected before step 3.");
    }

    // =====================================================================
    // Row 3 -- processed-marker hit -> Complete-without-invoke.
    // =====================================================================

    [Fact]
    public async Task Row3_ProcessedMarkerHit_ReturnsComplete_HandlerNeverInvoked()
    {
        var handler = new FakeProvisioningHandler(TestHandlerId);
        var idempotency = new FakeDispatchIdempotencyService { IsProcessedResult = true };
        var provider = BuildScopedProvider(handler, keyedAs: TestHandlerId, idempotency: idempotency);
        var envelope = MakeEnvelope(handlerId: TestHandlerId);
        var dispatcher = BuildDispatcher();

        var decision = await dispatcher.DispatchCoreAsync(envelope, deliveryCount: 1, provider, CancellationToken.None);

        decision.Should().BeOfType<DispatchDecision.Complete>();
        handler.WasInvoked.Should().BeFalse("a processed-marker hit short-circuits BEFORE step 5's handler invocation.");
        idempotency.TryAcquireLockCalls.Should().Be(0,
            "the processed check short-circuits before TryAcquireLockAsync is ever called (step 3's " +
            "IsProcessed check precedes the lock-acquire check).");
        idempotency.ReleaseLockCalls.Should().BeEmpty(
            "no lock was acquired on this path, so the finally block's release must never fire for this messageId.");
    }

    // =====================================================================
    // Row 4 -- lock-held -> Abandon.
    // =====================================================================

    [Fact]
    public async Task Row4_LockHeldByPeer_ReturnsAbandon_HandlerNeverInvoked()
    {
        var handler = new FakeProvisioningHandler(TestHandlerId);
        var idempotency = new FakeDispatchIdempotencyService { IsProcessedResult = false, TryAcquireLockResult = false };
        var provider = BuildScopedProvider(handler, keyedAs: TestHandlerId, idempotency: idempotency);
        var envelope = MakeEnvelope(handlerId: TestHandlerId);
        var dispatcher = BuildDispatcher();

        var decision = await dispatcher.DispatchCoreAsync(envelope, deliveryCount: 1, provider, CancellationToken.None);

        decision.Should().BeOfType<DispatchDecision.Abandon>();
        handler.WasInvoked.Should().BeFalse();
        idempotency.ReleaseLockCalls.Should().BeEmpty(
            "lock acquisition FAILED -- there is nothing for this instance to release.");
    }

    // =====================================================================
    // Row 5 -- orphan run -> DeadLetter(OrphanRun).
    // =====================================================================

    [Fact]
    public async Task Row5_OrphanRun_ReturnsDeadLetter_OrphanRun()
    {
        var handler = new FakeProvisioningHandler(TestHandlerId);
        var idempotency = new FakeDispatchIdempotencyService();
        var repository = new FakeProvisioningRunRepository((ProvisioningRunReadResult?)null);
        var applier = new RecordingHandlerOutcomeApplier();
        var provider = BuildScopedProvider(handler, TestHandlerId, idempotency, repository, applier);
        var envelope = MakeEnvelope(handlerId: TestHandlerId);
        var dispatcher = BuildDispatcher();

        var decision = await dispatcher.DispatchCoreAsync(envelope, deliveryCount: 1, provider, CancellationToken.None);

        var deadLetter = decision.Should().BeOfType<DispatchDecision.DeadLetter>().Subject;
        deadLetter.Reason.Should().Be(DeadLetterReasons.OrphanRun);
        handler.WasInvoked.Should().BeFalse("the run doc must exist before the handler is invoked (handler contract).");
        applier.Calls.Should().BeEmpty();
        idempotency.ReleaseLockCalls.Should().ContainSingle(
            "the lock WAS acquired for this messageId (this row is inside the try/finally), so it must be released.");
        idempotency.MarkProcessedCalls.Should().BeEmpty(
            "step 7's MarkProcessedAsync is never reached -- the OrphanRun dead-letter is an early return inside the try block.");
    }

    // =====================================================================
    // Row 6 -- terminal-status run -> Complete-without-invoke.
    // =====================================================================

    [Theory]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.Cancelled)]
    [InlineData(RunStatus.Quarantined)]
    public async Task Row6_TerminalStatusRun_ReturnsComplete_HandlerNeverInvoked(RunStatus terminalStatus)
    {
        var handler = new FakeProvisioningHandler(TestHandlerId);
        var idempotency = new FakeDispatchIdempotencyService();
        var run = MakeRun(terminalStatus);
        var repository = new FakeProvisioningRunRepository(new ProvisioningRunReadResult(run, "etag-1"));
        var applier = new RecordingHandlerOutcomeApplier();
        var provider = BuildScopedProvider(handler, TestHandlerId, idempotency, repository, applier);
        var envelope = MakeEnvelope(handlerId: TestHandlerId);
        var dispatcher = BuildDispatcher();

        var decision = await dispatcher.DispatchCoreAsync(envelope, deliveryCount: 1, provider, CancellationToken.None);

        decision.Should().BeOfType<DispatchDecision.Complete>();
        handler.WasInvoked.Should().BeFalse(
            "a stale dispatch against an already-terminal run must never invoke the handler.");
        applier.Calls.Should().BeEmpty();
        idempotency.MarkProcessedCalls.Should().BeEmpty(
            "the terminal-status short-circuit is an early return inside the try block, before step 7.");
        idempotency.ReleaseLockCalls.Should().ContainSingle();
    }

    // =====================================================================
    // Row 7 -- Success -> applier-called + Complete.
    // =====================================================================

    [Fact]
    public async Task Row7_HandlerSuccess_AppliesOutcome_ReturnsComplete_MarksProcessed()
    {
        var successOutcome = new HandlerResult.Success("idem-key-1");
        var handler = new FakeProvisioningHandler(TestHandlerId, onHandle: (_, _) => Task.FromResult<HandlerResult>(successOutcome));
        var idempotency = new FakeDispatchIdempotencyService();
        var run = MakeRun(RunStatus.Running);
        var repository = new FakeProvisioningRunRepository(new ProvisioningRunReadResult(run, "etag-1"));
        var applier = new RecordingHandlerOutcomeApplier();
        var provider = BuildScopedProvider(handler, TestHandlerId, idempotency, repository, applier);
        var envelope = MakeEnvelope(handlerId: TestHandlerId);
        var dispatcher = BuildDispatcher();

        var decision = await dispatcher.DispatchCoreAsync(envelope, deliveryCount: 1, provider, CancellationToken.None);

        decision.Should().BeOfType<DispatchDecision.Complete>();
        handler.WasInvoked.Should().BeTrue();
        applier.Calls.Should().ContainSingle().Which.Outcome.Should().BeSameAs(successOutcome);
        idempotency.MarkProcessedCalls.Should().ContainSingle(
            "step 7 marks the message processed on the happy path.");
        idempotency.ReleaseLockCalls.Should().ContainSingle();
    }

    // =====================================================================
    // Row 8 -- Failure -> applier-called + Complete.
    // (Handler DOMAIN failures are NEVER dead-lettered -- retry authority is
    // §4C RollbackTransitions inside the applier, never the SB Abandon-loop.)
    // =====================================================================

    [Fact]
    public async Task Row8_HandlerFailure_AppliesOutcome_ReturnsComplete_NotDeadLettered()
    {
        var failureOutcome = new HandlerResult.Failure(FailureClass.QuarantineRequired, "some-rejection-code", "diagnostic");
        var handler = new FakeProvisioningHandler(TestHandlerId, onHandle: (_, _) => Task.FromResult<HandlerResult>(failureOutcome));
        var idempotency = new FakeDispatchIdempotencyService();
        var run = MakeRun(RunStatus.Running);
        var repository = new FakeProvisioningRunRepository(new ProvisioningRunReadResult(run, "etag-1"));
        var applier = new RecordingHandlerOutcomeApplier();
        var provider = BuildScopedProvider(handler, TestHandlerId, idempotency, repository, applier);
        var envelope = MakeEnvelope(handlerId: TestHandlerId);
        var dispatcher = BuildDispatcher();

        var decision = await dispatcher.DispatchCoreAsync(envelope, deliveryCount: 1, provider, CancellationToken.None);

        decision.Should().BeOfType<DispatchDecision.Complete>(
            "handler DOMAIN failures apply their §4C transition then Complete the SB message -- " +
            "retry authority belongs to RollbackTransitions, never SB Abandon.");
        applier.Calls.Should().ContainSingle().Which.Outcome.Should().BeSameAs(failureOutcome);
        idempotency.MarkProcessedCalls.Should().ContainSingle();
    }

    // =====================================================================
    // Row 9 -- handler throws -> ClassifyException path + applier-called.
    // =====================================================================

    [Fact]
    public async Task Row9_HandlerThrows_ClassifiesException_AppliesFailureOutcome_ReturnsComplete()
    {
        var handler = new FakeProvisioningHandler(
            TestHandlerId, onHandle: (_, _) => throw new InvalidOperationException("handler bug"));
        var idempotency = new FakeDispatchIdempotencyService();
        var run = MakeRun(RunStatus.Running);
        var repository = new FakeProvisioningRunRepository(new ProvisioningRunReadResult(run, "etag-1"));
        var applier = new RecordingHandlerOutcomeApplier();
        var provider = BuildScopedProvider(handler, TestHandlerId, idempotency, repository, applier);
        var envelope = MakeEnvelope(handlerId: TestHandlerId);
        var dispatcher = BuildDispatcher();

        var decision = await dispatcher.DispatchCoreAsync(envelope, deliveryCount: 1, provider, CancellationToken.None);

        decision.Should().BeOfType<DispatchDecision.Complete>(
            "the dispatcher does not trust the handler contract -- an escaped exception is classified " +
            "via IFailureClassifier.ClassifyException and still routed through the applier, exactly like " +
            "a well-behaved HandlerResult.Failure.");
        applier.Calls.Should().ContainSingle();
        var appliedOutcome = applier.Calls[0].Outcome.Should().BeOfType<HandlerResult.Failure>().Subject;
        appliedOutcome.RejectionCode.Should().StartWith("handler-exception-InvalidOperationException");
        appliedOutcome.Class.Should().Be(FailureClass.Resumable,
            "FailureClassifier.ClassifyException's SAFE default for an unrecognized exception type is Resumable.");
    }

    // =====================================================================
    // Row 10 -- applier throws + deliveryCount < max -> Abandon.
    // =====================================================================

    [Fact]
    public async Task Row10_ApplierThrows_DeliveryCountBelowMax_ReturnsAbandon()
    {
        var handler = new FakeProvisioningHandler(
            TestHandlerId, onHandle: (_, _) => Task.FromResult<HandlerResult>(new HandlerResult.Success("k")));
        var idempotency = new FakeDispatchIdempotencyService();
        var run = MakeRun(RunStatus.Running);
        var repository = new FakeProvisioningRunRepository(new ProvisioningRunReadResult(run, "etag-1"));
        var applier = new RecordingHandlerOutcomeApplier { ThrowOnApply = new InvalidOperationException("Cosmos transient fault") };
        var provider = BuildScopedProvider(handler, TestHandlerId, idempotency, repository, applier);
        var envelope = MakeEnvelope(handlerId: TestHandlerId);
        var dispatcher = BuildDispatcher(maxDeliveryCount: 5);

        var decision = await dispatcher.DispatchCoreAsync(envelope, deliveryCount: 2, provider, CancellationToken.None);

        decision.Should().BeOfType<DispatchDecision.Abandon>();
        idempotency.MarkProcessedCalls.Should().BeEmpty(
            "step 7 is never reached when the outcome-apply itself fails.");
        idempotency.ReleaseLockCalls.Should().ContainSingle(
            "the finally block still releases the lock even when the try body returns via the " +
            "transient-failure helper.");
    }

    // =====================================================================
    // Row 11 -- applier throws + deliveryCount >= max -> DeadLetter(OutcomeApplyFailed).
    // =====================================================================

    [Fact]
    public async Task Row11_ApplierThrows_DeliveryCountAtMax_ReturnsDeadLetter_OutcomeApplyFailed()
    {
        var handler = new FakeProvisioningHandler(
            TestHandlerId, onHandle: (_, _) => Task.FromResult<HandlerResult>(new HandlerResult.Success("k")));
        var idempotency = new FakeDispatchIdempotencyService();
        var run = MakeRun(RunStatus.Running);
        var repository = new FakeProvisioningRunRepository(new ProvisioningRunReadResult(run, "etag-1"));
        var applier = new RecordingHandlerOutcomeApplier { ThrowOnApply = new InvalidOperationException("Cosmos still down") };
        var provider = BuildScopedProvider(handler, TestHandlerId, idempotency, repository, applier);
        var envelope = MakeEnvelope(handlerId: TestHandlerId);
        var dispatcher = BuildDispatcher(maxDeliveryCount: 5);

        var decision = await dispatcher.DispatchCoreAsync(envelope, deliveryCount: 5, provider, CancellationToken.None);

        var deadLetter = decision.Should().BeOfType<DispatchDecision.DeadLetter>().Subject;
        deadLetter.Reason.Should().Be(DeadLetterReasons.OutcomeApplyFailed);
        idempotency.ReleaseLockCalls.Should().ContainSingle();
    }

    // =====================================================================
    // Helpers -- dispatcher construction (offline placeholders for the ctor
    // params DispatchCoreAsync itself never touches).
    // =====================================================================

    private static DispatcherType BuildDispatcher(int maxDeliveryCount = 5)
    {
        var serviceBusClient = new ServiceBusClient("fake.servicebus.windows.net", new NeverInvokedTokenCredential());
        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var dispatcherOptions = Options.Create(new DispatcherOptions
        {
            MaxDeliveryCount = maxDeliveryCount,
            MaxHandlerDuration = TimeSpan.FromMinutes(65),
        });
        var sbOptions = Options.Create(new ServiceBusModuleOptions
        {
            FullyQualifiedNamespace = "fake.servicebus.windows.net",
            QueueName = "sprk-provisioning-jobs",
        });

        return new DispatcherType(
            serviceBusClient,
            scopeFactory,
            dispatcherOptions,
            sbOptions,
            TimeProvider.System,
            NullLogger<DispatcherType>.Instance);
    }

    private static IServiceProvider BuildScopedProvider(
        FakeProvisioningHandler handler,
        string keyedAs,
        FakeDispatchIdempotencyService idempotency,
        IProvisioningRunRepository? repository = null,
        IHandlerOutcomeApplier? applier = null)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IProvisioningHandler>(keyedAs, handler);
        services.AddSingleton<IDispatchIdempotencyService>(idempotency);
        services.AddSingleton(repository ?? new FakeProvisioningRunRepository());
        services.AddSingleton(applier ?? new RecordingHandlerOutcomeApplier());
        services.AddSingleton<IFailureClassifier, FailureClassifier>();
        return services.BuildServiceProvider();
    }

    private static IServiceProvider BuildScopedProviderWithThrowingHandlerFactory(
        string keyedAs, FakeDispatchIdempotencyService idempotency)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IProvisioningHandler>(
            keyedAs, (_, _) => throw new InvalidOperationException("Simulated handler DI construction failure."));
        services.AddSingleton<IDispatchIdempotencyService>(idempotency);
        services.AddSingleton<IProvisioningRunRepository>(new FakeProvisioningRunRepository());
        services.AddSingleton<IHandlerOutcomeApplier>(new RecordingHandlerOutcomeApplier());
        services.AddSingleton<IFailureClassifier, FailureClassifier>();
        return services.BuildServiceProvider();
    }

    private static HandlerEnvelope MakeEnvelope(string handlerId) => new()
    {
        HandlerId = handlerId,
        RunId = TestRunId,
        CustomerId = TestCustomerId,
        ParametersJson = "{}",
        EnqueuedAt = DateTimeOffset.Parse("2026-08-19T12:00:00Z"),
    };

    private static ProvisioningRun MakeRun(RunStatus status) => new()
    {
        RunId = TestRunId,
        CustomerId = TestCustomerId,
        EnvironmentId = "env-1",
        TenancyModel = "Model2Dedicated",
        Profile = "spaarke-hosted-model2",
        Status = status,
    };

    // =====================================================================
    // Fakes / test doubles.
    // =====================================================================

    /// <summary>Never invoked -- DispatchCoreAsync's ctor params for the Service Bus wire are unused by the method under test.</summary>
    private sealed class NeverInvokedTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException("DispatchCoreAsync never touches the Service Bus wire.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException("DispatchCoreAsync never touches the Service Bus wire.");
    }

    private sealed class FakeProvisioningHandler : IProvisioningHandler
    {
        private readonly Func<HandlerEnvelope, CancellationToken, Task<HandlerResult>> _onHandle;

        public FakeProvisioningHandler(
            string handlerId, Func<HandlerEnvelope, CancellationToken, Task<HandlerResult>>? onHandle = null)
        {
            HandlerId = handlerId;
            _onHandle = onHandle ?? ((_, _) => Task.FromResult<HandlerResult>(new HandlerResult.Success("default")));
        }

        public string HandlerId { get; }
        public bool WasInvoked { get; private set; }

        public Task<HandlerResult> HandleAsync(HandlerEnvelope envelope, CancellationToken cancellationToken)
        {
            WasInvoked = true;
            return _onHandle(envelope, cancellationToken);
        }
    }

    private sealed class FakeDispatchIdempotencyService : IDispatchIdempotencyService
    {
        public bool IsProcessedResult { get; init; }
        public bool TryAcquireLockResult { get; init; } = true;

        public List<string> Calls { get; } = new();
        public int TryAcquireLockCalls { get; private set; }
        public List<string> MarkProcessedCalls { get; } = new();
        public List<string> ReleaseLockCalls { get; } = new();

        public Task<bool> IsProcessedAsync(string messageId, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(IsProcessedAsync));
            return Task.FromResult(IsProcessedResult);
        }

        public Task<bool> TryAcquireLockAsync(string messageId, TimeSpan ttl, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(TryAcquireLockAsync));
            TryAcquireLockCalls++;
            return Task.FromResult(TryAcquireLockResult);
        }

        public Task MarkProcessedAsync(string messageId, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(MarkProcessedAsync));
            MarkProcessedCalls.Add(messageId);
            return Task.CompletedTask;
        }

        public Task ReleaseLockAsync(string messageId, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(ReleaseLockAsync));
            ReleaseLockCalls.Add(messageId);
            return Task.CompletedTask;
        }
    }

    /// <summary>Returns a configurable sequence of ReadRunAsync results (repeats the last entry once exhausted) so a test can differ step-4's initial read from step-6's fresh re-read.</summary>
    private sealed class FakeProvisioningRunRepository : IProvisioningRunRepository
    {
        private readonly IReadOnlyList<ProvisioningRunReadResult?> _reads;
        private int _index;

        public FakeProvisioningRunRepository(params ProvisioningRunReadResult?[] reads) => _reads = reads;

        public int ReadCallCount { get; private set; }

        public Task<ProvisioningRunReadResult?> ReadRunAsync(string customerId, string runId, CancellationToken cancellationToken)
        {
            ReadCallCount++;
            if (_reads.Count == 0)
            {
                return Task.FromResult<ProvisioningRunReadResult?>(null);
            }

            var result = _reads[Math.Min(_index, _reads.Count - 1)];
            _index++;
            return Task.FromResult(result);
        }

        public Task<ProvisioningRunReadResult> CreateRunAsync(ProvisioningRun run, CancellationToken cancellationToken) =>
            throw new NotSupportedException("DispatchCoreAsync never calls CreateRunAsync.");

        public Task<ReplaceRunResult> ReplaceRunAsync(ProvisioningRun run, string ifMatchEtag, CancellationToken cancellationToken) =>
            throw new NotSupportedException("DispatchCoreAsync delegates all Cosmos writes to IHandlerOutcomeApplier -- it never calls ReplaceRunAsync directly.");
    }

    private sealed class RecordingHandlerOutcomeApplier : IHandlerOutcomeApplier
    {
        public List<(ProvisioningRun Run, string ETag, HandlerResult Outcome, string HandlerId)> Calls { get; } = new();
        public Exception? ThrowOnApply { get; init; }
        public HandlerOutcomeApplied Result { get; init; } = new(RunStatus.Running, false, null);

        public Task<HandlerOutcomeApplied> ApplyHandlerOutcomeAsync(
            ProvisioningRun run, string ifMatchEtag, HandlerResult outcome, string handlerId, CancellationToken cancellationToken)
        {
            if (ThrowOnApply is not null)
            {
                throw ThrowOnApply;
            }

            Calls.Add((run, ifMatchEtag, outcome, handlerId));
            return Task.FromResult(Result);
        }
    }
}
