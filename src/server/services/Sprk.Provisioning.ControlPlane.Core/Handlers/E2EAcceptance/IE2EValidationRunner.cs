// -----------------------------------------------------------------------------
// IE2EValidationRunner.cs
//
// L2 abstraction over the extended Validate-DeployedEnvironment.ps1 (Phase B)
// wrapper — the FIRST substrate H13 depends on per spec.md SC #5. Production
// impl (<see cref="ValidateDeployedEnvironmentScriptRunner"/>) shells out to
// pwsh; unit tests inject fakes that return canned outcomes.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist by design (production script runner + test fakes
//   per unit test). The script IS the source-of-truth for the extended
//   sample-check catalog (BFF /healthz, sample analysis, sample doc upload+
//   index, workspace-layout render, wizard field-map); re-implementing in a
//   native runner would duplicate surface + fork the operator-runnable path.
//   The wrapper WRAPS pwsh + adds the deterministic-outcome discipline the H13
//   handler needs.
//
// PHASE-B EXTENSION POSTURE (per POML mandatory pre-work #8):
//   The r2 shipped Validate-DeployedEnvironment.ps1 already contains the
//   BFF /healthz + env-var + CORS + dev-leakage + naming-conformance checks.
//   The Phase-B extension (sample analysis + sample doc upload+index +
//   workspace-layout render + wizard field-map) is TRACKED but out-of-scope
//   for this task (task 055 is the L2 handler; the script extension is a
//   separate task in the same wave). The wrapper INVOKES whatever the script
//   supports today + surfaces a Skipped-checks list so the operator knows
//   which sample checks were validated vs deferred to Phase F (task 089
//   full-run acceptance). This is a Path A exception per CLAUDE.md §6.5 —
//   documented in projects/customer-provisioning-orchestration-r1/notes/task-055-deviations.md.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Runs the extended <c>scripts/Validate-DeployedEnvironment.ps1</c> and
/// surfaces a typed outcome. Production impl shells out to pwsh; test impls
/// return canned results.
/// </summary>
public interface IE2EValidationRunner
{
    /// <summary>
    /// Invokes the extended validate script against the customer stamp and
    /// returns a typed outcome. Domain failures (a sample check failed) do NOT
    /// throw — the handler decides how to react. Infrastructure faults (pwsh
    /// not on PATH, script not found, timeout) throw — the handler catches +
    /// classifies Resumable.
    /// </summary>
    Task<E2EValidationOutcome> RunAsync(E2EValidationRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single extended validation run. Immutable record; the caller
/// (<see cref="H13E2EAcceptanceGateHandler"/>) constructs one per invocation.
/// </summary>
/// <param name="CustomerId">Customer id — flows into log lines and per-run diagnostic file names.</param>
/// <param name="RunId">RunId — flows into log lines for cross-reference with Cosmos state.</param>
/// <param name="DataverseUrl">Target Dataverse environment URL (e.g. <c>https://sprk-acme.crm.dynamics.com</c>).</param>
/// <param name="BffApiUrl">Target BFF API URL (production slot post-H9 swap).</param>
/// <param name="TargetSlotName">App Service slot the sample checks target (<c>production</c> or <c>staging</c>).</param>
public sealed record E2EValidationRequest(
    string CustomerId,
    string RunId,
    string DataverseUrl,
    string BffApiUrl,
    string TargetSlotName);

/// <summary>
/// Typed outcome of a single extended validation run. Discriminated union:
/// <see cref="Success"/> when all sample checks passed (or were correctly
/// reported Skipped in the interim Phase-B posture); <see cref="Failure"/>
/// when at least one sample check failed.
/// </summary>
public abstract record E2EValidationOutcome
{
    private E2EValidationOutcome() { }

    /// <summary>The extended validate script reported all sample checks Passed (or Skipped per interim posture). No R7 EFFECT assertion failed.</summary>
    /// <param name="ChecksPassed">Names of the sample checks the script reported Passed.</param>
    /// <param name="ChecksSkipped">Names of the sample checks the script reported Skipped (interim Phase-B posture — Phase F task 089 covers).</param>
    public sealed record Success(IReadOnlyList<string> ChecksPassed, IReadOnlyList<string> ChecksSkipped) : E2EValidationOutcome;

    /// <summary>The extended validate script reported at least one sample-check failure.</summary>
    /// <param name="ChecksFailed">Names of the sample checks that failed.</param>
    /// <param name="Diagnostic">Operator-facing diagnostic aggregating every failing sample-check message.</param>
    public sealed record Failure(IReadOnlyList<string> ChecksFailed, string Diagnostic) : E2EValidationOutcome;
}
