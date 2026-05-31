using Sprk.Bff.Api.Services.Ai.CitationVerification;

namespace Sprk.Bff.Api.Services.Ai.Insights.Ingest;

/// <summary>
/// Reads document content + chunks for the universal ingest pipeline (D-P7). The ingest
/// orchestrator does NOT re-fetch documents from SPE — it reads from
/// <c>spaarke-files-index</c> (already chunked + cached by the existing files pipeline).
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1 contract</b>: given a <c>documentId</c> (the id used in
/// <c>spaarke-files-index</c>), returns:
/// <list type="bullet">
///   <item>Concatenated text content (chunks joined in order; consumed by Layer 1 + Layer 2 prompts)</item>
///   <item>The original chunks (consumed by <c>GroundingVerifier</c> for D-P9 grounding checks)</item>
///   <item>The <c>spe://</c> URI reference (used as <c>DocumentRef</c> on emitted Observations)</item>
/// </list>
/// </para>
/// <para>
/// <b>Why an interface seam</b>: the orchestrator needs to be unit-testable without a
/// real Azure Search dependency. Phase 1 ships <c>FilesIndexIngestDocumentSource</c> as
/// the production impl; tests substitute fakes that return canned content. Per ADR-010
/// §Exceptions this is a real testability seam.
/// </para>
/// <para>
/// <b>Zone A placement</b>: lives under <c>Services/Ai/Insights/Ingest/</c>. Caller is
/// the Zone A orchestrator; no Zone B impact.
/// </para>
/// </remarks>
public interface IIngestDocumentSource
{
    /// <summary>
    /// Fetches document content + chunks for the supplied document id.
    /// </summary>
    /// <param name="documentId">Document identifier as it appears in
    /// <c>spaarke-files-index</c>. Required.</param>
    /// <param name="tenantId">Tenant the document belongs to (D-52 single-tenant boundary).
    /// Required.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The document content + chunks + scheme-prefixed reference.
    /// Returns <c>null</c> when the document is not found or has no indexable content;
    /// the orchestrator treats null as a no-op terminal condition (no Observations emitted,
    /// no error propagated).</returns>
    Task<IngestDocumentContent?> FetchAsync(
        string documentId,
        string tenantId,
        CancellationToken ct);
}

/// <summary>
/// The content the orchestrator needs from a source document to run the universal ingest pipeline.
/// </summary>
/// <param name="DocumentRef">Scheme-prefixed reference (e.g.,
/// <c>spe://drive/{driveId}/item/{itemId}</c>) used as the <c>DocumentRef</c> on every emitted
/// Observation. Required.</param>
/// <param name="FullText">Concatenated text (chunks joined in order, with newline separators)
/// for the Layer 1 + Layer 2 prompts. Required.</param>
/// <param name="Chunks">The original chunks (in order). Consumed by
/// <see cref="IGroundingVerifier"/> to verify Layer 2 evidence quotes. Required (may be a
/// single chunk if the document is small).</param>
public sealed record IngestDocumentContent(
    string DocumentRef,
    string FullText,
    IReadOnlyList<ChunkRef> Chunks);
