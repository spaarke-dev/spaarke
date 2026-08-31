// -----------------------------------------------------------------------------
// ITrapProbe.cs
//
// Task 185 (Phase C'' Wave G-7 Batch G-7D) — SHARED per-trap probe seam.
// Direct parity with the earlier <see cref="IInvariantProbe"/> seam (Batch
// G-7A1 / task 173): both seams exist so a Composite* verifier can compose the
// per-item probes without needing to know the concrete type of any single one.
//
// COMPOSITE PATTERN:
//   Each per-trap real probe (T1–T6, tasks 171/177/178/180/172/175) already
//   exposes the same shape — a <see cref="TrapKind"/> Kind property and a
//   <see cref="ProbeAsync(TrapVerificationRequest, CancellationToken)"/> method
//   returning a <see cref="TrapVerificationOutcome"/>. This interface merely
//   PROMOTES that latent shape to a first-class DI seam so
//   <see cref="CompositeTrapVerifier"/> can inject
//   <c>IEnumerable&lt;ITrapProbe&gt;</c>, dispatch per <see cref="Kind"/>, and
//   fall back to <see cref="TrapVerificationOutcome.InfraFault"/> (Resumable
//   per H13's §4C table) for any trap without a registered probe.
//
// TERMINAL OWNER:
//   Task 185 (H13 gate aggregation assembly) is the terminal owner of this
//   seam + the CompositeTrapVerifier that consumes it. The 6 real trap probe
//   files were authored standalone (per each file's header) precisely so this
//   task had architectural flexibility to pick the composition surface after
//   ground-truthing all 6 shapes. All 6 shapes matched exactly → this seam is
//   the least-invasive uniform composition surface.
//
// PLACEMENT + JUSTIFICATION (CLAUDE.md §11):
//   Existing — 6 real trap probe classes with uniform Kind + ProbeAsync
//     signatures already exist (tasks 171/172/175/177/178/180).
//   Extension — annotating each probe with this interface is a two-token
//     ADD (`: ITrapProbe`) with zero behavior change; the composite is the
//     ONE new class this task authors.
//   Cost-of-doing-nothing — without this seam, either (a) CompositeTrapVerifier
//     would have to hand-roll a discriminated switch over 6 concrete types
//     (violating the ADR-010 minimalism principle the sibling
//     CompositeInvariantVerifier established) OR (b) each future trap addition
//     would require a hand-edit of the composite (silent-fail risk if the
//     author forgets a case). This seam mirrors CompositeInvariantVerifier's
//     shape so the two verifiers read identically and each new probe becomes
//     ONE `AddSingleton<ITrapProbe, TProbe>()` line in the module.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Runtime probe for a SINGLE §4B silent-fail trap. Composed into the aggregate
/// <see cref="IE2ETrapVerifier"/> by <see cref="CompositeTrapVerifier"/>.
/// </summary>
public interface ITrapProbe
{
    /// <summary>The <see cref="TrapKind"/> this probe verdicts.</summary>
    TrapKind Kind { get; }

    /// <summary>
    /// Runs the probe and returns its per-trap outcome. MUST NOT throw for a
    /// genuine "probe couldn't verdict" state — return
    /// <see cref="TrapVerificationOutcome.InfraFault"/> so the handler
    /// classifies Resumable. MUST return
    /// <see cref="TrapVerificationOutcome.Failed"/> only when the probe ran and
    /// observed the silent-fail CONDITION.
    /// <see cref="OperationCanceledException"/> is allowed to propagate.
    /// </summary>
    Task<TrapVerificationOutcome> ProbeAsync(
        TrapVerificationRequest request, CancellationToken cancellationToken);
}
