using Microsoft.AspNetCore.Http;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Unified RAG indexing service with multiple entry points.
/// All entry points converge to the same internal pipeline:
/// text extraction → chunking → embedding → indexing.
/// </summary>
/// <remarks>
/// <para>
/// This service provides three entry points for different scenarios:
/// </para>
/// <list type="bullet">
/// <item><see cref="IndexFileAsync"/> - User-triggered indexing via API (OBO authentication)</item>
/// <item><see cref="IndexFileAppOnlyAsync"/> - Background job indexing (app-only authentication)</item>
/// <item><see cref="IndexContentAsync"/> - Pre-extracted content indexing (most efficient)</item>
/// </list>
/// <para>
/// Regardless of entry point, the chunking, embedding, and indexing logic is identical,
/// ensuring consistent searchability across all document sources.
/// </para>
/// </remarks>
public interface IFileIndexingService
{
    /// <summary>
    /// Index a file using OBO authentication (user-triggered).
    /// Downloads file using user's token, extracts text, then uses shared pipeline.
    /// </summary>
    /// <param name="request">The file indexing request with file identifiers and metadata.</param>
    /// <param name="httpContext">HTTP context for OBO token extraction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success/failure and chunk count.</returns>
    /// <remarks>
    /// Use this entry point for user-initiated indexing via the API endpoint.
    /// The user must have read access to the file in SharePoint Embedded.
    /// </remarks>
    Task<FileIndexingResult> IndexFileAsync(
        FileIndexRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Index a file using app-only authentication (background job).
    /// Downloads file using application credentials, extracts text, then uses shared pipeline.
    /// </summary>
    /// <param name="request">The file indexing request with file identifiers and metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success/failure and chunk count.</returns>
    /// <remarks>
    /// Use this entry point for background processing such as document events or batch operations.
    /// Requires the application to have read access to the container.
    /// </remarks>
    Task<FileIndexingResult> IndexFileAppOnlyAsync(
        FileIndexRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Index pre-extracted content directly (most efficient).
    /// Skips download and extraction, goes straight to shared pipeline.
    /// </summary>
    /// <param name="request">The content indexing request with text and metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success/failure and chunk count.</returns>
    /// <remarks>
    /// Use this entry point when text content is already available (e.g., email processing).
    /// Most efficient path as it avoids redundant file download and text extraction.
    /// </remarks>
    Task<FileIndexingResult> IndexContentAsync(
        ContentIndexRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request for file-based RAG indexing operations.
/// Used by <see cref="IFileIndexingService.IndexFileAsync"/> and
/// <see cref="IFileIndexingService.IndexFileAppOnlyAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Requires both DriveId and ItemId to locate the file in SharePoint Embedded.
/// The file will be downloaded and text extracted before indexing.
/// </para>
/// </remarks>
public sealed record FileIndexRequest
{
    /// <summary>
    /// SharePoint Embedded drive (container) identifier.
    /// </summary>
    public required string DriveId { get; init; }

    /// <summary>
    /// SharePoint Embedded item (file) identifier within the drive.
    /// </summary>
    public required string ItemId { get; init; }

    /// <summary>
    /// The file name including extension.
    /// Used for text extraction format detection and metadata.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Tenant identifier for multi-tenant index routing.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Optional Dataverse document record identifier.
    /// Null for orphan files that have no linked sprk_document record.
    /// </summary>
    /// <remarks>
    /// When provided, enables linking index entries to the Dataverse document
    /// and supports document-level operations like bulk delete.
    /// </remarks>
    public string? DocumentId { get; init; }

    /// <summary>
    /// Optional knowledge source identifier for grouping related documents.
    /// </summary>
    public string? KnowledgeSourceId { get; init; }

    /// <summary>
    /// Optional knowledge source name for display purposes.
    /// </summary>
    public string? KnowledgeSourceName { get; init; }

    /// <summary>
    /// Optional additional metadata to store with indexed chunks.
    /// </summary>
    /// <remarks>
    /// Can include source-specific information such as email subject,
    /// sender, or custom classification tags.
    /// </remarks>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Request for content-based RAG indexing when text is already extracted.
/// Used by <see cref="IFileIndexingService.IndexContentAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the most efficient entry point as it skips file download
/// and text extraction. Use when content is already available.
/// </para>
/// </remarks>
public sealed record ContentIndexRequest
{
    /// <summary>
    /// The pre-extracted text content to index.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// The original file name (for metadata and chunk identification).
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Tenant identifier for multi-tenant index routing.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// SharePoint Embedded file identifier.
    /// Used to link index entries to the source file.
    /// </summary>
    public required string SpeFileId { get; init; }

    /// <summary>
    /// Optional Dataverse document record identifier.
    /// Null for orphan files that have no linked sprk_document record.
    /// </summary>
    public string? DocumentId { get; init; }

    /// <summary>
    /// Optional knowledge source identifier for grouping related documents.
    /// </summary>
    public string? KnowledgeSourceId { get; init; }

    /// <summary>
    /// Optional knowledge source name for display purposes.
    /// </summary>
    public string? KnowledgeSourceName { get; init; }

    /// <summary>
    /// Optional additional metadata to store with indexed chunks.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Result of a file indexing operation.
/// </summary>
public sealed record FileIndexingResult
{
    /// <summary>
    /// Whether the indexing operation succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Number of chunks successfully indexed.
    /// </summary>
    public int ChunksIndexed { get; init; }

    /// <summary>
    /// Error message if operation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Total time taken for the indexing operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// The Dataverse document ID if one was associated.
    /// </summary>
    public string? DocumentId { get; init; }

    /// <summary>
    /// The SharePoint Embedded file ID that was indexed.
    /// </summary>
    public string? SpeFileId { get; init; }

    /// <summary>
    /// Creates a failed result with the specified error message.
    /// </summary>
    /// <param name="error">The error message describing the failure.</param>
    /// <returns>A failed <see cref="FileIndexingResult"/>.</returns>
    public static FileIndexingResult Failed(string error) => new()
    {
        Success = false,
        ErrorMessage = error
    };

    /// <summary>
    /// Creates a successful result with the specified chunk count.
    /// </summary>
    /// <param name="chunksIndexed">Number of chunks indexed.</param>
    /// <param name="duration">Time taken for the operation.</param>
    /// <param name="documentId">Optional document ID.</param>
    /// <param name="speFileId">Optional SPE file ID.</param>
    /// <returns>A successful <see cref="FileIndexingResult"/>.</returns>
    public static FileIndexingResult Succeeded(
        int chunksIndexed,
        TimeSpan duration,
        string? documentId = null,
        string? speFileId = null) => new()
        {
            Success = true,
            ChunksIndexed = chunksIndexed,
            Duration = duration,
            DocumentId = documentId,
            SpeFileId = speFileId
        };
}
