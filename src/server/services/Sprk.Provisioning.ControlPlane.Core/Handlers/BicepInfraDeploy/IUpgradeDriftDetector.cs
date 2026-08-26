// -----------------------------------------------------------------------------
// IUpgradeDriftDetector.cs
//
// Runs `az deployment group what-if` (or an SDK equivalent) BEFORE an
// upgrade-mode Bicep apply and reports whether the customer's existing
// resources have drifted from the declarative template.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-34
//     (upgrade mode): H2a upgrade-mode runs `az deployment group what-if`
//     first; defaults to REJECT + escalate on drift with report at
//     runNotes/drift-{customerId}-{timestamp}.md.
//   - projects/customer-provisioning-orchestration-r1/design.md §14A.5
//     Drift detection: Options A/B/C — default is B (reject + escalate).
//   - projects/customer-provisioning-orchestration-r1/design.md §4C
//     Quarantine-required: drift-on-apply is quarantine-worthy because
//     an accidental "accept" would silently overwrite operator-intended
//     state.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 impls: <see cref="AzCliUpgradeDriftDetector"/> (production shell-out) +
//   test stubs.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <summary>
/// Detects drift between the declarative Bicep template and the customer's
/// current Azure resource state. Invoked ONLY in upgrade mode
/// (<c>sprk_dataverseenvironment.sprk_provisionedon</c> is non-null).
/// </summary>
public interface IUpgradeDriftDetector
{
    /// <summary>
    /// Runs the what-if analysis + returns a typed result. Domain outcomes
    /// (no-drift / drift) never throw; infra faults (auth, network, invalid
    /// Bicep) throw so the handler can classify per §4C.
    /// </summary>
    /// <param name="request">The same <see cref="BicepDeployRequest"/> the runner would receive on apply — the what-if compares against this template.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<UpgradeDriftDetectionResult> DetectDriftAsync(
        BicepDeployRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Discriminated result of <see cref="IUpgradeDriftDetector.DetectDriftAsync"/>.
/// </summary>
public abstract record UpgradeDriftDetectionResult
{
    private UpgradeDriftDetectionResult() { }

    /// <summary>What-if reports no drift — safe to apply. Handler proceeds to the runner.</summary>
    public sealed record NoDrift : UpgradeDriftDetectionResult;

    /// <summary>
    /// What-if reports drift. <paramref name="DriftReport"/> is the raw
    /// what-if output (JSON or human-readable text). Handler writes it to
    /// <c>runNotes/drift-{customerId}-{timestamp}.md</c> and returns
    /// Failure(QuarantineRequired, upgrade-mode-drift).
    /// </summary>
    public sealed record DriftDetected(string DriftReport) : UpgradeDriftDetectionResult;
}
