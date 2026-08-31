// -----------------------------------------------------------------------------
// DagAdvancerTests.cs
//
// L2 CONTROL-PLANE tests for the state-reconciler's DAG advancement logic
// (task 058, Wave C5).
//
// COVERAGE — one test per meaningful DAG shape from design.md §4.1:
//
//   1. Empty completedPhases         -> nothing ready (H0 dispatched by endpoint)
//   2. H0 completed                  -> H1 ready
//   3. H1 completed                  -> H2a ready
//   4. H2a completed                 -> {H2b, H4, H5} ready (3-way fan-out)
//   5. H2a + H4 completed            -> {H2b, H3, H5} ready (H3 unlocks after H4)
//   6. H2a + H4 + H3 completed       -> {H2b, H5, H8, H9} ready (H8+H9 fan-out from H3)
//   7. H2a + H5 completed            -> {H2b, H4, H6} ready (H6 unlocks after H5)
//   8. up through H10 completed      -> H11 ready
//   9. H11 completed                 -> {H12a, H12b} ready (parallel — H12b does NOT need H12a)
//  10. H12a + H12b + H2a completed   -> H12c ready (3-way join per handler code)
//  11. H12c completed                -> H14 ready
//  12. H14 completed                 -> H13 ready
//  13. Terminal status Completed/Failed/Cancelled/Quarantined -> empty
//  14. H0.5 is NEVER dispatched by the reconciler (entry point)
//  15. Handler already in completedPhases is NEVER re-dispatched
//
// This unit test suite is the AUTHORITATIVE regression net for the design.md
// §4.1 DAG diagram — the DagAdvancer's HandlerDependencies dictionary and this
// test file MUST be updated together.
//
// PATH (per docs/standards/TEST-ARCHITECTURE.md §3 KEEP categories):
//   L2 project-scoped test — mirrors existing L2 Handlers/*Tests.cs pattern.
//   The 7 KEEP path convention applies to tests/** (repo-level) — the L2
//   project has its own Sprk.Provisioning.ControlPlane.Tests project which
//   is where every L2 handler test lives.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Reconciler;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Reconciler;

/// <summary>
/// Unit tests for <see cref="DagAdvancer.ComputeReadyHandlers"/>. Pure-function
/// coverage — no mocks, no I/O, no time. Each test constructs a
/// <see cref="ProvisioningRun"/> snapshot and asserts the expected ready-set.
/// </summary>
public sealed class DagAdvancerTests
{
    private const string TestCustomerId = "test-customer";
    private const string TestRunId = "00000000-0000-0000-0000-000000000042";

    private readonly DagAdvancer _sut = new();

    // -----------------------------------------------------------------------
    // Entry-point exclusion tests
    // -----------------------------------------------------------------------

    [Fact]
    public void ComputeReadyHandlers_WithEmptyCompletedPhases_ReturnsEmpty()
    {
        var run = MakeRun(RunStatus.Running, completedPhases: Array.Empty<string>());

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().BeEmpty(
            "H0 and H0.5 are entry-point handlers dispatched by the endpoint layer / BFF, " +
            "NEVER by the reconciler. An empty completedPhases set (fresh run) must not " +
            "produce a ready set.");
    }

    [Fact]
    public void ComputeReadyHandlers_WithH05CompletedButNoH0_DoesNotDispatchH0()
    {
        // Model 2 self-service branch: H0.5 completes; the reconciler must NOT
        // dispatch H0 (H0.5 handler itself is responsible for chaining to H0).
        var run = MakeRun(RunStatus.Running, "H0.5");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().NotContain("H0",
            "H0 is an entry-point handler (dispatched by endpoint / H0.5 chain), " +
            "not by the reconciler.");
    }

    // -----------------------------------------------------------------------
    // DAG-chain tests
    // -----------------------------------------------------------------------

    [Fact]
    public void ComputeReadyHandlers_AfterH0_H1IsReady()
    {
        var run = MakeRun(RunStatus.Running, "H0");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().ContainSingle().Which.Should().Be("H1");
    }

