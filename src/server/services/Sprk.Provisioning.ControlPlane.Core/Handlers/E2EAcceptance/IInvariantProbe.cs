// -----------------------------------------------------------------------------
// IInvariantProbe.cs
//
// Phase C'' Wave G-7 — SHARED per-invariant probe seam. Coordinated with
// sibling parallel task 173 (I2 AI Search) to avoid a 4-way collision on the
// H13 invariant-verifier surface across tasks 170 (I1), 173 (I2), 174 (I3),
// 176 (I4), and 179 (I5).
//
// COMPOSITE PATTERN:
//   Each per-invariant real probe implements this ONE interface and registers
//   as an <see cref="IInvariantProbe"/> in DI. <see cref="CompositeInvariantVerifier"/>
//   injects <c>IEnumerable&lt;IInvariantProbe&gt;</c>, dispatches per
//   <see cref="Kind"/>, and falls back to
//   <see cref="InvariantVerificationOutcome.InfraFault"/> (with
//   <see cref="InvariantProbeDeferralMessages.DeferralDiagnostic"/>) for any
//   invariant without a registered probe — preserving PlaceholderInvariantVerifier's
//   deferral semantics until every probe lands.
//
// Task 185 (H13 gate aggregation assembly) is the terminal owner of the
// composite wiring — every real probe must be `IInvariantProbe`-shaped by then.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Runtime sample probe for a SINGLE §4D tenant-isolation invariant. Composed
/// into the aggregate <see cref="IE2EInvariantVerifier"/> by
/// <see cref="CompositeInvariantVerifier"/>.
/// </summary>
public interface IInvariantProbe
{
    /// <summary>The <see cref="InvariantKind"/> this probe verdicts.</summary>
    InvariantKind Kind { get; }

    /// <summary>
    /// Runs the probe and returns its per-invariant outcome. MUST NOT throw
    /// for a genuine "probe couldn't verdict" state — return
    /// <see cref="InvariantVerificationOutcome.InfraFault"/> so the handler
    /// classifies Resumable. MUST return
    /// <see cref="InvariantVerificationOutcome.Failed"/> only when the probe
    /// ran and observed a VIOLATION.
    /// </summary>
    Task<InvariantVerificationOutcome> ProbeAsync(
        InvariantVerificationRequest request, CancellationToken cancellationToken);
}
