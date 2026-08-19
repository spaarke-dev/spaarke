// -----------------------------------------------------------------------------
// H13AcceptanceOptions.cs
//
// Bound options for the H13 E2E acceptance-gate handler + its 6 collaborator
// seams (E2E validation runner + trap verifier + invariant verifier + naming
// conformance + cost envelope + registry status updater). Loaded from the
// "E2EAcceptance" configuration section — runtime-configurable so the linux-x64
// App Service publish layout can be honored without recompiling.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-18 (H13
//     acceptance) + SC #5 (extended validate script) + SC #6 (traps re-verified)
//     + SC #17 (naming-conformance exit 0) + §15 #14 (cost envelope).
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H13 row +
//     §4B (T1–T6 trap catalog) + §4C (Quarantined semantics) + §4D (I1–I5
//     tenant-isolation invariants).
//
// PATTERN PARITY:
//   Mirrors Handlers/BffDeploy/BffDeployOptions.cs (script paths + timeouts +
//   size thresholds) and Handlers/IntegrationWiring/IntegrationWiringOptions.cs
//   (pwsh executable + configuration-knob defaults with sensible values).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Bound options for <see cref="H13E2EAcceptanceGateHandler"/> collaborators.
/// Configuration key: <c>E2EAcceptance</c>.
/// </summary>
public sealed class H13AcceptanceOptions
{
    /// <summary>Path to the pwsh executable. Defaults to <c>pwsh</c> (resolved via PATH).</summary>
    public string PwshExecutable { get; set; } = "pwsh";

    /// <summary>Path to the <c>az</c> CLI executable. Defaults to <c>az</c> (resolved via PATH).</summary>
    public string AzCliExecutable { get; set; } = "az";

    /// <summary>
    /// Absolute path to <c>scripts/Validate-DeployedEnvironment.ps1</c> (Phase
    /// B extended). Defaults to <c>scripts/Validate-DeployedEnvironment.ps1</c>
    /// relative to <see cref="AppContext.BaseDirectory"/>; production
    /// deployments should override via app-setting so the linux-x64 publish
    /// layout is honored.
    /// </summary>
    public string ValidateDeployedEnvironmentScriptPath { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "scripts", "Validate-DeployedEnvironment.ps1");

    /// <summary>
    /// Absolute path to <c>scripts/naming-conformance-check.ps1</c> (r3 task
    /// 063 — the read-only KV/resource naming gate). Defaults to
    /// <c>scripts/naming-conformance-check.ps1</c> relative to
    /// <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    public string NamingConformanceScriptPath { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "scripts", "naming-conformance-check.ps1");

    /// <summary>
    /// Maximum time to wait for the <c>Validate-DeployedEnvironment.ps1</c>
    /// invocation. Defaults to 15 minutes — the extended Phase-B checks include
    /// a BFF /healthz probe with retry, a sample analysis round-trip, a sample
    /// document upload+index, plus the wrapped naming-conformance step.
    /// </summary>
    public TimeSpan ValidateScriptTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Maximum time to wait for a single <c>naming-conformance-check.ps1</c>
    /// invocation (H13 also invokes this INDEPENDENTLY of the wrapped call
    /// inside Validate-DeployedEnvironment.ps1 to satisfy SC #17 as its own
    /// pass/fail boundary). Defaults to 2 minutes.
    /// </summary>
    public TimeSpan NamingConformanceTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Maximum time to wait for a single trap verifier probe (T1–T6). Defaults
    /// to 3 minutes — each verifier is a bounded Graph/ARM/Dataverse REST or
    /// az CLI call.
    /// </summary>
    public TimeSpan TrapVerifierTimeout { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Maximum time to wait for a single invariant verifier probe (I1–I5).
    /// Defaults to 2 minutes.
    /// </summary>
    public TimeSpan InvariantVerifierTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Maximum time to wait for a single Azure Cost Management query. Defaults
    /// to 3 minutes — Cost Management APIs are often the slowest ARM surface.
    /// </summary>
    public TimeSpan CostQueryTimeout { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Expected monthly cost envelope for a Model 2 (dedicated) EMPTY customer
    /// stamp in whole USD. Per spec.md §15 #14 target: ≤ $400/mo.
    /// </summary>
    public decimal Model2EmptyEnvelopeUsd { get; set; } = 400m;

    /// <summary>
    /// Expected monthly cost envelope for a Model 1 (shared trial/SMB) MARGINAL
    /// per-tenant footprint in whole USD. Per spec.md §15 #14 target: ≤ $430/mo.
    /// </summary>
    public decimal Model1MarginalEnvelopeUsd { get; set; } = 430m;

    /// <summary>
    /// Expected monthly cost envelope for the Model 1 SHARED baseline floor in
    /// whole USD. Per spec.md §15 #14 target: ≤ $400/mo.
    /// </summary>
    public decimal Model1SharedFloorEnvelopeUsd { get; set; } = 400m;

    /// <summary>
    /// Cost drift fraction above which H13 emits an advisory warning per
    /// spec.md §15 #14. Defaults to 0.20 (20%). Per POML mandatory pre-work
    /// note: cost drift is currently AMBISUOUS — implemented as advisory-warn
    /// by default, NOT a fail-run classification, per the deviation note.
    /// </summary>
    public decimal CostDriftAdvisoryThreshold { get; set; } = 0.20m;

    /// <summary>
    /// When true, cost drift above <see cref="CostDriftAdvisoryThreshold"/>
    /// FAILS H13 QuarantineRequired instead of emitting an advisory warning.
    /// Defaults to <c>false</c> per deviation note (owner-configurable per
    /// spec.md Unresolved Questions).
    /// </summary>
    public bool CostDriftFailsRun { get; set; } = false;

    /// <summary>
    /// Deployment slot name (production URL vs staging) for the sample E2E
    /// checks — defaults to <c>production</c> so the extended validate script
    /// targets the live post-H9-swap slot.
    /// </summary>
    public string TargetSlotName { get; set; } = "production";

    /// <summary>
    /// When true, H13 short-circuits + returns Success WITHOUT invoking any
    /// collaborator seam if the run's registry Setup Status is already
    /// <c>Ready</c> AND the level-3 idempotency key matches. Defaults to
    /// <c>true</c> per project constraint acceptance criterion 8.
    /// </summary>
    public bool HonorRegistryStatusReadyShortCircuit { get; set; } = true;
}
