using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Compose;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Compose drafting workspace endpoints under <c>/api/compose/*</c>.
/// Post-cleanup: only DOCX-lifecycle endpoints (Load/Save/Promote/Checkout/Checkin/Heartbeat).
/// The AI dispatch endpoint <c>/action/{consumerType}</c> was retired — AI actions flow
/// through the Assistant pane via R7 LinearConsumers (see cleanup PR).
/// </summary>
public static class ComposeEndpoints
{
    /// <summary>
    /// Maps all Compose endpoints under <c>/api/compose</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapComposeEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/compose")
            .RequireAuthorization()
            .WithTags("Compose");

        // (1) POST /api/compose/upload — FR-03 (task 012): serve the retained bytes of an
        //     Assistant-uploaded session file so "open in Compose" mounts its content as a
        //     TRANSIENT working draft (create-on-save; no sprk_document until first Save).
        //     Reads the original binary already retained by ChatDocumentEndpoints step 9b in
        //     ITenantCache ("doc-upload-binary") — a deterministic Redis read, NOT AI dispatch
        //     (ADR-039) and NOT SPE/Graph access (ADR-007). Authz via the group's
        //     RequireAuthorization() (ADR-008 / NFR-04).
        group.MapPost("/upload", Upload)
            .WithName("ComposeUpload")
            .WithSummary("Serve a session-uploaded file's retained bytes for a transient Compose mount (FR-03)")
            .RequireRateLimiting("ai-upload")
            .Produces<ComposeUploadResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        // (2) GET /api/compose/documents/{documentSpeId} — load DOCX bytes
        group.MapGet("/documents/{documentSpeId}", Load)
            .WithName("ComposeLoadDocument")
            .WithSummary("Load DOCX bytes from SPE for an existing Compose document")
            .RequireRateLimiting("ai-context")
            .Produces<LoadComposeDocumentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        // (3) POST /api/compose/documents/{documentSpeId}/save — save DOCX
        group.MapPost("/documents/{documentSpeId}/save", Save)
            .WithName("ComposeSaveDocument")
            .WithSummary("Save DOCX bytes to SPE (idempotent first-Save promotion per FR-06)")
            .RequireRateLimiting("ai-upload")
            .Produces<SaveComposeDocumentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        // (4) POST /api/compose/documents/{documentSpeId}/promote — explicit promotion
        group.MapPost("/documents/{documentSpeId}/promote", Promote)
            .WithName("ComposePromoteDocument")
            .WithSummary("Idempotently promote an ephemeral SPE drive-item to a sprk_document row (FR-06)")
            .RequireRateLimiting("ai-context")
            .Produces<PromoteComposeDocumentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status500InternalServerError);

        // (5) POST /api/compose/documents/{documentId}/checkout — Phase 5 stub
        group.MapPost("/documents/{documentId:guid}/checkout", Checkout)
            .WithName("ComposeCheckoutDocument")
            .WithSummary("Phase 5 stub: acquires SPE check-out (use /api/documents/{id}/checkout in R1)")
            .RequireRateLimiting("ai-context")
            .Produces(StatusCodes.Status501NotImplemented)
            .Produces(StatusCodes.Status401Unauthorized);

        // (6) POST /api/compose/documents/{documentId}/checkin — Phase 5 stub
        group.MapPost("/documents/{documentId:guid}/checkin", Checkin)
            .WithName("ComposeCheckinDocument")
            .WithSummary("Phase 5 stub: releases SPE check-out (use /api/documents/{id}/checkin in R1)")
            .RequireRateLimiting("ai-context")
            .Produces(StatusCodes.Status501NotImplemented)
            .Produces(StatusCodes.Status401Unauthorized);

        // (7) POST /api/compose/document/{documentId}/heartbeat — refresh SPE lock heartbeat
        group.MapPost("/document/{documentId:guid}/heartbeat", RefreshHeartbeat)
            .WithName("ComposeRefreshHeartbeat")
            .WithSummary("Refresh the heartbeat timestamp on the caller's active checkout")
            .RequireRateLimiting("ai-context")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        // (8) POST /api/compose/edit-batch/validate — FR-19 deterministic edit validation (task 020)
        group.MapPost("/edit-batch/validate", ValidateEditBatch)
            .WithName("ComposeValidateEditBatch")
            .WithSummary("Deterministically resolve match_mode edits against document text; 422 on ambiguity/no-match/empty-target/overlap")
            .RequireRateLimiting("ai-context")
            .Produces<BatchValidationResult>(StatusCodes.Status200OK)
            .Produces<BatchValidationResult>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        // (9) POST /api/compose/document/{documentSpeId}/push-annotations — FR-24 (task 050):
        // render accepted annotations into the .docx as native OOXML track-changes + comments,
        // then persist to SPE with an If-Match ETag (optimistic concurrency).
        group.MapPost("/document/{documentSpeId}/push-annotations", PushAnnotations)
            .WithName("ComposePushAnnotations")
            .WithSummary("Render accepted Compose annotations as native Word track-changes + comments and push to SPE with If-Match")
            .RequireRateLimiting("ai-upload")
            .Produces<PushAnnotationsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status412PreconditionFailed)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status423Locked)
            .Produces(StatusCodes.Status500InternalServerError);

        return routes;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handlers
    // ─────────────────────────────────────────────────────────────────────────

    // FR-19 (task 020): deterministic match_mode edit validation. Pure — delegates to
    // IComposeEditValidator (ADR-013: no AI internals). 200 when the batch resolves cleanly,
    // 422 with the structured ambiguity/no-match/empty-target/overlap result otherwise.
    private static IResult ValidateEditBatch(
        [FromBody] EditBatchValidateRequest? body,
        IComposeEditValidator validator,
        ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");
        if (body is null) return Results.BadRequest("Request body is required.");
        if (body.DocumentText is null) return Results.BadRequest("documentText is required.");
        if (body.Edits is null || body.Edits.Count == 0) return Results.BadRequest("edits must contain at least one proposed edit.");

        var result = validator.Validate(body.DocumentText, body.Edits);
        logger.LogInformation("Compose edit-batch validate: edits={EditCount} isValid={IsValid} TraceId={TraceId}",
            body.Edits.Count, result.IsValid, httpContext.TraceIdentifier);

        return result.IsValid
            ? Results.Ok(result)
            : Results.Json(result, statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    // FR-24 (task 050): render accepted annotations into the .docx as native OOXML track-changes +
    // comments (delegated to the pure DocxAnnotationWriter via ComposeService.PushAnnotationsAsync)
    // and persist to SPE with an If-Match ETag. Deterministic — no AI dispatch (ADR-039/ADR-013);
    // SPE I/O stays behind the SpeFileStore facade (ADR-007). Concurrency conflicts surface as
    // typed facade exceptions mapped to 412 (ETag moved) / 423 (open in Word).
    private static async Task<IResult> PushAnnotations(
        string documentSpeId,
        [FromBody] PushAnnotationsBody? body,
        IComposeService composeService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (string.IsNullOrWhiteSpace(documentSpeId)) return BadRequest("documentSpeId is required.");
        if (body is null) return BadRequest("Request body is required.");
        if (string.IsNullOrWhiteSpace(body.DriveId)) return BadRequest("driveId is required in the request body.");
        if (string.IsNullOrWhiteSpace(body.TenantId)) return BadRequest("tenantId is required in the request body.");
        if (string.IsNullOrWhiteSpace(body.IfMatch)) return BadRequest("ifMatch (load-time ETag) is required for optimistic concurrency.");
        if (body.Annotations is null || body.Annotations.Count == 0) return BadRequest("annotations must contain at least one annotation.");

        logger.LogInformation(
            "Compose push-annotations: tenant={TenantId} drive={DriveId} item={DocumentSpeId} annotations={AnnotationCount} TraceId={TraceId}",
            body.TenantId, body.DriveId, documentSpeId, body.Annotations.Count, httpContext.TraceIdentifier);

        try
        {
            var request = new PushAnnotationsRequest
            {
                DriveId = body.DriveId,
                DocumentSpeId = documentSpeId,
                TenantId = body.TenantId,
                IfMatch = body.IfMatch,
                Annotations = body.Annotations,
            };

            var result = await composeService.PushAnnotationsAsync(request, httpContext, ct).ConfigureAwait(false);

            return Results.Ok(new PushAnnotationsResponse(
                DocumentSpeId: result.DocumentSpeId,
                DriveId: result.DriveId,
                VersionId: result.VersionId,
                ETag: result.ETag,
                Size: result.Size,
                AnnotationCount: result.AnnotationCount,
                CorrelationId: httpContext.TraceIdentifier));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (DocxAnnotationException ex) when (ex.Kind == DocxAnnotationErrorKind.MalformedDocument)
        {
            logger.LogWarning(ex, "Compose push-annotations: malformed DOCX. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Malformed Document",
                detail: "The stored document could not be read as a valid .docx; annotations were not applied.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }
        catch (DocxAnnotationException ex) when (ex.Kind == DocxAnnotationErrorKind.TargetNotFound)
        {
            logger.LogWarning(ex, "Compose push-annotations: annotation target not found. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Annotation Target Not Found",
                detail: ex.Message,
                type: "https://tools.ietf.org/html/rfc4918#section-11.2");
        }
        catch (Sprk.Bff.Api.Infrastructure.Graph.EtagPreconditionFailedException ex)
        {
            logger.LogWarning(ex, "Compose push-annotations: ETag precondition failed (412). TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status412PreconditionFailed,
                title: "Document Changed",
                detail: "This document changed since you loaded it (e.g. a Word autosave). Reload the latest " +
                        "version to keep both sets of changes — nothing was overwritten.",
                type: "https://tools.ietf.org/html/rfc7232#section-4.2");
        }
        catch (Sprk.Bff.Api.Infrastructure.Graph.DocumentLockedByWordException ex)
        {
            logger.LogWarning(ex, "Compose push-annotations: drive-item locked by Word (423). TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status423Locked,
                title: "Document Open in Word",
                detail: "Couldn't save — this document is open in Word for Web right now. Close it there, then " +
                        "push your changes again. Your Compose changes are safe and still pending.",
                type: "https://tools.ietf.org/html/rfc4918#section-11.3");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(ex, "Compose push-annotations: SPE drive-item not found. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Document Not Found",
                detail: $"SPE drive-item '{documentSpeId}' was not found or could not be written.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Compose push-annotations: OBO denied. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: "Caller lacks SPE ACL write permission for this drive-item.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.3");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Compose push-annotations: unexpected failure. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while pushing annotations.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FR-03 (task 012): retained-bytes serving for the transient Compose mount.
    // The chat upload pipeline (ChatDocumentEndpoints.UploadDocumentAsync step 9b)
    // stores the ORIGINAL binary in ITenantCache under "doc-upload-binary" keyed by
    // {sessionId}:{documentId} with a 4-hour session-lifetime TTL. This endpoint
    // reads that back so the Compose editor can mount the uploaded .docx as a
    // transient working draft. Constants mirror ChatDocumentEndpoints (same on-wire
    // key); keep in sync if that pipeline's resource/version changes.
    // ─────────────────────────────────────────────────────────────────────────
    private const string DocBinaryResource = "doc-upload-binary";
    private const string DocMetaResource = "doc-upload-meta";
    private const int DocCacheVersion = 1;

    private static async Task<IResult> Upload(
        [FromBody] ComposeUploadRequest? body,
        ITenantCache cache,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (body is null) return BadRequest("Request body is required.");
        if (string.IsNullOrWhiteSpace(body.SessionId)) return BadRequest("sessionId is required.");
        if (string.IsNullOrWhiteSpace(body.DocumentId)) return BadRequest("documentId (the session-uploaded file id) is required.");

        // Tenant scoping (ADR-014): dual-form tid claim + X-Tenant-Id fallback — same
        // extraction pattern as ChatDocumentEndpoints so the cache key resolves identically.
        var tenantId = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
            ?? httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "Tenant identity not found in token claims.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        logger.LogInformation(
            "Compose upload-mount: tenant={TenantId} session={SessionId} document={DocumentId} TraceId={TraceId}",
            tenantId, body.SessionId, body.DocumentId, httpContext.TraceIdentifier);

        try
        {
            // The chat upload route key uses the raw sessionId string the client sent. The
            // Compose-mount seed may carry a different GUID format (D vs N), so probe the
            // likely id spellings before giving up.
            var (binary, resolvedCacheId) = await ResolveRetainedBinaryAsync(
                cache, tenantId, body.SessionId, body.DocumentId, ct).ConfigureAwait(false);

            if (binary is null || binary.Length == 0)
            {
                logger.LogWarning(
                    "Compose upload-mount: retained bytes not found (expired or never uploaded) tenant={TenantId} session={SessionId} document={DocumentId} TraceId={TraceId}",
                    tenantId, body.SessionId, body.DocumentId, httpContext.TraceIdentifier);

                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Uploaded File Not Available",
                    detail: "The uploaded file's bytes are no longer available (the session may have expired). " +
                            "Re-upload the file in the Assistant, then open it in Compose again.",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
            }

            // Filename + content type from the metadata sidecar (best-effort; null-tolerant).
            string? fileName = null;
            try
            {
                var metadata = await cache.GetAsync<Ai.UploadedDocumentMetadata>(
                    tenantId, DocMetaResource, resolvedCacheId!, DocCacheVersion, ct: ct).ConfigureAwait(false);
                fileName = metadata?.Filename;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Compose upload-mount: metadata lookup failed (non-fatal) cacheId={CacheId} TraceId={TraceId}",
                    resolvedCacheId, httpContext.TraceIdentifier);
            }

            var extension = Path.GetExtension(fileName ?? string.Empty)?.ToLowerInvariant() ?? string.Empty;
            var contentType = extension switch
            {
                ".pdf" => "application/pdf",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".txt" => "text/plain",
                ".md" => "text/markdown",
                _ => "application/octet-stream"
            };

            return Results.Ok(new ComposeUploadResponse(
                SessionId: body.SessionId,
                DocumentId: body.DocumentId,
                FileName: fileName,
                ContentType: contentType,
                Content: binary,
                Size: binary.Length,
                CorrelationId: httpContext.TraceIdentifier));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Compose upload-mount: unexpected failure. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while loading the uploaded file.");
        }
    }

    /// <summary>
    /// Probes the retained-binary cache under the likely session-id spellings (as-sent, then
    /// GUID "D"/"N" normalizations) so a Compose-mount seed carrying a differently-formatted
    /// session id still resolves the bytes the chat upload pipeline stored.
    /// </summary>
    private static async Task<(byte[]? Binary, string? CacheId)> ResolveRetainedBinaryAsync(
        ITenantCache cache, string tenantId, string sessionId, string documentId, CancellationToken ct)
    {
        var candidates = new List<string> { sessionId };
        if (Guid.TryParse(sessionId, out var sessionGuid))
        {
            var dForm = sessionGuid.ToString("D");
            var nForm = sessionGuid.ToString("N");
            if (!candidates.Contains(dForm)) candidates.Add(dForm);
            if (!candidates.Contains(nForm)) candidates.Add(nForm);
        }

        foreach (var candidate in candidates)
        {
            var cacheId = $"{candidate}:{documentId}";
            var binary = await cache.GetAsync<byte[]>(
                tenantId, DocBinaryResource, cacheId, DocCacheVersion, ct: ct).ConfigureAwait(false);
            if (binary is { Length: > 0 })
            {
                return (binary, cacheId);
            }
        }

        return (null, null);
    }

    private static async Task<IResult> Load(
        string documentSpeId,
        [FromQuery] string driveId,
        [FromQuery] string tenantId,
        [FromQuery] Guid? documentRecordId,
        [FromQuery] string? displayName,
        IComposeService composeService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (string.IsNullOrWhiteSpace(documentSpeId)) return BadRequest("documentSpeId is required.");
        if (string.IsNullOrWhiteSpace(driveId)) return BadRequest("driveId query parameter is required for SPE drive-item access.");
        if (string.IsNullOrWhiteSpace(tenantId)) return BadRequest("tenantId query parameter is required for multi-tenant isolation.");

        logger.LogInformation(
            "Compose load: tenant={TenantId} drive={DriveId} item={DocumentSpeId} record={DocumentRecordId} TraceId={TraceId}",
            tenantId, driveId, documentSpeId, documentRecordId, httpContext.TraceIdentifier);

        try
        {
            var request = new LoadComposeDocumentRequest
            {
                DriveId = driveId,
                DocumentSpeId = documentSpeId,
                TenantId = tenantId,
                DocumentRecordId = documentRecordId,
                DisplayName = displayName,
            };

            var result = await composeService.LoadAsync(request, httpContext, ct).ConfigureAwait(false);

            return Results.Ok(new LoadComposeDocumentResponse(
                DocumentSpeId: result.DocumentSpeId,
                DriveId: result.DriveId,
                SessionId: result.SessionId,
                DocumentRecordId: result.DocumentRecordId,
                Content: result.Content.ToArray(),
                ETag: result.ETag,
                FileName: result.FileName,
                Size: result.Size,
                CorrelationId: httpContext.TraceIdentifier));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(ex, "Compose load: SPE drive-item not found. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Document Not Found",
                detail: $"SPE drive-item '{documentSpeId}' was not found or is unreadable.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Compose load: OBO denied. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: "Caller lacks SPE ACL permission for this drive-item.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.3");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Compose load: unexpected failure. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while loading the document.");
        }
    }

    private static async Task<IResult> Save(
        string documentSpeId,
        [FromBody] SaveComposeDocumentBody body,
        IComposeService composeService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (string.IsNullOrWhiteSpace(documentSpeId)) return BadRequest("documentSpeId is required.");
        if (body is null) return BadRequest("Request body is required.");
        if (string.IsNullOrWhiteSpace(body.DriveId)) return BadRequest("driveId is required in the request body.");
        if (string.IsNullOrWhiteSpace(body.TenantId)) return BadRequest("tenantId is required in the request body.");
        if (string.IsNullOrWhiteSpace(body.SessionId)) return BadRequest("sessionId is required in the request body for first-Save promotion rebind.");
        if (body.Content is null || body.Content.Length == 0) return BadRequest("content is required and must be non-empty.");

        logger.LogInformation(
            "Compose save: tenant={TenantId} drive={DriveId} item={DocumentSpeId} session={SessionId} record={DocumentRecordId} size={SizeBytes} TraceId={TraceId}",
            body.TenantId, body.DriveId, documentSpeId, body.SessionId, body.DocumentRecordId, body.Content.Length, httpContext.TraceIdentifier);

        try
        {
            var request = new SaveComposeDocumentRequest
            {
                DriveId = body.DriveId,
                DocumentSpeId = documentSpeId,
                Content = body.Content,
                SessionId = body.SessionId,
                TenantId = body.TenantId,
                DocumentRecordId = body.DocumentRecordId,
                DisplayName = body.DisplayName,
            };

            var result = await composeService.SaveAsync(request, httpContext, ct).ConfigureAwait(false);

            return Results.Ok(new SaveComposeDocumentResponse(
                DocumentSpeId: result.DocumentSpeId,
                DriveId: result.DriveId,
                SessionId: result.SessionId,
                DocumentRecordId: result.DocumentRecordId,
                VersionId: result.VersionId,
                ETag: result.ETag,
                Size: result.Size,
                WasPromotedThisSave: result.WasPromotedThisSave,
                CorrelationId: httpContext.TraceIdentifier));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(ex, "Compose save: SPE drive-item not found. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Document Not Found",
                detail: $"SPE drive-item '{documentSpeId}' was not found or could not be written.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Compose save: OBO denied. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: "Caller lacks SPE ACL write permission for this drive-item.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.3");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Compose save: unexpected failure. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: $"Save failed: {ex.GetType().Name}: {ex.Message}. TraceId={httpContext.TraceIdentifier}");
        }
    }

    private static async Task<IResult> Promote(
        string documentSpeId,
        [FromBody] PromoteComposeDocumentBody body,
        IComposeService composeService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (string.IsNullOrWhiteSpace(documentSpeId)) return BadRequest("documentSpeId is required.");
        if (body is null) return BadRequest("Request body is required.");
        if (string.IsNullOrWhiteSpace(body.SessionId)) return BadRequest("sessionId is required for the ephemeral→promoted rebind.");
        if (string.IsNullOrWhiteSpace(body.TenantId)) return BadRequest("tenantId is required for multi-tenant isolation.");

        logger.LogInformation(
            "Compose promote: tenant={TenantId} item={DocumentSpeId} session={SessionId} TraceId={TraceId}",
            body.TenantId, documentSpeId, body.SessionId, httpContext.TraceIdentifier);

        try
        {
            var request = new PromoteComposeDocumentRequest
            {
                DocumentSpeId = documentSpeId,
                SessionId = body.SessionId,
                TenantId = body.TenantId,
                DisplayName = body.DisplayName,
            };

            var result = await composeService.PromoteIfEphemeralAsync(request, httpContext, ct).ConfigureAwait(false);

            return Results.Ok(new PromoteComposeDocumentResponse(
                DocumentSpeId: result.DocumentSpeId,
                SessionId: result.SessionId,
                DocumentRecordId: result.DocumentRecordId,
                WasCreated: result.WasCreated,
                CorrelationId: httpContext.TraceIdentifier));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Compose promote: unexpected failure. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while promoting the document.");
        }
    }

    private static IResult Checkout(Guid documentId, ILoggerFactory loggerFactory, HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");
        logger.LogInformation(
            "Compose checkout stub called for documentId={DocumentId}. TraceId={TraceId}",
            documentId, httpContext.TraceIdentifier);

        return Results.Problem(
            statusCode: StatusCodes.Status501NotImplemented,
            title: "Not Implemented",
            detail: "Compose check-out is wired in Phase 5. In R1, call " +
                    "POST /api/documents/{documentId}/checkout directly.",
            type: "https://tools.ietf.org/html/rfc7231#section-6.6.2");
    }

    private static IResult Checkin(Guid documentId, ILoggerFactory loggerFactory, HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");
        logger.LogInformation(
            "Compose checkin stub called for documentId={DocumentId}. TraceId={TraceId}",
            documentId, httpContext.TraceIdentifier);

        return Results.Problem(
            statusCode: StatusCodes.Status501NotImplemented,
            title: "Not Implemented",
            detail: "Compose check-in is wired in Phase 5. In R1, call " +
                    "POST /api/documents/{documentId}/checkin directly.",
            type: "https://tools.ietf.org/html/rfc7231#section-6.6.2");
    }

    private static async Task<IResult> RefreshHeartbeat(
        Guid documentId,
        DocumentCheckoutService checkoutService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (documentId == Guid.Empty) return BadRequest("documentId is required.");

        try
        {
            var refreshed = await checkoutService
                .RefreshHeartbeatAsync(documentId, httpContext.User, ct)
                .ConfigureAwait(false);

            if (refreshed)
            {
                logger.LogDebug(
                    "Compose heartbeat refreshed for documentId={DocumentId} TraceId={TraceId}",
                    documentId, httpContext.TraceIdentifier);
                return Results.NoContent();
            }

            // Doc missing, not checked out, or held by another user — all three collapse to 404.
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "No active checkout to refresh",
                detail: "The document was not found, is not checked out, or the caller does not own the active lock.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex,
                "Compose heartbeat: auth contract violation TraceId={TraceId}",
                httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "Caller's identity could not be resolved from claims.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Compose heartbeat: unexpected failure for documentId={DocumentId} TraceId={TraceId}",
                documentId, httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while refreshing the heartbeat.");
        }
    }

    private static IResult BadRequest(string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            detail: detail,
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
}

// ─────────────────────────────────────────────────────────────────────────────
// Request / response DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Request body for <c>POST /api/compose/upload</c> (FR-03 transient mount). Identifies a
/// session-uploaded file whose original bytes the chat pipeline retained in ITenantCache.
/// </summary>
public sealed record ComposeUploadRequest(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("documentId")] string DocumentId);

/// <summary>
/// Response shape for <c>POST /api/compose/upload</c>. <c>Content</c> serializes as a base64
/// string (System.Text.Json byte[] convention) — the client decodes it into the editor's
/// <c>docxBytes</c> transient-mount seam, exactly like the Load endpoint's <c>content</c>.
/// </summary>
public sealed record ComposeUploadResponse(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("documentId")] string DocumentId,
    [property: JsonPropertyName("fileName")] string? FileName,
    [property: JsonPropertyName("contentType")] string ContentType,
    [property: JsonPropertyName("content")] byte[] Content,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("correlationId")] string CorrelationId);

/// <summary>Request body for <c>POST /api/compose/documents/{id}/save</c>.</summary>
public sealed record SaveComposeDocumentBody(
    [property: JsonPropertyName("driveId")] string DriveId,
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("content")] byte[] Content,
    [property: JsonPropertyName("documentRecordId")] Guid? DocumentRecordId = null,
    [property: JsonPropertyName("displayName")] string? DisplayName = null);

/// <summary>Request body for <c>POST /api/compose/documents/{id}/promote</c>.</summary>
public sealed record PromoteComposeDocumentBody(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("displayName")] string? DisplayName = null);

/// <summary>Request body for <c>POST /api/compose/document/{id}/push-annotations</c> (FR-24).
/// The Compose frontend assembles the accepted track-change insertions/deletions + comments into
/// <see cref="DocxAnnotation"/> entries; <c>ifMatch</c> is the load-time ETag for optimistic
/// concurrency (a blind overwrite is not offered on this path).</summary>
public sealed record PushAnnotationsBody(
    [property: JsonPropertyName("driveId")] string DriveId,
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("ifMatch")] string IfMatch,
    [property: JsonPropertyName("annotations")] IReadOnlyList<DocxAnnotation> Annotations);

/// <summary>Response shape for <c>POST /api/compose/document/{id}/push-annotations</c> (FR-24) —
/// the new SPE version id + ETag the client uses as the next optimistic-concurrency token.</summary>
public sealed record PushAnnotationsResponse(
    [property: JsonPropertyName("documentSpeId")] string DocumentSpeId,
    [property: JsonPropertyName("driveId")] string? DriveId,
    [property: JsonPropertyName("versionId")] string VersionId,
    [property: JsonPropertyName("eTag")] string? ETag,
    [property: JsonPropertyName("size")] long? Size,
    [property: JsonPropertyName("annotationCount")] int AnnotationCount,
    [property: JsonPropertyName("correlationId")] string CorrelationId);

/// <summary>Response shape for <c>GET /api/compose/documents/{id}</c>.</summary>
public sealed record LoadComposeDocumentResponse(
    [property: JsonPropertyName("documentSpeId")] string DocumentSpeId,
    [property: JsonPropertyName("driveId")] string? DriveId,
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("documentRecordId")] Guid? DocumentRecordId,
    [property: JsonPropertyName("content")] byte[] Content,
    [property: JsonPropertyName("eTag")] string? ETag,
    [property: JsonPropertyName("fileName")] string? FileName,
    [property: JsonPropertyName("size")] long? Size,
    [property: JsonPropertyName("correlationId")] string CorrelationId);

/// <summary>Response shape for <c>POST /api/compose/documents/{id}/save</c>.</summary>
public sealed record SaveComposeDocumentResponse(
    [property: JsonPropertyName("documentSpeId")] string DocumentSpeId,
    [property: JsonPropertyName("driveId")] string? DriveId,
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("documentRecordId")] Guid? DocumentRecordId,
    [property: JsonPropertyName("versionId")] string VersionId,
    [property: JsonPropertyName("eTag")] string? ETag,
    [property: JsonPropertyName("size")] long? Size,
    [property: JsonPropertyName("wasPromotedThisSave")] bool WasPromotedThisSave,
    [property: JsonPropertyName("correlationId")] string CorrelationId);

/// <summary>Response shape for <c>POST /api/compose/documents/{id}/promote</c>.</summary>
public sealed record PromoteComposeDocumentResponse(
    [property: JsonPropertyName("documentSpeId")] string DocumentSpeId,
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("documentRecordId")] Guid? DocumentRecordId,
    [property: JsonPropertyName("wasCreated")] bool WasCreated,
    [property: JsonPropertyName("correlationId")] string CorrelationId);