    [Fact]
    public void ComputeReadyHandlers_AfterH1_H2aIsReady()
    {
        var run = MakeRun(RunStatus.Running, "H0", "H1");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().ContainSingle().Which.Should().Be("H2a");
    }

    [Fact]
    public void ComputeReadyHandlers_AfterH2a_UnlocksH2bH4H5_ThreeWayFanOut()
    {
        var run = MakeRun(RunStatus.Running, "H0", "H1", "H2a");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().BeEquivalentTo(new[] { "H2b", "H4", "H5" },
            "design.md §4.1 DAG: H2a → {H2b, H4, H5} 3-way parallel post-Bicep.");
    }

    [Fact]
    public void ComputeReadyHandlers_AfterH4_UnlocksH3()
    {
        // H2a + H4 completed; H2b + H5 still ready; H3 now also ready (needs H4).
        var run = MakeRun(RunStatus.Running, "H0", "H1", "H2a", "H4");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().BeEquivalentTo(new[] { "H2b", "H3", "H5" },
            "design.md §4.1 DAG: H4 → H3 (needs KV for secrets storage).");
    }

    [Fact]
    public void ComputeReadyHandlers_AfterH3_UnlocksH8H9_FanOut()
    {
        var run = MakeRun(RunStatus.Running, "H0", "H1", "H2a", "H4", "H3");

        var ready = _sut.ComputeReadyHandlers(run);

        // Note: H2b + H5 still ready (they were ready after H2a; still not dispatched).
        ready.Should().BeEquivalentTo(new[] { "H2b", "H5", "H8", "H9" },
            "design.md §4.1 DAG: H3 → {H8, H9} parallel fan-out.");
    }

    [Fact]
    public void ComputeReadyHandlers_AfterH5_UnlocksH6()
    {
        var run = MakeRun(RunStatus.Running, "H0", "H1", "H2a", "H5");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().BeEquivalentTo(new[] { "H2b", "H4", "H6" },
            "design.md §4.1 DAG: H5 → H6 (solution import).");
    }

    [Fact]
    public void ComputeReadyHandlers_AfterFullChainThroughH10_H11IsReady()
    {
        var run = MakeRun(RunStatus.Running,
            "H0", "H1", "H2a", "H2b", "H4", "H3", "H8", "H9",
            "H5", "H6", "H7", "H10");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().ContainSingle().Which.Should().Be("H11",
            "H10 → H11 per DAG.");
    }

    [Fact]
    public void ComputeReadyHandlers_AfterH11_UnlocksH12aAndH12b_Parallel()
    {
        var run = MakeRun(RunStatus.Running,
            "H0", "H1", "H2a", "H2b", "H4", "H3", "H8", "H9",
            "H5", "H6", "H7", "H10", "H11");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().BeEquivalentTo(new[] { "H12a", "H12b" },
            "design.md §4.1 DAG v3.2: H12a and H12b are parallel — H12b does NOT need H12a.");
    }

    [Fact]
    public void ComputeReadyHandlers_H12aOnly_DoesNotUnlockH12c()
    {
        // H12c needs H12a + H12b + H2a — H12b missing.
        var run = MakeRun(RunStatus.Running,
            "H0", "H1", "H2a", "H2b", "H4", "H3", "H8", "H9",
            "H5", "H6", "H7", "H10", "H11", "H12a");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().NotContain("H12c",
            "H12c is a 3-way DAG join — H12b still missing.");
        ready.Should().Contain("H12b",
            "H12b is still ready (parallel with H12a).");
    }

    [Fact]
    public void ComputeReadyHandlers_H12aAndH12bAndH2a_UnlocksH12c_ThreeWayJoin()
    {
        var run = MakeRun(RunStatus.Running,
            "H0", "H1", "H2a", "H2b", "H4", "H3", "H8", "H9",
            "H5", "H6", "H7", "H10", "H11", "H12a", "H12b");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().ContainSingle().Which.Should().Be("H12c",
            "design.md §4.1 DAG: H12c needs {H12a, H12b, H2a} — 3-way join.");
    }

