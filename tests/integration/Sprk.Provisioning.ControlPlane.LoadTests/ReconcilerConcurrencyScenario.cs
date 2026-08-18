// -----------------------------------------------------------------------------
// ReconcilerConcurrencyScenario.cs
//
// L2 CONTROL-PLANE load-test scenario 3 (task 062, Wave C5 Batch 4E).
//
// ACCEPTANCE (task 062 POML criterion 3 / spec.md SC #20):
//   "With N=2..3 reconciler instances active, M enqueued runs each produce
//   EXACTLY the expected number of Service Bus messages per phase — no
//   duplicates (verified via receiver dedup metric)."
//
// WHAT IT MEASURES:
//   The state-reconciler's LEVEL-1 IDEMPOTENCY contract under concurrent-
//   instance load:
//     - N reconciler instances (production scale-out ceiling: 3-5) all
//       simultaneously scan the same active-run set.
//     - Each instance's DagAdvancer computes the SAME ready-handler set
//       from the SAME snapshot (pure function of CompletedPhases).
//     - Each instance's enqueuer builds the SAME HandlerEnvelope with the
//       SAME ParametersJson bytes.
//     - ServiceBusHandlerEnqueuer.ComputeMessageId is deterministic:
//       identical envelope -> identical SHA256 -> identical MessageId.
//     - Service Bus queue-level duplicate detection collapses N sends
//       with the same MessageId to ONE retained message.
//
//   The DedupingHandlerEnqueuer in this test suite reproduces the
//   production dedup contract (see LoadTestFixtures.cs). If the reconciler
//   ever regresses to non-deterministic envelope construction (e.g., a
//   timestamp field slipping into the ParametersJson body, or a run-side
//   read-modify-write racing to advance the CompletedPhases mid-scan),
//   the DistinctMessageIds count would exceed the expected value and this
//   scenario fails.
//
// WHY IN-MEMORY:
//   Running against real Service Bus with dedup enabled would validate
//   the SAME contract but require a dedicated test namespace + queue with
//   the correct dedup window configured — infrastructure the LoadTests
//   project deliberately does not depend on. The in-memory
//   DedupingHandlerEnqueuer models the wire-level dedup exactly (identical
//   MessageId -> retained once; different -> retained separately). If the
//   in-memory dedup passes and the production ServiceBusHandlerEnqueuer's
//   ComputeMessageId formula is unchanged, the wire dedup passes too. Any
//   MessageId formula change on the production side MUST update
//   DedupingHandlerEnqueuer.ComputeMessageIdParity in the same PR — this
//   is documented in LoadTestFixtures.cs.
//
// EXECUTION PATH — BackgroundService PUBLIC SURFACE:
//   StateReconcilerService's per-tick internal method (RunTickAsync) is
//   `internal`, and the SUT csproj exposes InternalsVisibleTo to
//   Sprk.Provisioning.ControlPlane.Tests ONLY — not to this LoadTests
//   project. Task 062 POML forbids modifying src/server/** (including a
//   second InternalsVisibleTo entry), so this scenario drives the
//   reconciler through its PUBLIC BackgroundService surface:
//
//     StartAsync -> ExecuteAsync -> PeriodicTimer -> RunTickAsync -> StopAsync
//
//   With PollInterval at its 1-second floor (ReconcilerOptions.Validate
//   rejects sub-second polling) and a wait budget of ~1.5 seconds per
//   test, each reconciler fires at least ONE tick. The scenario asserts
//   on the shared enqueuer's post-run state — the tick count itself is a
//   consequence of the wait duration, not a semantic invariant. The
//   INVARIANT under test is the dedup contract, which holds regardless
//   of the tick count.
//
//   Trade-off: three tests × ~1.5s = ~4.5s wall-clock; the alternative
//   (reflection on internal RunTickAsync) is B8-banned by tests/CLAUDE.md.
//   The BackgroundService route is honest and stays within the public API.
//
// PARAMETRIZATION:
//   Three test cases:
//     - Base: N=3 reconciler instances × 1 run × 1 ready handler (H1).
//     - Scale: N=5 reconciler instances × 10 runs × mixed fan-out —
//       exercises the M-run × N-instance scale scenario.
//     - No-stall: N=3 × 5 runs — verifies the tick loop drains cleanly
//       within a bounded wall-clock budget (no lock contention regression).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Reconciler;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Provisioning.ControlPlane.LoadTests;

