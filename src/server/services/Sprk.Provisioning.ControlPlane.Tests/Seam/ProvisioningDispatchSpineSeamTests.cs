// -----------------------------------------------------------------------------
// ProvisioningDispatchSpineSeamTests.cs
//
// L2 CONTROL-PLANE dispatch-spine integration seam test (task 118, Phase C''
// Wave G-1 -- the FINAL task of the wave). Fulfils the ADR-038
// tests/integration/seam/** vertical-slice-seam DoD category (added 2026-07-09
// by E-40) for THIS project's dispatch spine, per this project's CLAUDE.md
// ADR-038 pointer table entry.
//
// PROVES THE FULL LOOP (DS-2 §6.2's exact design):
//   enqueue-shape message -> DispatchCoreAsync -> real fake-scripted keyed
//   handler writes CompletedPhases -> the REAL HandlerOutcomeApplier (task
//   104) -> Cosmos transition landed correctly (task 106's serializer fix
//   makes this readable) -> one real StateReconcilerService.RunTickAsync ->
//   the NEXT handler's envelope reaches a recording enqueuer.
//
// ONLY the Service Bus wire is faked (IHandlerEnqueuer -> RecordingHandlerEnqueuer).
// Cosmos, DispatchCoreAsync, HandlerOutcomeApplier, StateReconcilerService,
// DagAdvancer, CosmosActiveRunScanner, IFailureClassifier, and IDispatchIdempotencyService
// are ALL the real production types, resolved from the REAL Worker composition
// root (WebApplicationFactory<WorkerHost::Program> -- the exact same technique
// task 103's HandlerRegistrationCompletenessTests.cs already established for
// proving DI-graph SHAPE; this test extends it to prove RUNTIME BEHAVIOR).
//
// TWO DOCUMENTED DEVIATIONS FROM THE POML'S LITERAL STEP WORDING (directional
// steps mode -- root CLAUDE.md §8.5 permits adapting the sequence when a step
// is wrong for what the codebase actually needs; both are noted here per that
// rule):
//
//   (1) "Cosmos emulator/test container" -> a real (opt-in) dev Cosmos account
//       via the SAME COSMOS_L2_SMOKE_* env-guard convention task 106's
//       CosmosActiveRunScannerSeamTests.cs already established, not a NEW
//       Testcontainers-based Cosmos emulator harness. Rationale (CLAUDE.md
//       §11 no-duplicate-component rule): this project has ZERO existing
//       Cosmos-emulator/test-container infrastructure; inventing one for a
//       single test file would be new-component scope creep when an
//       established, working, ALREADY-REVIEWED pattern (env-guarded live
//       dev Cosmos, skip-by-default so CI never depends on it) already
//       proves the identical serializer-boundary property this test also
//       needs. Per the task's own <escalation> trigger, if a genuine
//       emulator-only feature gap is ever hit, the documented fallback IS
//       this live-dev-Cosmos path -- so this is that fallback, taken
//       proactively rather than reactively.
//
//   (2) The scripted test handler is keyed-registered under the PRODUCTION
//       "H1" HandlerId (HandlerIds.H1), overriding the real
//       H1SubscriptionReadinessHandler's keyed registration (last
//       registration wins for .NET keyed DI resolution), rather than an
//       entirely synthetic id like "TestCanary". Rationale: DagAdvancer's
//       HandlerDependencies map (Reconciler/DagAdvancer.cs) is a REAL,
//       PRODUCTION, string-keyed dictionary anchored to the design.md §4.1
//       catalog -- a synthetic id has NO entry in that map, so
//       ComputeReadyHandlers could never surface a "next handler" for it and
//       step 4's core assertion ("assert the recording enqueuer received the
//       NEXT handler's envelope with the correct HandlerId per DagAdvancer's
//       ready-set computation") would be structurally unverifiable. Anchoring
//       to the real "H1" id keeps DagAdvancer 100% real/production (H2a is
//       the correct, provably-computed next handler) while the scripted
//       handler body itself is deliberately minimal (no live Azure calls,
//       unlike the real H1SubscriptionReadinessHandler) so this test never
//       becomes coupled to H1's own implementation evolving. The scripted
//       handler ALSO deliberately does NOT replicate H1's Wave-C4
//       "enqueue-H2a-directly" temporary bridge (see H1SubscriptionReadinessHandler.cs
//       file header "DOWNSTREAM ENQUEUE" section) -- that bridge is scaffolding
//       being phased out per design.md §4.2b ("the dispatcher does NOT advance
//       the DAG... the reconciler's existing 5s tick owns advancement"); baking
//       the temporary bridge into a permanent regression-net test would encode
//       obsolete behavior. This test proves the STEADY-STATE contract: the
//       reconciler alone computes + enqueues the next handler.
//
// FAILURE MODES THIS TEST CATCHES (report requirement -- confirms regression
// coverage across all 4 Wave-G1 dependency tasks):
//   - Task 102 (dispatcher) regresses: DispatchCoreAsync's step 2/3/4/6/7/8
//     flow breaks -> the "Assert dispatch" section's DispatchDecision.Complete
//     assertion or the CompletedPhases-landed assertion fails.
//   - Task 103 (keyed DI) regresses: GetKeyedService<IProvisioningHandler>("H1")
//     returns null / wrong type -> DispatchCoreAsync would DeadLetter(NoHandler)
//     instead of returning Complete -> "Assert dispatch" fails.
//   - Task 104 (IHandlerOutcomeApplier extraction) regresses: if the extracted
//     applier's Success-path no-op behavior breaks (e.g. starts double-writing
//     or throws), DispatchCoreAsync's step 6 apply call fails/throws ->
//     "Assert dispatch" fails.
//   - Task 106 (serializer fix) regresses: if the Newtonsoft StringEnumConverter
//     attribute is ever removed from RunStatus, the run becomes invisible to
//     CosmosActiveRunScanner's string-comparison query -> the "real Cosmos
//     transition is readable" assertion (IActiveRunScanner.QueryActiveRunsAsync
//     containing our run) fails -- THIS is the exact DS-5 finding ("the seam
//     test is the one that would have caught this") this task exists to close.
//   - Any of the above additionally break the "Assert DAG advance" section,
//     since RunTickAsync depends on the SAME Cosmos transition being readable
//     AND on the keyed-DI-resolved handler having actually run.
// -----------------------------------------------------------------------------

