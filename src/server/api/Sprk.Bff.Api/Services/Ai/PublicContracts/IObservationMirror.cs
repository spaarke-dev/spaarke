using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// D-P11 — projects emitted Observations to the <c>sprk_analysis</c> Dataverse table so the
/// review surface (Phase 1: a model-driven view; Phase 1.5+: a dedicated review queue UI)
/// can list and disposition Observations. Side-effect of the universal ingest playbook
/// (D-P7); fire-and-forget with error logging — mirror failures MUST NOT fail the ingest.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cross-zone seam (task 051 design decision)</b>: this interface is consumed by
/// Zone A (the universal-ingest@v1 <c>ObservationEmitterNodeExecutor</c> in
/// <c>Services/Ai/Insights/Nodes/</c>; previously also by <c>IngestOrchestrator</c> in
/// <c>Services/Ai/Insights/Ingest/</c> — retired Wave C-G4 / task 022) and implemented by
/// Zone B (<c>DataverseObservationMirror</c> in <c>Services/Insights/Observations/</c>).
/// Per project <c>CLAUDE.md §3.5.4</c>, Zone B is forbidden from importing
/// <c>Services.Ai.Insights.*</c> (only <c>PublicContracts</c> is permitted via the
/// <c>[^.P]</c> exception in the grep pattern). Placing this interface in
/// <c>Services/Ai/PublicContracts/</c> alongside <see cref="IInsightsAi"/> preserves the
/// §3.5.4 facade discipline without per-feature carve-outs.
/// </para>
/// <para>
/// <b>Phase 1 implementations</b>:
/// <list type="bullet">
///   <item><c>NoOpObservationMirror</c> (Zone A, <c>Services/Ai/Insights/Mirror/</c>) —
///   logs but does not write; default in dev/test (kept as fallback when
///   <c>InsightsMirrorOptions.InsightsObservationActionId</c> is unset).</item>
///   <item><c>DataverseObservationMirror</c> (Zone B,
///   <c>Services/Insights/Observations/</c>) — performs the real
///   <c>sprk_analysis</c> upsert via <see cref="Spaarke.Dataverse.IGenericEntityService"/>
///   when the action GUID is configured.</item>
/// </list>
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