public sealed class ReconcilerConcurrencyScenario
{
    private const string TestCustomerId = "recon-conc-customer";

    /// <summary>
    /// Wall-clock budget PER tick group — enough for at least ONE tick at
    /// the 1-sec PollInterval floor + startup latency.
    /// </summary>
    private static readonly TimeSpan TickWaitBudget = TimeSpan.FromMilliseconds(1_500);

    private readonly ITestOutputHelper _output;

    public ReconcilerConcurrencyScenario(ITestOutputHelper output)
    {
        _output = output;
    }

    // -------------------------------------------------------------------------
    // Base case — N=3 reconciler instances against a single-run/single-handler
    // scenario. Verifies: at least 3 dispatches (one per reconciler-tick), 1
    // distinct MessageId (H1 only). Each reconciler may tick more than once
    // during the wait window if system scheduling permits; dedup collapses
    // ALL identical dispatches to one distinct MessageId regardless.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreeReconcilers_OneRun_HandlerH0CompletedOnly_ProducesOneDistinctMessageId()
    {
        // Arrange
        var run = MakeRun("run-single", RunStatus.Running, "H0");
        var scanner = new StaticActiveRunScanner(new[] { run });
        var sharedEnqueuer = new DedupingHandlerEnqueuer();

        // Act — spin up 3 reconcilers concurrently; let them tick for the budget.
        await RunConcurrentReconcilersAsync(count: 3, scanner, sharedEnqueuer, TickWaitBudget);

        // Assert — regardless of how many ticks fire, dedup collapses all
        // identical dispatches into exactly ONE distinct message per (run, ready-handler).
        sharedEnqueuer.TotalCalls.Should().BeGreaterOrEqualTo(3,
            "each of 3 reconciler instances fires at least ONE tick during the wait budget " +
            $"(measured {sharedEnqueuer.TotalCalls}); a value < 3 signals a reconciler failed to start.");
        sharedEnqueuer.DistinctMessageIds.Should().ContainSingle(
            "level-1 idempotency: N identical envelopes from 3 instances collapse to ONE Service Bus message.");
        sharedEnqueuer.DistinctEnvelopes.Should().ContainSingle()
            .Which.HandlerId.Should().Be("H1", "H0 completed unlocks H1 per design.md §4.1.");

        _output.WriteLine("ReconcilerConcurrencyScenario base-case (3 instances × 1 run × 1 ready handler):");
        _output.WriteLine($"  totalCalls          = {sharedEnqueuer.TotalCalls}");
        _output.WriteLine($"  distinctMessageIds  = {sharedEnqueuer.DistinctMessageIds.Count}");
        _output.WriteLine($"  dedupRatio          = {sharedEnqueuer.DistinctMessageIds.Count}/{sharedEnqueuer.TotalCalls}" +
            $" (invariant: distinct == 1 regardless of tick count)");
    }