// extern alias required -- see Sprk.Provisioning.ControlPlane.Tests.csproj
// comment on the aliased Worker ProjectReference: both .Api and .Worker's
// top-level-statement Program class land in the GLOBAL namespace, so an
// unaliased Worker reference would make every WebApplicationFactory<Program>
// usage project-wide ambiguous (CS0433). Same pattern as
// Dispatch/HandlerRegistrationCompletenessTests.cs (task 103) and
// Dispatch/ProvisioningHandlerDispatcherInvariantTests.cs (task 102).
extern alias WorkerHost;

using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Dispatch;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Modules;
using Sprk.Provisioning.ControlPlane.Reconciler;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;
using DispatcherType = WorkerHost::Sprk.Provisioning.ControlPlane.Worker.Dispatch.ProvisioningHandlerDispatcher;
using WorkerProgram = WorkerHost::Program;

namespace Sprk.Provisioning.ControlPlane.Tests.Seam;

/// <summary>
/// Env-guarded (<c>COSMOS_L2_SMOKE_ENDPOINT</c> -- same opt-in convention as
/// <see cref="Reconciler.CosmosActiveRunScannerSeamTests"/>) dispatch-spine
/// seam test. Skipped by default so the CI unit run never depends on a live
/// Cosmos endpoint. See file header for the full loop this proves + the two
/// documented directional-mode deviations from the POML's literal wording.
/// </summary>
[Trait("Category", "Smoke")]
[Trait("RequiresLiveResource", "Cosmos")]
public sealed class ProvisioningDispatchSpineSeamTests : IAsyncLifetime
{
    private const string EndpointEnvVar = "COSMOS_L2_SMOKE_ENDPOINT";
    private const string DatabaseEnvVar = "COSMOS_L2_SMOKE_DATABASE";
    private const string ContainerEnvVar = "COSMOS_L2_SMOKE_CONTAINER";
    private const string MiClientIdEnvVar = "COSMOS_L2_SMOKE_MI_CLIENTID";

    private bool _skip;
    private string _databaseName = "spaarke-provisioning";
    private string _containerName = "runs";
    private SeamTestFactory? _factory;
    private readonly List<(string CustomerId, string RunId)> _createdRunIds = new();

    public Task InitializeAsync()
    {
        var endpoint = Environment.GetEnvironmentVariable(EndpointEnvVar);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            _skip = true;
            return Task.CompletedTask;
        }

