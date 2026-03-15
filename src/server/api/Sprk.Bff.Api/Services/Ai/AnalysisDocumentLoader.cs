using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Handles document loading, text extraction, and analysis caching for the analysis pipeline.
/// Extracted from AnalysisOrchestrationService to reduce constructor dependency count (ADR-010).
/// </summary>
public class AnalysisDocumentLoader
{
    private readonly IAnalysisDataverseService _analysisService;
    private readonly IDocumentDataverseService _documentService;
    private readonly ISpeFileOperations _speFileStore;
    private readonly ITextExtractor _textExtractor;
    private readonly IDistributedCache _cache;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AnalysisDocumentLoader> _logger;

    /// <summary>
    /// Cache key pattern for analysis state in Redis.
    /// </summary>
    private const string AnalysisCacheKeyPrefix = "sdap:ai:analysis:";

    /// <summary>
    /// TTL for cached analysis entries. Entries expire after 2 hours of inactivity.
    /// </summary>
    private static readonly DistributedCacheEntryOptions AnalysisCacheTtl = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2)
    };

    public AnalysisDocumentLoader(
        IAnalysisDataverseService analysisService,
        IDocumentDataverseService documentService,
        ISpeFileOperations speFileStore,
        ITextExtractor textExtractor,
        IDistributedCache cache,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AnalysisDocumentLoader> logger)
    {
        _analysisService = analysisService;
        _documentService = documentService;
        _speFileStore = speFileStore;
        _textExtractor = textExtractor;
        _cache = cache;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Store an analysis model in Redis via IDistributedCache with 2-hour TTL.
    /// </summary>
    public async Task CacheAnalysisAsync(Guid analysisId, AnalysisInternalModel analysis)
    {
        var entry = new AnalysisCacheEntry
        {
            AnalysisId = analysisId,
            DocumentId = analysis.DocumentId,
            DocumentText = analysis.DocumentText,
            Status = analysis.Status,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(entry);
        await _cache.SetAsync($"{AnalysisCacheKeyPrefix}{analysisId}", json, AnalysisCacheTtl);
    }

    /// <summary>
    /// Try to retrieve a cached analysis entry from Redis.
    /// Returns null on cache miss.
    /// </summary>
    public async Task<AnalysisCacheEntry?> GetCachedAnalysisAsync(Guid analysisId)
    {
        var bytes = await _cache.GetAsync($"{AnalysisCacheKeyPrefix}{analysisId}");
        if (bytes == null || bytes.Length == 0) return null;

        return JsonSerializer.Deserialize<AnalysisCacheEntry>(bytes);
    }

    /// <summary>
    /// Extract text from a document stored in SharePoint Embedded.
    /// Downloads the file using OBO authentication and uses TextExtractor to extract readable text.
    /// </summary>
    public async Task<string> ExtractDocumentTextAsync(
        DocumentEntity document,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Check if document has SPE file reference
        var hasValidGraphIds = !string.IsNullOrEmpty(document.GraphDriveId) && !string.IsNullOrEmpty(document.GraphItemId);
        if (!hasValidGraphIds)
        {
            _logger.LogWarning(
                "Document {DocumentId} has no SPE file reference (HasFile={HasFile}, DriveId={DriveId}, ItemId={ItemId})",
                document.Id, document.HasFile, document.GraphDriveId, document.GraphItemId);

            return $"[Document: {document.Name}]\n\nNo file content available for this document.";
        }

        // Check if file type is supported for extraction
        var fileName = document.FileName ?? document.Name ?? "unknown";
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;

        if (!_textExtractor.IsSupported(extension))
        {
            _logger.LogWarning(
                "File type {Extension} is not supported for text extraction (Document: {DocumentId})",
                extension, document.Id);

            return $"[Document: {document.Name}]\n\nFile type '{extension}' is not supported for text extraction.";
        }

        try
        {
            _logger.LogInformation(
                "Downloading document {DocumentId} from SPE (Drive={DriveId}, Item={ItemId}, FileName={FileName})",
                document.Id, document.GraphDriveId, document.GraphItemId, fileName);

            // Fetch file metadata to get ETag for cache key (ADR-009: Redis-first caching).
            string? etag = null;
            try
            {
                var metadata = await _speFileStore.GetFileMetadataAsUserAsync(
                    httpContext,
                    document.GraphDriveId!,
                    document.GraphItemId!,
                    cancellationToken);
                etag = metadata?.ETag;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to get file metadata for ETag (Document {DocumentId}), proceeding without text extraction cache",
                    document.Id);
            }

            // Download file from SharePoint Embedded using OBO authentication
            using var fileStream = await _speFileStore.DownloadFileAsUserAsync(
                httpContext,
                document.GraphDriveId!,
                document.GraphItemId!,
                cancellationToken);

            if (fileStream == null)
            {
                _logger.LogWarning("Failed to download document {DocumentId} from SPE - stream is null", document.Id);
                return $"[Document: {document.Name}]\n\nFailed to download file from storage.";
            }

            var streamLength = fileStream.CanSeek ? fileStream.Length : -1;
            _logger.LogInformation(
                "Downloaded document {DocumentId}, stream length: {StreamLength} bytes, extracting text from {FileName}",
                document.Id, streamLength, fileName);

            // Extract text using TextExtractor with Redis cache support (ADR-009).
            var extractionResult = await _textExtractor.ExtractAsync(
                fileStream, fileName, document.GraphDriveId, document.GraphItemId, etag, cancellationToken);

            _logger.LogInformation(
                "Extraction result for {DocumentId}: Success={Success}, Method={Method}, TextLength={TextLength}, Error={Error}",
                document.Id, extractionResult.Success, extractionResult.Method,
                extractionResult.Text?.Length ?? 0, extractionResult.ErrorMessage ?? "none");

            if (!extractionResult.Success)
            {
                _logger.LogWarning(
                    "Text extraction failed for document {DocumentId}: {Error}",
                    document.Id, extractionResult.ErrorMessage);

                return $"[Document: {document.Name}]\n\nText extraction failed: {extractionResult.ErrorMessage}";
            }

            if (extractionResult.IsVisionRequired)
            {
                _logger.LogInformation(
                    "Document {DocumentId} is an image file requiring vision model processing",
                    document.Id);

                return $"[Document: {document.Name}]\n\n[Image file - vision analysis not yet integrated for Analysis feature]";
            }

            _logger.LogInformation(
                "Successfully extracted {CharCount} characters from document {DocumentId} via {Method}",
                extractionResult.Text?.Length ?? 0, document.Id, extractionResult.Method);

            return extractionResult.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from document {DocumentId}", document.Id);
            return $"[Document: {document.Name}]\n\nError extracting text: {ex.Message}";
        }
    }

    /// <summary>
    /// Reload analysis context from Dataverse when not found in memory.
    /// Extracts document text so chat continuations have context.
    /// </summary>
    public async Task<AnalysisInternalModel> ReloadAnalysisFromDataverseAsync(
        Guid analysisId,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Reloading analysis {AnalysisId} from Dataverse", analysisId);

        var analysisRecord = await _analysisService.GetAnalysisAsync(analysisId.ToString(), cancellationToken)
            ?? throw new KeyNotFoundException($"Analysis {analysisId} not found in Dataverse");

        string? documentText = null;
        string documentName = "Unknown";

        if (analysisRecord.DocumentId != Guid.Empty)
        {
            try
            {
                var document = await _documentService.GetDocumentAsync(
                    analysisRecord.DocumentId.ToString(), cancellationToken);

                if (document != null)
                {
                    documentName = document.Name ?? document.FileName ?? "Unknown";
                    documentText = await ExtractDocumentTextAsync(document, httpContext, cancellationToken);
                    _logger.LogInformation(
                        "Extracted {CharCount} characters of document text for reloaded analysis {AnalysisId}",
                        documentText?.Length ?? 0, analysisId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to extract document text for analysis {AnalysisId}. Continuing without document context.",
                    analysisId);
            }
        }

        var chatHistory = DeserializeChatHistory(analysisRecord.ChatHistory, analysisId);

        var analysis = new AnalysisInternalModel
        {
            Id = analysisId,
            DocumentId = analysisRecord.DocumentId,
            DocumentName = documentName,
            ActionId = Guid.Empty,
            Status = "InProgress",
            DocumentText = documentText,
            SystemPrompt = BuildDefaultSystemPrompt(),
            WorkingDocument = analysisRecord.WorkingDocument,
            ChatHistory = chatHistory,
            StartedOn = DateTime.UtcNow
        };

        _logger.LogInformation(
            "Successfully reloaded analysis {AnalysisId} with {DocChars} chars of document text and {ChatCount} chat messages",
            analysisId, documentText?.Length ?? 0, chatHistory.Length);

        return analysis;
    }

    /// <summary>
    /// Get an analysis from the Redis cache, or reload it from Dataverse if cache miss.
    /// This is the "lite" version that does NOT extract document text (no HttpContext needed).
    /// Suitable for GET, save, and export operations that only need the persisted analysis data.
    /// </summary>
    public async Task<AnalysisInternalModel> GetOrReloadFromDataverseAsync(
        Guid analysisId,
        CancellationToken cancellationToken)
    {
        var cached = await GetCachedAnalysisAsync(analysisId);
        if (cached != null)
        {
            return new AnalysisInternalModel
            {
                Id = cached.AnalysisId,
                DocumentId = cached.DocumentId,
                DocumentText = cached.DocumentText,
                Status = cached.Status,
            };
        }

        _logger.LogInformation("Analysis {AnalysisId} not in cache, loading from Dataverse (lite)", analysisId);

        var record = await _analysisService.GetAnalysisAsync(analysisId.ToString(), cancellationToken)
            ?? throw new KeyNotFoundException($"Analysis {analysisId} not found in Dataverse");

        var chatHistory = DeserializeChatHistory(record.ChatHistory, analysisId);

        var analysis = new AnalysisInternalModel
        {
            Id = analysisId,
            DocumentId = record.DocumentId,
            DocumentName = record.Name ?? "Analysis",
            ActionId = Guid.Empty,
            Status = "Completed",
            WorkingDocument = record.WorkingDocument,
            ChatHistory = chatHistory,
            StartedOn = record.CreatedOn,
            CompletedOn = record.ModifiedOn,
        };

        await CacheAnalysisAsync(analysisId, analysis);

        _logger.LogInformation(
            "Loaded analysis {AnalysisId} from Dataverse (lite): {DocChars} chars, {ChatCount} messages",
            analysisId, record.WorkingDocument?.Length ?? 0, chatHistory.Length);

        return analysis;
    }

    /// <summary>
    /// Deserialize chat history from JSON string, returning empty array on failure.
    /// </summary>
    internal ChatMessageModel[] DeserializeChatHistory(string? chatHistoryJson, Guid analysisId)
    {
        if (string.IsNullOrWhiteSpace(chatHistoryJson))
            return [];

        try
        {
            var messages = JsonSerializer.Deserialize<ChatMessageModel[]>(
                chatHistoryJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (messages != null)
            {
                _logger.LogDebug("Restored {Count} chat messages for analysis {AnalysisId}",
                    messages.Length, analysisId);
                return messages;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize chat history for analysis {AnalysisId}", analysisId);
        }

        return [];
    }

    /// <summary>
    /// Get a document from Dataverse by ID.
    /// Exposes the underlying IDocumentDataverseService.GetDocumentAsync for callers that need
    /// document metadata without the full text extraction pipeline.
    /// </summary>
    public Task<DocumentEntity?> GetDocumentAsync(string documentId, CancellationToken cancellationToken)
    {
        return _documentService.GetDocumentAsync(documentId, cancellationToken);
    }

    /// <summary>
    /// Build a default system prompt for resumed analysis sessions.
    /// </summary>
    internal static string BuildDefaultSystemPrompt()
    {
        return """
            You are an AI assistant helping to analyze and discuss documents.

            ## Instructions
            - Provide helpful, accurate responses to questions about the document
            - Use the document content as your primary source of information
            - If asked to modify or update the analysis, provide the complete updated content
            - Format responses in clear, readable Markdown
            - Be concise but thorough in your answers

            ## Output Format
            Provide your response in Markdown format with appropriate headings and structure.
            """;
    }
}
