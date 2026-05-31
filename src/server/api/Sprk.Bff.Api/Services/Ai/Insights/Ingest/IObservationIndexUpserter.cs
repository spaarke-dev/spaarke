using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Ai.Insights.Ingest;

/// <summary>
/// Writes emitted Observations to <c>spaarke-insights-index</c>. Used by the D-P7
/// universal ingest orchestrator to persist surviving Observations after the three
/// mechanical gates (D-P9 grounding + D-P10 confidence + D-P10 emission).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not <c>ReferenceIndexingService</c></b>: that service is chunk-and-embed-then-upsert
/// for reference documents (one source becomes many chunked index rows). Observation
/// upserts are one-Observation-per-row — the Observation is itself the "atomic unit",
/// not the document. Using <c>ReferenceIndexingService</c> here would force a
/// degenerate single-chunk path that masked the actual semantics.
/// </para>
/// <para>
/// <b>Idempotency</b>: implementations use <c>MergeOrUploadDocumentsAsync</c> so
/// re-ingesting the same document produces deterministic upserts on Observation.Id
/// (which is itself derived from subject + field + document per
/// <c>ObservationEmitter.BuildObservationId</c>). No explicit delete-first pass needed
/// because the id is stable across re-runs.
/// </para>
/// <para>
/// <b>Zone A placement</b>: caller is the Zone A orchestrator; consumes
/// <c>IOpenAiClient</c> for embedding generation, so must live in Zone A. Phase 1.5+
/// may extract this into a shared substrate-write service if Precedent projection
/// (task 041) ends up with similar semantics.
/// </para>
/// </remarks>
public interface IObservationIndexUpserter
{
    /// <summary>
    /// Embeds + upserts a single Observation to <c>spaarke-insights-index</c>.
    /// Idempotent on Observation.Id.
    /// </summary>
    /// <param name="observation">The Observation to upsert. Required.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpsertAsync(ObservationArtifact observation, CancellationToken ct);
}
