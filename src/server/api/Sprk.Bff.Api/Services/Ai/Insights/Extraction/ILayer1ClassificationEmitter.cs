using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Ai.Insights.Extraction;

/// <summary>
/// D-P5 — the Layer 1 classification emission helper (per <c>SPEC-phase-1-minimum.md §3.3</c>):
/// given the typed <see cref="Layer1ClassificationResult"/> returned by the
/// <c>classification@v1</c> prompt, emit ONE <see cref="ObservationArtifact"/> (the
/// Classification Observation) with the SPEC §3.3 evidence + producedBy shape.
/// <para>
/// This is the Layer-1 analogue of <see cref="IObservationEmitter"/> (D-P10), but with two
/// structural differences:
/// <list type="bullet">
///   <item>Single emission per call (NOT per-field) — Layer 1 produces exactly one Observation.</item>
///   <item>Subject is the SOURCE DOCUMENT (per SPEC §3.3 "Subject = the document"), NOT the
///   matter the document belongs to (which is the subject for Layer 2 Observations).</item>
/// </list>
/// </para>
/// <para>
/// No confidence-threshold gating happens here. Per <c>SPEC-phase-1-minimum.md §3.3</c>,
/// confidence thresholds apply to Layer 2 outcome fields, not to Layer 1 — every classification
/// is emitted regardless of confidence so the review surface (D-P11) can calibrate Layer 1
/// behavior across all observed confidence values. The downstream consumer (D-P7 ingest
/// playbook, task 040) is responsible for the {classification, confidence >= 0.7} gate that
/// decides whether to invoke Layer 2.
/// </para>
/// <para>
/// Lives in Zone A per <c>SPEC §3.5</c> placement table (registered by
/// <c>InsightsExtractionModule</c>). Phase 1 consumers: D-P7 universal ingest playbook
/// (Wave 5 task 040). Phase 1.5+ consumers: <c>classification@v2</c> targeted re-extraction
/// tooling (D-62).
/// </para>
/// </summary>
public interface ILayer1ClassificationEmitter
{
    /// <summary>
    /// Emits one <see cref="ObservationArtifact"/> for the supplied Layer 1 classification
    /// result. The emitted Observation has:
    /// <list type="bullet">
    ///   <item><c>Subject = documentRef</c> (the document being classified, scheme-prefixed)</item>
    ///   <item><c>Predicate = "classification"</c></item>
    ///   <item><c>Value.Raw = classification enum (lower_snake_case string)</c></item>
    ///   <item><c>Value.DisplayHint = "enum"</c></item>
    ///   <item><c>Confidence = classification.Confidence</c></item>
    ///   <item><c>Evidence = [{refType: "document", ref: documentRef}, {refType: "playbook-run", ref: "playbook://classification@v1/run-{asOf}"}]</c></item>
    ///   <item><c>ProducedBy = {kind: "playbook", id: "playbook://classification@v1", version: "v1"}</c></item>
    /// </list>
    /// If <paramref name="upsertAsync"/> is supplied, the emitted Observation is also passed
    /// to it (substrate-write seam wired by D-P7 task 040). If <paramref name="upsertAsync"/>
    /// is <c>null</c>, the Observation is returned only — useful for unit tests and for Phase 1
    /// environments where the index upsert helper is not yet wired (parity with
    /// <see cref="IObservationEmitter.EmitFromExtractionAsync"/>).
    /// </summary>
    /// <param name="classification">The Layer 1 LLM result. Must not be null.</param>
    /// <param name="documentRef">
    /// Scheme-prefixed reference to the source document (e.g.,
    /// <c>spe://drive/acme-matters/item/closing-letter-M-2024-0341.docx</c>). Used as both
    /// the Observation subject AND the document evidence ref.
    /// </param>
    /// <param name="tenantId">Tenant the document belongs to. Required.</param>
    /// <param name="scope">
    /// Optional matter scope (matter id, practice area, etc.) propagated to the emitted
    /// Observation's <see cref="Models.Insights.Scope"/> envelope.
    /// </param>
    /// <param name="asOf">Wall-clock timestamp at which classification completed.</param>
    /// <param name="upsertAsync">Optional substrate-write callback. See remarks.</param>
    /// <param name="ct">Cancellation token. Propagated to <paramref name="upsertAsync"/>.</param>
    /// <returns>The single emitted Classification Observation.</returns>
    Task<ObservationArtifact> EmitAsync(
        Layer1ClassificationResult classification,
        string documentRef,
        string tenantId,
        ExtractionScope? scope,
        DateTimeOffset asOf,
        Func<ObservationArtifact, CancellationToken, Task>? upsertAsync,
        CancellationToken ct);
}
