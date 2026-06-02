namespace Sprk.Bff.Api.Models.Ai.PublicContracts;

/// <summary>
/// Request to <see cref="Services.Ai.PublicContracts.IInsightsAi.RunIngestAsync"/> —
/// the universal ingest path through the Insights Engine (D-P7 + D-P8).
/// </summary>
/// <remarks>
/// <para>
/// <b>Zone B-importable DTO</b> per SPEC §3.5 — the request shape the D-P8 SPE-upload
/// consumer constructs and passes through the facade after receiving an SPE upload
/// event. The consumer does NOT re-fetch document content from SPE; that read happens
/// inside the D-P7 ingest playbook via <c>spaarke-files-index</c> (already chunked by
/// the existing pipeline).
/// </para>
/// <para>
/// The consumer's only job is to translate the event into this DTO and dispatch it
/// through <see cref="Services.Ai.PublicContracts.IInsightsAi"/>. All AI internals
/// (Layer 1 classification, conditional Layer 2 extraction, mechanical gates,
/// per-field Observation emission) stay in Zone A.
/// </para>
/// <para>
/// <b>Phase 1.5 r2 Wave C5 (task 024) — Universal-ingest parameterization</b>:
/// the optional overrides (<see cref="PracticeAreaHint"/>, <see cref="CostCapOverride"/>,
/// <see cref="Layer2Threshold"/>) wire the design-a5 §6 parameter-schema overrides
/// onto the Zone-B-importable surface. Per design D-P15-02 (ONE canonical
/// universal-ingest playbook, parameterized — not multiplied), these flow through
/// the facade to Wave C4's playbook-engine invocation as runtime parameters.
/// All overrides are NULLABLE — callers may omit them and the playbook's
/// <c>sprk_configjson.parameterSchema</c> defaults apply (per design-a5 §6:
/// <c>layer2Threshold = 0.7</c>, <c>practiceAreaHint = null</c>, <c>costCapOverride = null</c>).
/// During the Wave C1→C3 transition window (universal-ingest.playbook.json shipped
/// in C1; <c>IngestOrchestrator.cs</c> code path retired in C3), the optional
/// parameters are validated at the facade layer (see
/// <c>InsightsOrchestrator.ValidateIngestParameters</c>) and threaded through to
/// <see cref="Services.Ai.Insights.Ingest.IIngestOrchestrator"/> — currently the
/// internal orchestrator does not consume them (zero-effect during the window).
/// C4 (task 023) rewires <see cref="Services.Ai.PublicContracts.IInsightsAi.RunIngestAsync"/>
/// to invoke the playbook engine where parameters take effect end-to-end.
/// </para>
/// </remarks>
/// <param name="DocumentId">Document identifier the ingest playbook will process.
/// Corresponds to the document's id in <c>spaarke-files-index</c>. Required.</param>
/// <param name="MatterId">Matter the document belongs to (scheme-prefixed not required —
/// callers pass the matter local part since SPE events deliver it that way). Required.</param>
/// <param name="TenantId">Tenant identifier (D-52 single-tenant). Required.</param>
/// <param name="PracticeAreaHint">OPTIONAL — practice-area code (e.g., <c>"CTRNS"</c>,
/// <c>"IPPAT"</c>) for Wave D2/D3 per-area Layer 1 / Layer 2 prompt routing. Must be a
/// non-whitespace string when supplied. Defaults to <c>null</c> (litigation-default
/// prompts per Phase 1 D-59). Validation against <c>sprk_practicearea_ref</c> codes
/// happens downstream in the playbook node executor; the facade only enforces
/// well-formedness (non-empty when present).</param>
/// <param name="CostCapOverride">OPTIONAL — per-invocation cost cap in USD; overrides
/// <c>sprk_configjson.costCap</c>. Must be strictly positive when supplied. Defaults
/// to <c>null</c> (uses tenant's monthly cap from D-P9). Phase 1.5 enforcement is
/// observability-only per design D-52.</param>
/// <param name="Layer2Threshold">OPTIONAL — confidence threshold for the Layer 2 gate.
/// Must be in <c>[0.0, 1.0]</c> when supplied. Defaults to <c>null</c> (playbook applies
/// the schema default of <c>0.7</c> per Phase 1 D-59). Per-invocation override for SME
/// calibration or fixture testing.</param>
public sealed record InsightsIngestRequest(
    string DocumentId,
    string MatterId,
    string TenantId,
    string? PracticeAreaHint = null,
    decimal? CostCapOverride = null,
    double? Layer2Threshold = null);
