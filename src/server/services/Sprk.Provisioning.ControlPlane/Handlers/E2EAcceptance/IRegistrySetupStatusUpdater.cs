// -----------------------------------------------------------------------------
// IRegistrySetupStatusUpdater.cs
//
// L2 abstraction over the Dataverse registry `sprk_dataverseenvironment`
// SetupStatus → Ready transition H13 issues at the end of a fully-green
// acceptance pass. Production impl issues a Dataverse Web API PATCH; test
// impls return canned outcomes.
//
// TRANSITION SEMANTIC (per POML constraint):
//   The transition to `Ready` is CONDITIONAL on ALL of:
//     - Extended validate script exit 0 (SC #5)
//     - ALL 6 T1–T6 traps clear (SC #6)
//     - ALL 5 I1–I5 invariants clear (§4D)
//     - naming-conformance exit 0 (SC #17)
//     - cost envelope conforms (§15 #14 — advisory-warn only unless opted-in fail)
//   Partial-pass does NOT transition. On failure, Cosmos state = Quarantined
//   per §4C rollback semantics.
//
// COMPANION UPDATE:
//   The updater ALSO clears `sprk_currentrunid` on success (spec.md FR-23
//   integration point — releasing the same-customer serialization guard's
//   Dataverse side of the story) so the customer's next run can proceed
//   without an operator clear-quarantine step.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 impls (production Dataverse Web API updater + test fakes per unit
//   test). Reuses the L2 IDataverseEnvironmentRegistryClient pattern (task
//   042) for the READ path; the WRITE path is a distinct concern with a
//   distinct interface, mirroring H10's split of App User creator vs verifier.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Transitions the Dataverse registry `sprk_dataverseenvironment.SetupStatus`
/// to <c>Ready</c> and clears <c>sprk_currentrunid</c>. Production impl issues
/// a Dataverse Web API PATCH; test impls return canned outcomes.
/// </summary>
public interface IRegistrySetupStatusUpdater
{
    /// <summary>
    /// PATCHes the registry row to <c>SetupStatus=Ready + sprk_currentrunid=null</c>
    /// and returns a typed outcome. Infrastructure faults (Web API 4xx/5xx,
    /// token acquisition failure, concurrency conflict) throw — the handler
    /// catches + classifies Resumable.
    /// </summary>
    Task<RegistrySetupStatusUpdateOutcome> TransitionToReadyAsync(
        RegistrySetupStatusUpdateRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single registry-transition PATCH. Immutable record.
/// </summary>
/// <param name="CustomerId">Customer id — flows into log lines.</param>
/// <param name="RunId">RunId — for log correlation + audit trail.</param>
/// <param name="TenantId">Explicit tenantId (§4D I1 no-hardcoded-tenant) — token acquisition MUST use this scope.</param>
/// <param name="EnvironmentId">The Dataverse row id (as GUID string) — target of the PATCH.</param>
/// <param name="RegistryDataverseUrl">The Dataverse environment URL hosting the registry table (Spaarke internal, NOT the customer's own env URL).</param>
public sealed record RegistrySetupStatusUpdateRequest(
    string CustomerId,
    string RunId,
    string TenantId,
    string EnvironmentId,
    string RegistryDataverseUrl);

/// <summary>
/// Typed outcome of a registry-transition PATCH. Discriminated union:
/// <see cref="Success"/> when the PATCH landed; <see cref="Failure"/> when
/// the domain rejected the write (concurrency conflict, row missing, etc.).
/// </summary>
public abstract record RegistrySetupStatusUpdateOutcome
{
    private RegistrySetupStatusUpdateOutcome() { }

    /// <summary>PATCH landed — registry Setup Status now <c>Ready</c>.</summary>
    public sealed record Success() : RegistrySetupStatusUpdateOutcome;

    /// <summary>PATCH rejected — row missing, ETag conflict, or Web API domain error.</summary>
    /// <param name="Diagnostic">Operator-facing diagnostic — should include HTTP status + response body summary.</param>
    public sealed record Failure(string Diagnostic) : RegistrySetupStatusUpdateOutcome;
}
