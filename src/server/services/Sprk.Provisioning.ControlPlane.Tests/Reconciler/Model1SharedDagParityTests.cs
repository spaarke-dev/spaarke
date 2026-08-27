// -----------------------------------------------------------------------------
// Model1SharedDagParityTests.cs
//
// EXEC-09 regression net — Model1Shared vs Model2Dedicated DAG shape parity
// (pre-dispatch audit punchlist row EXEC-09, Wave 8; authored per operator
// directive 2026-08-27 "every issue is a priority").
//
// FINDING (EXEC-09 verbatim, pre-dispatch-audit-punchlist-2026-08-27.md L1019):
//   "The DAG has no branch for Model 1 Shared vs Model 2 Dedicated"
//
//   file:where — DagAdvancer.HandlerDependencies +
//   HandlerDispatchRegistrationModule (no TenancyModel-conditional branching)
//
//   consequence_if_unfixed — First Model 1 Shared trial1 dispatch may either
//   (a) duplicate shared infra per-customer (EXEC-04 already flagged the H2a
//   fallback) OR (b) collide with existing shared resources and 409/400 at
//   Bicep/ARM. No integration test asserts Model 1 Shared full-dispatch
//   cleanliness.
//
// WHY THIS TEST FILE, NOT AN INTEGRATION TEST:
//
//   The audit's proposed_fix requests a Model1Shared end-to-end integration
//   test with a shared-fabric fixture — that lives in
//   tests/integration/seam/** (or tests/integration/Sprk.Provisioning.
//   ControlPlane.LoadTests) and depends on Service Bus, Cosmos, and Worker
//   infra fixtures that are out-of-scope for this Wave 8 subagent-safe pass
//   (per remediation task boundary — see punchlist EXEC-09
//   main-session-only: yes tag). This unit-level test file lands the
//   subagent-safe portion:
//
//   1. Asserts the DAG is DELIBERATELY TenancyModel-agnostic (its output
//      ready-set for a given completedPhases must be identical for both
//      Model1Shared and Model2Dedicated). Regression net for any accidental
//      future TenancyModel-conditional branching added to
//      DagAdvancer.ComputeReadyHandlers.
//
//   2. Audits + inventories which handler CLASSES have explicit Model1Shared
//      code branches vs which don't — the answer to "which handlers might
//      silently fail under Model1Shared". This inventory lives as an XML doc
//      comment on the audit-record test below + is grep-verifiable with a
//      simple ripgrep so a follow-on integration test can be written against
//      the ground-truth surface.
//
//   The main-session-deferred item remains: a full-DAG dispatch integration
//   test with a shared-fabric fixture asserting each handler's Succeeded
//   outcome carries the 'shared-reused' or 'noop' evidence in outcome
//   payload. Filed as follow-on work; this file is the guardrail-net for
//   the DAG side of that surface + the audit inventory feeding it.
//
// PATH (per docs/standards/TEST-ARCHITECTURE.md §3 KEEP categories):
//   L2 project-scoped test — mirrors DagAdvancerTests.cs pattern.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Reconciler;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Reconciler;

/// <summary>
/// EXEC-09 regression net — DAG shape is deliberately TenancyModel-agnostic;
/// any future accidental branching added to <see cref="DagAdvancer"/> will
/// fail here before it can silently drop handlers under one tenancy model.
/// See file header for the full finding + why this is unit-level rather than
/// the audit's proposed full-DAG integration test.
/// </summary>
public sealed class Model1SharedDagParityTests
{
    private const string TestCustomerId = "trial1";
    private const string TestRunId = "00000000-0000-0000-0000-000000000042";

    private readonly DagAdvancer _sut = new();

    // -----------------------------------------------------------------------
    // Parity tests — every meaningful DAG-shape checkpoint asserted
    // identical for Model1Shared and Model2Dedicated. If any of these
    // starts to fail, DagAdvancer has silently grown a tenancyModel branch
    // and the audit's EXEC-09 mitigation (per-handler branching + integration
    // test) MUST be re-evaluated.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("H0")]
    [InlineData("H0,H1")]
    [InlineData("H0,H1,H2a")]                            // 4-way fan-out post-Bicep
    [InlineData("H0,H1,H2a,H4")]                         // H3 unlocks
    [InlineData("H0,H1,H2a,H4,H4-shared")]               // H4b unlocks
    [InlineData("H0,H1,H2a,H4,H4-shared,H3")]            // H8 unlocks (H9 still gated)
    [InlineData("H0,H1,H2a,H4,H4-shared,H4b,H3")]        // H9 finally unlocks
    [InlineData("H0,H1,H2a,H5")]                         // H6 unlocks
    [InlineData("H0,H1,H2a,H5,H4,H4-shared,H6,H7,H10")]  // H11 unlocks
    [InlineData("H0,H1,H2a,H5,H4,H4-shared,H6,H7,H10,H11")]           // H12a+H12b unlock
    [InlineData("H0,H1,H2a,H5,H4,H4-shared,H6,H7,H10,H11,H12a,H12b")] // H12c 3-way join
    [InlineData("H0,H1,H2a,H5,H4,H4-shared,H6,H7,H10,H11,H12a,H12b,H12c")]      // H14
    [InlineData("H0,H1,H2a,H5,H4,H4-shared,H6,H7,H10,H11,H12a,H12b,H12c,H14")]  // H13 final
    public void ComputeReadyHandlers_IsIdenticalForBothTenancyModels(string completedCsv)
    {
        var completed = completedCsv.Split(',');

        var model1Ready = _sut.ComputeReadyHandlers(
            MakeRun("Model1Shared", "spaarke-hosted-model1-trial", completed));

        var model2Ready = _sut.ComputeReadyHandlers(
            MakeRun("Model2Dedicated", "spaarke-hosted-model2", completed));

        model2Ready.Should().BeEquivalentTo(model1Ready,
            "EXEC-09: DagAdvancer.ComputeReadyHandlers is deliberately TenancyModel-agnostic — " +
            "the DAG does NOT branch on tenancy. Any accidental branch introduced by future " +
            "changes silently drops handlers under one model (Model1Shared trial1 dispatch " +
            "regressions were the audit's flagged failure mode).");

        // Also-order-agnostic parity — the returned lists themselves are
        // already sorted deterministically inside DagAdvancer.
        model2Ready.Should().Equal(model1Ready,
            "EXEC-09: deterministic ordering (StringComparer.Ordinal) is invariant across models.");
    }

