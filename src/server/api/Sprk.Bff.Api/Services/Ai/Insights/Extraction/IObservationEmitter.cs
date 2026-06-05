using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Ai.Insights.Extraction;

/// <summary>
/// D-P10 — the third mechanical post-processing gate in the universal ingest playbook
/// (per <c>SPEC-phase-1-minimum.md §3.4</c>): given a grounded extraction result (D-P6 +
/// D-P9 already applied), apply <see cref="ConfidenceThresholdOptions"/> per-field gating
/// and emit one <see cref="ObservationArtifact"/> per surviving field.
/// <para>
/// Fields whose confidence is below the configured threshold are DROPPED (not emitted) and
/// logged to App Insights for the D-P11 review-surface drift dashboard. Fields whose
/// grounding quote could not be verified should have been removed by D-P9 upstream — D-P10
/// does NOT re-verify grounding.
/// </para>
/// <para>
/// Lives in Zone A per <c>SPEC §3.5</c> placement table. Phase 1 consumers:
/// D-P7 universal ingest playbook (Wave 5 task 040). Phase 1.5+ consumers include
/// targeted re-extraction tooling (D-62).
/// </para>
/// </summary>
public interface IObservationEmitter
{
    /// <summary>
    /// Applies per-field confidence gating against the current <see cref="ConfidenceThresholdOptions"/>
    /// snapshot (admin-tunable per D-63 via <c>IOptionsMonitor</c>) and produces one
    /// <see cref="ObservationArtifact"/> per surviving field.
    /// <para>
    /// If <paramref name="upsertAsync"/> is supplied, each emitted Observation is also
    /// passed to it (D-P10 is responsible for emission semantics; the upsert callback is
    /// the substrate-write seam wired by D-P7 / W3.5 task 025 / D-P11 task 051). If
    /// <paramref name="upsertAsync"/> is <c>null</c>, Observations are returned only —
    /// useful for unit tests and for Phase 1 environments where the index-upsert helper
    /// is not yet wired.
    /// </para>
    /// </summary>
    /// <param name="extraction">
    /// The grounded extraction result (D-P9 already applied). Fields the LLM did not
    /// attempt MUST be omitted from <c>extraction.Fields</c> upstream; D-P10 does not
    /// distinguish "not attempted" from "attempted with confidence 0".
    /// </param>
    /// <param name="upsertAsync">
    /// Optional substrate-write callback invoked once per surviving Observation. Designed
    /// as a callback so D-P10 does not take a direct dependency on the index-upserter
    /// (which is parameterized by task 025's <c>ReferenceIndexingService</c> refactor and
    /// wired into the ingest playbook by task 040 / D-P7). If <c>null</c>, no persistence
    /// is attempted — the caller (or unit tests) receives the Observations and writes them.
    /// </param>
    /// <param name="ct">Cancellation token. Propagated to <paramref name="upsertAsync"/>.</param>
    /// <returns>The Observations that PASSED gating, in field-iteration order.</returns>
    Task<IReadOnlyList<ObservationArtifact>> EmitFromExtractionAsync(
        ExtractionResult extraction,
        Func<ObservationArtifact, CancellationToken, Task>? upsertAsync,
        CancellationToken ct);
}
