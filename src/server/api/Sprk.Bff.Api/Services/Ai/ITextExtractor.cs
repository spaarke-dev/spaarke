using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Interface for text extraction from documents.
/// Enables unit testing of services that depend on text extraction.
/// </summary>
public interface ITextExtractor
{
    /// <summary>
    /// Extract text from a file stream.
    /// </summary>
    Task<TextExtractionResult> ExtractAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extract text from a file stream with Redis cache support (ADR-009).
    /// When driveId, itemId, and etag are provided, extracted text is cached in Redis
    /// with key <c>sdap:ai:text:{driveId}:{itemId}:v{etag}</c> and 24-hour TTL.
    /// Cache hit skips extraction entirely. ETag in key ensures auto-invalidation on document change.
    /// </summary>
    /// <param name="fileStream">The file content stream.</param>
    /// <param name="fileName">The file name (used to determine extraction method).</param>
    /// <param name="driveId">SPE drive ID for cache key. Null disables caching.</param>
    /// <param name="itemId">SPE item ID for cache key. Null disables caching.</param>
    /// <param name="etag">Document ETag for cache key versioning. Null disables caching.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extraction result with text or error message.</returns>
    Task<TextExtractionResult> ExtractAsync(
        Stream fileStream,
        string fileName,
        string? driveId,
        string? itemId,
        string? etag,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a file extension is supported for extraction.
    /// </summary>
    bool IsSupported(string extension);

    /// <summary>
    /// Get the extraction method for a file extension.
    /// </summary>
    ExtractionMethod? GetMethod(string extension);
}