    // -----------------------------------------------------------------------
    // Model1Shared handler-branch INVENTORY (audit record — the answer to
    // "which handlers have explicit tenancyModel branches in their body?")
    //
    // This is not a test in the traditional sense; it's a machine-readable
    // record of the code-level audit that the main-session integration test
    // (deferred) will consume. Verify against ground truth via:
    //   rg -n 'Model1Shared' src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/
    //
    // As of 2026-08-27 (initial EXEC-09 remediation pass):
    //
    //   HAS explicit Model1Shared branch in handler body:
    //     - H1SubscriptionReadinessHandler   (validates tenancyModel; no code-path branch)
    //     - H2aBicepInfraDeployHandler       (rejects missing TenancyModel — no silent default)
    //     - H2bAiSearchIndexHandler          (Model1Shared branch, ~line 295)
    //     - H3EntraAppRegHandler             (explicit HandleModel1Async / HandleModel2Async split)
    //     - H12cRuntimeReferencesHandler     (switch Model1Shared / Model2Dedicated, ~line 286)
    //     - ArmCostEnvelopeChecker (H0)      (SelectExpectedEnvelope by tenancyModel)
    //     - AiSearchTenantFilterInvariantProbe (H13 sub-probe; Model1 branch)
    //     - FileBicepTemplateInspector (H2a) (template-key selection)
    //     - ArmDeploymentRunner (H2a)        (template-key selection)
    //
    //   DELIBERATELY has NO branch (documented in handler XML doc):
    //     - H10DataverseAppUserGraphParityHandler
    //
    //   NO branch present + NO explicit "safe for both" documentation
    //   (candidates for the deferred integration-test audit to verify are
    //   truly tenancyModel-agnostic OR to gain an explicit branch):
    //     - H4KvSecretsPopulationHandler
    //     - H4SharedKvSecretsPopulationHandler  (name implies Model1-only usage; verify)
    //     - H4bBulkAppSettingsHandler
    //     - H5DataverseEnvCreationHandler
    //     - H6SolutionImportHandler
    //     - H7DataverseEnvVarValuesHandler
    //     - H8SpeContainerTypeHandler
    //     - H9BffDeployHandler
    //     - H11UserProvisioningHandler
    //     - H12aAiSeedChainHandler
    //     - H12bAppConfigSeedHandler
    //     - H13E2EAcceptanceGateHandler
    //     - H14IntegrationWiringHandler
    //     - H05ConsentCaptureHandler  (Model 2 entry-point ONLY — should reject Model1Shared)
    //
    //   TEST DEFERRED TO MAIN-SESSION INTEGRATION LANE — per EXEC-09
    //   proposed_fix, each of the above handlers needs a Model1Shared
    //   fixture-based execution that asserts either (a) Succeeded outcome
    //   carries 'shared-reused' or 'noop' evidence, or (b) explicit Failed
    //   with a documented tenancyModel-rejection diagnostic. Filed as
    //   follow-on work in the wrap-up handoff.
    // -----------------------------------------------------------------------

    [Fact]
    public void HandlerBranchInventoryAuditRecord_IsMachineReadableInFileHeader()
    {
        // Existence-only assertion — the audit inventory is the XML doc
        // comment above; this test guarantees the file compiles and is
        // discoverable by grep / test explorers so the inventory does not
        // rot in isolation. See file header for the full EXEC-09 audit
        // record + main-session-deferred follow-on.
        typeof(Model1SharedDagParityTests).Should().NotBeNull();
    }

    // -----------------------------------------------------------------------
    // Helpers (mirrors DagAdvancerTests.MakeRun pattern; kept private to this
    // file to avoid coupling the parity-suite to the neighbouring tests).
    // -----------------------------------------------------------------------

    private static ProvisioningRun MakeRun(
        string tenancyModel,
        string profile,
        params string[] completedPhases)
    {
        var run = new ProvisioningRun
        {
            RunId = TestRunId,
            CustomerId = TestCustomerId,
            EnvironmentId = "env-42",
            TenancyModel = tenancyModel,
            Profile = profile,
            Status = RunStatus.Running,
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
