using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Implementation of unified RAG file indexing with multiple entry points.
/// All entry points converge to the same internal pipeline for consistent results.
/// </summary>
/// <remarks>
/// <para>
/// Pipeline flow:
/// 1. Download file (entry point specific) or use provided content
/// 2. Extract text via ITextExtractor
/// 3. Chunk text via ITextChunkingService
/// 4. Build KnowledgeDocument objects for each chunk
/// 5. Batch index via IRagService.IndexDocumentsBatchAsync
/// 6. Return FileIndexingResult with statistics
/// </para>
/// </remarks>
public sealed class FileIndexingService : IFileIndexingService
{
    private readonly ISpeFileOperations _speFileOperations;
    private readonly ITextExtractor _textExtractor;
    private readonly ITextChunkingService _chunkingService;
    private readonly IRagService _ragService;
    private readonly ILogger<FileIndexingService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileIndexingService"/> class.
    /// </summary>
    /// <param name="speFileOperations">File operations for SPE access.</param>
    /// <param name="textExtractor">Text extraction service.</param>
    /// <param name="chunkingService">Text chunking service.</param>
    /// <param name="ragService">RAG indexing service.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public FileIndexingService(
        ISpeFileOperations speFileOperations,
        ITextExtractor textExtractor,
        ITextChunkingService chunkingService,
        IRagService ragService,
        ILogger<FileIndexingService> logger)
    {
        _speFileOperations = speFileOperations;
        _textExtractor = textExtractor;
        _chunkingService = chunkingService;
        _ragService = ragService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<FileIndexingResult> IndexFileAsync(
        FileIndexRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug(
                "Starting OBO file indexing for {FileName} (DriveId: {DriveId}, ItemId: {ItemId})",
                request.FileName, request.DriveId, request.ItemId);

            // Download via OBO authentication
            await using var stream = await _speFileOperations.DownloadFileAsUserAsync(
                httpContext,
                request.DriveId,
                request.ItemId,
                cancellationToken);

            if (stream is null)
            {
                _logger.LogWarning(
                    "File not found for OBO indexing: {DriveId}/{ItemId}",
                    request.DriveId, request.ItemId);

                return FileIndexingResult.Failed($"File not found: {request.DriveId}/{request.ItemId}");
            }

            // Extract text from file
            var extraction = await _textExtractor.ExtractAsync(
                stream,
                request.FileName,
                cancellationToken);

            if (!extraction.Success || string.IsNullOrWhiteSpace(extraction.Text))
            {
                _logger.LogWarning(
                    "Text extraction failed for {FileName}: {Error}",
                    request.FileName, extraction.ErrorMessage ?? "No text extracted");

                return FileIndexingResult.Failed(extraction.ErrorMessage ?? "Text extraction failed");
            }

            // Use shared pipeline
            return await IndexTextInternalAsync(
                extraction.Text,
                request.FileName,
                request.TenantId,
                request.ItemId,
                request.DocumentId,
                request.KnowledgeSourceId,
                request.KnowledgeSourceName,
                request.Metadata,
                stopwatch,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index file via OBO: {FileName}", request.FileName);

            return new FileIndexingResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Duration = stopwatch.Elapsed,
                SpeFileId = request.ItemId
            };
        }
    }

    /// <inheritdoc />
    public async Task<FileIndexingResult> IndexFileAppOnlyAsync(
        FileIndexRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug(
                "Starting app-only file indexing for {FileName} (DriveId: {DriveId}, ItemId: {ItemId})",
                request.FileName, request.DriveId, request.ItemId);

            // Download via app-only authentication
            await using var stream = await _speFileOperations.DownloadFileAsync(
                request.DriveId,
                request.ItemId,
                cancellationToken);

            if (stream is null)
            {
                _logger.LogWarning(
                    "File not found for app-only indexing: {DriveId}/{ItemId}",
                    request.DriveId, request.ItemId);

                return FileIndexingResult.Failed($"File not found: {request.DriveId}/{request.ItemId}");
            }

            // Extract text from file
            var extraction = await _textExtractor.ExtractAsync(
                stream,
                request.FileName,
                cancellationToken);

            if (!extraction.Success || string.IsNullOrWhiteSpace(extraction.Text))
            {
                _logger.LogWarning(
                    "Text extraction failed for {FileName}: {Error}",
                    request.FileName, extraction.ErrorMessage ?? "No text extracted");

                return FileIndexingResult.Failed(extraction.ErrorMessage ?? "Text extraction failed");
            }

            // Use shared pipeline
            return await IndexTextInternalAsync(
                extraction.Text,
                request.FileName,
                request.TenantId,
                request.ItemId,
                request.DocumentId,
                request.KnowledgeSourceId,
                request.KnowledgeSourceName,
                request.Metadata,
                stopwatch,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index file via app-only: {FileName}", request.FileName);

            return new FileIndexingResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Duration = stopwatch.Elapsed,
                SpeFileId = request.ItemId
            };
        }
    }

    /// <inheritdoc />
    public async Task<FileIndexingResult> IndexContentAsync(
        ContentIndexRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug(
                "Starting pre-extracted content indexing for {FileName} ({ContentLength} chars)",
                request.FileName, request.Content.Length);

            if (string.IsNullOrWhiteSpace(request.Content))
            {
                return FileIndexingResult.Failed("Content is empty");
            }

            // Use shared pipeline directly (no download or extraction needed)
            return await IndexTextInternalAsync(
                request.Content,
                request.FileName,
                request.TenantId,
                request.SpeFileId,
                request.DocumentId,
                request.KnowledgeSourceId,
                request.KnowledgeSourceName,
                request.Metadata,
                stopwatch,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index pre-extracted content: {FileName}", request.FileName);

            return new FileIndexingResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Duration = stopwatch.Elapsed,
                SpeFileId = request.SpeFileId
            };
        }
    }

    /// <summary>
    /// Shared pipeline that all entry points converge to.
    /// Handles: chunk → build documents → index batch.
    /// </summary>
    private async Task<FileIndexingResult> IndexTextInternalAsync(
        string text,
        string fileName,
        string tenantId,
        string speFileId,
        string? documentId,
        string? knowledgeSourceId,
        string? knowledgeSourceName,
        Dictionary<string, string>? metadata,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        // Step 1: Chunk the text
        var chunks = await _chunkingService.ChunkTextAsync(text, null, cancellationToken);

        if (chunks.Count == 0)
        {
            _logger.LogWarning(
                "No chunks generated from content for {FileName}",
                fileName);

            return new FileIndexingResult
            {
                Success = false,
                ErrorMessage = "No chunks generated from content",
                Duration = stopwatch.Elapsed,
                DocumentId = documentId,
                SpeFileId = speFileId
            };
        }

        // Step 2: Build KnowledgeDocument objects for each chunk
        var chunkIdBase = documentId ?? speFileId;
        var fileExtension = Path.GetExtension(fileName)?.TrimStart('.').ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;

        var documents = chunks.Select(chunk => new KnowledgeDocument
        {
            Id = $"{chunkIdBase}_{chunk.Index}",
            TenantId = tenantId,
            DocumentId = documentId,
            SpeFileId = speFileId,
            FileName = fileName,
            FileType = fileExtension,
            Content = chunk.Content,
            ChunkIndex = chunk.Index,
            ChunkCount = chunks.Count,
            KnowledgeSourceId = knowledgeSourceId,
            KnowledgeSourceName = knowledgeSourceName,
            Metadata = metadata is not null
                ? JsonSerializer.Serialize(metadata)
                : null,
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();

        // Step 3: Batch index (embeddings generated inside RagService)
        var results = await _ragService.IndexDocumentsBatchAsync(documents, cancellationToken);

        var successCount = results.Count(r => r.Succeeded);
        var allSucceeded = successCount == results.Count;

        // Log metrics only (no content per ADR-015)
        _logger.LogInformation(
            "Indexed {FileName}: {SuccessCount}/{TotalCount} chunks in {Duration}ms",
            fileName, successCount, results.Count, stopwatch.ElapsedMilliseconds);

        return new FileIndexingResult
        {
            Success = allSucceeded,
            ChunksIndexed = successCount,
            ErrorMessage = allSucceeded ? null : $"Failed to index {results.Count - successCount} of {results.Count} chunks",
            Duration = stopwatch.Elapsed,
            DocumentId = documentId,
            SpeFileId = speFileId
        };
    }
}
