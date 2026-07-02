using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Compose;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Compose drafting workspace endpoints — surface FR-04 / FR-05 / FR-06 / FR-09 of
/// <c>spaarkeai-compose-r1</c> as seven minimal-API endpoints under <c>/api/compose/*</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Endpoint surface</b> (locked by spec FR-21, spike #3 §8, and spike #4 §5):
/// <list type="bullet">
///   <item>POST <c>/api/compose/upload</c> — R2-reserved inline upload; in R1 returns 501
///   (callers use the existing Assistant upload pipeline + LoadAsync).</item>
///   <item>GET <c>/api/compose/documents/{documentSpeId}</c> — load DOCX bytes via
///   <see cref="IComposeService.LoadAsync"/>.</item>
///   <item>POST <c>/api/compose/documents/{documentSpeId}/save</c> — save DOCX bytes
///   (includes idempotent first-Save promotion per FR-06).</item>
///   <item>POST <c>/api/compose/documents/{documentSpeId}/promote</c> — explicit
///   first-Save promotion seam (idempotent; matches spike #3 §8 endpoint table).</item>
///   <item>POST <c>/api/compose/documents/{documentId}/checkout</c> — Phase 5 stub
///   (returns 501 in R1; Phase 5 task 050 wires to existing
///   <c>DocumentCheckoutService</c> via the <c>/api/documents/{id}/checkout</c>
///   endpoint per spike #3 §8 reuse-pivot).</item>
///   <item>POST <c>/api/compose/documents/{documentId}/checkin</c> — Phase 5 stub
///   (returns 501 in R1; Phase 5 routes to existing checkin endpoint).</item>
///   <item>POST <c>/api/compose/action/{consumerType}</c> — dispatch a Compose AI
///   action (e.g. <c>compose-summarize</c>) via the PublicContracts facade
///   (<see cref="IConsumerRoutingService"/> + <see cref="IInvokePlaybookAi"/>).</item>
/// </list>
/// </para>
/// <para>
/// <b>ADR compliance verification</b>:
/// <list type="bullet">
///   <item><b>ADR-001</b> Minimal API — uses <c>MapGroup</c> + <c>MapGet</c>/<c>MapPost</c>;
///   delegated handlers per static local function pattern.</item>
///   <item><b>ADR-008</b> Endpoint filters — group applies <c>RequireAuthorization()</c>;
///   every endpoint inherits the auth requirement.</item>
///   <item><b>ADR-013 refined (2026-05-20)</b> — Dispatch handler injects ONLY
///   <see cref="IConsumerRoutingService"/> + <see cref="IInvokePlaybookAi"/> from
///   <c>Services/Ai/PublicContracts/</c>. Does NOT inject <c>IOpenAiClient</c>,
///   <c>IPlaybookService</c>, <c>IPlaybookOrchestrationService</c>, or
///   <c>IPlaybookExecutionEngine</c>. The other six handlers inject the three Compose
///   services from task 021/022/023, all CRUD-shaped.</item>
///   <item><b>ADR-015</b> Tier 3 multi-tenant isolation — inherited via auth pipeline +
///   <c>ChatSession</c> binding via <see cref="ComposeSessionService"/> (concrete per ADR-010 strict).</item>
///   <item><b>ADR-019</b> route conventions — all endpoints under <c>/api/compose/*</c>.</item>
///   <item><b>ADR-028</b> Spaarke Auth v2 — bearer-token auth via existing pipeline (the
///   <c>RequireAuthorization()</c> on the group hooks into the same auth scheme as the
///   rest of the BFF).</item>
///   <item><b>Project CLAUDE.md §10 #6 (asymmetric-registration rule)</b> — endpoints map
///   UNCONDITIONALLY (no feature flag wrapping the <c>MapPost</c>/<c>MapGet</c> calls);
///   service registration in task 025 must match (matches via three unconditional
///   <c>AddScoped</c> calls in <c>ComposeServicesModule</c>).</item>
/// </list>
/// </para>
/// <para>
/// <b>Error model</b>: every handler catches expected failures and returns
/// <see cref="Results.Problem(string?, string?, int?, string?, string?, IDictionary{string, object?}?)"/>
/// per RFC 7807. 400 for validation, 404 for missing drive-item, 503 for "no routing
/// configured" or unknown consumer type, 500 for unexpected. Auth pipeline returns 401
/// automatically.
/// </para>
/// <para>
/// <b>Rate limiting</b>: applied per-endpoint with consumption-appropriate policies:
/// <c>ai-batch</c> for the dispatch endpoint (LLM-billed), <c>ai-upload</c> for save,
/// <c>ai-context</c> for the read-heavy load/promote/checkout/checkin paths.
/// </para>
/// <para>
/// <b>Test surface (per bff-extensions.md §F)</b>: corresponding endpoint-shape tests
/// are owned by task 027 (integration tests landing in
/// <c>tests/integration/Sprk.Bff.Api.IntegrationTests/Compose/</c>). This task's PR
/// closes the test-update obligation via task 027 in the same wave; the unit-test surface
/// for the three Compose services lives in task 026.
/// </para>
/// </remarks>
public static class ComposeEndpoints
{
    /// <summary>
    /// Maps all Compose endpoints under <c>/api/compose</c>. Idempotent — safe to call
    /// once at <see cref="Microsoft.AspNetCore.Builder.WebApplication"/> startup.
    /// </summary>
    public static IEndpointRouteBuilder MapComposeEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        // Group applies the auth requirement once; every endpoint inherits it (ADR-008).
        // Endpoint mapping is UNCONDITIONAL per project CLAUDE.md §10 #6 — no feature
        // flag wraps these calls. Service registration in ComposeServicesModule
        // (task 025) MUST be unconditional to match (avoids RB-T028 asymmetric pattern).
        var group = routes.MapGroup("/api/compose")
            .RequireAuthorization()
            .WithTags("Compose");

