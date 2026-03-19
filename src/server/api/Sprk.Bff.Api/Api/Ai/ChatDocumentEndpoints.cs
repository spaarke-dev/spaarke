using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Document upload endpoints for SprkChat sessions.
///
/// Provides the <c>POST /api/ai/chat/sessions/{sessionId}/documents</c> endpoint
/// that accepts user-uploaded documents (PDF, DOCX, TXT, MD), extracts text via
/// Document Intelligence or native parsing, and stores extracted content in
/// session-scoped Redis for injection into conversation context.
///
/// Follows ADR-001 (Minimal API), ADR-008 (endpoint filters), ADR-009 (Redis),
/// ADR-015 (no document content logging), and ADR-016 (rate limiting).
/// </summary>
public static class ChatDocumentEndpoints
{
    /// <summary>
    /// Maximum upload file size: 50 MB.
    /// </summary>
    private const long MaxFileSizeBytes = 50L * 1024 * 1024;

    /// <summary>
    /// Session-scoped document storage TTL: 4 hours.
    /// Matches session lifetime per NFR-06. Redis key auto-expires when session ends.
    /// </summary>
    private static readonly TimeSpan UploadDocumentTtl = TimeSpan.FromHours(4);

    /// <summary>
    /// Document Intelligence processing timeout: 30 seconds.
    /// Graceful 504 returned if exceeded. NFR-02 target is 15s for docs under 50 pages.
    /// </summary>
    private const int ProcessingTimeoutSeconds = 30;

