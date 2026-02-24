namespace Sprk.Bff.Api.Models.Jobs;

/// <summary>
/// Message contract for RAG document indexing jobs submitted to Azure Service Bus.
/// Carries all information required to locate, parse, and index a document after
/// analysis completes.
///
/// Idempotency key convention (ADR-004): "{TenantId}:{DocumentId}"
/// This ensures that re-queuing the same document for the same tenant is safe.
/// </summary>
public sealed record RagIndexingJob
{
    /// <summary>
    /// Dataverse document identifier (sprk_document record ID).
    /// Used as the stable partition key in AI Search and the idempotency key suffix.
    /// </summary>
    public required string DocumentId { get; init; }

    /// <summary>
    /// Azure AD tenant identifier for multi-tenant isolation.
    /// Used as the idempotency key prefix and for AI Search document scoping.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Identifier of the analysis that triggered this indexing job.
    /// Provides traceability between analysis runs and their resulting index entries.
    /// </summary>
    public required string AnalysisId { get; init; }

    /// <summary>
    /// URL or SharePoint Embedded reference to the source document.
    /// Format: "spe://{driveId}/{itemId}" for SharePoint Embedded files,
    /// or a full HTTPS URL for externally accessible documents.
    /// </summary>
    public required string DocumentUrl { get; init; }

    /// <summary>
    /// When this indexing job was triggered (UTC).
    /// Recorded for latency measurement against NFR-11 (indexing must complete
    /// within 60 seconds of analysis).
    /// </summary>
    public DateTimeOffset TriggeredAt { get; init; } = DateTimeOffset.UtcNow;
}