        // (1) POST /api/compose/upload — R2-reserved
        group.MapPost("/upload", Upload)
            .WithName("ComposeUpload")
            .WithSummary("Reserved for R2 inline upload; R1 returns 501")
            .RequireRateLimiting("ai-upload")
            .Produces(StatusCodes.Status501NotImplemented)
            .Produces(StatusCodes.Status401Unauthorized);

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
        // Per spike #3 §9 row 050 (REVISED): Phase 5 routes Compose check-out through
        // the existing /api/documents/{documentId}/checkout endpoint owned by
        // DocumentCheckoutService. This Compose-scoped stub exists so the editor host
        // can program against /api/compose/* uniformly today; in R1 it returns 501 with
        // an actionable redirect hint.
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

        // (7) POST /api/compose/action/{consumerType} — AI dispatch
        //
        // spaarkeai-compose-r1 task 097 (Phase 8 SSE backend, per FR-S3): converted
        // from single-shot aggregated JSON response to SSE streaming (Content-Type:
        // text/event-stream). Follows the `WorkspaceFileEndpoints.SummarizeFileSse`
        // precedent + reuses the shared `AnalysisStreamChunk` model. Consumes the
        // task 094 `IDocxTextExtractor` and the task 095/096 widened
        // `IInvokePlaybookAi` facade to thread pre-loaded document context through
        // the playbook orchestration path — eliminates the "AI analysis node
        // requires document context" summarize failure surfaced at UAT.
        //
        // Validation errors return JSON 400 BEFORE SSE headers are set. Once headers
        // are set (`text/event-stream`), all subsequent failures emit `error` SSE
        // events + `[DONE]` sentinel instead of 5xx status codes.
        //
        // PublicContracts facade only — IOpenAiClient / IPlaybookOrchestrationService
        // never injected here (ADR-013 refined + task 095 amendment).
        group.MapPost("/action/{consumerType}", DispatchAction)
            .WithName("ComposeDispatchAction")
            .WithSummary("Dispatch a Compose AI action by consumer type (SSE streaming per task 097)")
            .RequireRateLimiting("ai-batch")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        // (8) POST /api/compose/document/{documentId}/heartbeat — Spike #3 §4.2
        // Refresh the heartbeat timestamp on the caller's active checkout. Used by the
        // Compose editor's 3-min sliding heartbeat (visibility-gated) to keep a
        // Dataverse-side lock alive. The StaleCheckoutSweeperHostedService (Spike #3
        // §4.3) releases locks whose sprk_lastheartbeatutc is older than 15 minutes.
        //
        // Auth: RequireAuthorization() (inherited from group) — caller must be Spaarke-
        // authenticated. The handler additionally enforces a same-user guard inside
        // DocumentCheckoutService.RefreshHeartbeatAsync (only the lock holder can refresh).
        //
        // Rate limit: ai-context — protects against runaway / misbehaving clients firing
        // heartbeats faster than the 3-min interval. (Heartbeat traffic is metadata-only
        // PATCHes to sprk_documents; rate-limited but not on the heavy LLM-billed path.)
        group.MapPost("/document/{documentId:guid}/heartbeat", RefreshHeartbeat)
            .WithName("ComposeRefreshHeartbeat")
            .WithSummary("Refresh the heartbeat timestamp on the caller's active checkout (Spike #3 §4.2)")
            .RequireRateLimiting("ai-context")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        return routes;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handlers — static local-style for testability + DI clarity (ADR-001)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/compose/upload — R2-reserved. R1 routes upload through the existing
    /// Assistant upload pipeline; this seam is preserved for forward compat.
    /// </summary>
    private static IResult Upload(ILoggerFactory loggerFactory, HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");
        logger.LogInformation(
            "Compose upload endpoint called (R1 stub — routes to existing Assistant upload pipeline). TraceId: {TraceId}",
            httpContext.TraceIdentifier);

        return Results.Problem(
            statusCode: StatusCodes.Status501NotImplemented,
            title: "Not Implemented",
            detail: "Compose upload routes through the existing Assistant upload pipeline in R1. " +
                    "Upload via the Assistant, then call GET /api/compose/documents/{documentSpeId} " +
                    "with the resulting SPE drive-item id. Inline upload is reserved for R2.",
            type: "https://tools.ietf.org/html/rfc7231#section-6.6.2");
    }

