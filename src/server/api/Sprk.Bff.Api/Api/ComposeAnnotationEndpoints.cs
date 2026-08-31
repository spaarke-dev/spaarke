using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using static Sprk.Bff.Api.Api.ComposeEndpoints;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Compose <b>annotation</b> routes: the OOXML read direction (<c>.../pull-annotations</c>,
/// <c>.../reanchor-annotations</c>) and the session-state read/write pair
/// (<c>GET|POST /sessions/{sessionId}/annotations</c>).
///
/// <para><b>Reason to change</b>: the annotation + anchor model — what is extracted from a
/// document's <c>w:comment</c>/<c>w:ins</c>/<c>w:del</c>, how a prior anchor re-binds after a Word
/// round-trip (the confidence bands), and what mutable annotation state a session carries.</para>
/// </summary>
internal static class ComposeAnnotationEndpoints
{
    /// <summary>Maps this cluster's routes onto the shared <c>/api/compose</c> group.</summary>
    internal static RouteGroupBuilder MapComposeAnnotationEndpoints(this RouteGroupBuilder group)
    {
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
            .AddSessionOwnershipFilter()
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
            .AddSessionOwnershipFilter()
            .WithName("ComposeSaveAnnotations")
            .WithSummary("Persist a Compose session's anchored annotations + defined-terms (FR-29)")
            // Mutable session UI state in Redis (no SPE/Graph, no AI dispatch) → read/context bucket,
            // not the 5/min ai-upload ingest bucket.
            .RequireRateLimiting("ai-context")
            .Produces<ComposeAnnotationsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        return group;
    }

    // FR-25 (task 051): the reader is a pure, stateless byte[]->record transform (same shape as
    // DocxAnnotationWriter). Instantiated directly here rather than DI-registered, keeping this
    // task's footprint scoped to ComposeEndpoints.cs + the new reader file only (no edits to the
    // shared IComposeService/ComposeService/ComposeModule.cs orchestration surface, which a
    // parallel task may be touching in this shared worktree).
    private static readonly DocxAnnotationReader AnnotationReader = new();

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
        IComposeService composeService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (string.IsNullOrWhiteSpace(sessionId)) return BadRequest("sessionId is required.");

        // Task 059. This took `?tenantId=` and consulted no claim, so the "multi-tenant isolation"
        // the old message claimed was isolation the CALLER chose. Callers may still send the
        // parameter — it is ignored.
        var tenantId = TenantResolution.ResolveTenantId(httpContext.User);
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "Tenant identity ('tid' claim) not found in authentication token.");
        }

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
}

// ─────────────────────────────────────────────────────────────────────────────
// Request / response DTOs
// ─────────────────────────────────────────────────────────────────────────────

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