    /// <summary>
    /// Allowed file extensions for upload.
    /// </summary>
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".txt", ".md"
    };

    /// <summary>
    /// Allowed MIME types for upload validation.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ["application/pdf"],
        [".docx"] = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
        [".txt"] = ["text/plain"],
        [".md"] = ["text/plain", "text/markdown"]
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Registers chat document upload endpoints on the provided route builder.
    /// Called from <see cref="Infrastructure.DI.EndpointMappingExtensions"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapChatDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/chat")
            .RequireAuthorization()
            .WithTags("AI Chat Documents");

        // POST /api/ai/chat/sessions/{sessionId}/documents — upload a document
        group.MapPost("/sessions/{sessionId}/documents", UploadDocumentAsync)
            .AddAiAuthorizationFilter()
            .RequireRateLimiting("ai-upload")
            .DisableAntiforgery()
            .WithName("UploadChatDocument")
            .WithSummary("Upload a document for chat context injection")
            .WithDescription(
                "Accepts a PDF, DOCX, TXT, or MD file (max 50 MB), extracts text via Document Intelligence, " +
                "stores extracted content in session-scoped Redis, and returns document metadata. " +
                "The document content becomes available for AI reasoning within the chat session.")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<DocumentUploadResponse>(202)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(413)
            .ProducesProblem(422)
            .ProducesProblem(429)
            .ProducesProblem(500)
            .ProducesProblem(504);

        // POST /api/ai/chat/sessions/{sessionId}/documents/{documentId}/persist — save to SPE container
        group.MapPost("/sessions/{sessionId}/documents/{documentId}/persist", PersistDocumentAsync)
            .AddAiAuthorizationFilter()
            .RequireRateLimiting("ai-persist")
            .WithName("PersistChatDocument")
            .WithSummary("Save an uploaded document to the matter's SPE container")
            .WithDescription(
                "Persists a previously-uploaded chat document to the entity's SharePoint Embedded container. " +
                "The document remains available in session-scoped Redis for AI context. " +
                "Idempotent: calling again with the same documentId returns the existing SPE file metadata.")
            .Produces<SpeFilePersistResponse>(201)
            .Produces<SpeFilePersistResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(422)
            .ProducesProblem(429)
            .ProducesProblem(500);

        return app;
    }

    /// <summary>
    /// Handles document upload: validates, extracts text, stores in Redis, returns metadata.
    ///
    /// Pipeline:
    /// 1. Read multipart form data
    /// 2. Validate file type and size
    /// 3. Extract text via <see cref="ITextExtractor"/>
    /// 4. Store extracted text in Redis with session-scoped TTL
    /// 5. Return 202 Accepted with document metadata
    ///
    /// ADR-015: MUST NOT log extracted document text content.
    /// </summary>
    private static async Task<IResult> UploadDocumentAsync(
        string sessionId,
        HttpContext httpContext,
        ITextExtractor textExtractor,
        IDistributedCache cache,
        ChatSessionManager sessionManager,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Sprk.Bff.Api.Api.Ai.ChatDocumentEndpoints");

        // 1. Extract tenant ID from JWT claims (ADR-014: tenant-scoped keys)
        var tenantId = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();

        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Tenant identity not found in token claims");
        }

        // 2. Verify session exists
        var session = await sessionManager.GetSessionAsync(tenantId, sessionId, httpContext.RequestAborted);
        if (session == null)
        {
            return Results.Problem(
                statusCode: 404,
                title: "Not Found",
                detail: $"Chat session '{sessionId}' not found or has expired");
        }

        // 3. Read multipart form data
        if (!httpContext.Request.HasFormContentType)
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Request must be multipart/form-data with a 'file' field");
        }

        var form = await httpContext.Request.ReadFormAsync(httpContext.RequestAborted);
        var file = form.Files.GetFile("file");

        if (file == null || file.Length == 0)
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "No file provided. Include a 'file' field in the multipart form data.");
        }

        // 4. Determine filename (prefer form field, fall back to upload filename)
        var filename = form["filename"].FirstOrDefault()
            ?? file.FileName
            ?? "document";

        // 5. Validate file size (50 MB max)
        if (file.Length > MaxFileSizeBytes)
        {
            var sizeMb = file.Length / (1024.0 * 1024.0);
            logger.LogWarning(
                "Document upload rejected: file {Filename} exceeds size limit ({SizeMB:F1} MB > 50 MB) for session {SessionId}",
                filename, sizeMb, sessionId);

            return Results.Problem(
                statusCode: 413,
                title: "Request Entity Too Large",
                detail: $"File size ({sizeMb:F1} MB) exceeds the 50 MB limit");
        }

        // 6. Validate file extension
        var extension = Path.GetExtension(filename)?.ToLowerInvariant() ?? string.Empty;
        if (!AllowedExtensions.Contains(extension))
        {
            logger.LogWarning(
                "Document upload rejected: unsupported file type '{Extension}' for session {SessionId}",
                extension, sessionId);

            return Results.Problem(
                statusCode: 422,
                title: "Unprocessable Entity",
                detail: $"File type '{extension}' is not supported. Allowed types: PDF, DOCX, TXT, MD.");
        }

        // 7. Generate document ID
        var documentId = Guid.NewGuid().ToString("N");

        logger.LogInformation(
            "Processing document upload: DocumentId={DocumentId}, Filename={Filename}, " +
            "Size={SizeBytes} bytes, Extension={Extension}, SessionId={SessionId}",
            documentId, filename, file.Length, extension, sessionId);

        // 8. Read original binary into memory for both extraction and optional SPE persistence (R2-014)
        byte[] originalBinary;
        using (var memoryStream = new MemoryStream())
        {
            await using var rawStream = file.OpenReadStream();
            await rawStream.CopyToAsync(memoryStream, httpContext.RequestAborted);
            originalBinary = memoryStream.ToArray();
        }

        // 8a. Extract text via Document Intelligence / native parser
        TextExtractionResult extractionResult;
        try
        {
            using var timeoutCts = new CancellationTokenSource(
                TimeSpan.FromSeconds(ProcessingTimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                httpContext.RequestAborted, timeoutCts.Token);

            using var extractionStream = new MemoryStream(originalBinary);
            extractionResult = await textExtractor.ExtractAsync(
                extractionStream, filename, linkedCts.Token);
        }
        catch (OperationCanceledException) when (!httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(
                "Document Intelligence timed out after {TimeoutSeconds}s for DocumentId={DocumentId}, Filename={Filename}",
                ProcessingTimeoutSeconds, documentId, filename);

            return Results.Problem(
                statusCode: 504,
                title: "Gateway Timeout",
                detail: $"Document processing timed out after {ProcessingTimeoutSeconds} seconds. " +
                        "The document may be too large or complex. Please try a smaller file.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Text extraction failed for DocumentId={DocumentId}, Filename={Filename}",
                documentId, filename);

            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "An error occurred while extracting text from the document");
        }

        if (!extractionResult.Success || string.IsNullOrWhiteSpace(extractionResult.Text))
        {
            logger.LogWarning(
                "Text extraction returned no content for DocumentId={DocumentId}, Filename={Filename}, " +
                "Error={Error}",
                documentId, filename, extractionResult.ErrorMessage);

            return Results.Problem(
                statusCode: 422,
                title: "Unprocessable Entity",
                detail: extractionResult.ErrorMessage
                    ?? "No text could be extracted from the uploaded document");
        }

        // 9. Store extracted text in Redis with session-scoped TTL (ADR-009, NFR-06)
        // Key pattern: doc-upload:{sessionId}:{documentId}
        // ADR-015: Do NOT log extracted text content — only metadata
        var cacheKey = $"doc-upload:{sessionId}:{documentId}";
        var tokenEstimate = extractionResult.EstimatedTokenCount;
        var wasTruncated = tokenEstimate > DocumentContextService.MaxTokenBudget;

        try
        {
            var textBytes = Encoding.UTF8.GetBytes(extractionResult.Text);
            await cache.SetAsync(
                cacheKey,
                textBytes,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = UploadDocumentTtl
                },
                httpContext.RequestAborted);

            logger.LogInformation(
                "Stored uploaded document in Redis: Key={CacheKey}, TokenEstimate={TokenEstimate}, " +
                "CharCount={CharCount}, TTL={TtlHours}h",
                cacheKey, tokenEstimate, extractionResult.Text.Length, UploadDocumentTtl.TotalHours);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to store uploaded document in Redis: Key={CacheKey}, DocumentId={DocumentId}",
                cacheKey, documentId);

            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to store document content. Please try again.");
        }

        // 9b. Store original binary in Redis for optional SPE persistence (R2-014)
        // Key pattern: doc-binary:{sessionId}:{documentId}
        // ADR-015: Do NOT log binary content — only metadata
        var binaryCacheKey = $"doc-binary:{sessionId}:{documentId}";
        try
        {
            await cache.SetAsync(
                binaryCacheKey,
                originalBinary,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = UploadDocumentTtl
                },
                httpContext.RequestAborted);

            logger.LogInformation(
                "Stored original binary in Redis: Key={BinaryCacheKey}, SizeBytes={SizeBytes}, TTL={TtlHours}h",
                binaryCacheKey, originalBinary.Length, UploadDocumentTtl.TotalHours);
        }
        catch (Exception ex)
        {
            // Non-fatal: binary cache miss means SPE persist won't work, but AI context is still available
            logger.LogWarning(ex,
                "Failed to store original binary in Redis: Key={BinaryCacheKey}, DocumentId={DocumentId}",
                binaryCacheKey, documentId);
        }

        // 10. Also store document metadata for retrieval (filename, etc.)
        var metadataCacheKey = $"doc-upload-meta:{sessionId}:{documentId}";
        var metadata = new UploadedDocumentMetadata(documentId, filename, tokenEstimate, wasTruncated);
        try
        {
            var metadataJson = JsonSerializer.SerializeToUtf8Bytes(metadata, JsonOptions);
            await cache.SetAsync(
                metadataCacheKey,
                metadataJson,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = UploadDocumentTtl
                },
                httpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            // Non-fatal: metadata cache miss is recoverable, text is already stored
            logger.LogWarning(ex,
                "Failed to cache document metadata: Key={MetadataCacheKey}, DocumentId={DocumentId}",
                metadataCacheKey, documentId);
        }

        // 11. Return 202 Accepted with document metadata
        // Processing is synchronous in R2, so status is always "ready"
        var response = new DocumentUploadResponse(
            DocumentId: documentId,
            Filename: filename,
            Status: "ready",
            PageCount: 0, // Page count not available from simple extraction; 0 for native text
            TokenEstimate: tokenEstimate,
            WasTruncated: wasTruncated);

        logger.LogInformation(
            "Document upload complete: DocumentId={DocumentId}, Filename={Filename}, " +
            "Status=ready, TokenEstimate={TokenEstimate}, WasTruncated={WasTruncated}, SessionId={SessionId}",
            documentId, filename, tokenEstimate, wasTruncated, sessionId);

        return Results.Accepted(
            uri: $"/api/ai/chat/sessions/{sessionId}/documents/{documentId}",
            value: response);
    }

    /// <summary>
    /// Handles SPE persistence: retrieves original binary from Redis, uploads to SPE container.
    ///
    /// Pipeline:
    /// 1. Extract tenant ID and verify session ownership
    /// 2. Check idempotency marker (doc-persist:{sessionId}:{documentId})
    /// 3. Retrieve original binary from Redis (doc-binary:{sessionId}:{documentId})
    /// 4. Resolve SPE container ID from ChatHostContext or configuration fallback
    /// 5. Upload to SPE via SpeFileStore.UploadSmallAsUserAsync (ADR-007)
    /// 6. Store idempotency marker with SPE metadata
    /// 7. Return 201 Created with SPE file metadata
    ///
    /// ADR-007: MUST use SpeFileStore facade for all SPE writes.
    /// ADR-015: MUST NOT log document binary content.
    /// NFR-06: MUST NOT delete session-scoped Redis entry on persist.
    /// </summary>
    private static async Task<IResult> PersistDocumentAsync(
        string sessionId,
        string documentId,
        SpeFilePersistRequest? request,
        HttpContext httpContext,
        IDistributedCache cache,
        ChatSessionManager sessionManager,
        SpeFileStore speFileStore,
        IConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Sprk.Bff.Api.Api.Ai.ChatDocumentEndpoints");

        // 1. Extract tenant ID from JWT claims (ADR-014: tenant-scoped keys)
        var tenantId = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();

        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Tenant identity not found in token claims");
        }

        // 2. Verify session exists and caller owns it
        var session = await sessionManager.GetSessionAsync(tenantId, sessionId, httpContext.RequestAborted);
        if (session == null)
        {
            return Results.Problem(
                statusCode: 404,
                title: "Not Found",
                detail: $"Chat session '{sessionId}' not found or has expired");
        }

        // 3. Check idempotency: if already persisted, return existing metadata (200 OK)
        var persistKey = $"doc-persist:{sessionId}:{documentId}";
        try
        {
            var existingBytes = await cache.GetAsync(persistKey, httpContext.RequestAborted);
            if (existingBytes != null)
            {
                var existingResponse = JsonSerializer.Deserialize<SpeFilePersistResponse>(existingBytes, JsonOptions);
                if (existingResponse != null)
                {
                    logger.LogInformation(
                        "Document already persisted (idempotent): DocumentId={DocumentId}, SpeFileId={SpeFileId}, SessionId={SessionId}",
                        documentId, existingResponse.SpeFileId, sessionId);

                    return Results.Ok(existingResponse);
                }
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: if idempotency check fails, proceed with upload
            logger.LogWarning(ex,
                "Idempotency check failed for doc-persist:{SessionId}:{DocumentId} — proceeding with upload",
                sessionId, documentId);
        }

        // 4. Retrieve original binary from Redis
        var binaryCacheKey = $"doc-binary:{sessionId}:{documentId}";
        var binaryContent = await cache.GetAsync(binaryCacheKey, httpContext.RequestAborted);
        if (binaryContent == null || binaryContent.Length == 0)
        {
            logger.LogWarning(
                "Document binary not found in Redis: Key={BinaryCacheKey}, DocumentId={DocumentId}, SessionId={SessionId}",
                binaryCacheKey, documentId, sessionId);

            return Results.Problem(
                statusCode: 404,
                title: "Not Found",
                detail: $"Document '{documentId}' not found in session storage. It may have expired or was never uploaded.");
        }

        // 5. Retrieve document metadata for filename resolution
        var metadataCacheKey = $"doc-upload-meta:{sessionId}:{documentId}";
        string filename;
        string contentType;
        try
        {
            var metadataBytes = await cache.GetAsync(metadataCacheKey, httpContext.RequestAborted);
            if (metadataBytes != null)
            {
                var metadata = JsonSerializer.Deserialize<UploadedDocumentMetadata>(metadataBytes, JsonOptions);
                filename = request?.Filename ?? metadata?.Filename ?? "document";
            }
            else
            {
                filename = request?.Filename ?? "document";
            }
        }
        catch
        {
            filename = request?.Filename ?? "document";
        }

        // Determine content type from extension
        var extension = Path.GetExtension(filename)?.ToLowerInvariant() ?? string.Empty;
        contentType = extension switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            _ => "application/octet-stream"
        };

        // 6. Resolve SPE container ID from session's ChatHostContext or configuration fallback
        var containerId = ResolveContainerId(session, configuration);
        if (string.IsNullOrEmpty(containerId))
        {
            logger.LogWarning(
                "SPE persist failed: no SPE container resolvable for session {SessionId} (entity={EntityType}/{EntityId})",
                sessionId, session.HostContext?.EntityType, session.HostContext?.EntityId);

            return Results.Problem(
                statusCode: 422,
                title: "Unprocessable Entity",
                detail: "Cannot resolve SPE container for file upload. Ensure the chat session has a valid host context.");
        }

        logger.LogInformation(
            "Persisting document to SPE: DocumentId={DocumentId}, Filename={Filename}, " +
            "SizeBytes={SizeBytes}, ContainerId={ContainerId}, SessionId={SessionId}",
            documentId, filename, binaryContent.Length, containerId, sessionId);

        try
        {
            // 7. Resolve container to drive ID and upload via SpeFileStore (ADR-007)
            var driveId = await speFileStore.ResolveDriveIdAsync(containerId, httpContext.RequestAborted);

            using var uploadStream = new MemoryStream(binaryContent);
            var uploadPath = $"chat-uploads/{filename}";

            var uploadResult = await speFileStore.UploadSmallAsUserAsync(
                httpContext,
                driveId,
                uploadPath,
                uploadStream,
                httpContext.RequestAborted);

            if (uploadResult == null)
            {
                logger.LogError(
                    "SPE upload returned null for DocumentId={DocumentId}, ContainerId={ContainerId}",
                    documentId, containerId);

                return Results.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "Failed to upload document to SharePoint Embedded storage.");
            }

            // 8. Build response and store idempotency marker
            var speResponse = new SpeFilePersistResponse(
                SpeFileId: uploadResult.Id,
                Filename: filename,
                Url: uploadResult.WebUrl ?? $"spe://file/{uploadResult.Id}",
                SizeBytes: binaryContent.Length,
                UploadedAt: DateTimeOffset.UtcNow);

            // Store idempotency marker with same TTL as session (4 hours)
            try
            {
                var responseJson = JsonSerializer.SerializeToUtf8Bytes(speResponse, JsonOptions);
                await cache.SetAsync(
                    persistKey,
                    responseJson,
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = UploadDocumentTtl
                    },
                    httpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                // Non-fatal: upload succeeded but idempotency marker failed — next call may re-upload
                logger.LogWarning(ex,
                    "Failed to store idempotency marker: Key={PersistKey}, DocumentId={DocumentId}",
                    persistKey, documentId);
            }

            logger.LogInformation(
                "Document persisted to SPE: DocumentId={DocumentId}, SpeFileId={SpeFileId}, " +
                "Filename={Filename}, SizeBytes={SizeBytes}, SessionId={SessionId}",
                documentId, uploadResult.Id, filename, binaryContent.Length, sessionId);

            return Results.Created(
                uri: $"/api/ai/chat/sessions/{sessionId}/documents/{documentId}/persist",
                value: speResponse);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex,
                "SPE persist failed: container resolution error for DocumentId={DocumentId}, SessionId={SessionId}",
                documentId, sessionId);

            return Results.Problem(
                statusCode: 422,
                title: "Unprocessable Entity",
                detail: ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "SPE persist failed: unexpected error for DocumentId={DocumentId}, SessionId={SessionId}",
                documentId, sessionId);

            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "An error occurred while persisting the document to SharePoint Embedded storage.");
        }
    }

    // =========================================================================
    // Private Helpers
    // =========================================================================

    /// <summary>
    /// Resolves the SPE container ID for file persistence.
    /// Uses the staging container from configuration as the upload destination.
    /// Same resolution strategy as <see cref="ChatWordExportEndpoints"/>.
    /// </summary>
    private static string? ResolveContainerId(ChatSession session, IConfiguration configuration)
    {
        // Use the staging container (consistent with ChatWordExportEndpoints and MatterPreFillService pattern).
        var stagingContainerId = configuration["SharePointEmbedded:StagingContainerId"];
        if (!string.IsNullOrEmpty(stagingContainerId))
        {
            return stagingContainerId;
        }

        // Fallback: default container from email processing config
        var defaultContainerId = configuration["EmailProcessing:DefaultContainerId"];
        return defaultContainerId;
    }
}

/// <summary>
/// Internal metadata record for uploaded documents stored alongside the extracted text in Redis.
/// Used to recover document info when the session references the uploaded document.
/// </summary>
/// <param name="DocumentId">Generated GUID for the uploaded document.</param>
/// <param name="Filename">Original or user-supplied filename.</param>
/// <param name="TokenEstimate">Estimated token count of extracted text.</param>
/// <param name="WasTruncated">Whether the text exceeded the token budget.</param>
internal record UploadedDocumentMetadata(
    string DocumentId,
    string Filename,
    int TokenEstimate,
    bool WasTruncated);