    /// <summary>
    /// GET /api/compose/documents/{documentSpeId}?driveId={driveId}&amp;tenantId={tenantId}&amp;documentRecordId={guid?}
    /// Load DOCX bytes from SPE via <see cref="IComposeService.LoadAsync"/>.
    /// </summary>
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

        if (string.IsNullOrWhiteSpace(documentSpeId))
        {
            return BadRequest("documentSpeId is required.");
        }

        if (string.IsNullOrWhiteSpace(driveId))
        {
            return BadRequest("driveId query parameter is required for SPE drive-item access.");
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return BadRequest("tenantId query parameter is required for multi-tenant isolation.");
        }

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

    /// <summary>
    /// POST /api/compose/documents/{documentSpeId}/save
    /// Save DOCX bytes to SPE; idempotent first-Save promotion per FR-06.
    /// </summary>
    private static async Task<IResult> Save(
        string documentSpeId,
        [FromBody] SaveComposeDocumentBody body,
        IComposeService composeService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (string.IsNullOrWhiteSpace(documentSpeId))
        {
            return BadRequest("documentSpeId is required.");
        }

        if (body is null)
        {
            return BadRequest("Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(body.DriveId))
        {
            return BadRequest("driveId is required in the request body.");
        }

        if (string.IsNullOrWhiteSpace(body.TenantId))
        {
            return BadRequest("tenantId is required in the request body.");
        }

        if (string.IsNullOrWhiteSpace(body.SessionId))
        {
            return BadRequest("sessionId is required in the request body for first-Save promotion rebind.");
        }

        if (body.Content is null || body.Content.Length == 0)
        {
            return BadRequest("content is required and must be non-empty.");
        }

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

    /// <summary>
    /// POST /api/compose/documents/{documentSpeId}/promote
    /// Idempotently promote an ephemeral SPE drive-item to a <c>sprk_document</c> row.
    /// Matches spike #3 §8 endpoint table; locked surface for FR-06 testing.
    /// </summary>
    private static async Task<IResult> Promote(
        string documentSpeId,
        [FromBody] PromoteComposeDocumentBody body,
        IComposeService composeService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (string.IsNullOrWhiteSpace(documentSpeId))
        {
            return BadRequest("documentSpeId is required.");
        }

        if (body is null)
        {
            return BadRequest("Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(body.SessionId))
        {
            return BadRequest("sessionId is required for the ephemeral→promoted rebind.");
        }

        if (string.IsNullOrWhiteSpace(body.TenantId))
        {
            return BadRequest("tenantId is required for multi-tenant isolation.");
        }

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

    /// <summary>
    /// POST /api/compose/documents/{documentId}/checkout — R1 stub.
    /// Phase 5 task 050 routes through the existing
    /// <c>POST /api/documents/{documentId}/checkout</c> endpoint per spike #3 §9 reuse.
    /// </summary>
    private static IResult Checkout(Guid documentId, ILoggerFactory loggerFactory, HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");
        logger.LogInformation(
            "Compose checkout stub called for documentId={DocumentId} (R1 stub — use /api/documents/{{documentId}}/checkout). TraceId={TraceId}",
            documentId, httpContext.TraceIdentifier);

        return Results.Problem(
            statusCode: StatusCodes.Status501NotImplemented,
            title: "Not Implemented",
            detail: "Compose check-out is wired in Phase 5. In R1, call " +
                    "POST /api/documents/{documentId}/checkout directly per spike #3 §8 reuse pivot.",
            type: "https://tools.ietf.org/html/rfc7231#section-6.6.2");
    }

    /// <summary>
    /// POST /api/compose/documents/{documentId}/checkin — R1 stub.
    /// Phase 5 routes through the existing
    /// <c>POST /api/documents/{documentId}/checkin</c> endpoint.
    /// </summary>
    private static IResult Checkin(Guid documentId, ILoggerFactory loggerFactory, HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");
        logger.LogInformation(
            "Compose checkin stub called for documentId={DocumentId} (R1 stub — use /api/documents/{{documentId}}/checkin). TraceId={TraceId}",
            documentId, httpContext.TraceIdentifier);

        return Results.Problem(
            statusCode: StatusCodes.Status501NotImplemented,
            title: "Not Implemented",
            detail: "Compose check-in is wired in Phase 5. In R1, call " +
                    "POST /api/documents/{documentId}/checkin directly per spike #3 §8 reuse pivot.",
            type: "https://tools.ietf.org/html/rfc7231#section-6.6.2");
    }

    /// <summary>
    /// POST /api/compose/action/{consumerType} — dispatch a Compose AI action via SSE.
    ///
    /// spaarkeai-compose-r1 task 097 (Phase 8 SSE backend, per FR-S3): converted from
    /// single-shot aggregated JSON to <c>text/event-stream</c>. Follows the
    /// <c>WorkspaceFileEndpoints.SummarizeFileSse</c> precedent. Per refined ADR-013
    /// (2026-05-20) + task 095 amendment — injects ONLY
    /// <see cref="IConsumerRoutingService"/> + <see cref="IInvokePlaybookAi"/>
    /// + <see cref="IComposeDocumentService"/> + <see cref="IDocxTextExtractor"/>.
    /// </summary>
    /// <remarks>
    /// SSE event shape (reuses <see cref="AnalysisStreamChunk"/>):
    /// <list type="bullet">
    ///   <item><c>data: {"type":"progress","step":"document_loaded"}</c></item>
    ///   <item><c>data: {"type":"progress","step":"extracting_text"}</c></item>
    ///   <item><c>data: {"type":"progress","step":"invoking_playbook"}</c></item>
    ///   <item><c>data: {"type":"result","content":"..."}</c> (once — final terminal-node output)</item>
    ///   <item><c>data: {"type":"done","analysisId":"...","tokenUsage":{...}}</c></item>
    ///   <item><c>data: [DONE]</c> (sentinel — clients close the stream on this)</item>
    /// </list>
    ///
    /// Validation errors (400) are emitted BEFORE SSE headers are set. Once the
    /// response is committed to <c>text/event-stream</c>, failures use
    /// <c>AnalysisStreamChunk.FromError(...)</c> followed by <c>[DONE]</c> — the HTTP
    /// status stays 200.
    /// </remarks>
    private static async Task DispatchAction(
        string consumerType,
        [FromBody] ComposeActionRequest body,
        IConsumerRoutingService routing,
        IInvokePlaybookAi invokePlaybook,
        IComposeDocumentService documentService,
        IDocxTextExtractor docxTextExtractor,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");
        var response = httpContext.Response;

        // Pre-SSE validation gate. Once SSE headers are set below, all subsequent
        // failures MUST emit SSE error chunks — mixing 4xx after SSE headers is a
        // protocol violation.
        if (string.IsNullOrWhiteSpace(consumerType))
        {
            await WriteJsonBadRequestAsync(response, "consumerType is required in the URL path.", ct);
            return;
        }

        if (body is null)
        {
            await WriteJsonBadRequestAsync(response, "Request body is required.", ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(body.TenantId))
        {
            await WriteJsonBadRequestAsync(response, "tenantId is required for multi-tenant isolation.", ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(body.DocumentSpeId))
        {
            await WriteJsonBadRequestAsync(response, "documentSpeId is required.", ct);
            return;
        }

        // Defense-in-depth: validate that the consumer type is a known constant BEFORE
        // touching the routing layer or requiring driveId. Spike #4 §8 row "Unknown
        // consumer" → 404. Preserved ahead of the driveId check (task 097) so callers
        // that supply an obviously-wrong consumer type still get 404 rather than
        // 400-driveId (avoids leaking which field would have been checked next).
        if (!ConsumerTypes.All.Contains(consumerType, StringComparer.Ordinal))
        {
            logger.LogInformation(
                "Compose dispatch: unknown consumerType={ConsumerType} TraceId={TraceId}",
                consumerType, httpContext.TraceIdentifier);

            await WriteJsonProblemAsync(
                response,
                StatusCodes.Status404NotFound,
                title: "Unknown Consumer Type",
                detail: $"Consumer type '{consumerType}' is not registered. " +
                        $"Known types: {string.Join(", ", ConsumerTypes.All)}.",
                ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(body.DriveId))
        {
            await WriteJsonBadRequestAsync(
                response,
                "driveId is required to load the document from SPE for AI dispatch (task 097 SSE conversion).",
                ct);
            return;
        }

        logger.LogInformation(
            "Compose dispatch SSE: consumerType={ConsumerType} tenant={TenantId} item={DocumentSpeId} record={DocumentRecordId} session={SessionId} TraceId={TraceId}",
            consumerType, body.TenantId, body.DocumentSpeId, body.DocumentRecordId, body.SessionId, httpContext.TraceIdentifier);

        // Set SSE headers — from here on, ALL failures emit error chunks + [DONE].
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";
        httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        try
        {
            // Stage 1: resolve the playbook via Dataverse-backed sprk_playbookconsumer table.
            var routingContext = new RoutingContext
            {
                MimeType = body.DocumentMimeType,
                DocumentType = null, // Phase 4 classification pipeline not in R1 scope
            };

            var playbookId = await routing.ResolveAsync(
                consumerType: consumerType,
                consumerCode: "default",
                context: routingContext,
                environment: null,
                cancellationToken: ct).ConfigureAwait(false);

            if (!playbookId.HasValue || playbookId.Value == Guid.Empty)
            {
                logger.LogInformation(
                    "Compose dispatch SSE: no playbook routing configured for consumerType={ConsumerType} TraceId={TraceId}",
                    consumerType, httpContext.TraceIdentifier);

                await WriteSSEAsync(
                    response,
                    AnalysisStreamChunk.FromError(
                        $"No playbook routing is configured for consumer type '{consumerType}'. " +
                        "Verify the sprk_playbookconsumer row exists and is enabled in this environment."),
                    ct);
                await response.WriteAsync("data: [DONE]\n\n", ct);
                await response.Body.FlushAsync(ct);
                return;
            }

            // Stage 2: load DOCX bytes from SPE via IComposeDocumentService (existing
            // OBO plumbing, no change to loader semantics).
            await WriteSSEAsync(
                response,
                AnalysisStreamChunk.Progress("document_loaded", "Loading document from SharePoint Embedded..."),
                ct);

            var loadResultRaw = await documentService
                .LoadDocxAsync(httpContext, body.DriveId, body.DocumentSpeId, ct)
                .ConfigureAwait(false);
            using var loadResult = new LoadResultLease(loadResultRaw);

            if (!loadResult.Value.Found || loadResult.Value.Content is null)
            {
                logger.LogInformation(
                    "Compose dispatch SSE: SPE drive-item not found. drive={DriveId} item={DocumentSpeId} TraceId={TraceId}",
                    body.DriveId, body.DocumentSpeId, httpContext.TraceIdentifier);

                await WriteSSEAsync(
                    response,
                    AnalysisStreamChunk.FromError(
                        $"Document '{body.DocumentSpeId}' was not found in drive '{body.DriveId}'. " +
                        "The file may have been moved or deleted since the editor opened."),
                    ct);
                await response.WriteAsync("data: [DONE]\n\n", ct);
                await response.Body.FlushAsync(ct);
                return;
            }

            // Stage 3: extract plain text via IDocxTextExtractor (task 094).
            await WriteSSEAsync(
                response,
                AnalysisStreamChunk.Progress("extracting_text", "Extracting document text..."),
                ct);

            string extractedText;
            try
            {
                extractedText = await docxTextExtractor
                    .ExtractPlainTextAsync(loadResult.Value.Content, cancellationToken: ct)
                    .ConfigureAwait(false);
            }
            catch (InvalidDataException ex)
            {
                logger.LogWarning(ex,
                    "Compose dispatch SSE: DOCX extraction failed (malformed input). TraceId={TraceId}",
                    httpContext.TraceIdentifier);
                await WriteSSEAsync(
                    response,
                    AnalysisStreamChunk.FromError(
                        "The document could not be parsed as a valid DOCX file. " +
                        "Please try re-saving the document and dispatching again."),
                    ct);
                await response.WriteAsync("data: [DONE]\n\n", ct);
                await response.Body.FlushAsync(ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                logger.LogWarning(
                    "Compose dispatch SSE: extracted text is empty. TraceId={TraceId}",
                    httpContext.TraceIdentifier);
                await WriteSSEAsync(
                    response,
                    AnalysisStreamChunk.FromError(
                        "No text could be extracted from the document body. " +
                        "The playbook cannot run without document content."),
                    ct);
                await response.WriteAsync("data: [DONE]\n\n", ct);
                await response.Body.FlushAsync(ct);
                return;
            }

            logger.LogInformation(
                "Compose dispatch SSE: text extraction complete. chars={CharCount} TraceId={TraceId}",
                extractedText.Length, httpContext.TraceIdentifier);

            // Stage 4: invoke the playbook via the widened facade (task 095/096).
            await WriteSSEAsync(
                response,
                AnalysisStreamChunk.Progress("invoking_playbook", "Invoking playbook..."),
                ct);

            // Build parameters dict from the compose-document scope payload (spike #4 §4.2).
            // ADR-015 binding: all values are deterministic identifiers / display values.
            var parameters = BuildScopeParameters(body, consumerType);

            // Build the document-context DTO forwarded through the widened facade.
            var documentContext = new DocumentContext
            {
                DocumentId = body.DocumentRecordId ?? Guid.Empty,
                Name = body.DocumentName ?? loadResult.Value.FileName ?? body.DocumentSpeId,
                FileName = loadResult.Value.FileName ?? body.DocumentName,
                ContentType = body.DocumentMimeType,
                ExtractedText = extractedText,
            };

            var invocationContext = new PlaybookInvocationContext
            {
                TenantId = body.TenantId,
                HttpContext = httpContext,
                CorrelationId = httpContext.TraceIdentifier,
            };

            var result = await invokePlaybook
                .InvokePlaybookAsync(
                    playbookId.Value,
                    parameters,
                    invocationContext,
                    ct,
                    userContext: extractedText,
                    document: documentContext)
                .ConfigureAwait(false);

            // Stage 5: project the final result onto SSE events.
            if (result.Success)
            {
                var resultPayload = JsonSerializer.Serialize(
                    new
                    {
                        runId = result.RunId,
                        textContent = result.TextContent,
                        structuredData = result.StructuredData,
                        confidence = result.Confidence,
                        durationMs = (long)result.Duration.TotalMilliseconds,
                        citationCount = result.Citations.Count,
                        correlationId = httpContext.TraceIdentifier,
                    },
                    SseJsonOptions);

                await WriteSSEAsync(response, AnalysisStreamChunk.Result(resultPayload), ct);
                await WriteSSEAsync(
                    response,
                    AnalysisStreamChunk.Completed(result.RunId, new TokenUsage(0, 0)),
                    ct);
            }
            else
            {
                await WriteSSEAsync(
                    response,
                    AnalysisStreamChunk.FromError(
                        result.ErrorMessage ?? result.ErrorCode ?? "Playbook invocation failed."),
                    ct);
            }

            await response.WriteAsync("data: [DONE]\n\n", ct);
            await response.Body.FlushAsync(ct);
        }
        catch (Sprk.Bff.Api.Configuration.FeatureDisabledException ex)
        {
            // ADR-032 P3 — kill-switch off → SSE error chunk (HTTP status stays 200
            // because headers were already committed).
            logger.LogWarning(ex,
                "Compose dispatch SSE: AI feature disabled. consumerType={ConsumerType} errorCode={ErrorCode} TraceId={TraceId}",
                consumerType, ex.ErrorCode, httpContext.TraceIdentifier);
            await WriteSSEAsync(
                response,
                AnalysisStreamChunk.FromError($"[{ex.ErrorCode}] {ex.Message}"),
                CancellationToken.None);
            await response.WriteAsync("data: [DONE]\n\n", CancellationToken.None);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "Compose dispatch SSE: timed out. TraceId={TraceId}",
                httpContext.TraceIdentifier);
            await WriteSSEAsync(
                response,
                AnalysisStreamChunk.FromError("Playbook invocation timed out. Please try again."),
                CancellationToken.None);
            await response.WriteAsync("data: [DONE]\n\n", CancellationToken.None);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "Compose dispatch SSE: unexpected failure. consumerType={ConsumerType} TraceId={TraceId}",
                consumerType, httpContext.TraceIdentifier);
            await WriteSSEAsync(
                response,
                AnalysisStreamChunk.FromError(
                    $"Compose dispatch failed: {ex.GetType().Name}: {ex.Message}. TraceId={httpContext.TraceIdentifier}"),
                CancellationToken.None);
            await response.WriteAsync("data: [DONE]\n\n", CancellationToken.None);
        }
    }

    /// <summary>
    /// Serialization options for the compose SSE result payload. Matches the shape
    /// clients previously parsed from <c>ComposeActionResponse</c> so the payload
    /// inside the <c>result</c> chunk stays field-compatible.
    /// </summary>
    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Writes a JSON <c>ProblemDetails</c>-shaped 400 to the response BEFORE any
    /// SSE headers are set. Called during pre-SSE validation only — never after
    /// <c>ContentType=text/event-stream</c> has been committed.
    /// </summary>
    private static async Task WriteJsonBadRequestAsync(HttpResponse response, string detail, CancellationToken ct)
    {
        response.StatusCode = StatusCodes.Status400BadRequest;
        response.ContentType = "application/problem+json";
        var problem = JsonSerializer.Serialize(new
        {
            title = "Bad Request",
            status = 400,
            detail,
        }, SseJsonOptions);
        await response.WriteAsync(problem, ct);
    }

    /// <summary>
    /// Writes a JSON <c>ProblemDetails</c>-shaped error with an arbitrary status
    /// code BEFORE any SSE headers are set (e.g. 404 for unknown consumer type).
    /// </summary>
    private static async Task WriteJsonProblemAsync(
        HttpResponse response,
        int statusCode,
        string title,
        string detail,
        CancellationToken ct)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/problem+json";
        var problem = JsonSerializer.Serialize(new
        {
            title,
            status = statusCode,
            detail,
        }, SseJsonOptions);
        await response.WriteAsync(problem, ct);
    }

    /// <summary>
    /// Serializes an <see cref="AnalysisStreamChunk"/> as a single SSE
    /// <c>data:</c> frame and flushes the response body. Mirrors the
    /// <c>WorkspaceFileEndpoints.WriteSSEAsync</c> helper (shape kept private to
    /// each endpoint per ADR-001 preference for endpoint locality).
    /// </summary>
    private static async Task WriteSSEAsync(HttpResponse response, AnalysisStreamChunk chunk, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(chunk, SseJsonOptions);
        await response.WriteAsync($"data: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    /// <summary>
    /// Wraps a <see cref="ComposeLoadResult"/> so the outer `using` statement can
    /// dispose the underlying <see cref="Stream"/> even when the load result is the
    /// `NotFound` singleton (whose <c>Content</c> is null). Keeps the happy-path
    /// code linear without a nested try/finally.
    /// </summary>
    private readonly struct LoadResultLease : IDisposable
    {
        public LoadResultLease(ComposeLoadResult value) { Value = value; }
        public ComposeLoadResult Value { get; }
        public void Dispose() => Value.Content?.Dispose();
    }

    /// <summary>
    /// POST /api/compose/document/{documentId}/heartbeat
    /// Refresh the heartbeat timestamp on the caller's active checkout (Spike #3 §4.2).
    /// Returns 204 NoContent on success, 404 if the caller has no active checkout to
    /// refresh (document not found, not checked out, or held by another user — all
    /// three collapse to 404 to avoid leaking lock-state info to non-owners).
    /// </summary>
    private static async Task<IResult> RefreshHeartbeat(
        Guid documentId,
        DocumentCheckoutService checkoutService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (documentId == Guid.Empty)
        {
            return BadRequest("documentId is required.");
        }

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

            // Doc missing, not checked out, or held by another user — all three collapse
            // to 404 here. The service has already logged the specific reason for ops.
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "No active checkout to refresh",
                detail: "The document was not found, is not checked out, or the caller does not own the active lock.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
        }
        catch (InvalidOperationException ex)
        {
            // Surfaced from GetUserId when oid claim missing — auth contract violation.
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
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static IResult BadRequest(string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            detail: detail,
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");

    /// <summary>
    /// Build the playbook parameters dict from the compose-document / compose-selection
    /// scope payload (spike #4 §4). All values are deterministic identifiers / display
    /// values per ADR-015 binding.
    /// </summary>
    private static Dictionary<string, string> BuildScopeParameters(
        ComposeActionRequest body,
        string consumerType)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["consumerType"] = consumerType,
            ["documentSpeId"] = body.DocumentSpeId,
            ["tenantId"] = body.TenantId,
        };

        if (!string.IsNullOrWhiteSpace(body.SessionId))
        {
            parameters["sessionId"] = body.SessionId;
        }

        if (!string.IsNullOrWhiteSpace(body.DriveId))
        {
            parameters["driveId"] = body.DriveId;
        }

        if (body.DocumentRecordId.HasValue && body.DocumentRecordId.Value != Guid.Empty)
        {
            parameters["documentRecordId"] = body.DocumentRecordId.Value.ToString();
        }

        if (body.MatterId.HasValue && body.MatterId.Value != Guid.Empty)
        {
            parameters["matterId"] = body.MatterId.Value.ToString();
        }

        if (!string.IsNullOrWhiteSpace(body.DocumentName))
        {
            parameters["documentName"] = body.DocumentName;
        }

        if (!string.IsNullOrWhiteSpace(body.DocumentMimeType))
        {
            parameters["documentMimeType"] = body.DocumentMimeType;
        }

        if (!string.IsNullOrWhiteSpace(body.DocumentVersionEtag))
        {
            parameters["documentVersionEtag"] = body.DocumentVersionEtag;
        }

        // Selection-scoped fields (compose-selection per spike #4 §4.1) — optional in R1.
        if (body.Selection is not null && !string.IsNullOrWhiteSpace(body.Selection.SelectionText))
        {
            parameters["selectionText"] = body.Selection.SelectionText;
            parameters["selectionAnchorStart"] = body.Selection.SelectionAnchorStart.ToString();
            parameters["selectionAnchorEnd"] = body.Selection.SelectionAnchorEnd.ToString();
        }

        return parameters;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Request / response DTOs (per POML Step 1 — "Keep DTOs alongside endpoints")
// ─────────────────────────────────────────────────────────────────────────────

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

/// <summary>Request body for <c>POST /api/compose/action/{consumerType}</c> — locked
/// shape per spike #4 §6 (compose-document + optional compose-selection scope fields).
/// </summary>
public sealed record ComposeActionRequest(
    [property: JsonPropertyName("documentSpeId")] string DocumentSpeId,
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("sessionId")] string? SessionId = null,
    [property: JsonPropertyName("driveId")] string? DriveId = null,
    [property: JsonPropertyName("documentRecordId")] Guid? DocumentRecordId = null,
    [property: JsonPropertyName("matterId")] Guid? MatterId = null,
    [property: JsonPropertyName("documentName")] string? DocumentName = null,
    [property: JsonPropertyName("documentMimeType")] string? DocumentMimeType = null,
    [property: JsonPropertyName("documentVersionEtag")] string? DocumentVersionEtag = null,
    [property: JsonPropertyName("selection")] ComposeSelectionPayload? Selection = null);

/// <summary>Optional selection-scoped payload (compose-selection scope per spike #4 §4.1);
/// reserved for R2 selection-scoped actions; null for whole-document R1 dispatches.</summary>
public sealed record ComposeSelectionPayload(
    [property: JsonPropertyName("selectionText")] string SelectionText,
    [property: JsonPropertyName("selectionAnchorStart")] int SelectionAnchorStart,
    [property: JsonPropertyName("selectionAnchorEnd")] int SelectionAnchorEnd);

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

/// <summary>Response shape for <c>POST /api/compose/action/{consumerType}</c> — per
/// spike #4 §6 locked surface (mirrors <c>PlaybookInvocationResult</c> projection).</summary>
public sealed record ComposeActionResponse(
    [property: JsonPropertyName("runId")] Guid RunId,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("textContent")] string? TextContent,
    [property: JsonPropertyName("structuredData")] JsonElement? StructuredData,
    [property: JsonPropertyName("confidence")] double? Confidence,
    [property: JsonPropertyName("durationMs")] long DurationMs,
    [property: JsonPropertyName("citationCount")] int CitationCount,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage,
    [property: JsonPropertyName("errorCode")] string? ErrorCode,
    [property: JsonPropertyName("correlationId")] string CorrelationId);