        _databaseName = Environment.GetEnvironmentVariable(DatabaseEnvVar) ?? _databaseName;
        _containerName = Environment.GetEnvironmentVariable(ContainerEnvVar) ?? _containerName;
        var miClientId = Environment.GetEnvironmentVariable(MiClientIdEnvVar);

        _factory = new SeamTestFactory(endpoint, _databaseName, _containerName, miClientId);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_skip || _factory is null)
        {
            return;
        }

        try
        {
            var cosmosClient = _factory.Services.GetRequiredService<CosmosClient>();
            var container = cosmosClient.GetContainer(_databaseName, _containerName);
            foreach (var (customerId, runId) in _createdRunIds)
            {
                try
                {
                    await container.DeleteItemAsync<ProvisioningRun>(
                        id: runId,
                        partitionKey: new PartitionKey(customerId)).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort cleanup -- a leaked test doc is a maintenance
                    // nuisance, not a test-failure signal (matches
                    // CosmosActiveRunScannerSeamTests convention).
                }
            }
        }
        finally
        {
            _factory.Dispose();
        }
    }

    [Fact]
    public async Task FullLoop_DispatchAppliesOutcome_CosmosTransitionReadable_ReconcilerAdvancesToNextHandler()
    {
        if (_skip || _factory is null)
        {
            return; // env-guarded skip
        }

        var customerId = $"seam-dispatch-{Guid.NewGuid():N}";
        var runId = Guid.NewGuid().ToString("D");

        // ARRANGE -- seed a run already past H0 (the entry-point handler,
        // never reconciler-dispatched per DagAdvancer.EntryPointHandlers),
        // status Running, so H1 is the sole ready-to-dispatch handler.
        using (var seedScope = _factory.Services.CreateScope())
        {
            var repository = seedScope.ServiceProvider.GetRequiredService<IProvisioningRunRepository>();
            var run = new ProvisioningRun
            {
                RunId = runId,
                CustomerId = customerId,
                EnvironmentId = Guid.NewGuid().ToString("D"),
                TenancyModel = "Model2Dedicated",
                Profile = "spaarke-hosted-model2",
                Status = RunStatus.Running,
            };
            run.CompletedPhases.Add(new CompletedPhase
            {
                Phase = HandlerIds.H0,
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
                IdempotencyKey = $"preflight-{customerId}-seam",
                JobId = runId,
            });
            await repository.CreateRunAsync(run, CancellationToken.None);
        }
        _createdRunIds.Add((customerId, runId));

        // ACT (1) -- dispatch. Real ProvisioningHandlerDispatcher.DispatchCoreAsync
        // (task 102), real keyed-DI resolution (task 103) of the "H1" id ->
        // the canary handler registered by SeamTestFactory, real
        // IHandlerOutcomeApplier (task 104) applying a Success outcome (a
        // documented no-op per HandlerOutcomeApplier's Success-path contract
        // -- the handler itself owns the CompletedPhases write on Success).
        var envelope = new HandlerEnvelope
        {
            HandlerId = HandlerIds.H1,
            RunId = runId,
            CustomerId = customerId,
            ParametersJson = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow,
        };

        DispatchDecision decision;
        using (var dispatchScope = _factory.Services.CreateScope())
        {
            var dispatcher = BuildDispatcher(_factory);
            decision = await dispatcher
                .DispatchCoreAsync(envelope, deliveryCount: 1, dispatchScope.ServiceProvider, CancellationToken.None);
        }

        // ASSERT (1) -- the message was fully processed (both Success AND
        // applied-Failure settle as Complete per DS-2 §2.5 step 8; a
        // DeadLetter/Abandon here means task 102, 103, or 104 regressed).
        decision.Should().BeOfType<DispatchDecision.Complete>(
            "task 102's dispatcher must fully process a valid H1 dispatch through the real keyed " +
            "handler (task 103) + the real HandlerOutcomeApplier (task 104) without dead-lettering " +
            "or abandoning.");

        // ASSERT (2) -- THE task-106 regression proof, reused verbatim from
        // CosmosActiveRunScannerSeamTests: query via the REAL
        // CosmosActiveRunScanner (the exact production cross-partition scan
        // StateReconcilerService's next tick will use). Pre-task-106, RunStatus
        // wrote as a Newtonsoft integer and this string-comparison query
        // would return ZERO rows -- our run would be permanently invisible.
        using (var scanScope = _factory.Services.CreateScope())
        {
            var scanner = scanScope.ServiceProvider.GetRequiredService<IActiveRunScanner>();
            var activeRuns = await scanner.QueryActiveRunsAsync(CancellationToken.None);
            activeRuns.Should().Contain(
                r => r.RunId == runId && r.CustomerId == customerId,
                "the real Cosmos transition landed via the handler's own write (Success-path -- " +
                "HandlerOutcomeApplier does not double-write) must be readable back through the SAME " +
                "production scan the reconciler + crash-recovery use; a serializer regression (task " +
                "106's defect class) would make this run invisible to the string-comparison query.");
        }

        using (var readScope = _factory.Services.CreateScope())
        {
            var repository = readScope.ServiceProvider.GetRequiredService<IProvisioningRunRepository>();
            var fresh = await repository.ReadRunAsync(customerId, runId, CancellationToken.None);
            fresh.Should().NotBeNull();
            fresh!.Run.CompletedPhases.Should().Contain(
                cp => cp.Phase == HandlerIds.H1,
                "the scripted H1 canary handler owns its own CompletedPhases append on Success, " +
                "mirroring every real handler's Success-path write shape.");
        }

        // ACT (2) -- one real StateReconcilerService tick (task 058, wiring
        // task 104 extracted). Real IDagAdvancer (production DagAdvancer --
        // H2a is the ONLY handler whose HandlerDependencies are now fully
        // satisfied: H2a requires [H1]; H2b/H4/H5 require [H2a] which is not
        // yet complete). Real IActiveRunScanner. Fake IHandlerEnqueuer
        // (RecordingHandlerEnqueuer) -- the ONE faked boundary (Service Bus
        // wire), per DS-2 §6.2.
        var reconciler = BuildReconciler(_factory);
        await reconciler.RunTickAsync(CancellationToken.None);

        // ASSERT (3) -- the reconciler alone (no handler-owned temporary
        // bridge -- see file header deviation (2)) computed + enqueued
        // exactly the DagAdvancer-correct next handler for OUR run.
        var enqueuer = (RecordingHandlerEnqueuer)_factory.Services.GetRequiredService<IHandlerEnqueuer>();
        var ourEnvelopes = enqueuer.Envelopes.Where(e => e.RunId == runId).ToList();
        ourEnvelopes.Should().ContainSingle(
            "the reconciler tick must enqueue exactly one handler for our run -- DagAdvancer's " +
            "ready-set computation over CompletedPhases=[H0,H1] yields exactly {H2a}.");
        ourEnvelopes[0].HandlerId.Should().Be(
            HandlerIds.H2a,
            "H2a is the sole handler whose HandlerDependencies ([H1]) are satisfied by " +
            "CompletedPhases=[H0,H1] per the REAL production DagAdvancer.HandlerDependencies map " +
            "(Reconciler/DagAdvancer.cs) -- if this drifts, either the DAG map changed (expected, " +
            "update this test) or the reconciler wiring (tasks 058/102/104) regressed (not expected).");
        ourEnvelopes[0].CustomerId.Should().Be(customerId);
    }

    [Fact]
    public async Task ReconcilerTick_TerminalStatusRun_ProducesNoEnqueue()
    {
        if (_skip || _factory is null)
        {
            return; // env-guarded skip
        }

        var customerId = $"seam-terminal-{Guid.NewGuid():N}";
        var runId = Guid.NewGuid().ToString("D");

        // ARRANGE -- a run already in a terminal status. CosmosActiveRunScanner's
        // filter (WHERE c.status IN ('Running','WaitingOnGate')) excludes it
        // from the cross-partition scan entirely; DagAdvancer.ComputeReadyHandlers
        // ALSO defensively returns an empty set for any terminal status (defense
        // in depth -- see DagAdvancer.cs). Either layer alone would satisfy this
        // negative case; both being real here means both are exercised.
        using (var seedScope = _factory.Services.CreateScope())
        {
            var repository = seedScope.ServiceProvider.GetRequiredService<IProvisioningRunRepository>();
            var run = new ProvisioningRun
            {
                RunId = runId,
                CustomerId = customerId,
                EnvironmentId = Guid.NewGuid().ToString("D"),
                TenancyModel = "Model2Dedicated",
                Profile = "spaarke-hosted-model2",
                Status = RunStatus.Completed,
            };
            await repository.CreateRunAsync(run, CancellationToken.None);
        }
        _createdRunIds.Add((customerId, runId));

        // ACT
        var reconciler = BuildReconciler(_factory);
        await reconciler.RunTickAsync(CancellationToken.None);

        // ASSERT -- negative-case control: NO enqueue for the terminal run.
        var enqueuer = (RecordingHandlerEnqueuer)_factory.Services.GetRequiredService<IHandlerEnqueuer>();
        enqueuer.Envelopes.Should().NotContain(
            e => e.RunId == runId,
            "a run in a terminal status (Completed here; Cancelled/Quarantined share the same " +
            "exclusion) must never be re-dispatched by a reconciler tick.");
    }

    /// <summary>
    /// Constructs a real <see cref="DispatcherType"/> instance directly
    /// (rather than resolving it via DI as an <see cref="IHostedService"/> --
    /// SeamTestFactory strips those) so <c>DispatchCoreAsync</c> -- the DS-2
    /// §2.5 pure decision function -- can be invoked deterministically without
    /// spinning a live <see cref="ServiceBusSessionProcessor"/>. The
    /// <see cref="ServiceBusClient"/> passed here is a syntactically-valid
    /// but NEVER-DIALED placeholder: <c>DispatchCoreAsync</c> does not touch
    /// it (only <c>ExecuteAsync</c>, which this test never calls, does) --
    /// exact same construction technique
    /// <see cref="Dispatch.ProvisioningHandlerDispatcherInvariantTests"/>
    /// established for testing <c>BuildSessionProcessorOptions</c>.
    /// </summary>
    private static DispatcherType BuildDispatcher(SeamTestFactory factory)
    {
        var fakeServiceBusClient = new ServiceBusClient(
            "Endpoint=sb://seam-test-unreachable.servicebus.windows.net/;" +
            "SharedAccessKeyName=fake;SharedAccessKey=ZmFrZWtleQ==");

        return new DispatcherType(
            fakeServiceBusClient,
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new DispatcherOptions()),
            Options.Create(new ServiceBusModuleOptions { QueueName = "seam-test-queue" }),
            TimeProvider.System,
            NullLogger<DispatcherType>.Instance);
    }

    /// <summary>
    /// Constructs a real <see cref="StateReconcilerService"/> directly (same
    /// rationale as <see cref="BuildDispatcher"/> -- SeamTestFactory strips
    /// <see cref="IHostedService"/> registrations so the production
    /// <c>PeriodicTimer</c> loop never runs unattended against the shared dev
    /// Cosmos account; the test drives exactly one tick via
    /// <c>RunTickAsync</c>, which internally resolves its own per-tick DI
    /// scope -- see StateReconcilerService.cs).
    /// </summary>
    private static StateReconcilerService BuildReconciler(SeamTestFactory factory)
        => new(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ReconcilerOptions()),
            TimeProvider.System,
            NullLogger<StateReconcilerService>.Instance);

    /// <summary>
    /// Minimal real <see cref="IProvisioningHandler"/> -- writes its own
    /// CompletedPhases entry (mirroring every production handler's
    /// Success-path write shape; see file header for why this does NOT
    /// replicate H1's real Wave-C4 enqueue-H2a-directly bridge) and returns
    /// <see cref="HandlerResult.Success"/>. Registered under the production
    /// "H1" HandlerId -- see file header deviation (2) for the full
    /// rationale.
    /// </summary>
    private sealed class CanaryH1Handler : IProvisioningHandler
    {
        private readonly IProvisioningRunRepository _repository;

        public CanaryH1Handler(IProvisioningRunRepository repository) => _repository = repository;

        public string HandlerId => HandlerIds.H1;

        public async Task<HandlerResult> HandleAsync(HandlerEnvelope envelope, CancellationToken cancellationToken)
        {
            var read = await _repository
                .ReadRunAsync(envelope.CustomerId, envelope.RunId, cancellationToken)
                .ConfigureAwait(false);
            if (read is null)
            {
                return new HandlerResult.Failure(
                    FailureClass.Resumable, "seam-canary-run-not-found", "Canary H1: run doc absent.");
            }

            var idempotencyKey = $"seam-canary-h1-{envelope.CustomerId}";
            var run = read.Run;
            run.CompletedPhases.Add(new CompletedPhase
            {
                Phase = HandlerIds.H1,
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
                IdempotencyKey = idempotencyKey,
                JobId = envelope.RunId,
            });

            await _repository.ReplaceRunAsync(run, read.ETag, cancellationToken).ConfigureAwait(false);
            return new HandlerResult.Success(idempotencyKey);
        }
    }

    /// <summary>
    /// Records every enqueued envelope -- the ONE faked boundary (Service Bus
    /// wire) per DS-2 §6.2. Singleton so state is observable across the
    /// dispatch scope + the reconciler's own internal scope within a single
    /// test.
    /// </summary>
    private sealed class RecordingHandlerEnqueuer : IHandlerEnqueuer
    {
        private readonly List<HandlerEnvelope> _envelopes = new();
        private readonly object _gate = new();

        public IReadOnlyList<HandlerEnvelope> Envelopes
        {
            get { lock (_gate) { return _envelopes.ToArray(); } }
        }

        public Task EnqueueAsync(HandlerEnvelope envelope, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _envelopes.Add(envelope);
            }
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// <see cref="WebApplicationFactory{TEntryPoint}"/> over the REAL
    /// <see cref="Sprk.Provisioning.ControlPlane.Worker"/> composition root --
    /// same technique as
    /// <see cref="Dispatch.WorkerTestFactory"/> (task 103), extended with a
    /// REAL Cosmos endpoint (this test needs actual reads/writes, not just
    /// DI-graph shape) and two DI overrides applied AFTER Program.cs's own
    /// registrations run (last-registration-wins for both keyed and
    /// non-keyed .NET DI resolution): the "H1" keyed <see cref="IProvisioningHandler"/>
    /// -> <see cref="CanaryH1Handler"/>, and <see cref="IHandlerEnqueuer"/> ->
    /// <see cref="RecordingHandlerEnqueuer"/> (the one faked boundary).
    /// </summary>
    private sealed class SeamTestFactory : WebApplicationFactory<WorkerProgram>
    {
        private readonly string _cosmosEndpoint;
        private readonly string _databaseName;
        private readonly string _containerName;
        private readonly string? _miClientId;

        public SeamTestFactory(string cosmosEndpoint, string databaseName, string containerName, string? miClientId)
        {
            _cosmosEndpoint = cosmosEndpoint;
            _databaseName = databaseName;
            _containerName = containerName;
            _miClientId = miClientId;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Cosmos:AccountEndpoint", _cosmosEndpoint);
            builder.UseSetting("Cosmos:DatabaseName", _databaseName);
            builder.UseSetting("Cosmos:ContainerName", _containerName);
            if (!string.IsNullOrWhiteSpace(_miClientId))
            {
                builder.UseSetting("ManagedIdentity:ClientId", _miClientId);
            }

            // Service Bus is the ONE faked boundary (DS-2 §6.2) -- the wire
            // FQN below is syntactically valid but NEVER dialed: the
            // ServiceBusClient singleton AddServiceBusModule registers is
            // never resolved (IHandlerEnqueuer is overridden below to the
            // recording fake, and this test never constructs the dispatcher's
            // hosted-service form -- see BuildDispatcher). Same placeholder
            // value Dispatch/HandlerRegistrationCompletenessTests.cs's
            // WorkerTestFactory already uses.
            builder.UseSetting("ServiceBus:FullyQualifiedNamespace", "l2-test.servicebus.windows.net");

            // Testing environment -- TelemetryModule's AzureMonitorGuard skips
            // exporter wiring silently on non-Development/Production envs.
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services =>
            {
                // Strip every IHostedService (StateReconcilerService,
                // CrashRecoveryStartupService, ProvisioningHandlerDispatcher)
                // so accessing .Services never starts unattended background
                // work against the shared dev Cosmos account -- this test
                // drives the dispatch step + exactly one reconciler tick
                // deterministically instead. Identical technique to
                // Dispatch/HandlerRegistrationCompletenessTests.cs's
                // WorkerTestFactory.
                for (var i = services.Count - 1; i >= 0; i--)
                {
                    if (services[i].ServiceType == typeof(IHostedService))
                    {
                        services.RemoveAt(i);
                    }
                }

                // The ONE faked boundary (Service Bus wire) -- registered
                // AFTER Program.cs's AddServiceBusModule() call, so it wins
                // resolution (last-registration-wins). Singleton so the SAME
                // recorder is observable across every scope this test opens.
                services.AddSingleton<IHandlerEnqueuer, RecordingHandlerEnqueuer>();

                // Scripted canary handler under the production "H1" key --
                // see file header deviation (2). Registered AFTER
                // HandlerDispatchRegistrationModule's real
                // AddKeyedScoped<IProvisioningHandler>(HandlerIds.H1, ...)
                // line, so it wins keyed resolution.
                services.AddKeyedScoped<IProvisioningHandler>(
                    HandlerIds.H1,
                    (sp, _) => new CanaryH1Handler(sp.GetRequiredService<IProvisioningRunRepository>()));
            });
        }
    }
}
