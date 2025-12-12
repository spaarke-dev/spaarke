using System.ClientModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Azure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Gateway to Azure AI Document Intelligence capabilities.
/// Orchestrates document analysis: download from SPE → extract text → stream AI completion.
/// Supports summarization, entity extraction, and structured metadata generation.
/// </summary>
public class DocumentIntelligenceService : IDocumentIntelligenceService
{
    private readonly IOpenAiClient _openAiClient;
    private readonly ITextExtractor _textExtractor;
    private readonly ISpeFileOperations _speFileOperations;
    private readonly DocumentIntelligenceOptions _options;
    private readonly ILogger<DocumentIntelligenceService> _logger;
    private readonly AiTelemetry? _telemetry;

    public DocumentIntelligenceService(
        IOpenAiClient openAiClient,
        ITextExtractor textExtractor,
        ISpeFileOperations speFileOperations,
        IOptions<DocumentIntelligenceOptions> options,
        ILogger<DocumentIntelligenceService> logger,
        AiTelemetry? telemetry = null)
    {
        _openAiClient = openAiClient;
        _textExtractor = textExtractor;
        _speFileOperations = speFileOperations;
        _options = options.Value;
        _logger = logger;
        _telemetry = telemetry;
    }

    /// <summary>
    /// Analyze a document with streaming output using user context (OBO).
    /// Use this for real-time SSE streaming to the browser.
    /// </summary>
    /// <param name="httpContext">The HTTP context for user authentication (OBO).</param>
    /// <param name="request">The analysis request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of AnalysisChunk for streaming.</returns>
    public async IAsyncEnumerable<AnalysisChunk> AnalyzeStreamAsync(
        HttpContext httpContext,
        DocumentAnalysisRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Start telemetry tracking
        var stopwatch = _telemetry?.RecordRequestStart("streaming") ?? Stopwatch.StartNew();
        string? extractionMethod = null;
        string? fileType = null;
        long? fileSize = null;

        _logger.LogInformation(
            "Starting streaming analysis for document {DocumentId}, DriveId={DriveId}, ItemId={ItemId}",
            request.DocumentId, request.DriveId, request.ItemId);

        // Step 0: Resolve containerId to driveId if needed (PCF sends containerId as driveId)
        string? resolvedDriveId = null;
        string? driveResolutionError = null;
        try
        {
            resolvedDriveId = await _speFileOperations.ResolveDriveIdAsync(request.DriveId, cancellationToken);
            if (resolvedDriveId != request.DriveId)
            {
                _logger.LogDebug("Resolved container {ContainerId} to drive {DriveId}",
                    request.DriveId, resolvedDriveId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve drive ID for {DriveId}", request.DriveId);
            _telemetry?.RecordFailure(stopwatch, "drive_resolution_failed", "streaming");
            driveResolutionError = "Failed to resolve file storage location.";
        }

        if (driveResolutionError != null || resolvedDriveId == null)
        {
            yield return AnalysisChunk.FromError(driveResolutionError ?? "Failed to resolve file storage location.");
            yield break;
        }

        // Step 1: Get file metadata for name (using user OBO for streaming)
        var metadata = await _speFileOperations.GetFileMetadataAsUserAsync(
            httpContext, resolvedDriveId, request.ItemId, cancellationToken);

        if (metadata == null)
        {
            _logger.LogWarning("File not found: DriveId={DriveId}, ItemId={ItemId}",
                resolvedDriveId, request.ItemId);
            _telemetry?.RecordFailure(stopwatch, "file_not_found", "streaming");
            yield return AnalysisChunk.FromError("File not found in SharePoint Embedded.");
            yield break;
        }

        var fileName = metadata.Name;
        fileType = Path.GetExtension(fileName)?.ToLowerInvariant();
        fileSize = metadata.Size;
        _logger.LogDebug("File metadata retrieved: {FileName}", fileName);

        // Step 2: Download file content (using user OBO for streaming)
        using var fileStream = await _speFileOperations.DownloadFileAsUserAsync(
            httpContext, resolvedDriveId, request.ItemId, cancellationToken);

        if (fileStream == null)
        {
            _logger.LogWarning("Failed to download file: DriveId={DriveId}, ItemId={ItemId}",
                resolvedDriveId, request.ItemId);
            _telemetry?.RecordFailure(stopwatch, "file_download_failed", "streaming");
            yield return AnalysisChunk.FromError("Failed to download file content.");
            yield break;
        }

        // Step 3: Check extraction requirements (text vs vision)
        var extractionResult = await _textExtractor.ExtractAsync(fileStream, fileName, cancellationToken);
        extractionMethod = extractionResult.Method.ToTelemetryString();

        if (!extractionResult.Success)
        {
            _logger.LogWarning("Text extraction failed for {FileName}: {Error}",
                fileName, extractionResult.ErrorMessage);
            _telemetry?.RecordFailure(stopwatch, "extraction_failed", "streaming", extractionMethod);
            yield return AnalysisChunk.FromError(extractionResult.ErrorMessage ?? "Text extraction failed.");
            yield break;
        }

        // Step 4: Stream AI completion (vision or text based on extraction result)
        var summaryBuilder = new StringBuilder();

        IAsyncEnumerable<string> streamingChunks;
        if (extractionResult.IsVisionRequired)
        {
            // Handle image files with vision model
            _logger.LogDebug("Using vision model for image file {FileName}", fileName);

            // Reset stream position and read image bytes
            if (fileStream.CanSeek)
            {
                fileStream.Position = 0;
            }
            var imageBytes = await ReadAllBytesAsync(fileStream, cancellationToken);
            var mediaType = GetMediaType(fileName);

            streamingChunks = _openAiClient.StreamVisionCompletionAsync(
                _options.VisionPromptTemplate, imageBytes, mediaType, cancellationToken);
        }
        else
        {
            // Handle text-based documents
            _logger.LogDebug("Extracted {CharCount} characters from {FileName}",
                extractionResult.CharacterCount, fileName);

            var prompt = BuildPrompt(extractionResult.Text!);
            streamingChunks = _openAiClient.StreamCompletionAsync(prompt, cancellationToken: cancellationToken);
        }

        // Stream with error handling (can't yield in catch, so capture error state)
        AnalysisChunk? capturedErrorChunk = null;
        await using var enumerator = streamingChunks.GetAsyncEnumerator(cancellationToken);
        while (capturedErrorChunk == null)
        {
            string chunk;
            bool hasMore;
            try
            {
                hasMore = await enumerator.MoveNextAsync();
                if (!hasMore)
                    break;
                chunk = enumerator.Current;
            }
            catch (RequestFailedException rfEx)
            {
                var (errChunk, logMessage) = HandleOpenAiError(rfEx, request.DocumentId);
                _logger.LogError(rfEx, logMessage);
                _telemetry?.RecordFailure(stopwatch, GetErrorCode(rfEx), "streaming", extractionMethod);
                capturedErrorChunk = errChunk;
                break;
            }
            catch (ClientResultException crEx)
            {
                var (errChunk, logMessage) = HandleClientResultError(crEx, request.DocumentId);
                _logger.LogError(crEx, logMessage);
                _telemetry?.RecordFailure(stopwatch, "openai_client_error", "streaming", extractionMethod);
                capturedErrorChunk = errChunk;
                break;
            }
            catch (TaskCanceledException tcEx) when (tcEx.InnerException is TimeoutException)
            {
                _logger.LogError(tcEx, "OpenAI request timed out for document {DocumentId}", request.DocumentId);
                _telemetry?.RecordFailure(stopwatch, "openai_timeout", "streaming", extractionMethod);
                capturedErrorChunk = AnalysisChunk.FromError("The AI service request timed out. Please try again.");
                break;
            }

            summaryBuilder.Append(chunk);
            yield return AnalysisChunk.FromContent(chunk);
        }

        if (capturedErrorChunk != null)
        {
            yield return capturedErrorChunk;
            yield break;
        }

        // Step 5: Yield final chunk with complete result
        var completeSummary = summaryBuilder.ToString();
        _logger.LogInformation(
            "Streaming analysis completed for document {DocumentId}, SummaryLength={Length}",
            request.DocumentId, completeSummary.Length);

        _telemetry?.RecordSuccess(stopwatch, "streaming", extractionMethod, fileType, fileSize);

        // Parse structured result if enabled, otherwise return raw summary
        if (_options.StructuredOutputEnabled)
        {
            var result = ParseStructuredResponse(completeSummary);

            // Include email metadata if this was an email file
            _logger.LogInformation(
                "Email check for document {DocumentId}: Method={Method}, IsEmail={IsEmail}, HasEmailMetadata={HasMetadata}",
                request.DocumentId,
                extractionResult.Method,
                extractionResult.IsEmail,
                extractionResult.EmailMetadata != null);

            if (extractionResult.IsEmail)
            {
                result.EmailMetadata = extractionResult.EmailMetadata;
                _logger.LogInformation(
                    "Attached email metadata for document {DocumentId}: From={From}, Subject={Subject}, Attachments={AttachmentCount}",
                    request.DocumentId,
                    extractionResult.EmailMetadata?.From,
                    extractionResult.EmailMetadata?.Subject,
                    extractionResult.EmailMetadata?.Attachments.Count ?? 0);
            }

            yield return AnalysisChunk.Completed(result);
        }
        else
        {
            yield return AnalysisChunk.Completed(completeSummary);
        }
    }

    /// <summary>
    /// Analyze a document without streaming.
    /// Use this for background job processing.
    /// </summary>
    /// <param name="request">The analysis request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The analysis result.</returns>
    public async Task<AnalysisResult> AnalyzeAsync(
        DocumentAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        // Start telemetry tracking
        var stopwatch = _telemetry?.RecordRequestStart("batch") ?? Stopwatch.StartNew();
        string? extractionMethod = null;
        string? fileType = null;
        long? fileSize = null;

        _logger.LogInformation(
            "Starting non-streaming analysis for document {DocumentId}, DriveId={DriveId}, ItemId={ItemId}",
            request.DocumentId, request.DriveId, request.ItemId);

        // Step 0: Resolve containerId to driveId if needed (PCF sends containerId as driveId)
        string resolvedDriveId;
        try
        {
            resolvedDriveId = await _speFileOperations.ResolveDriveIdAsync(request.DriveId, cancellationToken);
            if (resolvedDriveId != request.DriveId)
            {
                _logger.LogDebug("Resolved container {ContainerId} to drive {DriveId}",
                    request.DriveId, resolvedDriveId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve drive ID for {DriveId}", request.DriveId);
            _telemetry?.RecordFailure(stopwatch, "drive_resolution_failed", "batch");
            return AnalysisResult.Failed("Failed to resolve file storage location.");
        }

        // Step 1: Get file metadata for name
        var metadata = await _speFileOperations.GetFileMetadataAsync(
            resolvedDriveId, request.ItemId, cancellationToken);

        if (metadata == null)
        {
            _logger.LogWarning("File not found: DriveId={DriveId}, ItemId={ItemId}",
                resolvedDriveId, request.ItemId);
            _telemetry?.RecordFailure(stopwatch, "file_not_found", "batch");
            return AnalysisResult.Failed("File not found in SharePoint Embedded.");
        }

        var fileName = metadata.Name;
        fileType = Path.GetExtension(fileName)?.ToLowerInvariant();
        fileSize = metadata.Size;

        // Step 2: Download file content
        using var fileStream = await _speFileOperations.DownloadFileAsync(
            resolvedDriveId, request.ItemId, cancellationToken);

        if (fileStream == null)
        {
            _logger.LogWarning("Failed to download file: DriveId={DriveId}, ItemId={ItemId}",
                resolvedDriveId, request.ItemId);
            _telemetry?.RecordFailure(stopwatch, "file_download_failed", "batch");
            return AnalysisResult.Failed("Failed to download file content.");
        }

        // Step 3: Check extraction requirements (text vs vision)
        var extractionResult = await _textExtractor.ExtractAsync(fileStream, fileName, cancellationToken);
        extractionMethod = extractionResult.Method.ToTelemetryString();

        if (!extractionResult.Success)
        {
            _logger.LogWarning("Text extraction failed for {FileName}: {Error}",
                fileName, extractionResult.ErrorMessage);
            _telemetry?.RecordFailure(stopwatch, "extraction_failed", "batch", extractionMethod);
            return AnalysisResult.Failed(extractionResult.ErrorMessage ?? "Text extraction failed.");
        }

        // Step 4: Get completion (vision or text based on extraction result)
        string summary;
        try
        {
            if (extractionResult.IsVisionRequired)
            {
                // Handle image files with vision model
                _logger.LogDebug("Using vision model for image file {FileName}", fileName);

                // Reset stream position and read image bytes
                if (fileStream.CanSeek)
                {
                    fileStream.Position = 0;
                }
                var imageBytes = await ReadAllBytesAsync(fileStream, cancellationToken);
                var mediaType = GetMediaType(fileName);

                summary = await _openAiClient.GetVisionCompletionAsync(
                    _options.VisionPromptTemplate, imageBytes, mediaType, cancellationToken);
            }
            else
            {
                // Handle text-based documents
                _logger.LogDebug("Extracted {CharCount} characters from {FileName}",
                    extractionResult.CharacterCount, fileName);

                var prompt = BuildPrompt(extractionResult.Text!);
                summary = await _openAiClient.GetCompletionAsync(prompt, cancellationToken: cancellationToken);
            }
        }
        catch (RequestFailedException rfEx)
        {
            var (_, logMessage) = HandleOpenAiError(rfEx, request.DocumentId);
            _logger.LogError(rfEx, logMessage);
            _telemetry?.RecordFailure(stopwatch, GetErrorCode(rfEx), "batch", extractionMethod);
            throw MapToSummarizationException(rfEx);
        }
        catch (ClientResultException crEx)
        {
            var (_, logMessage) = HandleClientResultError(crEx, request.DocumentId);
            _logger.LogError(crEx, logMessage);
            _telemetry?.RecordFailure(stopwatch, "openai_client_error", "batch", extractionMethod);
            throw MapToSummarizationException(crEx);
        }
        catch (TaskCanceledException tcEx) when (tcEx.InnerException is TimeoutException)
        {
            _logger.LogError(tcEx, "OpenAI request timed out for document {DocumentId}", request.DocumentId);
            _telemetry?.RecordFailure(stopwatch, "openai_timeout", "batch", extractionMethod);
            throw SummarizationException.OpenAiTimeout(inner: tcEx);
        }

        _logger.LogInformation(
            "Non-streaming analysis completed for document {DocumentId}, SummaryLength={Length}",
            request.DocumentId, summary.Length);

        _telemetry?.RecordSuccess(stopwatch, "batch", extractionMethod, fileType, fileSize);

        // Parse structured result if enabled, otherwise return raw summary
        if (_options.StructuredOutputEnabled)
        {
            var structuredResult = ParseStructuredResponse(summary);

            // Include email metadata if this was an email file
            if (extractionResult.IsEmail)
            {
                structuredResult.EmailMetadata = extractionResult.EmailMetadata;
                _logger.LogDebug(
                    "Attached email metadata for document {DocumentId}: From={From}, Subject={Subject}, Attachments={AttachmentCount}",
                    request.DocumentId,
                    extractionResult.EmailMetadata?.From,
                    extractionResult.EmailMetadata?.Subject,
                    extractionResult.EmailMetadata?.Attachments.Count ?? 0);
            }

            _logger.LogDebug(
                "Parsed structured result for document {DocumentId}: ParsedSuccessfully={Parsed}, TlDrCount={TlDr}, EntitiesCount={Orgs}+{People}",
                request.DocumentId,
                structuredResult.ParsedSuccessfully,
                structuredResult.TlDr.Length,
                structuredResult.Entities.Organizations.Length,
                structuredResult.Entities.People.Length);

            return AnalysisResult.SucceededWithStructuredResult(
                structuredResult,
                extractionResult.CharacterCount,
                extractionResult.Method);
        }

        return AnalysisResult.Succeeded(summary, extractionResult.CharacterCount, extractionResult.Method);
    }

    /// <summary>
    /// Build the prompt for summarization using the configured template.
    /// Uses StructuredAnalysisPromptTemplate when StructuredOutputEnabled is true.
    /// </summary>
    private string BuildPrompt(string documentText)
    {
        var template = _options.StructuredOutputEnabled
            ? _options.StructuredAnalysisPromptTemplate
            : _options.SummarizePromptTemplate;

        return template.Replace("{documentText}", documentText);
    }

    /// <summary>
    /// JSON serializer options for parsing AI responses.
    /// Case-insensitive for flexibility with AI output variations.
    /// </summary>
    private static readonly JsonSerializerOptions JsonParseOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Parse the AI response as structured JSON into DocumentAnalysisResult.
    /// Falls back to using raw text as summary when JSON parsing fails.
    /// </summary>
    /// <param name="aiResponse">The raw AI response string.</param>
    /// <returns>DocumentAnalysisResult with ParsedSuccessfully indicating parse status.</returns>
    public DocumentAnalysisResult ParseStructuredResponse(string aiResponse)
    {
        if (string.IsNullOrWhiteSpace(aiResponse))
        {
            _logger.LogWarning("AI response is empty, returning fallback result");
            return DocumentAnalysisResult.Fallback(aiResponse, summary: string.Empty);
        }

        // Trim any leading/trailing whitespace and potential markdown code blocks
        var cleanedResponse = CleanJsonResponse(aiResponse);

        try
        {
            var result = JsonSerializer.Deserialize<DocumentAnalysisResult>(cleanedResponse, JsonParseOptions);

            if (result != null)
            {
                // Ensure all properties are initialized (AI might omit some)
                result.RawResponse = aiResponse;
                result.ParsedSuccessfully = true;
                result.Entities ??= new ExtractedEntities();
                result.TlDr ??= [];

                _logger.LogDebug(
                    "Successfully parsed structured AI response: Summary={SummaryLength}chars, TlDr={TlDrCount}items, Keywords={KeywordsLength}chars",
                    result.Summary?.Length ?? 0,
                    result.TlDr.Length,
                    result.Keywords?.Length ?? 0);

                return result;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Failed to parse AI response as JSON, falling back to raw text. Response preview: {Preview}",
                aiResponse.Length > 200 ? aiResponse[..200] + "..." : aiResponse);
        }

        // Fallback: use raw response as summary
        _logger.LogInformation("Using fallback mode: raw AI response will be used as summary");
        return DocumentAnalysisResult.Fallback(aiResponse, summary: aiResponse);
    }

    /// <summary>
    /// Clean the AI response to extract valid JSON.
    /// Handles common AI response patterns like markdown code blocks.
    /// </summary>
    private static string CleanJsonResponse(string response)
    {
        var trimmed = response.Trim();

        // Handle markdown code blocks: ```json ... ``` or ``` ... ```
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var startIndex = trimmed.IndexOf('\n');
            if (startIndex > 0)
            {
                var endIndex = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (endIndex > startIndex)
                {
                    trimmed = trimmed[(startIndex + 1)..endIndex].Trim();
                }
            }
        }

        return trimmed;
    }

    /// <summary>
    /// Read all bytes from a stream asynchronously.
    /// </summary>
    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);
        return memoryStream.ToArray();
    }

    /// <summary>
    /// Get the MIME media type for an image file based on extension.
    /// </summary>
    private static string GetMediaType(string fileName)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
        return extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Handle Azure RequestFailedException and return appropriate error chunk and log message.
    /// </summary>
    private static (AnalysisChunk errorChunk, string logMessage) HandleOpenAiError(
        RequestFailedException ex, Guid documentId)
    {
        return ex.Status switch
        {
            429 => (
                AnalysisChunk.FromError("The AI service is currently overloaded. Please try again later."),
                $"OpenAI rate limit exceeded for document {documentId}"),
            401 or 403 => (
                AnalysisChunk.FromError("AI service authentication failed."),
                $"OpenAI authentication error for document {documentId}"),
            400 when ex.Message.Contains("content_filter", StringComparison.OrdinalIgnoreCase) => (
                AnalysisChunk.FromError("The document content was blocked by the content safety system."),
                $"OpenAI content filter triggered for document {documentId}"),
            _ => (
                AnalysisChunk.FromError("An error occurred while generating the summary."),
                $"OpenAI API error (status {ex.Status}) for document {documentId}: {ex.Message}")
        };
    }

    /// <summary>
    /// Handle ClientResultException and return appropriate error chunk and log message.
    /// </summary>
    private static (AnalysisChunk errorChunk, string logMessage) HandleClientResultError(
        ClientResultException ex, Guid documentId)
    {
        // ClientResultException is thrown for non-HTTP errors from the OpenAI SDK
        var message = ex.Message;

        if (message.Contains("content_filter", StringComparison.OrdinalIgnoreCase))
        {
            return (
                AnalysisChunk.FromError("The document content was blocked by the content safety system."),
                $"OpenAI content filter triggered for document {documentId}");
        }

        return (
            AnalysisChunk.FromError("An error occurred while generating the summary."),
            $"OpenAI client error for document {documentId}: {message}");
    }

    /// <summary>
    /// Map Azure SDK exceptions to SummarizationException for non-streaming error handling.
    /// </summary>
    private static SummarizationException MapToSummarizationException(RequestFailedException ex)
    {
        return ex.Status switch
        {
            429 => SummarizationException.OpenAiRateLimit(
                retryAfterSeconds: TryParseRetryAfter(ex)),
            401 or 403 => SummarizationException.OpenAiError("Authentication failed", inner: ex),
            400 when ex.Message.Contains("content_filter", StringComparison.OrdinalIgnoreCase) =>
                SummarizationException.ContentFiltered(),
            _ => SummarizationException.OpenAiError(ex.Message, inner: ex)
        };
    }

    /// <summary>
    /// Map ClientResultException to SummarizationException for non-streaming error handling.
    /// </summary>
    private static SummarizationException MapToSummarizationException(ClientResultException ex)
    {
        if (ex.Message.Contains("content_filter", StringComparison.OrdinalIgnoreCase))
        {
            return SummarizationException.ContentFiltered();
        }

        return SummarizationException.OpenAiError(ex.Message, inner: ex);
    }

    /// <summary>
    /// Try to parse retry-after header from rate limit exception.
    /// </summary>
    private static int? TryParseRetryAfter(RequestFailedException ex)
    {
        // Azure OpenAI typically includes retry-after in the response headers
        // The exception message may contain hints about retry timing
        if (ex.Message.Contains("retry", StringComparison.OrdinalIgnoreCase))
        {
            // Default to 60 seconds if we can't parse specific value
            return 60;
        }
        return null;
    }

    /// <summary>
    /// Get error code for telemetry from RequestFailedException.
    /// </summary>
    private static string GetErrorCode(RequestFailedException ex) => ex.Status switch
    {
        429 => "openai_rate_limit",
        401 or 403 => "openai_auth_error",
        400 when ex.Message.Contains("content_filter", StringComparison.OrdinalIgnoreCase) => "openai_content_filter",
        _ => $"openai_error_{ex.Status}"
    };
}
