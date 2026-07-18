using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.EventRules;
using Sprk.Bff.Api.Services.Ai.Telemetry;

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

    // ITenantCache resources (FR-05 redis remediation r1). Final on-wire keys:
    //   spaarke:tenant:{tenantId}:doc-upload-text:{sessionId}:{documentId}:v1
    //   spaarke:tenant:{tenantId}:doc-upload-binary:{sessionId}:{documentId}:v1
    //   spaarke:tenant:{tenantId}:doc-upload-meta:{sessionId}:{documentId}:v1
    //   spaarke:tenant:{tenantId}:doc-upload-persist:{sessionId}:{documentId}:v1
    private const string DocTextResource = "doc-upload-text";
    private const string DocBinaryResource = "doc-upload-binary";
    private const string DocMetaResource = "doc-upload-meta";
    private const string DocPersistResource = "doc-upload-persist";
    private const int CacheVersion = 1;

    private static string DocCacheId(string sessionId, string documentId) => $"{sessionId}:{documentId}";

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

        // GET /api/ai/chat/sessions/{sessionId}/documents/{documentId}/content — return the
        // session document's ORIGINAL binary (spaarkeai-assistant-enhancements-r1 UAT W-2/W-5:
        // the create-flow "file leg"). A chat upload's binary is cached in session-scoped Redis
        // (DocBinaryResource, 4h TTL — same source PersistDocumentAsync uploads to SPE). The
        // launched Create-Matter wizard fetches it here, wraps it as a browser File, and runs it
        // through its EXISTING upload+link+index pipeline so the drafted-from file lands in the
        // NEW matter's SPE container + an sprk_document links it — no new create-write path.
        group.MapGet("/sessions/{sessionId}/documents/{documentId}/content", GetDocumentContentAsync)
            .AddAiAuthorizationFilter()
            .RequireRateLimiting("ai-context")
            .WithName("GetChatDocumentContent")
            .WithSummary("Download a session document's original binary (create-flow file hand-off)")
            .WithDescription(
                "Returns the original uploaded binary for a chat session document from session-scoped Redis " +
                "(the same cached bytes PersistDocumentAsync saves to SPE). Used by the Assistant create-flow " +
                "hand-off so a launched wizard can attach the drafted-from file to the new record. 404 when the " +
                "binary has expired (4h TTL) or the document/session is unknown.")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        // POST /api/ai/chat/sessions/{sessionId}/events/document-uploaded — the Event entry
        // path emission point (FR-P1-03, ai-architecture-redesign-r1 task 022). The client
        // signals batch completion here (per-file 202s above can't see batch boundaries or
        // typed-command context); the SERVER owns every routing + bounds decision.
        group.MapPost("/sessions/{sessionId}/events/document-uploaded", FireDocumentUploadedEventAsync)
            .AddAiAuthorizationFilter()
            .RequireRateLimiting("ai-context")
            .WithName("FireDocumentUploadedEvent")
            .WithSummary("Fire the document_uploaded event rule for a completed upload batch (Event path, FR-P1-03)")
            .WithDescription(
                "Resolves the document_uploaded event rule from sprk_playbookconsumer.sprk_oneventbindings " +
                "(classify(1) → summarize(2)) and streams ChatSseEvent SSE items: event_classification, " +
                "event_output / event_confirmation (M4 policy), chips, done — or a graceful event_notice when a " +
                "FR-P1-03 bound (daily cap, opt-out, explicit-command supersede) or the empty-attachments " +
                "precondition denies the run. Every output is ledger-written before it renders (ADR-040).")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(429)
            .ProducesProblem(500)
            .ProducesProblem(503);

        // GET/PUT /api/ai/chat/event-rules/opt-out — the per-user Event-path opt-out
        // (FR-P1-03 bound b). Preference routes: token auth only (no AI resource filter);
        // backed by IEventPathUserState (unconditionally registered — no AI dependency).
        group.MapGet("/event-rules/opt-out", GetEventRulesOptOutAsync)
            .WithName("GetEventRulesOptOut")
            .WithSummary("Read the caller's Event-path auto-analysis opt-out state")
            .Produces<EventRulesOptOutResponse>(200)
            .ProducesProblem(401);
        group.MapPut("/event-rules/opt-out", SetEventRulesOptOutAsync)
            .WithName("SetEventRulesOptOut")
            .WithSummary("Set the caller's Event-path auto-analysis opt-out state")
            .Produces<EventRulesOptOutResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(401);

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
        ITenantCache cache,
        ChatSessionManager sessionManager,
        IContextEventEmitter contextEventEmitter,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Sprk.Bff.Api.Api.Ai.ChatDocumentEndpoints");

        // chat-routing-redesign-r1 task 074 — total-pipeline stopwatch for context.upload_completed.
        // Started AFTER validation passes (after tenantId + session checks) so durationMs reflects
        // actual pipeline work, not request entry. ADR-015 binding: numeric metric only.
        var pipelineStopwatch = Stopwatch.StartNew();
        var sessionGuidForEmit = Guid.TryParse(sessionId, out var parsedSessionGuid) ? parsedSessionGuid : (Guid?)null;

        // 1. Extract tenant ID from JWT claims (ADR-014: tenant-scoped keys).
        // Microsoft.Identity.Web's JwtBearer middleware may rename `tid` to the schema URL
        // form depending on Microsoft.IdentityModel.Tokens.DefaultInboundClaimTypeMap state.
        // Check both forms to match the pattern used by ChatEndpoints.cs and
        // SummarizeSessionEndpoint.cs (R5). Fallback to X-Tenant-Id header for
        // server-to-server scenarios that bypass JWT.
        var tenantId = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
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

        // 2a. R5 task 032 — defense-in-depth NFR-02 per-session cap (20 files).
        // Frontend enforces too (chat composer + slash command); this mirrors
        // SummarizeSessionEndpoint's pattern (ADR-019: stable errorCode `summarize.too-many-files`).
        // Reject BEFORE reading the multipart form, extracting text, or writing any Redis state
        // so the session manifest is NOT mutated when the 21st upload arrives.
        var existingFileCount = session.UploadedFiles?.Count ?? 0;
        if (existingFileCount >= ChatSession.MaxUploadedFiles)
        {
            logger.LogWarning(
                "Document upload rejected: session {SessionId} already has {ExistingFileCount} files (cap={MaxFiles}) — NFR-02 defense-in-depth",
                sessionId, existingFileCount, ChatSession.MaxUploadedFiles);

            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: $"This chat session already has {existingFileCount} uploaded files. The per-session cap is {ChatSession.MaxUploadedFiles}.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "summarize.too-many-files"
                });
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

        // chat-routing-redesign-r1 task 074 — emit context.upload_started.
        // ADR-015 Tier 1 SAFE: deterministic IDs + contentType + numeric size only.
        // Resolve contentType from extension (mirrors the switch later in step 10a) so the
        // started-event carries the same MIME enum string as downstream events.
        var startedContentType = extension switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            _ => file.ContentType ?? "application/octet-stream"
        };
        contextEventEmitter.UploadStarted(
            sessionId: sessionGuidForEmit,
            fileId: documentId,
            contentType: startedContentType,
            fileSizeBytes: file.Length,
            tenantId: tenantId);

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
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 2 (D-09 §2 L4): NullTextExtractor surfaced.
            logger.LogDebug(
                "Document upload text extraction called while AI feature disabled. ErrorCode={ErrorCode}, DocumentId={DocumentId}",
                ex.ErrorCode, documentId);
            return ex.AsFeatureDisabled503();
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

        // 9. Store extracted text in Redis with session-scoped TTL (ADR-009, NFR-06 + FR-05)
        // ADR-015: Do NOT log extracted text content — only metadata
        var docCacheId = DocCacheId(sessionId, documentId);
        var tokenEstimate = extractionResult.EstimatedTokenCount;
        var wasTruncated = tokenEstimate > DocumentContextService.MaxTokenBudget;

        try
        {
            await cache.SetAsync(
                tenantId, DocTextResource, docCacheId, CacheVersion,
                extractionResult.Text, UploadDocumentTtl, ct: httpContext.RequestAborted);

            logger.LogInformation(
                "Stored uploaded document in Redis: CacheId={CacheId}, TokenEstimate={TokenEstimate}, " +
                "CharCount={CharCount}, TTL={TtlHours}h",
                docCacheId, tokenEstimate, extractionResult.Text.Length, UploadDocumentTtl.TotalHours);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to store uploaded document in Redis: CacheId={CacheId}, DocumentId={DocumentId}",
                docCacheId, documentId);

            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to store document content. Please try again.");
        }

        // 9b. Store original binary in Redis for optional SPE persistence (R2-014)
        // ADR-015: Do NOT log binary content — only metadata
        try
        {
            await cache.SetAsync(
                tenantId, DocBinaryResource, docCacheId, CacheVersion,
                originalBinary, UploadDocumentTtl, ct: httpContext.RequestAborted);

            logger.LogInformation(
                "Stored original binary in Redis: CacheId={CacheId}, SizeBytes={SizeBytes}, TTL={TtlHours}h",
                docCacheId, originalBinary.Length, UploadDocumentTtl.TotalHours);
        }
        catch (Exception ex)
        {
            // Non-fatal: binary cache miss means SPE persist won't work, but AI context is still available
            logger.LogWarning(ex,
                "Failed to store original binary in Redis: CacheId={CacheId}, DocumentId={DocumentId}",
                docCacheId, documentId);
        }

        // 10. Also store document metadata for retrieval (filename, etc.)
        var metadata = new UploadedDocumentMetadata(documentId, filename, tokenEstimate, wasTruncated);
        try
        {
            await cache.SetAsync(
                tenantId, DocMetaResource, docCacheId, CacheVersion,
                metadata, UploadDocumentTtl, ct: httpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            // Non-fatal: metadata cache miss is recoverable, text is already stored
            logger.LogWarning(ex,
                "Failed to cache document metadata: CacheId={CacheId}, DocumentId={DocumentId}",
                docCacheId, documentId);
        }

        // 10a. R5 task 032 — wire upload into the session-files RAG index + ChatSession manifest.
        // This is the integration that ties the legacy R3 upload endpoint (Redis-only storage above)
        // into R5's chat-driven Summarize vertical:
        //   - IndexSessionFileAsync writes chunks into the `spaarke-session-files` AI Search index
        //     (carries tenantId + sessionId per ADR-014 / R5 FR-09).
        //   - ChatSession.UploadedFiles gains a new ChatSessionFile entry so
        //     the summarize dispatch path (task 012) sees the file via
        //     session.UploadedFiles instead of declining.
        //   - UpdateSessionCacheAsync persists the manifest through Redis hot tier
        //     + fire-and-forget Cosmos warm tier (decision D-06).
        //
        // RagIndexingPipeline is conditionally registered (DocumentIntelligence:Enabled gate);
        // resolve defensively so the endpoint preserves its existing AI-off behavior:
        // ITextExtractor above already throws FeatureDisabledException when AI is off (step 8a),
        // so this code is unreachable in practice when AI is off. The defensive GetService
        // call protects against the unlikely case where ITextExtractor is real (DocIntel on)
        // but RagIndexingPipeline failed to register for an unrelated reason — we still want
        // the Redis writes to succeed and the file to be discoverable via the legacy path.
        var ragIndexingPipeline = httpContext.RequestServices.GetService<RagIndexingPipeline>();
        if (ragIndexingPipeline is null)
        {
            // AI off (or RagIndexingPipeline missing) — log and continue. The endpoint
            // returned 503 earlier via the ITextExtractor catch path if AI is truly off;
            // reaching here without a pipeline indicates a configuration anomaly.
            logger.LogWarning(
                "RagIndexingPipeline unavailable — skipping session-files indexing for DocumentId={DocumentId}, SessionId={SessionId}. " +
                "Legacy Redis storage succeeded; the summarize dispatch path will not see this file.",
                documentId, sessionId);
        }
        else
        {
            try
            {
                // Build ParsedDocument from the extracted text. The pipeline does not need
                // page count or tables for session-files indexing (single-granularity chunking
                // per knowledge profile; per RagIndexingPipeline docstring lines 233-237).
                var parsedDocument = new ParsedDocument
                {
                    Text = extractionResult.Text!,
                    Pages = 0,
                    ExtractedAt = DateTimeOffset.UtcNow,
                    // ParserUsed defaults to DocumentIntelligence; ITextExtractor produced the
                    // text via either DocIntel or native parsing — telemetry-only field.
                };

                // ADR-014: tenantId + sessionId BOTH set so the session-files index entries
                // carry both partition keys. Pass documentId as both documentId and speFileId
                // (the latter is a "best effort" link for future SPE persistence; for
                // session-uploads we use the chat-document GUID as both per task POML §3.1).
                //
                // chat-routing-redesign-r1 task 074 — indexing stopwatch for context.upload_indexed.
                var indexingStopwatch = Stopwatch.StartNew();
                var indexingResult = await ragIndexingPipeline.IndexSessionFileAsync(
                    document: parsedDocument,
                    documentId: documentId,
                    tenantId: tenantId,
                    sessionId: sessionId,
                    fileName: filename,
                    speFileId: documentId,
                    cancellationToken: httpContext.RequestAborted);
                indexingStopwatch.Stop();

                // chat-routing-redesign-r1 task 074 — emit context.upload_indexed.
                // ADR-015 Tier 1 SAFE: chunkCount + durationMs only.
                contextEventEmitter.UploadIndexed(
                    sessionId: sessionGuidForEmit,
                    fileId: documentId,
                    chunkCount: indexingResult.KnowledgeChunksIndexed,
                    durationMs: indexingStopwatch.ElapsedMilliseconds,
                    tenantId: tenantId);

                // Reconstruct chunk IDs from the deterministic pattern used by
                // BuildKnowledgeDocuments (chunkIdSuffix "s" for session-files, per
                // RagIndexingPipeline.cs line 332 + line 448): "{documentId}_s_{index}".
                // SearchDocumentIdsCsv is consumed by the session-files cleanup job (task 007)
                // to enumerate index documents for deletion on session end.
                var chunkCount = indexingResult.KnowledgeChunksIndexed;
                var chunkIds = Enumerable.Range(0, chunkCount)
                    .Select(i => $"{documentId}_s_{i}")
                    .ToArray();
                var searchDocumentIdsCsv = string.Join(",", chunkIds);

                // Determine content type from filename extension (mirrors PersistDocumentAsync §5).
                var sessionFileContentType = extension switch
                {
                    ".pdf" => "application/pdf",
                    ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    ".txt" => "text/plain",
                    ".md" => "text/markdown",
                    _ => file.ContentType ?? "application/octet-stream"
                };

                // Build the ChatSessionFile manifest entry (six fields per ChatSession.cs §134).
                //
                // R7 Wave 12.3 Phase 12.3a UAT fix (2026-07-03): also persist ExtractedText
                // (the same text just fed into the RAG indexing pipeline). This closes the
                // Azure AI Search index-catchup race that caused "session files contained no
                // text to summarize" errors when users typed "summarize this document" within
                // seconds of upload. SessionFileTextSource reads this directly — no RAG hop.
                var newFile = new ChatSessionFile(
                    FileId: documentId,
                    FileName: filename,
                    ContentType: sessionFileContentType,
                    SizeBytes: file.Length,
                    SearchDocumentIdsCsv: searchDocumentIdsCsv,
                    UploadedAt: DateTimeOffset.UtcNow)
                {
                    ExtractedText = extractionResult.Text,
                };

                // Append to UploadedFiles (immutable record — use `with` expression).
                var updatedFiles = (session.UploadedFiles ?? Array.Empty<ChatSessionFile>())
                    .Append(newFile)
                    .ToList();

                // Wave 2 (UAT-R3 Test #2 fix): a chat upload also becomes the session's ACTIVE
                // document — most-recent-upload-wins. This is the "which document is the user acting
                // on?" fact SendWorkspaceArtifactHandler.ResolveActiveDocumentAsync reads when the LLM
                // supplies no explicit pointer, so "open this file" mounts the just-uploaded file
                // (source=session-upload → compose.upload seed) instead of a blank Compose tab.
                // Pointer only (ADR-015 Tier 3 identifiers): SessionFileId is the same GUID used as the
                // ChatSessionFile.FileId / doc-upload cache documentId. DocumentSessionId is left null
                // (fail-soft) — a later compose active-document registration fills it if one is created.
                var activeDocument = new ActiveDocumentIdentity(
                    Source: ActiveDocumentIdentity.SourceSessionUpload,
                    SessionFileId: documentId,
                    FileName: filename,
                    RegisteredAt: DateTimeOffset.UtcNow);

                var updatedSession = session with
                {
                    UploadedFiles = updatedFiles,
                    ActiveDocument = activeDocument,
                };

                // Persist via UpdateSessionCacheAsync (decision D-06: Redis hot tier +
                // fire-and-forget Cosmos write-through). Internal virtual; same-assembly access.
                await sessionManager.UpdateSessionCacheAsync(updatedSession, httpContext.RequestAborted);

                logger.LogInformation(
                    "R5 session-files indexing + manifest update complete: DocumentId={DocumentId}, " +
                    "ChunkCount={ChunkCount}, DurationMs={DurationMs}, SessionId={SessionId}, " +
                    "ManifestSize={ManifestSize}",
                    documentId, chunkCount, indexingResult.DurationMs, sessionId, updatedFiles.Count);
            }
            catch (FeatureDisabledException ex)
            {
                // RagIndexingPipeline downstream surfaced a kill-switch (e.g., NullRagService
                // via the AI-Search-keys-missing sub-gate). Mirror the ITextExtractor catch
                // pattern at step 8a — return 503 ProblemDetails.
                logger.LogDebug(
                    "Session-files indexing called while RAG feature disabled. ErrorCode={ErrorCode}, DocumentId={DocumentId}",
                    ex.ErrorCode, documentId);
                return ex.AsFeatureDisabled503();
            }
            catch (Exception ex)
            {
                // Non-fatal: legacy Redis writes already succeeded. Log the failure and let
                // the 202 response proceed — the file is at least discoverable via the
                // R3 doc-upload Redis path, even if Summarize can't find it. This preserves
                // back-compat for any consumer still on the legacy path.
                logger.LogError(ex,
                    "R5 session-files indexing OR manifest update failed for DocumentId={DocumentId}, SessionId={SessionId}. " +
                    "Legacy Redis writes succeeded; the summarize dispatch path will not see this file in UploadedFiles.",
                    documentId, sessionId);
            }
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

        // chat-routing-redesign-r1 task 074 — emit context.upload_completed (end-of-pipeline).
        // ADR-015 Tier 1 SAFE: totalDurationMs (numeric) + IDs only — never filename, content, token text.
        pipelineStopwatch.Stop();
        contextEventEmitter.UploadCompleted(
            sessionId: sessionGuidForEmit,
            fileId: documentId,
            totalDurationMs: pipelineStopwatch.ElapsedMilliseconds,
            tenantId: tenantId);

        return Results.Accepted(
            uri: $"/api/ai/chat/sessions/{sessionId}/documents/{documentId}",
            value: response);
    }

    /// <summary>
    /// GET the original binary for a chat session document (spaarkeai-assistant-enhancements-r1
    /// UAT W-2/W-5 — the create-flow "file leg"). Reads the SAME session-scoped Redis binary
    /// (<see cref="DocBinaryResource"/>) that <see cref="PersistDocumentAsync"/> uploads to SPE, so a
    /// launched wizard can attach the drafted-from file to the new record via its own upload pipeline.
    /// ADR-015: never logs binary content. NFR-06: read-only — never deletes the session entry.
    /// </summary>
    private static async Task<IResult> GetDocumentContentAsync(
        string sessionId,
        string documentId,
        HttpContext httpContext,
        ITenantCache cache,
        ChatSessionManager sessionManager,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Sprk.Bff.Api.Api.Ai.ChatDocumentEndpoints");

        var tenantId = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
            ?? httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();

        if (string.IsNullOrEmpty(tenantId))
        {
            return Results.Problem(statusCode: 401, title: "Unauthorized", detail: "Tenant identity not found in token claims");
        }

        // Verify the session exists + is owned by the caller (the AddAiAuthorizationFilter already
        // enforced AI access; this confirms the session scope for the tenant-keyed cache read).
        var session = await sessionManager.GetSessionAsync(tenantId, sessionId, httpContext.RequestAborted);
        if (session == null)
        {
            return Results.Problem(statusCode: 404, title: "Not Found", detail: $"Chat session '{sessionId}' not found or has expired");
        }

        var docCacheId = DocCacheId(sessionId, documentId);

        var binaryContent = await cache.GetAsync<byte[]>(
            tenantId, DocBinaryResource, docCacheId, CacheVersion, ct: httpContext.RequestAborted);
        if (binaryContent == null || binaryContent.Length == 0)
        {
            return Results.Problem(
                statusCode: 404,
                title: "Not Found",
                detail: $"Document '{documentId}' binary not found in session storage. It may have expired (4h) or was never uploaded.");
        }

        // Filename + content-type from the cached metadata (best-effort; the filename drives the
        // downloaded File's name so the wizard shows the real document name).
        string filename;
        try
        {
            var metadata = await cache.GetAsync<UploadedDocumentMetadata>(
                tenantId, DocMetaResource, docCacheId, CacheVersion, ct: httpContext.RequestAborted);
            filename = metadata?.Filename ?? "document";
        }
        catch
        {
            filename = "document";
        }

        var extension = Path.GetExtension(filename)?.ToLowerInvariant() ?? string.Empty;
        var contentType = extension switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            _ => "application/octet-stream"
        };

        logger.LogInformation(
            "Returning session document binary: DocumentId={DocumentId}, Filename={Filename}, SizeBytes={SizeBytes}, SessionId={SessionId}",
            documentId, filename, binaryContent.Length, sessionId);

        return Results.File(binaryContent, contentType, fileDownloadName: filename);
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
        ITenantCache cache,
        ChatSessionManager sessionManager,
        SpeFileStore speFileStore,
        IConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Sprk.Bff.Api.Api.Ai.ChatDocumentEndpoints");

        // 1. Extract tenant ID from JWT claims (ADR-014: tenant-scoped keys).
        // Microsoft.Identity.Web's JwtBearer middleware may rename `tid` to the schema URL
        // form depending on Microsoft.IdentityModel.Tokens.DefaultInboundClaimTypeMap state.
        // Check both forms to match the pattern used by ChatEndpoints.cs and
        // SummarizeSessionEndpoint.cs (R5). Fallback to X-Tenant-Id header for
        // server-to-server scenarios that bypass JWT.
        var tenantId = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
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
        var docCacheId = DocCacheId(sessionId, documentId);
        try
        {
            var existingResponse = await cache.GetAsync<SpeFilePersistResponse>(
                tenantId, DocPersistResource, docCacheId, CacheVersion, ct: httpContext.RequestAborted);
            if (existingResponse != null)
            {
                logger.LogInformation(
                    "Document already persisted (idempotent): DocumentId={DocumentId}, SpeFileId={SpeFileId}, SessionId={SessionId}",
                    documentId, existingResponse.SpeFileId, sessionId);

                return Results.Ok(existingResponse);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: if idempotency check fails, proceed with upload
            logger.LogWarning(ex,
                "Idempotency check failed for doc-persist {SessionId}:{DocumentId} — proceeding with upload",
                sessionId, documentId);
        }

        // 4. Retrieve original binary from Redis
        var binaryContent = await cache.GetAsync<byte[]>(
            tenantId, DocBinaryResource, docCacheId, CacheVersion, ct: httpContext.RequestAborted);
        if (binaryContent == null || binaryContent.Length == 0)
        {
            logger.LogWarning(
                "Document binary not found in Redis: CacheId={CacheId}, DocumentId={DocumentId}, SessionId={SessionId}",
                docCacheId, documentId, sessionId);

            return Results.Problem(
                statusCode: 404,
                title: "Not Found",
                detail: $"Document '{documentId}' not found in session storage. It may have expired or was never uploaded.");
        }

        // 5. Retrieve document metadata for filename resolution
        string filename;
        string contentType;
        try
        {
            var metadata = await cache.GetAsync<UploadedDocumentMetadata>(
                tenantId, DocMetaResource, docCacheId, CacheVersion, ct: httpContext.RequestAborted);
            filename = request?.Filename ?? metadata?.Filename ?? "document";
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

            // Post-upload RAG indexing for chat-persisted documents is triggered client-side
            // via `@spaarke/sdap-client.SdapApiClient.indexFile()` — see project
            // `sdap-client-shared-library-fix-r1`. Re-wiring inline indexing here is tracked
            // as future work once the SprkChat surface adopts `@spaarke/sdap-client`.

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
                await cache.SetAsync(
                    tenantId, DocPersistResource, docCacheId, CacheVersion,
                    speResponse, UploadDocumentTtl, ct: httpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                // Non-fatal: upload succeeded but idempotency marker failed — next call may re-upload
                logger.LogWarning(ex,
                    "Failed to store idempotency marker: CacheId={CacheId}, DocumentId={DocumentId}",
                    docCacheId, documentId);
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
    // Event path (FR-P1-03, ai-architecture-redesign-r1 task 022)
    // =========================================================================

    /// <summary>
    /// Handler for <c>POST /api/ai/chat/sessions/{sessionId}/events/document-uploaded</c>.
    /// Thin SSE writer over <see cref="IEventRulesService.FireAsync"/> — no routing or
    /// bounds logic lives here (ADR-039: the Binding table routes; the Event Rules
    /// service enforces bounds server-side regardless of what the client sends).
    /// SSE probe-then-stream pattern mirrors <see cref="SummarizeSessionEndpoint"/>.
    /// </summary>
    private static async Task FireDocumentUploadedEventAsync(
        string sessionId,
        [Microsoft.AspNetCore.Mvc.FromBody] DocumentUploadedEventRequest? body,
        HttpContext httpContext,
        IEventRulesService eventRules,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Sprk.Bff.Api.Api.Ai.ChatDocumentEndpoints");
        var cancellationToken = httpContext.RequestAborted;
        var response = httpContext.Response;
        var correlationId = httpContext.TraceIdentifier;

        // ─── Pre-stream validation (ProblemDetails, ADR-019) ────────────────────────
        if (string.IsNullOrWhiteSpace(sessionId) || !Guid.TryParse(sessionId, out _))
        {
            await WriteEventProblemAsync(response, 400, "Bad Request",
                "'sessionId' must be a valid GUID.", "sessionId.invalid", correlationId, cancellationToken);
            return;
        }

        var tenantId = GetTenantIdClaim(httpContext);
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            await WriteEventProblemAsync(response, 401, "Unauthorized",
                "Tenant identity ('tid' claim) not found in authentication token.",
                "auth.tid-missing", correlationId, cancellationToken);
            return;
        }

        var userOid = GetUserOidClaim(httpContext);
        if (string.IsNullOrWhiteSpace(userOid))
        {
            await WriteEventProblemAsync(response, 401, "Unauthorized",
                "User identity ('oid' claim) not found in authentication token.",
                "auth.oid-missing", correlationId, cancellationToken);
            return;
        }

        var request = new SurfaceEventRequest(
            EventName: SurfaceEventNames.DocumentUploaded,
            TenantId: tenantId,
            SessionId: sessionId,
            UserOid: userOid,
            FileIds: body?.FileIds,
            TypedCommand: body?.TypedCommand,
            CorrelationId: correlationId);

        logger.LogInformation(
            "[EVENT-RULES] document_uploaded fired. tenant={TenantId} session={SessionId} oid={Oid} " +
            "fileIds={FileIdCount} typedCommand={HasTypedCommand} correlationId={CorrelationId}",
            tenantId, sessionId, userOid, body?.FileIds?.Count ?? 0,
            !string.IsNullOrWhiteSpace(body?.TypedCommand), correlationId);

        // ─── Probe the first item BEFORE setting SSE headers (early-failure → ProblemDetails)
        IAsyncEnumerator<ChatSseEvent>? enumerator = null;
        var hasFirst = false;
        ChatSseEvent first = default!;
        ExceptionDispatchInfo? earlyFailure = null;
        try
        {
            enumerator = eventRules.FireAsync(request, cancellationToken).GetAsyncEnumerator(cancellationToken);
            hasFirst = await enumerator.MoveNextAsync().ConfigureAwait(false);
            if (hasFirst)
            {
                first = enumerator.Current;
            }
        }
        catch (OperationCanceledException)
        {
            if (enumerator is not null)
            {
                try { await enumerator.DisposeAsync().ConfigureAwait(false); } catch { /* cleanup-tail */ }
            }
            return;
        }
        catch (Exception ex)
        {
            earlyFailure = ExceptionDispatchInfo.Capture(ex);
        }

        if (earlyFailure is not null)
        {
            if (enumerator is not null)
            {
                try { await enumerator.DisposeAsync().ConfigureAwait(false); } catch { /* cleanup-tail */ }
            }

            switch (earlyFailure.SourceException)
            {
                case FeatureDisabledException fde:
                    logger.LogDebug(
                        "[EVENT-RULES] Feature disabled. errorCode={ErrorCode} tenant={TenantId} session={SessionId}",
                        fde.ErrorCode, tenantId, sessionId);
                    await fde.AsFeatureDisabled503().ExecuteAsync(httpContext);
                    return;
                case InvalidOperationException ioe when ioe.Message.Contains("not found", StringComparison.OrdinalIgnoreCase):
                    await WriteEventProblemAsync(response, 404, "Not Found",
                        "The chat session was not found.", "event-rules.session-not-found",
                        correlationId, cancellationToken);
                    return;
                case ArgumentException:
                    logger.LogWarning(earlyFailure.SourceException,
                        "[EVENT-RULES] Request validation failed tenant={TenantId} session={SessionId}",
                        tenantId, sessionId);
                    await WriteEventProblemAsync(response, 400, "Bad Request",
                        "document_uploaded event request validation failed.",
                        "event-rules.invalid-request", correlationId, cancellationToken);
                    return;
                default:
                    logger.LogError(earlyFailure.SourceException,
                        "[EVENT-RULES] Event rule failed (pre-stream) tenant={TenantId} session={SessionId}",
                        tenantId, sessionId);
                    await WriteEventProblemAsync(response, 500, "Internal Server Error",
                        "Failed to start the event stream. See server logs for details.",
                        "event-rules.internal-error", correlationId, cancellationToken);
                    return;
            }
        }

        // ─── SSE headers + stream (matches SummarizeSessionEndpoint / ChatEndpoints) ──
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            if (hasFirst)
            {
                await WriteEventSseAsync(response, first, cancellationToken);
            }
            while (enumerator is not null)
            {
                bool moveNext;
                try
                {
                    moveNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Never let the SSE stream die without a terminal marker.
                    logger.LogError(ex,
                        "[EVENT-RULES] Mid-stream exception tenant={TenantId} session={SessionId}",
                        tenantId, sessionId);
                    try
                    {
                        await WriteEventSseAsync(response,
                            new ChatSseEvent("error", "The automatic analysis stream was interrupted."),
                            CancellationToken.None);
                    }
                    catch { /* response may be unwritable */ }
                    break;
                }

                if (!moveNext) break;
                await WriteEventSseAsync(response, enumerator.Current, cancellationToken);
            }
        }
        finally
        {
            if (enumerator is not null)
            {
                try { await enumerator.DisposeAsync().ConfigureAwait(false); } catch { /* cleanup-tail */ }
            }
        }
    }

    /// <summary>GET handler — the caller's Event-path opt-out state.</summary>
    private static async Task<IResult> GetEventRulesOptOutAsync(
        HttpContext httpContext,
        IEventPathUserState userState)
    {
        var tenantId = GetTenantIdClaim(httpContext);
        var userOid = GetUserOidClaim(httpContext);
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(userOid))
        {
            return Results.Problem(statusCode: 401, title: "Unauthorized",
                detail: "Tenant or user identity not found in token claims");
        }

        var optedOut = await userState.IsOptedOutAsync(tenantId, userOid, httpContext.RequestAborted);
        return Results.Ok(new EventRulesOptOutResponse(optedOut));
    }

    /// <summary>PUT handler — sets the caller's Event-path opt-out state.</summary>
    private static async Task<IResult> SetEventRulesOptOutAsync(
        [Microsoft.AspNetCore.Mvc.FromBody] EventRulesOptOutRequest? body,
        HttpContext httpContext,
        IEventPathUserState userState)
    {
        if (body is null)
        {
            return Results.Problem(statusCode: 400, title: "Bad Request",
                detail: "Request body { optedOut: boolean } is required.");
        }

        var tenantId = GetTenantIdClaim(httpContext);
        var userOid = GetUserOidClaim(httpContext);
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(userOid))
        {
            return Results.Problem(statusCode: 401, title: "Unauthorized",
                detail: "Tenant or user identity not found in token claims");
        }

        await userState.SetOptOutAsync(tenantId, userOid, body.OptedOut, httpContext.RequestAborted);
        return Results.Ok(new EventRulesOptOutResponse(body.OptedOut));
    }

    /// <summary>Tenant claim extraction — same dual-form pattern as UploadDocumentAsync.</summary>
    private static string? GetTenantIdClaim(HttpContext httpContext) =>
        httpContext.User.FindFirst("tid")?.Value
        ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
        ?? httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();

    /// <summary>User oid claim extraction — same dual-form pattern as SummarizeSessionEndpoint.</summary>
    private static string? GetUserOidClaim(HttpContext httpContext) =>
        httpContext.User.FindFirst("oid")?.Value
        ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
        ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    /// <summary>SSE frame writer — <c>data: {json}\n\n</c>, camelCase (chat wire format).</summary>
    private static async Task WriteEventSseAsync(
        HttpResponse response, ChatSseEvent sseEvent, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(sseEvent, EventSseJsonOptions);
        await response.WriteAsync($"data: {json}\n\n", cancellationToken).ConfigureAwait(false);
        await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Pre-stream ProblemDetails writer (ADR-019: stable errorCode + correlationId).</summary>
    private static async Task WriteEventProblemAsync(
        HttpResponse response, int statusCode, string title, string detail,
        string errorCode, string correlationId, CancellationToken cancellationToken)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/problem+json";
        var json = JsonSerializer.Serialize(
            new { title, status = statusCode, detail, errorCode, correlationId },
            EventSseJsonOptions);
        await response.WriteAsync(json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>CamelCase + omit-null options for the event SSE wire format.</summary>
    private static readonly JsonSerializerOptions EventSseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

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

/// <summary>
/// Request body for <c>POST /api/ai/chat/sessions/{sessionId}/events/document-uploaded</c>
/// (FR-P1-03 Event path).
/// </summary>
/// <param name="FileIds">
/// The just-completed upload batch (session-file document ids, in upload order —
/// the first is the deterministic "top-1" for the bulk bound). Null/empty falls
/// back to the full session manifest.
/// </param>
/// <param name="TypedCommand">
/// Command text the user typed alongside the upload, if any. Non-empty triggers
/// the explicit-command supersede bound: the rule does not fire (the Text path
/// handles the command separately).
/// </param>
public sealed record DocumentUploadedEventRequest(
    IReadOnlyList<string>? FileIds = null,
    string? TypedCommand = null);

/// <summary>Request body for <c>PUT /api/ai/chat/event-rules/opt-out</c>.</summary>
public sealed record EventRulesOptOutRequest(bool OptedOut);

/// <summary>Response body for the Event-path opt-out routes.</summary>
public sealed record EventRulesOptOutResponse(bool OptedOut);
