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
//   4. H2a completed                 -> {H2b, H4, H5} ready (3-way fan-out post-Bicep)
//   5. H2a + H4 completed            -> {H2b, H3, H5} ready (H3 unlocks after H4; H4b waits
//                                      for H3 too — T245a: it reads H3's BffAppRegId)
//   6. H2a + H4 + H3 completed       -> {H2b, H5} ready (H4b and H8 also wait for H5 — H8 binds
//                                      its container to the environment's root business unit,
//                                      task 165; H9 still blocked because H4b not landed — EXEC-01)
//   7. H2a + H4 + H4b + H3           -> {H2b, H5, H9} ready (H9 finally unlocks; H8 after H5)
//   8. H6 needs H5 AND H3 (T245a — reads BffAppRegId); H7 needs H6 AND H8 (T245a — reads
//      SpeContainerId)
//   9. up through H10 completed      -> H11 ready
//  10. H11 completed                 -> {H12a, H12b} ready (parallel — H12b does NOT need H12a)
//  11. H12a + H12b + H2a completed   -> H12c ready (3-way join per handler code)
//  12. H12c completed                -> H14 ready
//  13. H14 completed                 -> H13 ready
//  14. Terminal status Completed/Failed/Cancelled/Quarantined -> empty
//  15. H0.5 is NEVER dispatched by the reconciler (entry point)
//  16. Handler already in completedPhases is NEVER re-dispatched
//  17. HANDLER-01/EXEC-01 verification shape tests (H4b / H9-gate)
//
// This unit test suite is the AUTHORITATIVE regression net for the design.md
// §4.1 DAG diagram — the DagAdvancer's HandlerDependencies dictionary and this
// test file MUST be updated together.
//
// PATH (per docs/standards/TEST-ARCHITECTURE.md §3 KEEP categories):
//   L2 project-scoped test — mirrors existing L2 Handlers/*Tests.cs pattern.
//   The ADR-038 KEEP path convention (eight paths) applies to tests/** (repo-level) — the L2
//   project has its own Sprk.Provisioning.ControlPlane.Tests project which
//   is where every L2 handler test lives.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers;
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
    private const string TestCustomerId = "testcust";
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
            "design.md §4.1 DAG: H2a → {H2b, H4, H5} 3-way parallel post-Bicep " +
            "(T226 retired task 200's H4-shared — no shared vault in the dedicated model).");
    }

    [Fact]
    public void ComputeReadyHandlers_AfterH4_UnlocksH3_H4bWaitsForH3()
    {
        // H2a + H4 completed; H2b + H5 still ready; H3 (needs KV) now ready. H4b also needs the
        // populated customer KV, AND H3's BffAppRegId for AzureAd__ClientId (T245a) — before T245a
        // it was ready here and would read a value H3 had not written yet.
        var run = MakeRun(RunStatus.Running, "H0", "H1", "H2a", "H4");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().BeEquivalentTo(new[] { "H2b", "H3", "H5" },
            "design.md §4.1 DAG: H4 → H3 (needs KV for secrets storage); H4b needs H4 and H3.");
    }

    [Fact]
    public void ComputeReadyHandlers_AfterH4H3H5AndH8_UnlocksH4b()
    {
        var withoutH5 = MakeRun(RunStatus.Running, "H0", "H1", "H2a", "H4", "H3", "H8");
        var withoutH8 = MakeRun(RunStatus.Running, "H0", "H1", "H2a", "H4", "H3", "H5");
        var withAll = MakeRun(RunStatus.Running, "H0", "H1", "H2a", "H4", "H3", "H5", "H8");

        _sut.ComputeReadyHandlers(withoutH5).Should().NotContain("H4b",
            "T245b: Dataverse__ServiceUrl / __EnvironmentUrl are H5's DataverseEnvUrl");
        _sut.ComputeReadyHandlers(withoutH8).Should().NotContain("H4b",
            "T227c: EmailProcessing__DefaultContainerId / Communication__ArchiveContainerId are H8's SpeContainerId");
        _sut.ComputeReadyHandlers(withAll).Should().Contain("H4b",
            "H4b's producers (H4 for the populated KV, H3 for BffAppRegId, H5 for the Dataverse URL, H8 for the container) have all run.");
    }

    [Fact]
    public void ComputeReadyHandlers_WithH3ButWithoutH4b_DoesNotIncludeH9()
    {
        // EXEC-01 verification shape test: H3 completed but H4b NOT completed → H9 must NOT be ready.
        // This is the EXEC-01 gate — the F20 IOptions-chain halt the r1 project exists to eliminate.
        var run = MakeRun(RunStatus.Running, "H0", "H1", "H2a", "H4", "H3");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().NotContain("H9",
            "EXEC-01: HandlerDependencies[H9] = { H3, H4b } — H9 must remain blocked until " +
            "H4b lands the batched app-settings; deploying BFF against an incomplete app-settings " +
            "surface is precisely the F20 IOptions fail-fast chain r1 was written to prevent.");
        ready.Should().NotContain("H8",
            "H8 also needs H5 (unified-access-control-r2 task 165, owner round 35 item 1): it binds the container it " +
            "creates to the new environment's root business unit, which exists only once H5 has run.");
    }

    [Fact]
    public void ComputeReadyHandlers_AfterH3AndH5_UnlocksH8()
    {
        var run = MakeRun(RunStatus.Running, "H0", "H1", "H2a", "H4", "H3", "H5");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().Contain("H8", "H8 needs H3 (ordering) AND H5 (the environment whose root unit owns the container)");
    }

    [Fact]
    public void ComputeReadyHandlers_AfterH3AndH4b_UnlocksH9()
    {
        // EXEC-01 companion — the "green path": H3 + H4b + H4 all done → H9 finally ready.
        var run = MakeRun(RunStatus.Running,
            "H0", "H1", "H2a", "H4", "H4b", "H3");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().Contain("H9",
            "EXEC-01 green path: with H3 + H4b both landed, H9 (BFF deploy) is finally " +
            "unblocked; BFF boots against complete KV refs + batched app-settings.");
        ready.Should().NotContain("H8",
            "H8 is unchanged by the H4b addition — it is gated on H3 and H5 (task 165), and H5 has not run here.");
    }

    [Fact]
    public void ComputeReadyHandlers_AfterH3AndH5_H8ReadyButH9BlockedOnH4b()
    {
        // The EXEC-01 discriminator: after H3 (and H5 — task 165), H8 is ready but H9 is NOT — H8 is
        // Graph-only, H9 needs the batched app-settings that H4b lands. This is the whole point of the DAG
        // change: separate SPE container creation (H8, Graph-only) from BFF deploy (H9, needs KV/app-settings).
        var run = MakeRun(RunStatus.Running, "H0", "H1", "H2a", "H4", "H3", "H5");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().Contain("H8", "H8 depends on H3 and H5, both complete.");
        ready.Should().NotContain("H9", "H9 also requires H4b.");
    }

    [Fact]
    public void ComputeReadyHandlers_AfterH5WithoutH3_H6Waits()
    {
        // T245a: H6 reads InterStepState.BffAppRegId (H3 output). Before, H5 alone unlocked H6,
        // which could then run before H3 had written the value.
        var run = MakeRun(RunStatus.Running, "H0", "H1", "H2a", "H5");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().BeEquivalentTo(new[] { "H2b", "H4" },
            "H6 needs H5 (Dataverse environment) and H3 (BFF app registration).");
    }

    [Fact]
    public void ComputeReadyHandlers_AfterH5AndH3_UnlocksH10_AndH6WaitsForIt()
    {
        // T228: H6 signs in to the customer environment as the BFF app registration, an application user there only once
        // H10 has registered it — and H10 needs only H3 + H5 (H2a upstream of both).
        var beforeH10 = _sut.ComputeReadyHandlers(MakeRun(RunStatus.Running, "H0", "H1", "H2a", "H4", "H3", "H5"));
        beforeH10.Should().Contain("H10").And.NotContain("H6");

        var afterH10 = _sut.ComputeReadyHandlers(MakeRun(RunStatus.Running, "H0", "H1", "H2a", "H4", "H3", "H5", "H10"));
        afterH10.Should().Contain("H6").And.NotContain("H11", "H11's users get the solution's roles — it waits for H7 too");
    }

    [Fact]
    public void ComputeReadyHandlers_AfterH6WithoutH8_H7Waits_ThenUnlocksWithH8()
    {
        // T245a: H7 writes the SPE container-id env var from InterStepState.SpeContainerId (H8 output).
        // T245b: + H9 — sprk_BffApiBaseUrl is the stamp's own BFF URL (InterStepState.BffApiUrl).
        var withoutH8 = MakeRun(RunStatus.Running, "H0", "H1", "H2a", "H4", "H3", "H5", "H4b", "H9", "H6");
        var withoutH9 = MakeRun(RunStatus.Running, "H0", "H1", "H2a", "H4", "H3", "H5", "H4b", "H6", "H8");
        var withBoth = MakeRun(RunStatus.Running, "H0", "H1", "H2a", "H4", "H3", "H5", "H4b", "H6", "H8", "H9");

        _sut.ComputeReadyHandlers(withoutH8).Should().NotContain("H7", "H8 has not produced SpeContainerId yet");
        _sut.ComputeReadyHandlers(withoutH9).Should().NotContain("H7", "H9 has not produced the stamp's BFF URL yet");
        _sut.ComputeReadyHandlers(withBoth).Should().Contain("H7");
    }

    [Fact]
    public void ComputeReadyHandlers_H14WaitsForH9_EvenAfterH12c()
    {
        // T245b: H14's webhook receivers are the stamp's BFF (InterStepState.BffApiUrl, H9 output).
        var withoutH9 = MakeRun(RunStatus.Running,
            "H0", "H1", "H2a", "H2b", "H4", "H3", "H5", "H4b", "H6", "H8", "H10", "H11", "H12a", "H12b", "H12c");

        _sut.ComputeReadyHandlers(withoutH9).Should().NotContain("H14");
        _sut.ComputeReadyHandlers(MakeRun(RunStatus.Running,
            "H0", "H1", "H2a", "H2b", "H4", "H3", "H5", "H4b", "H6", "H8", "H9", "H7", "H10", "H11", "H12a", "H12b", "H12c"))
            .Should().Contain("H14");
    }

    [Fact]
    public void ComputeReadyHandlers_AfterH6_H7WaitsForH8_WhichHandsOffOnlyABoundContainer()
    {
        // H8 dispatched but not complete — e.g. waiting out the 24h SPE replication window with its root container
        // created and recorded but NOT yet bound (unified-access-control-r2 task 165, owner round 41 item 1).
        var run = MakeRun(RunStatus.WaitingOnGate, "H0", "H1", "H2a", "H2b", "H4", "H4b", "H3", "H5", "H9", "H6");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().NotContain("H7",
            "H7 writes H8's root container into sprk_SharePointEmbeddedContainerId — it must wait until H8 has bound it");
        ready.Should().Contain("H8");
    }

    [Fact]
    public void ComputeReadyHandlers_AfterH6AndH8_UnlocksH7()
    {
        var run = MakeRun(RunStatus.Running, "H0", "H1", "H2a", "H2b", "H4", "H4b", "H3", "H5", "H10", "H9", "H6", "H8");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().BeEquivalentTo(new[] { "H7" });
    }

    [Fact]
    public void ComputeReadyHandlers_AfterFullChainThroughH10_H11IsReady()
    {
        var run = MakeRun(RunStatus.Running,
            "H0", "H1", "H2a", "H2b", "H4", "H4b", "H3", "H8", "H9",
            "H5", "H6", "H7", "H10");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().ContainSingle().Which.Should().Be("H11",
            "H10 → H11 per DAG.");
    }

    [Fact]
    public void ComputeReadyHandlers_AfterH11_UnlocksH12aAndH12b_Parallel()
    {
        var run = MakeRun(RunStatus.Running,
            "H0", "H1", "H2a", "H2b", "H4", "H4b", "H3", "H8", "H9",
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
            "H0", "H1", "H2a", "H2b", "H4", "H4b", "H3", "H8", "H9",
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
            "H0", "H1", "H2a", "H2b", "H4", "H4b", "H3", "H8", "H9",
            "H5", "H6", "H7", "H10", "H11", "H12a", "H12b");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().ContainSingle().Which.Should().Be("H12c",
            "design.md §4.1 DAG: H12c needs {H12a, H12b, H2a} — 3-way join.");
    }

    [Fact]
    public void ComputeReadyHandlers_AfterH12c_UnlocksH14()
    {
        var run = MakeRun(RunStatus.Running,
            "H0", "H1", "H2a", "H2b", "H4", "H4b", "H3", "H8", "H9",
            "H5", "H6", "H7", "H10", "H11", "H12a", "H12b", "H12c");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().ContainSingle().Which.Should().Be("H14",
            "design.md §4.1 DAG: H12c → H14 (post-deploy integration wiring parent).");
    }

    [Fact]
    public void ComputeReadyHandlers_AfterH14_UnlocksH13_FinalGate()
    {
        var run = MakeRun(RunStatus.Running,
            "H0", "H1", "H2a", "H2b", "H4", "H4b", "H3", "H8", "H9",
            "H5", "H6", "H7", "H10", "H11", "H12a", "H12b", "H12c", "H14");

        var ready = _sut.ComputeReadyHandlers(run);

        ready.Should().ContainSingle().Which.Should().Be("H13",
            "design.md §4.1 DAG: H14 → H13 (final acceptance gate).");
    }

    [Fact]
    public void ComputeReadyHandlers_AllHandlersComplete_ReturnsEmpty()
    {
        var run = MakeRun(RunStatus.Running,
            "H0", "H1", "H2a", "H2b", "H4", "H4b", "H3", "H8", "H9",
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
    // HANDLER-12 parity — HandlerIds.Dispatchable ↔ HandlerDependencies keys
    //
    // Exactly the regression net HANDLER-01 was born from: HandlerIds.H4Shared
    // (since retired, T226) + HandlerIds.H4b were in Dispatchable + had keyed-DI registrations + had
    // handler classes on disk, but no entry in DagAdvancer.HandlerDependencies
    // meant the reconciler NEVER dispatched them. Any future Dispatchable
    // addition without a paired DAG entry will now fail at build time here.
    // -----------------------------------------------------------------------

    [Fact]
    public void HandlerDependencies_ContainsEveryDispatchableIdExceptEntryPoints_Parity()
    {
        // Every reconciler-dispatchable handler (i.e. Dispatchable minus entry
        // points that transport-layer triggers dispatch) MUST have a
        // HandlerDependencies entry — otherwise ComputeReadyHandlers can never
        // return it and the reconciler silently drops it forever.
        var expectedKeys = HandlerIds.Dispatchable
            .Except(DagAdvancer.EntryPointHandlers)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var actualKeys = DagAdvancer.HandlerDependencies.Keys
            .Except(DagAdvancer.EntryPointHandlers)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        actualKeys.Should().BeEquivalentTo(expectedKeys,
            "HANDLER-12: every id in HandlerIds.Dispatchable (except entry points H0/H0.5) " +
            "MUST appear as a key in DagAdvancer.HandlerDependencies. A future Dispatchable " +
            "addition without a paired DAG entry silently drops from reconciler dispatch — " +
            "exactly the F19/F20-automation-inert failure mode that HANDLER-01 fixed.");
    }

    [Fact]
    public void HandlerDependencies_DoesNotContainUnknownIds_ReverseParity()
    {
        // Reverse direction: every HandlerDependencies key (except entry points,
        // which are documented-for-completeness) MUST resolve to a real
        // Dispatchable id. A stray key (typo, deleted handler) would cause the
        // reconciler to try to dispatch a non-registered handler → NoHandler
        // dead-letter at task 102's dispatcher.
        var strayKeys = DagAdvancer.HandlerDependencies.Keys
            .Except(DagAdvancer.EntryPointHandlers)
            .Except(HandlerIds.Dispatchable)
            .ToArray();

        strayKeys.Should().BeEmpty(
            "HANDLER-12 reverse parity: any DAG key not in HandlerIds.Dispatchable is a " +
            "typo or orphan (stray keys: {0}). Either add the id to Dispatchable + register " +
            "a keyed handler, or remove the DAG key.",
            string.Join(", ", strayKeys));
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
            TenancyModel = "Model2",
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
