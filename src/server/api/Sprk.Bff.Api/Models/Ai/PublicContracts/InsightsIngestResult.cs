namespace Sprk.Bff.Api.Models.Ai.PublicContracts;

/// <summary>
/// Result returned from <see cref="Services.Ai.PublicContracts.IInsightsAi.RunIngestAsync"/>.
/// Reports what the universal ingest playbook produced for a single SPE-upload event
/// (D-P7 + D-P8).
/// </summary>
/// <remarks>
/// <para>
/// <b>Zone B-importable DTO</b> per SPEC §3.5 — pure value type with primitive fields,
/// no AI internals. The D-P8 consumer (BackgroundService or Function per ADR-001) uses
/// this to write structured telemetry / dead-letter on failure and to populate
/// disposition events for the D-P11 review surface.
/// </para>
/// <para>
/// <b>Field semantics</b>:
/// <list type="bullet">
///   <item><see cref="ObservationsEmitted"/> — count of Observations that survived all
///   three D-P10 mechanical gates (confidence threshold + GroundingVerifier + non-empty
///   evidence) and were persisted to <c>spaarke-insights-index</c>. Zero is valid (low
///   confidence everywhere, or Layer 2 not triggered).</item>
///   <item><see cref="Layer1Classification"/> — the document-type label emitted by
///   Layer 1 (e.g., <c>closing_letter</c>, <c>contract_amendment</c>). Null on
///   inconclusive classification.</item>
///   <item><see cref="Layer2Triggered"/> — whether Layer 1's verdict caused the
///   playbook to run Layer 2 outcome extraction. False = cheap layers gated cost
///   out (D-59 gating principle).</item>
/// </list>
/// </para>
/// </remarks>
/// <param name="ObservationsEmitted">Number of Observations persisted to
/// <c>spaarke-insights-index</c> after all D-P10 mechanical gates.</param>
/// <param name="Layer1Classification">Layer 1 document-type label, or null when
/// classification was inconclusive.</param>
/// <param name="Layer2Triggered">True if Layer 2 outcome extraction was executed for
/// this document. False indicates the cheap-layer gate (D-59) saved the cost.</param>
public sealed record InsightsIngestResult(
    int ObservationsEmitted,
    string? Layer1Classification,
    bool Layer2Triggered);
