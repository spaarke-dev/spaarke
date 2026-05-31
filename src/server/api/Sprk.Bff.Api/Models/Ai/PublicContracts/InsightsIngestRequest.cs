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
/// </remarks>
/// <param name="DocumentId">Document identifier the ingest playbook will process.
/// Corresponds to the document's id in <c>spaarke-files-index</c>. Required.</param>
/// <param name="MatterId">Matter the document belongs to (scheme-prefixed not required —
/// callers pass the matter local part since SPE events deliver it that way). Required.</param>
/// <param name="TenantId">Tenant identifier (D-52 single-tenant). Required.</param>
public sealed record InsightsIngestRequest(
    string DocumentId,
    string MatterId,
    string TenantId);