    // -------------------------------------------------------------------------
    // Scale case — N=5 reconciler instances × M=10 concurrent runs.
    // Verifies: the DISTINCT-messages count matches sum(ready-per-run)
    // regardless of how many ticks fire. Total-calls is a multiple of
    // distinct-count (each tick re-fires the same set).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FiveReconcilers_TenRuns_DistinctMessageIdsEqualsSumReadyPerRun()
    {
        const int reconcilerN = 5;
        const int runsM = 10;

        // Arrange — build 10 runs with a mix of completed-phase states:
        //   5 runs at [H0]        -> ready = [H1]              (1 handler)
        //   3 runs at [H0,H1]     -> ready = [H2a]             (1 handler)
        //   2 runs at [H0,H1,H2a] -> ready = [H2b,H4,H5]       (3 handlers — fan-out)
        var runs = new List<ProvisioningRun>();
        for (var i = 0; i < 5; i++)
        {
            runs.Add(MakeRun($"run-{i}", RunStatus.Running, "H0"));
        }
        for (var i = 5; i < 8; i++)
        {
            runs.Add(MakeRun($"run-{i}", RunStatus.Running, "H0", "H1"));
        }
        for (var i = 8; i < 10; i++)
        {
            runs.Add(MakeRun($"run-{i}", RunStatus.Running, "H0", "H1", "H2a"));
        }
        runs.Should().HaveCount(runsM);

        // Ready-handler count = 5*1 + 3*1 + 2*3 = 14.
        const int expectedDistinct = 14;

        var scanner = new StaticActiveRunScanner(runs);
        var sharedEnqueuer = new DedupingHandlerEnqueuer();

        // Act
        await RunConcurrentReconcilersAsync(reconcilerN, scanner, sharedEnqueuer, TickWaitBudget);

        // Assert
        sharedEnqueuer.TotalCalls.Should().BeGreaterOrEqualTo(reconcilerN * expectedDistinct,
            $"each of {reconcilerN} reconciler instances fires at least one tick dispatching all " +
            $"{expectedDistinct} ready handlers; total >= {reconcilerN * expectedDistinct} " +
            $"(measured {sharedEnqueuer.TotalCalls}).");

        sharedEnqueuer.DistinctMessageIds.Should().HaveCount(expectedDistinct,
            $"level-1 idempotency: MessageId dedup collapses all identical-per-(handler,run) " +
            $"dispatches into {expectedDistinct} distinct Service Bus messages.");

        // Cross-check: distinct-envelope grouping matches the run-plan.
        var distinctByHandler = sharedEnqueuer.DistinctEnvelopes
            .GroupBy(e => e.HandlerId)
            .ToDictionary(g => g.Key, g => g.Count());
        distinctByHandler["H1"].Should().Be(5, "5 runs at [H0] each unlock H1.");
        distinctByHandler["H2a"].Should().Be(3, "3 runs at [H0,H1] each unlock H2a.");
        distinctByHandler["H2b"].Should().Be(2, "2 runs at [H0,H1,H2a] each unlock H2b.");
        distinctByHandler["H4"].Should().Be(2, "2 runs at [H0,H1,H2a] each unlock H4.");
        distinctByHandler["H5"].Should().Be(2, "2 runs at [H0,H1,H2a] each unlock H5.");

        // total-calls should be a multiple of distinct-count (each tick re-fires the full set).
        (sharedEnqueuer.TotalCalls % expectedDistinct).Should().Be(0,
            $"totalCalls should be a whole multiple of distinct ({expectedDistinct}); " +
            $"a non-multiple would indicate a reconciler mid-tick was interrupted.");

        _output.WriteLine("ReconcilerConcurrencyScenario scale-case (5 instances × 10 runs × mixed fan-out):");
        _output.WriteLine($"  reconcilerN         = {reconcilerN}");
        _output.WriteLine($"  runsM               = {runsM}");
        _output.WriteLine($"  totalCalls          = {sharedEnqueuer.TotalCalls}");
        _output.WriteLine($"  distinctMessageIds  = {sharedEnqueuer.DistinctMessageIds.Count}");
        _output.WriteLine($"  distinctByHandler   = " +
            string.Join(", ", distinctByHandler.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")));
        _output.WriteLine($"  totalCalls/distinct = {sharedEnqueuer.TotalCalls / expectedDistinct}" +
            $" (ticks fired × instances)");
    }

    // -------------------------------------------------------------------------
    // No-stall assertion — the tick loop drains cleanly for all instances
    // within the wait budget. Bounded wall-clock so a lock contention or
    // ThreadPool starvation regression surfaces here.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreeReconcilers_FiveRuns_TickLoopCompletesWithinBudget_NoStall()
    {
        var runs = Enumerable.Range(0, 5)
            .Select(i => MakeRun($"run-nostall-{i}", RunStatus.Running, "H0"))
            .ToList();
        var scanner = new StaticActiveRunScanner(runs);
        var sharedEnqueuer = new DedupingHandlerEnqueuer();

        // Total-budget headroom for startup + at-least-one-tick + graceful stop.
        var overall = System.Diagnostics.Stopwatch.StartNew();
        await RunConcurrentReconcilersAsync(count: 3, scanner, sharedEnqueuer, TickWaitBudget);
        overall.Stop();

        overall.ElapsedMilliseconds.Should().BeLessThan(5_000,
            $"the reconciler tick loop MUST complete within 5 sec (measured {overall.ElapsedMilliseconds} ms); " +
            "a stall would signal lock contention or ThreadPool starvation under concurrent instances.");

        sharedEnqueuer.DistinctMessageIds.Should().HaveCount(5,
            "5 distinct runs -> 5 distinct MessageIds (level-1 idempotency).");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Spins <paramref name="count"/> reconciler instances via
    /// <see cref="BackgroundService.StartAsync"/>, lets them tick for
    /// <paramref name="tickBudget"/>, then stops them concurrently. All
    /// instances share the <paramref name="sharedEnqueuer"/> (production
    /// parity — one Service Bus queue serves all L2 App Service instances)
    /// but each has an independent IServiceScopeFactory (production parity —
    /// each L2 App Service instance has its own DI graph).
    /// </summary>
    private static async Task RunConcurrentReconcilersAsync(
        int count,
        IActiveRunScanner scanner,
        IHandlerEnqueuer sharedEnqueuer,
        TimeSpan tickBudget)
    {
        var reconcilers = BuildReconcilers(count, scanner, sharedEnqueuer);
        using var runCts = new CancellationTokenSource();

        // Start all instances concurrently.
        await Task.WhenAll(reconcilers.Select(r => r.StartAsync(runCts.Token)));

        // Wait for the tick budget to elapse.
        await Task.Delay(tickBudget);

        // Stop all instances gracefully.
        runCts.Cancel();
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await Task.WhenAll(reconcilers.Select(r => r.StopAsync(stopCts.Token)));
    }

    private static IReadOnlyList<StateReconcilerService> BuildReconcilers(
        int count, IActiveRunScanner scanner, IHandlerEnqueuer sharedEnqueuer)
    {
        var reconcilers = new List<StateReconcilerService>(count);
        for (var i = 0; i < count; i++)
        {
            var services = new ServiceCollection();
            services.AddSingleton(scanner);
            services.AddSingleton(sharedEnqueuer);
            services.AddSingleton<IDagAdvancer, DagAdvancer>();
            var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

            reconcilers.Add(new StateReconcilerService(
                scopeFactory,
                // PollInterval at the 1-sec floor so a single tick fires
                // within the TickWaitBudget above.
                Options.Create(new ReconcilerOptions
                {
                    PollInterval = TimeSpan.FromSeconds(1),
                    Enabled = true,
                }),
                TimeProvider.System,
                NullLogger<StateReconcilerService>.Instance));
        }
        return reconcilers;
    }

    private static ProvisioningRun MakeRun(string runId, RunStatus status, params string[] completedPhases)
    {
        var run = new ProvisioningRun
        {
            RunId = runId,
            CustomerId = TestCustomerId,
            EnvironmentId = "env-recon",
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
                IdempotencyKey = $"{phase.ToLowerInvariant()}-{TestCustomerId}-{runId}",
                JobId = runId,
            });
        }
        return run;
    }

    /// <summary>
    /// Static-snapshot scanner — returns the pre-built list on every call.
    /// Parity with StateReconcilerServiceTests.StubActiveRunScanner.
    /// </summary>
    private sealed class StaticActiveRunScanner : IActiveRunScanner
    {
        private readonly IReadOnlyList<ProvisioningRun> _runs;

        public StaticActiveRunScanner(IEnumerable<ProvisioningRun> runs)
        {
            _runs = runs.ToArray();
        }

        public Task<IReadOnlyList<ProvisioningRun>> QueryActiveRunsAsync(CancellationToken ct)
            => Task.FromResult(_runs);
    }
}
