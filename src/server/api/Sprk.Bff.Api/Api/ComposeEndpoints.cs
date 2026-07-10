using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Communication.Models;
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
    // FR-25 (task 051): the reader is a pure, stateless byte[]->record transform (same shape as
    // DocxAnnotationWriter). Instantiated directly here rather than DI-registered, keeping this
    // task's footprint scoped to ComposeEndpoints.cs + the new reader file only (no edits to the
    // shared IComposeService/ComposeService/ComposeModule.cs orchestration surface, which a
    // parallel task may be touching in this shared worktree).
    private static readonly DocxAnnotationReader AnnotationReader = new();

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

        // (3b) POST /api/compose/documents/create-on-save — FR-05 create-on-save (task 100).
        // A TRANSIENT Browse/Upload draft has NO SPE drive-item, so the `{documentSpeId}` path
        // segment on the replace route (3) would be empty → `/documents//save` 404s. This
        // literal-segment sibling route carries no id in the path; the client sends the
        // client-resolved BU `containerId` (Fork A) and no `documentSpeId`, reaching
        // ComposeService.SaveAsync's transient-create branch (container → record → indexing).
        // Distinct literal `create-on-save` cannot collide with `{documentSpeId}/save` (3) or
        // GET `{documentSpeId}` (2) — different segment counts / verbs.
        group.MapPost("/documents/create-on-save", CreateOnSave)
            .WithName("ComposeCreateOnSaveDocument")
            .WithSummary("Create a new sprk_document from a transient Compose draft in the client-resolved BU container (FR-05)")
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

        // (9b) POST /api/compose/document/{documentSpeId}/push-preview — FR-28 (task 055):
        // Tier-2c PRE-CONFIRM preview (comment/track-change counts + Word-vs-Compose split).
        // Non-mutating — no SPE download, no write. The clean seam the future Policy v2 Tier 2c
        // gate dialog calls; this task builds no dialog/rendering.
        group.MapPost("/document/{documentSpeId}/push-preview", PushPreview)
            .WithName("ComposePushPreview")
            .WithSummary("Compute the Tier-2c push preview (comment/track-change counts + Word-vs-Compose split) without writing")
            .RequireRateLimiting("ai-context")
            .Produces<PushPreviewResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status500InternalServerError);

        // (10) POST /api/compose/document/{documentSpeId}/pull-annotations — FR-25 (task 051):
        // download the CURRENT SPE bytes and parse them for native w:comment/w:ins/w:del
        // (DocxAnnotationReader), returning the structured payload the Compose UI uses to
        // re-anchor prior annotations after a Word-for-Web round-trip (task 054 consumes it).
        // Deterministic Open XML parse — no AI dispatch (ADR-039/ADR-013); SPE I/O stays behind
        // the SpeFileStore/ISpeFileOperations facade (ADR-007). This is the READ direction;
        // push-annotations (above) is the WRITE direction.
        group.MapPost("/document/{documentSpeId}/pull-annotations", PullAnnotations)
            .WithName("ComposePullAnnotations")
            .WithSummary("Parse the current SPE document for w:comment/w:ins/w:del and return the structured annotation payload for re-anchoring")
            .RequireRateLimiting("ai-context")
            .Produces<PullAnnotationsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        // (11) POST /api/compose/webhooks/spe-doc-changed — FR-26 (task 053): the Graph
        // change-notification receiver for the SPE subscription task 052's SpeSyncOrchestrator
        // creates on `drives/{driveId}/root`. Registered on `routes` (NOT the authenticated
        // `group` above) because Graph's subscription-validation handshake + notification
        // delivery are unauthenticated by Graph's own contract (AllowAnonymous). Mirrors the
        // existing Communication Graph webhook receiver's two-layer defense exactly (task 044 /
        // CommunicationEndpoints.HandleIncomingWebhookAsync):
        //   1. WebhookSignatureFilter validates X-Hub-Signature-256 (HMAC-SHA256 over the raw
        //      body) using Compose:Webhook:SigningKey; the validationToken handshake probe
        //      bypasses HMAC (Graph does not sign that probe) via the filter's built-in check.
        //   2. The handler validates the body-level clientState in constant time against
        //      Compose:Webhook:ClientState — the SAME config key SpeSyncOrchestrator already
        //      reads when creating the subscription, so what we verify here is exactly what we
        //      told Graph to echo back.
        // Both checks are mandatory; there is no DEVELOPMENT_MODE bypass (fail-closed).
        routes.MapPost("/api/compose/webhooks/spe-doc-changed", HandleSpeDocChangedWebhookAsync)
            .AllowAnonymous()
            .RequireWebhookSignature(
                signatureHeader: WebhookSignatureFilter.DefaultSignatureHeader,
                signingKeyAccessor: sp => sp.GetRequiredService<IConfiguration>()["Compose:Webhook:SigningKey"],
                filterName: "Compose")
            .RequireRateLimiting("webhook-graph")
            .WithName("ComposeSpeDocChangedWebhook")
            .WithTags("Compose")
            .WithSummary("Receive Microsoft Graph change notifications for SPE document changes (HMAC-signed + clientState-verified)")
            .Produces<SpeDocChangedWebhookResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status500InternalServerError);

        // (12) POST /api/compose/document/{documentSpeId}/check-changes — FR-26 (task 053): the
        // explicit poll fallback for when the webhook is unreliable (Risk R6) or for testing.
        // Under the authenticated `group` (RequireAuthorization already applied above). Drives
        // the SAME etag-comparison substrate as the webhook (SpeSyncOrchestrator.
        // EnumerateChangesAsync) rather than a second ad hoc etag-comparison path, so "stored vs
        // current SPE etag" always means the one Redis-backed comparison task 052 built
        // (ADR-009); no Microsoft.Graph type crosses this endpoint (ADR-007).
        group.MapPost("/document/{documentSpeId}/check-changes", CheckDocumentChangesAsync)
            .WithName("ComposeCheckDocumentChanges")
            .WithSummary("Poll fallback: compare the stored SPE etag vs the current SPE etag for a Compose document (FR-26)")
            .RequireRateLimiting("ai-context")
            .Produces<CheckChangesResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status500InternalServerError);

        // (13) POST /api/compose/document/{documentSpeId}/reanchor-annotations — FR-27 (task 054):
        // after a Word round-trip produced a new SPE version (detected by 052/053), download the
        // CURRENT bytes, extract paragraph text, and re-anchor the client's prior Compose anchors
        // against it with confidence bands (≥0.85 auto / 0.6–0.85 review / <0.6 orphan) + the
        // Spike-6 ambiguity guard. Returns the per-band summary the Workspace banner + conflict UX
        // render; persists it to Redis (ADR-009) so it survives the gap until the user returns.
        // Deterministic scoring — NO LLM call (ADR-013/NFR-05); SPE download stays behind the
        // ISpeFileOperations facade (ADR-007). A dedicated route (not a ride on pull-annotations)
        // because re-anchoring needs the CLIENT's prior anchors in the request body — pull's
        // contract carries none.
        group.MapPost("/document/{documentSpeId}/reanchor-annotations", ReanchorAnnotations)
            .WithName("ComposeReanchorAnnotations")
            .WithSummary("Re-anchor prior Compose annotations against the reloaded Word document; return banded summary (FR-27)")
            .RequireRateLimiting("ai-context")
            .Produces<ReanchorAnnotationsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        // (14) GET /api/compose/sessions/{sessionId}/annotations — FR-29 (task 102, gap 4.3): read
        // the CURRENT anchored annotations + defined-terms stored on a Compose session so the client
        // can rehydrate them (Load already returns them on the document-open path; this is the
        // standalone read for a Context-pane refresh or a re-sync). Session-keyed because the two
        // collections live on the ChatSession (ADR-015 Tier 3), exactly matching
        // ComposeService.GetComposeAnnotationsAsync. Read-only — no SPE/Graph, no AI dispatch
        // (ADR-013/ADR-039). Injects only IComposeService (the CRUD facade), never an AI internal.
        group.MapGet("/sessions/{sessionId}/annotations", GetAnnotations)
            .WithName("ComposeGetAnnotations")
            .WithSummary("Read a Compose session's anchored annotations + defined-terms (FR-29)")
            .RequireRateLimiting("ai-context")
            .Produces<ComposeAnnotationsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status500InternalServerError);

        // (15) POST /api/compose/sessions/{sessionId}/annotations — FR-29 (task 102, gap 4.3): the
        // WRITE half that makes annotations survive a reopen. Persists the client's anchored
        // annotations + defined-terms onto the EXISTING session via
        // ComposeService.SaveComposeAnnotationsAsync (partial-replace: a null collection leaves the
        // stored one unchanged; a non-null one replaces it wholesale). These are MUTABLE session
        // UI state (accept/reject/edit), NOT the append-only ledger (contrast push-annotations,
        // which writes native OOXML into the .docx). A malformed ADR-040 provenance ledgerRef 400s;
        // a missing session 404s. No SPE/Graph, no AI dispatch (ADR-013/ADR-039).
        group.MapPost("/sessions/{sessionId}/annotations", SaveAnnotations)
            .WithName("ComposeSaveAnnotations")
            .WithSummary("Persist a Compose session's anchored annotations + defined-terms (FR-29)")
            .RequireRateLimiting("ai-upload")
            .Produces<ComposeAnnotationsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        // (16) POST /api/compose/active-document — task 113 (UAT defects 4/5): register the
        // session-scoped ACTIVE-DOCUMENT so both surfaces resolve "the document the user is acting
        // on" deterministically. Marks an already-landed session file (compose-direct Browse upload
        // or a chat upload — its bytes become a ChatSessionFile via the existing chat upload
        // endpoint, reused client-side) OR a stored sprk_document as active on the chat session.
        // Deterministic ChatSession write via ChatSessionManager (no parallel document store —
        // CLAUDE.md §11) — NOT AI dispatch (ADR-039) and NOT SPE/Graph access (ADR-007). Authz via
        // the group's RequireAuthorization() (ADR-008 / ADR-028).
        group.MapPost("/active-document", RegisterActiveDocument)
            .WithName("ComposeRegisterActiveDocument")
            .WithSummary("Register the session-scoped active document for the chat↔Compose bridge (task 113)")
            .RequireRateLimiting("ai-context")
            .Produces<ComposeActiveDocumentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
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
                SessionId = body.SessionId,
            };

            var result = await composeService.PushAnnotationsAsync(request, httpContext, ct).ConfigureAwait(false);

            return Results.Ok(new PushAnnotationsResponse(
                DocumentSpeId: result.DocumentSpeId,
                DriveId: result.DriveId,
                VersionId: result.VersionId,
                ETag: result.ETag,
                Size: result.Size,
                AnnotationCount: result.AnnotationCount,
                CorrelationId: httpContext.TraceIdentifier,
                Preview: result.Preview,
                CompletionState: result.CompletionState));
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

    // FR-28 (task 055): Tier-2c PRE-CONFIRM preview — deterministic comment/track-change counts +
    // the Word-vs-Compose split for an annotation batch the caller is ABOUT to push. Non-mutating
    // (no SPE download, no write, no ETag) — safe to call repeatedly while the user is still
    // deciding accept/reject in the (not-yet-built) gate dialog. This route is the clean seam the
    // future Policy v2 Tier 2c dialog calls; it renders no UI itself (ADR-013/ADR-039 — no AI
    // dispatch either; pure deterministic categorization).
    private static async Task<IResult> PushPreview(
        string documentSpeId,
        [FromBody] PushPreviewBody? body,
        IComposeService composeService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (string.IsNullOrWhiteSpace(documentSpeId)) return BadRequest("documentSpeId is required.");
        if (body is null) return BadRequest("Request body is required.");
        if (string.IsNullOrWhiteSpace(body.TenantId)) return BadRequest("tenantId is required in the request body.");
        if (body.Annotations is null || body.Annotations.Count == 0) return BadRequest("annotations must contain at least one annotation.");

        try
        {
            var request = new PreviewPushAnnotationsRequest
            {
                TenantId = body.TenantId,
                Annotations = body.Annotations,
                SessionId = body.SessionId,
            };

            var preview = await composeService.PreviewPushAnnotationsAsync(request, ct).ConfigureAwait(false);

            return Results.Ok(new PushPreviewResponse(
                Preview: preview,
                CorrelationId: httpContext.TraceIdentifier));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Compose push-preview: unexpected failure. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while computing the push preview.");
        }
    }

    // FR-25 (task 051): download the current SPE bytes and parse them (DocxAnnotationReader) for
    // native w:comment/w:ins/w:del, returning the structured payload for re-anchoring. Read-only —
    // no SPE write, no ETag, no AI dispatch (ADR-013/ADR-039). SPE I/O stays behind the
    // ISpeFileOperations facade (ADR-007), matching the download half of Load/PushAnnotations.
    private static async Task<IResult> PullAnnotations(
        string documentSpeId,
        [FromBody] PullAnnotationsBody? body,
        ISpeFileOperations spe,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (string.IsNullOrWhiteSpace(documentSpeId)) return BadRequest("documentSpeId is required.");
        if (body is null) return BadRequest("Request body is required.");
        if (string.IsNullOrWhiteSpace(body.DriveId)) return BadRequest("driveId is required in the request body.");
        if (string.IsNullOrWhiteSpace(body.TenantId)) return BadRequest("tenantId is required in the request body.");

        logger.LogInformation(
            "Compose pull-annotations: tenant={TenantId} drive={DriveId} item={DocumentSpeId} TraceId={TraceId}",
            body.TenantId, body.DriveId, documentSpeId, httpContext.TraceIdentifier);

        try
        {
            var stream = await spe.DownloadFileAsUserAsync(httpContext, body.DriveId, documentSpeId, ct)
                .ConfigureAwait(false);

            if (stream is null)
            {
                logger.LogWarning(
                    "Compose pull-annotations: SPE drive-item not found or unreadable. TraceId={TraceId}",
                    httpContext.TraceIdentifier);
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Document Not Found",
                    detail: $"SPE drive-item '{documentSpeId}' was not found or is unreadable.",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
            }

            byte[] sourceBytes;
            await using (stream.ConfigureAwait(false))
            {
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
                sourceBytes = buffer.ToArray();
            }

            // Pure parse — zero annotations returns empty lists, not an error (FR-25 negative
            // criterion). A malformed/non-DOCX stream throws DocxAnnotationException, caught below.
            var result = AnnotationReader.Read(sourceBytes);

            return Results.Ok(new PullAnnotationsResponse(
                DocumentSpeId: documentSpeId,
                DriveId: body.DriveId,
                Comments: result.Comments,
                Revisions: result.Revisions,
                CorrelationId: httpContext.TraceIdentifier));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (DocxAnnotationException ex) when (ex.Kind == DocxAnnotationErrorKind.MalformedDocument)
        {
            logger.LogWarning(ex, "Compose pull-annotations: malformed DOCX. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Malformed Document",
                detail: "The stored document could not be read as a valid .docx; no annotations were extracted.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Compose pull-annotations: OBO denied. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: "Caller lacks SPE ACL read permission for this drive-item.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.3");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Compose pull-annotations: unexpected failure. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while pulling annotations.");
        }
    }

    // FR-26 (task 053): Graph change-notification receiver for the SPE webhook subscription
    // task 052's SpeSyncOrchestrator creates on `drives/{driveId}/root`. Two request shapes:
    //   1. Subscription validation: Graph POSTs ?validationToken=<token>; echo it back as
    //      text/plain within 10s (Graph's hard requirement). WebhookSignatureFilter already lets
    //      this probe through unsigned (Graph does not sign it).
    //   2. Change notification batch: JSON body of notifications; each MUST carry a clientState
    //      matching Compose:Webhook:ClientState (constant-time compare) or the WHOLE batch is
    //      rejected 401 — mirrors CommunicationEndpoints.HandleIncomingWebhookAsync exactly.
    // A verified notification's `resource` (e.g. "drives/{driveId}/root") is reverse-resolved to
    // the tracked containerId (SpeSyncOrchestrator.ResolveContainerIdForDriveIdAsync — Graph
    // notifications never carry the SPE containerId itself), then EnumerateChangesAsync is
    // called to enumerate + persist net changes (the "enqueue/flag re-anchor work" task 054
    // consumes — the persisted Redis delta/etag state IS the queue; no separate job dispatch).
    private static async Task<IResult> HandleSpeDocChangedWebhookAsync(
        HttpRequest request,
        SpeSyncOrchestrator orchestrator,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");
        var traceId = request.HttpContext.TraceIdentifier;

        try
        {
            // ─── Step 1: Graph subscription validation handshake ───
            if (SpeWebhookNotificationVerifier.TryGetValidationToken(request.Query, out var validationToken))
            {
                logger.LogInformation(
                    "Compose webhook: Graph subscription validation request, echoing token. TraceId={TraceId}",
                    traceId);
                return Results.Text(validationToken!, "text/plain", statusCode: StatusCodes.Status200OK);
            }

            // ─── Step 2: read + parse the notification body ───
            request.EnableBuffering();
            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            var requestBody = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(requestBody))
            {
                logger.LogWarning("Compose webhook: empty payload received. TraceId={TraceId}", traceId);
                return BadRequest("Webhook payload is empty.");
            }

            GraphChangeNotificationCollection? notifications;
            try
            {
                notifications = JsonSerializer.Deserialize<GraphChangeNotificationCollection>(requestBody);
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Compose webhook: failed to parse notification payload. TraceId={TraceId}", traceId);
                return BadRequest($"Failed to parse notification payload: {ex.Message}");
            }

            if (notifications?.Value is not { Length: > 0 })
            {
                logger.LogWarning("Compose webhook: payload contains no notifications. TraceId={TraceId}", traceId);
                return BadRequest("Notification payload contains no notifications.");
            }

            // ─── Step 3: verify clientState on every notification (fail-closed, constant-time) ───
            var expectedClientState = configuration["Compose:Webhook:ClientState"];
            if (string.IsNullOrEmpty(expectedClientState))
            {
                logger.LogError(
                    "Compose webhook: Compose:Webhook:ClientState not configured — rejecting batch. TraceId={TraceId}",
                    traceId);
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Server Misconfigured",
                    detail: "Webhook clientState validation is not configured on this server.");
            }

            if (!SpeWebhookNotificationVerifier.VerifyClientState(notifications.Value, expectedClientState, out var invalidNotification))
            {
                logger.LogWarning(
                    "Compose webhook: invalid clientState on notification for subscription {SubscriptionId}. Rejecting batch. TraceId={TraceId}",
                    invalidNotification?.SubscriptionId, traceId);
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized",
                    detail: "Invalid clientState in notification.");
            }

            // ─── Step 4: resolve each notification's driveId -> tracked containerId, enumerate ───
            var processedContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var totalChanges = 0;

            foreach (var notification in notifications.Value)
            {
                var resource = notification.Resource ?? string.Empty;
                if (!SpeWebhookNotificationVerifier.TryExtractDriveIdFromResource(resource, out var driveId) || driveId is null)
                {
                    logger.LogWarning(
                        "Compose webhook: could not extract driveId from resource '{Resource}'. Skipping notification. TraceId={TraceId}",
                        resource, traceId);
                    continue;
                }

                var containerId = await orchestrator.ResolveContainerIdForDriveIdAsync(driveId, ct).ConfigureAwait(false);
                if (containerId is null)
                {
                    logger.LogWarning(
                        "Compose webhook: no tracked container for driveId {DriveId} (resource {Resource}); notification ignored. TraceId={TraceId}",
                        driveId, resource, traceId);
                    continue;
                }

                if (!processedContainers.Add(containerId))
                {
                    // A batch may repeat the same container across multiple changed items;
                    // EnumerateChangesAsync already enumerates the whole container in one call.
                    continue;
                }

                var netChanges = await orchestrator.EnumerateChangesAsync(containerId, ct).ConfigureAwait(false);
                totalChanges += netChanges.Count;

                logger.LogInformation(
                    "Compose webhook: container {ContainerId} drive {DriveId} — {Count} net changes enumerated (re-anchor work flagged for task 054). TraceId={TraceId}",
                    containerId, driveId, netChanges.Count, traceId);
            }

            // ─── Step 5: fast 202 Accepted (Graph requires a quick response) ───
            return Results.Accepted(value: new SpeDocChangedWebhookResponse(
                NotificationsReceived: notifications.Value.Length,
                ContainersProcessed: processedContainers.Count,
                ChangesDetected: totalChanges,
                CorrelationId: traceId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Compose webhook: unexpected failure. TraceId={TraceId}", traceId);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while processing the webhook.");
        }
    }

    // FR-26 (task 053): explicit poll fallback comparing the stored SPE etag vs the current SPE
    // etag for a single document. Delegates to SpeSyncOrchestrator.EnumerateChangesAsync — the
    // SAME Redis-backed delta/etag substrate the webhook receiver drives (task 052) — rather than
    // a second etag-comparison mechanism, so "poll" and "webhook" always agree on what "changed"
    // means. No Microsoft.Graph type crosses this endpoint (ADR-007); etag state is Redis via the
    // orchestrator (ADR-009).
    private static async Task<IResult> CheckDocumentChangesAsync(
        string documentSpeId,
        [FromBody] CheckChangesBody? body,
        SpeSyncOrchestrator orchestrator,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (string.IsNullOrWhiteSpace(documentSpeId)) return BadRequest("documentSpeId is required.");
        if (body is null) return BadRequest("Request body is required.");
        if (string.IsNullOrWhiteSpace(body.ContainerId)) return BadRequest("containerId is required in the request body.");

        try
        {
            var netChanges = await orchestrator.EnumerateChangesAsync(body.ContainerId, ct).ConfigureAwait(false);
            var match = netChanges.FirstOrDefault(c => string.Equals(c.ItemId, documentSpeId, StringComparison.OrdinalIgnoreCase));
            var changed = match is not null;

            logger.LogInformation(
                "Compose check-changes: container={ContainerId} item={DocumentSpeId} changed={Changed} TraceId={TraceId}",
                body.ContainerId, documentSpeId, changed, httpContext.TraceIdentifier);

            return Results.Ok(new CheckChangesResponse(
                DocumentSpeId: documentSpeId,
                ContainerId: body.ContainerId,
                Changed: changed,
                Deleted: match?.Deleted ?? false,
                ETag: match?.ETag,
                Name: match?.Name,
                CorrelationId: httpContext.TraceIdentifier));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Compose check-changes: unexpected failure for documentSpeId={DocumentSpeId} TraceId={TraceId}",
                documentSpeId, httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while checking for changes.");
        }
    }

    // FR-27 (task 054): re-anchor prior Compose annotations against the reloaded Word document.
    // Downloads the CURRENT SPE bytes (facade, like PullAnnotations), extracts paragraph text, and
    // scores each client-supplied prior anchor into auto/review/orphan bands via the deterministic
    // AnnotationReanchorService (no LLM — ADR-013/NFR-05). Persists the summary to Redis (ADR-009,
    // via the injected IDistributedCache) so the banner survives the user's return. The service is
    // instantiated directly (not DI-registered) — same footprint-scoping choice as the
    // DocxAnnotationReader above, keeping this task off the shared ComposeModule.cs DI surface.
    private static async Task<IResult> ReanchorAnnotations(
        string documentSpeId,
        [FromBody] ReanchorAnnotationsBody? body,
        ISpeFileOperations spe,
        IDistributedCache cache,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (string.IsNullOrWhiteSpace(documentSpeId)) return BadRequest("documentSpeId is required.");
        if (body is null) return BadRequest("Request body is required.");
        if (string.IsNullOrWhiteSpace(body.DriveId)) return BadRequest("driveId is required in the request body.");
        if (string.IsNullOrWhiteSpace(body.TenantId)) return BadRequest("tenantId is required in the request body.");
        if (body.PriorAnchors is null) return BadRequest("priorAnchors is required (may be empty, but must be present).");

        logger.LogInformation(
            "Compose reanchor-annotations: tenant={TenantId} drive={DriveId} item={DocumentSpeId} priorAnchors={AnchorCount} TraceId={TraceId}",
            body.TenantId, body.DriveId, documentSpeId, body.PriorAnchors.Count, httpContext.TraceIdentifier);

        try
        {
            var stream = await spe.DownloadFileAsUserAsync(httpContext, body.DriveId, documentSpeId, ct)
                .ConfigureAwait(false);

            if (stream is null)
            {
                logger.LogWarning(
                    "Compose reanchor-annotations: SPE drive-item not found or unreadable. TraceId={TraceId}",
                    httpContext.TraceIdentifier);
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Document Not Found",
                    detail: $"SPE drive-item '{documentSpeId}' was not found or is unreadable.",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
            }

            byte[] sourceBytes;
            await using (stream.ConfigureAwait(false))
            {
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
                sourceBytes = buffer.ToArray();
            }

            var service = new AnnotationReanchorService(cache);
            var summary = await service
                .ComputeAndPersistAsync(documentSpeId, body.PriorAnchors, sourceBytes, ct)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Compose reanchor-annotations: item={DocumentSpeId} total={Total} auto={Auto} review={Review} orphan={Orphan} TraceId={TraceId}",
                documentSpeId, summary.Total, summary.AutoCount, summary.ReviewCount, summary.OrphanCount, httpContext.TraceIdentifier);

            return Results.Ok(new ReanchorAnnotationsResponse(
                DocumentSpeId: documentSpeId,
                DriveId: body.DriveId,
                Summary: summary,
                CorrelationId: httpContext.TraceIdentifier));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (DocxAnnotationException ex) when (ex.Kind == DocxAnnotationErrorKind.MalformedDocument)
        {
            logger.LogWarning(ex, "Compose reanchor-annotations: malformed DOCX. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Malformed Document",
                detail: "The stored document could not be read as a valid .docx; annotations were not re-anchored.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Compose reanchor-annotations: OBO denied. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: "Caller lacks SPE ACL read permission for this drive-item.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.3");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Compose reanchor-annotations: unexpected failure. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while re-anchoring annotations.");
        }
    }

    // FR-29 (task 102, gap 4.3): read a Compose session's anchored annotations + defined-terms.
    // Pure delegation to IComposeService.GetComposeAnnotationsAsync (the CRUD facade — no AI
    // internals per ADR-013). Returns empty collections (never null) for a session with none
    // stored, or an unknown session id — same contract as the service.
    private static async Task<IResult> GetAnnotations(
        string sessionId,
        [FromQuery] string tenantId,
        IComposeService composeService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (string.IsNullOrWhiteSpace(sessionId)) return BadRequest("sessionId is required.");
        if (string.IsNullOrWhiteSpace(tenantId)) return BadRequest("tenantId query parameter is required for multi-tenant isolation.");

        try
        {
            var state = await composeService.GetComposeAnnotationsAsync(tenantId, sessionId, ct).ConfigureAwait(false);

            return Results.Ok(new ComposeAnnotationsResponse(
                SessionId: sessionId,
                AnchoredAnnotations: state.AnchoredAnnotations,
                DefinedTermsTracking: state.DefinedTermsTracking,
                CorrelationId: httpContext.TraceIdentifier));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Compose get-annotations: unexpected failure. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while reading annotations.");
        }
    }

    // FR-29 (task 102, gap 4.3): persist a Compose session's anchored annotations + defined-terms.
    // Delegates to IComposeService.SaveComposeAnnotationsAsync (partial-replace semantics). A
    // malformed ADR-040 provenance ledgerRef surfaces as ArgumentException → 400; a missing session
    // surfaces as InvalidOperationException("...not found") → 404.
    private static async Task<IResult> SaveAnnotations(
        string sessionId,
        [FromBody] SaveComposeAnnotationsBody? body,
        IComposeService composeService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (string.IsNullOrWhiteSpace(sessionId)) return BadRequest("sessionId is required.");
        if (body is null) return BadRequest("Request body is required.");
        if (string.IsNullOrWhiteSpace(body.TenantId)) return BadRequest("tenantId is required in the request body.");

        logger.LogInformation(
            "Compose save-annotations: tenant={TenantId} session={SessionId} annotations={AnnotationCount} definedTerms={DefinedTermCount} TraceId={TraceId}",
            body.TenantId, sessionId, body.AnchoredAnnotations?.Count, body.DefinedTermsTracking?.Count, httpContext.TraceIdentifier);

        try
        {
            var state = await composeService.SaveComposeAnnotationsAsync(
                new SaveComposeAnnotationsRequest
                {
                    TenantId = body.TenantId,
                    SessionId = sessionId,
                    AnchoredAnnotations = body.AnchoredAnnotations,
                    DefinedTermsTracking = body.DefinedTermsTracking,
                },
                ct).ConfigureAwait(false);

            return Results.Ok(new ComposeAnnotationsResponse(
                SessionId: sessionId,
                AnchoredAnnotations: state.AnchoredAnnotations,
                DefinedTermsTracking: state.DefinedTermsTracking,
                CorrelationId: httpContext.TraceIdentifier));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(ex, "Compose save-annotations: session not found. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Session Not Found",
                detail: "The Compose session was not found. Annotations can only be saved onto an existing session (open the document first).",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Compose save-annotations: unexpected failure. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while saving annotations.");
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
        // FR-29/FR-33 (task 102, gap 4.1 — the linchpin): a reopen carries the KNOWN prior
        // sessionId (and, when hosted on a Matter, the matterId) so ComposeService.LoadAsync
        // RESUMES that session — restoring its anchored annotations, defined terms, and action
        // history — instead of minting a fresh empty session on every reopen. Both are OPTIONAL:
        // a missing/unmatched sessionId falls back to the R1 mint-new behavior unchanged, and a
        // null matterId preserves the FR-29 DocumentId-only resume match (backward compatible).
        [FromQuery] string? sessionId,
        [FromQuery] string? matterId,
        IComposeService composeService,
        // FR-26 (task 103, gap 3.2): the SPE change-detection origin call. EnsureSubscriptionAsync
        // had ZERO callers, so the container was never tracked → the renewal service renewed an
        // empty set forever AND the poll path had no state to build on. Opening a Compose document
        // is the natural origin: it is exactly when return-from-Word change detection must begin.
        SpeSyncOrchestrator syncOrchestrator,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (string.IsNullOrWhiteSpace(documentSpeId)) return BadRequest("documentSpeId is required.");
        if (string.IsNullOrWhiteSpace(driveId)) return BadRequest("driveId query parameter is required for SPE drive-item access.");
        if (string.IsNullOrWhiteSpace(tenantId)) return BadRequest("tenantId query parameter is required for multi-tenant isolation.");

        logger.LogInformation(
            "Compose load: tenant={TenantId} drive={DriveId} item={DocumentSpeId} record={DocumentRecordId} session={SessionId} matter={MatterId} TraceId={TraceId}",
            tenantId, driveId, documentSpeId, documentRecordId, sessionId, matterId, httpContext.TraceIdentifier);

        try
        {
            var request = new LoadComposeDocumentRequest
            {
                DriveId = driveId,
                DocumentSpeId = documentSpeId,
                TenantId = tenantId,
                DocumentRecordId = documentRecordId,
                DisplayName = displayName,
                // gap 4.1: honor the incoming resume key so a reopen resumes the SAME session.
                SessionId = sessionId,
                MatterId = matterId,
            };

            var result = await composeService.LoadAsync(request, httpContext, ct).ConfigureAwait(false);

            // FR-26 (task 103, gap 3.2): ensure the SPE change-detection subscription exists for
            // this document's drive so a return-from-Word save is detected. The drive id IS a valid
            // container key here — SpeFileStore.ResolveDriveIdAsync returns a `b!` drive id unchanged
            // (ISpeFileOperations contract), and EnsureSubscriptionAsync keys its Redis state by that
            // value consistently with the poll/check-changes path (task 053). When the webhook config
            // (Compose:Webhook:{SigningKey,ClientState,NotificationUrl}) is UNPROVISIONED (owner task
            // 056 / DEF-03), EnsureSubscriptionAsync makes NO Graph call — it persists the container
            // into the tracked index with FallbackToPolling=true, which (a) stops the renewal service
            // renewing an empty set forever and (b) seeds the poll-fallback state the client's
            // poll-on-focus check-changes path drives. Non-fatal by construction: a subscription
            // failure degrades to poll and MUST NOT fail the document load.
            // ✅◐ E2E-pending on task 056 for the webhook-DELIVERY leg (secrets); the origin call +
            // poll fallback are fully wired + testable here.
            try
            {
                await syncOrchestrator.EnsureSubscriptionAsync(result.DriveId ?? driveId, ct).ConfigureAwait(false);
            }
            catch (Exception subEx)
            {
                logger.LogWarning(subEx,
                    "Compose load: EnsureSubscriptionAsync origin call failed for drive {DriveId} (non-fatal; change detection degrades to poll). TraceId={TraceId}",
                    result.DriveId ?? driveId, httpContext.TraceIdentifier);
            }

            return Results.Ok(new LoadComposeDocumentResponse(
                DocumentSpeId: result.DocumentSpeId,
                DriveId: result.DriveId,
                SessionId: result.SessionId,
                DocumentRecordId: result.DocumentRecordId,
                Content: result.Content.ToArray(),
                ETag: result.ETag,
                FileName: result.FileName,
                Size: result.Size,
                // gaps 4.2/4.4: surface the three collections the (unchanged) service already
                // returns from the resumed/created session — previously dropped before the wire.
                AnchoredAnnotations: result.AnchoredAnnotations,
                DefinedTermsTracking: result.DefinedTermsTracking,
                ActionHistory: result.ActionHistory,
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

        var request = new SaveComposeDocumentRequest
        {
            DriveId = body.DriveId,
            DocumentSpeId = documentSpeId,
            // ContainerId is ignored on the replace path (DocumentSpeId present) but forwarded
            // for symmetry so both save routes map the same body shape.
            ContainerId = body.ContainerId,
            Content = body.Content,
            // Replace path still requires a session (guarded above at the endpoint); non-null here.
            SessionId = body.SessionId!,
            TenantId = body.TenantId,
            DocumentRecordId = body.DocumentRecordId,
            DisplayName = body.DisplayName,
        };

        return await ExecuteSaveAsync(request, documentSpeId, composeService, logger, httpContext, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// POST /api/compose/documents/create-on-save — FR-05 create-on-save (task 100). Persists a
    /// TRANSIENT Browse/Upload draft (no SPE drive-item yet) as a new <c>sprk_document</c> in the
    /// client-resolved Business-Unit <c>containerId</c> (Fork A — the BFF does NOT resolve
    /// BU→container; the client passes it in, same convention as the 7 Create*Wizards). Maps a
    /// null <c>DocumentSpeId</c> into <see cref="SaveComposeDocumentRequest"/> so
    /// <see cref="IComposeService.SaveAsync"/> takes its transient-create branch
    /// (container → record → indexing), then rebinds the session's DocumentId to the new record.
    /// </summary>
    private static async Task<IResult> CreateOnSave(
        [FromBody] SaveComposeDocumentBody body,
        IComposeService composeService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (body is null) return BadRequest("Request body is required.");
        if (string.IsNullOrWhiteSpace(body.ContainerId)) return BadRequest("containerId is required for create-on-save (the client resolves it from the user's Business Unit).");
        if (string.IsNullOrWhiteSpace(body.TenantId)) return BadRequest("tenantId is required in the request body.");
        // sessionId is OPTIONAL on the transient-create path (task 110): a Browse/local-file first
        // Save has no chat session. The FR-07 rebind is skipped server-side when it is absent; the
        // SPE create + sprk_document create + indexing all complete without one.
        if (body.Content is null || body.Content.Length == 0) return BadRequest("content is required and must be non-empty.");

        logger.LogInformation(
            "Compose create-on-save: tenant={TenantId} container={ContainerId} session={SessionId} size={SizeBytes} TraceId={TraceId}",
            body.TenantId, body.ContainerId, body.SessionId, body.Content.Length, httpContext.TraceIdentifier);

        var request = new SaveComposeDocumentRequest
        {
            // DocumentSpeId null → SaveAsync transient-create branch. DriveId is derived from
            // ContainerId server-side; the client does not (and cannot) know it for a new draft.
            DocumentSpeId = null,
            DriveId = null,
            ContainerId = body.ContainerId,
            Content = body.Content,
            // Empty when no session is bound (Browse/local-file first Save). The service treats an
            // empty/whitespace SessionId as "no session" and skips the FR-07 rebind (task 110).
            SessionId = body.SessionId ?? string.Empty,
            TenantId = body.TenantId,
            DocumentRecordId = null,
            DisplayName = body.DisplayName,
        };

        return await ExecuteSaveAsync(request, documentSpeId: null, composeService, logger, httpContext, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Shared save execution used by both the replace route (<see cref="Save"/>) and the
    /// create-on-save route (<see cref="CreateOnSave"/>). Delegates to
    /// <see cref="IComposeService.SaveAsync"/> and maps the result / exceptions to HTTP.
    /// </summary>
    private static async Task<IResult> ExecuteSaveAsync(
        SaveComposeDocumentRequest request,
        string? documentSpeId,
        IComposeService composeService,
        ILogger logger,
        HttpContext httpContext,
        CancellationToken ct)
    {
        try
        {
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
                detail: $"SPE drive-item '{documentSpeId ?? "(transient create)"}' was not found or could not be written.",
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

    // ─────────────────────────────────────────────────────────────────────────
    // task 113 (UAT defects 4/5): session-scoped active-document registration.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/compose/active-document — records which document the user is acting on
    /// (session-scoped) so (a) chat can resolve a Compose-direct upload ("summarize this
    /// document") and (b) <c>SendWorkspaceArtifactHandler</c> mounts the just-active document
    /// when the LLM supplies no explicit pointer ("edit in Compose"). Provide EXACTLY ONE of
    /// <c>sessionFileId</c> (a session-uploaded / compose-direct <see cref="ChatSessionFile"/>)
    /// or <c>documentId</c> (a stored <c>sprk_document</c> GUID). Deterministic
    /// <see cref="ChatSession"/> write via <see cref="ChatSessionManager"/> — no AI dispatch
    /// (ADR-039), no SPE/Graph (ADR-007). The compose-direct file's BYTES are landed as a
    /// ChatSessionFile by the EXISTING chat upload endpoint (reused client-side, CLAUDE.md §11);
    /// this endpoint only records the pointer.
    /// </summary>
    private static async Task<IResult> RegisterActiveDocument(
        [FromBody] ComposeActiveDocumentRequest? body,
        ChatSessionManager sessionManager,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (body is null) return BadRequest("Request body is required.");
        if (string.IsNullOrWhiteSpace(body.SessionId)) return BadRequest("sessionId is required.");

        var hasSessionFile = !string.IsNullOrWhiteSpace(body.SessionFileId);
        var hasDocument = !string.IsNullOrWhiteSpace(body.DocumentId);
        if (!hasSessionFile && !hasDocument)
            return BadRequest("Provide sessionFileId (a session-uploaded / compose-direct file) or documentId (a stored sprk_document).");
        if (hasSessionFile && hasDocument)
            return BadRequest("Provide at most one of sessionFileId or documentId — they are mutually exclusive (upload vs stored).");

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

        try
        {
            var (session, sessionKey) = await ResolveSessionAsync(sessionManager, tenantId, body.SessionId, ct)
                .ConfigureAwait(false);
            if (session is null)
            {
                logger.LogWarning(
                    "Compose active-document: session not found tenant={TenantId} session={SessionId} TraceId={TraceId}",
                    tenantId, body.SessionId, httpContext.TraceIdentifier);
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Session Not Found",
                    detail: "The chat session was not found or has expired. Register the active document on an existing session.",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
            }

            ActiveDocumentIdentity identity;
            if (hasSessionFile)
            {
                // Best-effort display name from the session manifest (the bytes were landed as a
                // ChatSessionFile by the existing chat upload endpoint — reused client-side).
                var file = session.UploadedFiles?
                    .FirstOrDefault(f => string.Equals(f.FileId, body.SessionFileId, StringComparison.Ordinal));
                var source = string.IsNullOrWhiteSpace(body.Source)
                    ? ActiveDocumentIdentity.SourceComposeDirect
                    : body.Source!;
                identity = new ActiveDocumentIdentity(
                    Source: source,
                    SessionFileId: body.SessionFileId,
                    FileName: body.FileName ?? file?.FileName,
                    RegisteredAt: DateTimeOffset.UtcNow);
            }
            else
            {
                identity = new ActiveDocumentIdentity(
                    Source: ActiveDocumentIdentity.SourceStored,
                    SprkDocumentId: body.DocumentId,
                    SpeDriveItemId: body.SpeDriveItemId,
                    SpeDriveId: body.SpeDriveId,
                    FileName: body.FileName,
                    RegisteredAt: DateTimeOffset.UtcNow);
            }

            var updated = session with { ActiveDocument = identity };
            await sessionManager.UpdateSessionCacheAsync(updated, ct).ConfigureAwait(false);

            logger.LogInformation(
                "Compose active-document registered: tenant={TenantId} session={SessionKey} source={Source} kind={Kind} TraceId={TraceId}",
                tenantId, sessionKey, identity.Source, hasSessionFile ? "session-file" : "stored", httpContext.TraceIdentifier);

            return Results.Ok(new ComposeActiveDocumentResponse(
                SessionId: body.SessionId,
                Source: identity.Source,
                SessionFileId: identity.SessionFileId,
                DocumentId: identity.SprkDocumentId,
                FileName: identity.FileName,
                CorrelationId: httpContext.TraceIdentifier));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Compose active-document: unexpected failure. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while registering the active document.");
        }
    }

    /// <summary>
    /// Resolves a chat session, probing the client-sent id then its GUID "N"/"D" normalizations —
    /// the same tolerance the Compose upload path applies, since a client may send either spelling.
    /// </summary>
    private static async Task<(ChatSession? Session, string? Key)> ResolveSessionAsync(
        ChatSessionManager sessionManager, string tenantId, string sessionId, CancellationToken ct)
    {
        foreach (var candidate in EnumerateSessionIdForms(sessionId))
        {
            var session = await sessionManager.GetSessionAsync(tenantId, candidate, ct).ConfigureAwait(false);
            if (session is not null) return (session, candidate);
        }
        return (null, null);
    }

    private static IEnumerable<string> EnumerateSessionIdForms(string sessionId)
    {
        yield return sessionId;
        if (Guid.TryParse(sessionId, out var g))
        {
            var n = g.ToString("N");
            var d = g.ToString("D");
            if (!string.Equals(n, sessionId, StringComparison.Ordinal)) yield return n;
            if (!string.Equals(d, sessionId, StringComparison.Ordinal)) yield return d;
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

/// <summary>
/// Request body for <c>POST /api/compose/active-document</c> (task 113 / UAT defects 4/5).
/// Registers the session-scoped active document on the chat session. Provide EXACTLY ONE of
/// <see cref="SessionFileId"/> (a session-uploaded / compose-direct <see cref="ChatSessionFile"/>)
/// or <see cref="DocumentId"/> (a stored <c>sprk_document</c> GUID, D form). <see cref="Source"/>
/// is an optional provenance discriminant (defaults to <c>compose-direct</c> for a session file,
/// <c>stored</c> for a document) — see <see cref="ActiveDocumentIdentity"/>.
/// </summary>
public sealed record ComposeActiveDocumentRequest(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("sessionFileId")] string? SessionFileId = null,
    [property: JsonPropertyName("documentId")] string? DocumentId = null,
    [property: JsonPropertyName("source")] string? Source = null,
    [property: JsonPropertyName("fileName")] string? FileName = null,
    [property: JsonPropertyName("speDriveItemId")] string? SpeDriveItemId = null,
    [property: JsonPropertyName("speDriveId")] string? SpeDriveId = null);

/// <summary>Response shape for <c>POST /api/compose/active-document</c> (task 113) — echoes the
/// registered active-document pointer.</summary>
public sealed record ComposeActiveDocumentResponse(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("sessionFileId")] string? SessionFileId,
    [property: JsonPropertyName("documentId")] string? DocumentId,
    [property: JsonPropertyName("fileName")] string? FileName,
    [property: JsonPropertyName("correlationId")] string CorrelationId);

/// <summary>Request body for <c>POST /api/compose/documents/{id}/save</c> (replace path) and
/// <c>POST /api/compose/documents/create-on-save</c> (FR-05 transient create path, task 100).
/// On the create-on-save path <see cref="DriveId"/> is null and <see cref="ContainerId"/> carries
/// the client-resolved BU container; on the replace path <see cref="ContainerId"/> is ignored.</summary>
public sealed record SaveComposeDocumentBody(
    /// <summary>Bound ChatSession id. OPTIONAL on the create-on-save (transient Browse/local-file)
    /// path (task 110) — absent when the draft has no chat session; the server skips the FR-07
    /// rebind. Still REQUIRED on the replace path (guarded at that endpoint).</summary>
    [property: JsonPropertyName("sessionId")] string? SessionId,
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("content")] byte[] Content,
    [property: JsonPropertyName("driveId")] string? DriveId = null,
    /// <summary>Client-resolved SPE container id for the create-on-save path (Fork A —
    /// businessunit.sprk_containerid). Required when there is no drive-item yet; ignored on replace.</summary>
    [property: JsonPropertyName("containerId")] string? ContainerId = null,
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
    [property: JsonPropertyName("annotations")] IReadOnlyList<DocxAnnotation> Annotations,
    /// <summary>FR-28 (task 055, additive/optional): bound ChatSession id — enables the
    /// Compose-only side of the response's <c>preview</c> split. See
    /// <see cref="PushAnnotationsRequest.SessionId"/> remarks.</summary>
    [property: JsonPropertyName("sessionId")] string? SessionId = null);

/// <summary>Response shape for <c>POST /api/compose/document/{id}/push-annotations</c> (FR-24) —
/// the new SPE version id + ETag the client uses as the next optimistic-concurrency token, plus
/// (FR-28, task 055) the Tier-2c preview + per-step completion state as post-write evidence.</summary>
public sealed record PushAnnotationsResponse(
    [property: JsonPropertyName("documentSpeId")] string DocumentSpeId,
    [property: JsonPropertyName("driveId")] string? DriveId,
    [property: JsonPropertyName("versionId")] string VersionId,
    [property: JsonPropertyName("eTag")] string? ETag,
    [property: JsonPropertyName("size")] long? Size,
    [property: JsonPropertyName("annotationCount")] int AnnotationCount,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("preview")] ComposePushSavePreview? Preview = null,
    [property: JsonPropertyName("completionState")] Sprk.Bff.Api.Services.Ai.PublicContracts.JobAwareCompletionState? CompletionState = null);

/// <summary>Request body for <c>POST /api/compose/document/{id}/push-preview</c> (FR-28, task 055)
/// — the Tier-2c PRE-CONFIRM preview call. Non-mutating: no SPE download, no write. Safe to call
/// repeatedly as the user adjusts accept/reject choices before confirming the gate dialog (not
/// built by this task — see <see cref="IComposeService.PreviewPushAnnotationsAsync"/> remarks).</summary>
public sealed record PushPreviewBody(
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("annotations")] IReadOnlyList<DocxAnnotation> Annotations,
    [property: JsonPropertyName("sessionId")] string? SessionId = null);

/// <summary>Response shape for <c>POST /api/compose/document/{id}/push-preview</c> (FR-28).</summary>
public sealed record PushPreviewResponse(
    [property: JsonPropertyName("preview")] ComposePushSavePreview Preview,
    [property: JsonPropertyName("correlationId")] string CorrelationId);

/// <summary>Response shape for <c>GET /api/compose/documents/{id}</c>. The three FR-29/FR-33
/// collections (task 102, gaps 4.2/4.4) are projected from the (unchanged)
/// <see cref="LoadComposeDocumentResult"/> the service returns for the resumed/created session —
/// this is what makes a reopen restore prior annotations, defined terms, and action history.</summary>
public sealed record LoadComposeDocumentResponse(
    [property: JsonPropertyName("documentSpeId")] string DocumentSpeId,
    [property: JsonPropertyName("driveId")] string? DriveId,
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("documentRecordId")] Guid? DocumentRecordId,
    [property: JsonPropertyName("content")] byte[] Content,
    [property: JsonPropertyName("eTag")] string? ETag,
    [property: JsonPropertyName("fileName")] string? FileName,
    [property: JsonPropertyName("size")] long? Size,
    [property: JsonPropertyName("anchoredAnnotations")] IReadOnlyList<AnchoredAnnotation> AnchoredAnnotations,
    [property: JsonPropertyName("definedTermsTracking")] IReadOnlyList<DefinedTerm> DefinedTermsTracking,
    [property: JsonPropertyName("actionHistory")] IReadOnlyList<ComposeActionHistoryEntry> ActionHistory,
    [property: JsonPropertyName("correlationId")] string CorrelationId);

/// <summary>Request body for <c>POST /api/compose/sessions/{sessionId}/annotations</c> (FR-29,
/// task 102). Partial-replace: a <c>null</c> collection leaves the stored one unchanged; a non-null
/// (possibly empty) collection replaces it wholesale — mirrors
/// <see cref="SaveComposeAnnotationsRequest"/> (sessionId comes from the route).</summary>
public sealed record SaveComposeAnnotationsBody(
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("anchoredAnnotations")] IReadOnlyList<AnchoredAnnotation>? AnchoredAnnotations = null,
    [property: JsonPropertyName("definedTermsTracking")] IReadOnlyList<DefinedTerm>? DefinedTermsTracking = null);

/// <summary>Response shape for the <c>GET/POST /api/compose/sessions/{sessionId}/annotations</c>
/// routes (FR-29, task 102) — the CURRENT session collections after the read/write.</summary>
public sealed record ComposeAnnotationsResponse(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("anchoredAnnotations")] IReadOnlyList<AnchoredAnnotation> AnchoredAnnotations,
    [property: JsonPropertyName("definedTermsTracking")] IReadOnlyList<DefinedTerm> DefinedTermsTracking,
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

/// <summary>Request body for <c>POST /api/compose/document/{id}/pull-annotations</c> (FR-25).</summary>
public sealed record PullAnnotationsBody(
    [property: JsonPropertyName("driveId")] string DriveId,
    [property: JsonPropertyName("tenantId")] string TenantId);

/// <summary>Response shape for <c>POST /api/compose/document/{id}/pull-annotations</c> (FR-25) —
/// the structured comments + revisions <see cref="DocxAnnotationReader"/> recovered from the
/// current SPE bytes, for the Compose UI to re-anchor (task 054).</summary>
public sealed record PullAnnotationsResponse(
    [property: JsonPropertyName("documentSpeId")] string DocumentSpeId,
    [property: JsonPropertyName("driveId")] string? DriveId,
    [property: JsonPropertyName("comments")] IReadOnlyList<RecoveredComment> Comments,
    [property: JsonPropertyName("revisions")] IReadOnlyList<RecoveredRevision> Revisions,
    [property: JsonPropertyName("correlationId")] string CorrelationId);

/// <summary>Request body for <c>POST /api/compose/document/{id}/reanchor-annotations</c> (FR-27).
/// Carries the CLIENT's prior Compose anchored-annotations (from Compose session state) to
/// re-locate against the reloaded document; the reloaded bytes are fetched server-side by driveId
/// + documentSpeId (OBO), so the client sends only the anchors + tenant/drive scoping.</summary>
public sealed record ReanchorAnnotationsBody(
    [property: JsonPropertyName("driveId")] string DriveId,
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("priorAnchors")] IReadOnlyList<PriorAnchor> PriorAnchors);

/// <summary>Response shape for <c>POST /api/compose/document/{id}/reanchor-annotations</c> (FR-27) —
/// the banded re-anchor <see cref="ReanchorSummary"/> the Workspace banner ("N re-anchored, M need
/// review") + conflict UX render.</summary>
public sealed record ReanchorAnnotationsResponse(
    [property: JsonPropertyName("documentSpeId")] string DocumentSpeId,
    [property: JsonPropertyName("driveId")] string? DriveId,
    [property: JsonPropertyName("summary")] ReanchorSummary Summary,
    [property: JsonPropertyName("correlationId")] string CorrelationId);

/// <summary>Response shape for <c>POST /api/compose/webhooks/spe-doc-changed</c> (FR-26) — a
/// same-request summary of how many notifications were verified and how many net SPE changes
/// were enumerated across the (deduplicated) containers they resolved to.</summary>
public sealed record SpeDocChangedWebhookResponse(
    [property: JsonPropertyName("notificationsReceived")] int NotificationsReceived,
    [property: JsonPropertyName("containersProcessed")] int ContainersProcessed,
    [property: JsonPropertyName("changesDetected")] int ChangesDetected,
    [property: JsonPropertyName("correlationId")] string CorrelationId);

/// <summary>Request body for <c>POST /api/compose/document/{id}/check-changes</c> (FR-26). The
/// SPE containerId is required to key the Redis-backed <c>SpeSyncOrchestrator</c> state (task
/// 052) — driveId is resolved internally by the orchestrator, so callers do not supply it.</summary>
public sealed record CheckChangesBody(
    [property: JsonPropertyName("containerId")] string ContainerId);

/// <summary>Response shape for <c>POST /api/compose/document/{id}/check-changes</c> (FR-26) —
/// whether the document's SPE etag differs from the last-observed (Redis-stored) etag, plus the
/// changed item's metadata when it does.</summary>
public sealed record CheckChangesResponse(
    [property: JsonPropertyName("documentSpeId")] string DocumentSpeId,
    [property: JsonPropertyName("containerId")] string ContainerId,
    [property: JsonPropertyName("changed")] bool Changed,
    [property: JsonPropertyName("deleted")] bool Deleted,
    [property: JsonPropertyName("eTag")] string? ETag,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("correlationId")] string CorrelationId);
