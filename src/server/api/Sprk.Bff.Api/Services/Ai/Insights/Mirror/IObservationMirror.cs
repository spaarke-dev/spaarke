using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Ai.Insights.Mirror;

/// <summary>
/// D-P11 — projects emitted Observations to the <c>sprk_analysis</c> Dataverse table so the
/// review surface (Phase 1: a model-driven view; Phase 1.5+: a dedicated review queue UI)
/// can list and disposition Observations. Side-effect of the universal ingest playbook
/// (D-P7); fire-and-forget with error logging — mirror failures MUST NOT fail the ingest.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1 scaffold (task 040)</b>: the real implementation lands with task 051
/// (D-P11 mirror sync). Task 040 ships this interface seam plus a
/// <see cref="NoOpObservationMirror"/> impl that logs but does not write — the seam is
/// load-bearing so the ingest orchestrator wires the call today and task 051 swaps
/// the registration without touching the orchestrator.
/// </para>
/// <para>
/// <b>Zone A placement</b>: lives under <c>Services/Ai/Insights/Mirror/</c>. The seam is
/// in Zone A because the orchestrator (Zone A) is the caller; task 051's
/// <c>DataverseObservationMirror</c> will also live in Zone A so it can be called from
/// the orchestrator without a Zone B import.
/// </para>
/// <para>
/// <b>Why "fire-and-forget with error logging" semantics</b>: per SPEC-phase-1-minimum.md §5,
/// the mirror exists to power QA sampling — it's a UX convenience, not a system-of-record.
/// The system-of-record IS <c>spaarke-insights-index</c>. A mirror write that fails (e.g.,
/// Dataverse throttling, transient network error) is recoverable by re-projecting from
/// the index later; failing the ingest because of a mirror failure would lose the
/// authoritative Observation. The orchestrator catches mirror exceptions and logs them
/// rather than propagating.
/// </para>
/// </remarks>
public interface IObservationMirror
{
    /// <summary>
    /// Mirror a single emitted Observation to <c>sprk_analysis</c>. May fail; callers
    /// MUST treat failures as non-fatal (log + continue). Implementations should be
    /// idempotent per (Observation.Id) so re-runs of the orchestrator don't create
    /// duplicate mirror rows.
    /// </summary>
    /// <param name="observation">The Observation that was just emitted to
    /// <c>spaarke-insights-index</c>. Required.</param>
    /// <param name="ct">Cancellation token.</param>
    Task MirrorAsync(ObservationArtifact observation, CancellationToken ct);
}