    [Fact]
    public void ComputeReadyHandlers_AfterH12c_UnlocksH14()
    {
        var run = MakeRun(RunStatus.Running,
            "H0", "H1", "H2a", "H2b", "H4", "H3", "H8", "H9",
            "H5", "H6", "H7", "H10", "H11", "H12a", "H12b", "H12c");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().ContainSingle().Which.Should().Be("H14",
            "design.md §4.1 DAG: H12c → H14 (post-deploy integration wiring parent).");
    }

    [Fact]
    public void ComputeReadyHandlers_AfterH14_UnlocksH13_FinalGate()
    {
        var run = MakeRun(RunStatus.Running,
            "H0", "H1", "H2a", "H2b", "H4", "H3", "H8", "H9",
            "H5", "H6", "H7", "H10", "H11", "H12a", "H12b", "H12c", "H14");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().ContainSingle().Which.Should().Be("H13",
            "design.md §4.1 DAG: H14 → H13 (final acceptance gate).");
    }

    [Fact]
    public void ComputeReadyHandlers_AllHandlersComplete_ReturnsEmpty()
    {
        var run = MakeRun(RunStatus.Running,
            "H0", "H1", "H2a", "H2b", "H4", "H3", "H8", "H9",
            "H5", "H6", "H7", "H10", "H11", "H12a", "H12b", "H12c", "H14", "H13");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().BeEmpty(
            "with every DAG-participating handler complete, there is nothing to dispatch. " +
            "The terminal-status transition (Completed) is owned by H13 itself, not the reconciler.");
    }

    // -----------------------------------------------------------------------
    // Terminal-status guard
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.Failed)]
    [InlineData(RunStatus.Cancelled)]
    [InlineData(RunStatus.Quarantined)]
    public void ComputeReadyHandlers_TerminalStatus_ReturnsEmpty(RunStatus terminalStatus)
    {
        // Even if the completedPhases would leave something ready, a terminal-
        // status run must not advance. Defense-in-depth: the scanner filter
        // (status ∈ {Running, WaitingOnGate}) already prevents these from
        // reaching the advancer, but a direct call must still return empty.
        var run = MakeRun(terminalStatus, "H0", "H1");  // H2a would be ready if Running

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().BeEmpty(
            "terminal status {0} must not participate in DAG advancement.", terminalStatus);
    }

    [Fact]
    public void ComputeReadyHandlers_WaitingOnGateStatus_StillAdvances()
    {
        // WaitingOnGate is NOT terminal — the reconciler still evaluates whether
        // any handler downstream of the gated one is unblocked by other completed
        // work. The gated handler itself is not in completedPhases so downstream
        // handlers that depend on it stay pending.
        var run = MakeRun(RunStatus.WaitingOnGate, "H0", "H1", "H2a");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().BeEquivalentTo(new[] { "H2b", "H4", "H5" },
            "WaitingOnGate is a soft-pause; unrelated downstream handlers still advance.");
    }

    // -----------------------------------------------------------------------
    // Idempotency (no re-dispatch of already-completed handlers)
    // -----------------------------------------------------------------------

    [Fact]
    public void ComputeReadyHandlers_AlreadyCompletedHandler_IsNotReDispatched()
    {
        // Given H1 completed + H2a already ALSO completed, the ready set must
        // not contain H2a again.
        var run = MakeRun(RunStatus.Running, "H0", "H1", "H2a");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().NotContain("H2a",
            "an already-completed handler must never appear in the ready set.");
    }

    // -----------------------------------------------------------------------
    // Argument validation
    // -----------------------------------------------------------------------

    [Fact]
    public void ComputeReadyHandlers_NullRun_ThrowsArgumentNullException()
    {
        var act = () => _sut.ComputeReadyHandlers(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a minimal <see cref="ProvisioningRun"/> whose CompletedPhases list
    /// contains the given handler ids. Timestamps + idempotency keys are
    /// placeholders — the DAG advancer only reads Phase strings.
    /// </summary>
    private static ProvisioningRun MakeRun(RunStatus status, params string[] completedPhases)
    {
        var run = new ProvisioningRun
        {
            RunId = TestRunId,
            CustomerId = TestCustomerId,
            EnvironmentId = "env-42",
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
}
