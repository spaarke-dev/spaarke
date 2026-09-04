using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Compose;
using static Sprk.Bff.Api.Api.ComposeEndpoints;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Compose <b>transient mount</b> routes: <c>POST /api/compose/upload</c> and
/// <c>POST /api/compose/project</c>.
///
/// <para><b>Reason to change</b>: how bytes reach the editor for a draft that has no
/// <c>sprk_document</c> yet — the retained-bytes cache convention shared with the chat upload
/// pipeline, and the stateless bytes-&gt;projection contract for the Browse-local-file door.</para>
/// </summary>
internal static class ComposeMountEndpoints
{
    /// <summary>Maps this cluster's routes onto the shared <c>/api/compose</c> group.</summary>
    internal static RouteGroupBuilder MapComposeMountEndpoints(this RouteGroupBuilder group)
    {
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
            // Read-shaped: a deterministic Redis serve of already-retained bytes (NOT an SPE upload).
            // Belongs on the read bucket like sibling Load (2), not the 5/min ai-upload ingest bucket —
            // sharing ai-upload caused interactive drafting to 429 (UAT 2026-07-14).
            .RequireRateLimiting("ai-context")
            .Produces<ComposeUploadResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        // (1b) POST /api/compose/project — FR-03 (task 011, spaarkeai-compose-fidelity-r4.5, T-2):
        //     stateless, read-only DOCX->projection endpoint for the Browse-local-file door. Takes
        //     the caller-supplied bytes directly and renders them — NO ITenantCache write, NO SPE
        //     write, NO sprk_document authoring; a call leaves zero server-side state. This is a
        //     projection READ, not byte-authoring, so it does NOT violate ADR-040 / R4 I-2 (the
        //     client still authors no .docx bytes) — see design.md §9 T-2 path-A resolution. Reuses
        //     the SAME IComposeService.ProjectDocument seam Upload (1) uses, so Browse renders
        //     through the one reader (F-2) instead of the client mammoth fallback.
        group.MapPost("/project", Project)
            .WithName("ComposeProject")
            .WithSummary("Stateless bytes->projection render for the Browse-local-file door (FR-03, no persistence)")
            // #696 (DEF-02): this door runs SYNCHRONOUS OOXML projection on caller-supplied bytes, and had
            // only Kestrel's implicit ~28.6 MB body cap. Bounded on the same two levels the save routes use
            // (ComposeSaveEndpoints), from the same constants, so a document that Compose would refuse to
            // SAVE is not one it will burn CPU projecting. The transport cap is deliberately the LARGER
            // number: base64 inflates by 4/3 inside a JSON envelope, so a cap set at MaxDocumentBytes would
            // reject a legal 25 MB document at the transport with no body — the unexplained-413 failure
            // FR-S08 removed. The honest per-document check lives in the handler.
            .WithMetadata(new RequestSizeLimitAttribute(ComposeSaveLimits.MaxRequestBodyBytes))
            // Read-shaped, deterministic, in-memory CPU work — same bucket as sibling Upload/Load,
            // not a persistence/ingest bucket (nothing is written).
            .RequireRateLimiting("ai-context")
            .Produces<ComposeProjectResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);
            // Deliberately NOT .Produces(413): the raised cap is a transport backstop, and an oversize
            // document is refused by the handler as a 400 ProblemDetails that names the limit. The save
            // routes declare their responses the same way — a 413 in the contract would advertise a
            // bodiless rejection as a normal outcome, which is the failure mode FR-S08 removed.

        return group;
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
        // FR-01 (task 010, spaarkeai-compose-fidelity-r4.5): project the retained bytes through the
        // SAME builder LoadAsync uses (IComposeService.ProjectDocument), so this door renders via the
        // one-reader projection branch instead of the client mammoth fallback (F-2).
        IComposeService composeService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (body is null) return BadRequest("Request body is required.");
        if (string.IsNullOrWhiteSpace(body.SessionId)) return BadRequest("sessionId is required.");
        if (string.IsNullOrWhiteSpace(body.DocumentId)) return BadRequest("documentId (the session-uploaded file id) is required.");

        // Tenant scoping (ADR-014): the caller's dual-form tid claim, via the one resolver every
        // endpoint shares, so the cache key resolves identically across the BFF (task 059).
        var tenantId = TenantResolution.ResolveTenantId(httpContext.User);

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

            // FR-01 (task 010, spaarkeai-compose-fidelity-r4.5, WS-1 "one reader everywhere"): run the
            // retained bytes through the SAME projection builder LoadAsync uses so the assistant-upload
            // door renders through the one reader (F-2) instead of the client mammoth fallback.
            // Task 012 (the client cutover): upgraded ProjectDocument → ProjectForMount — the bytes are
            // paraId-minted FIRST (in-memory, fail-open) and BOTH the HTML projection and the canonical
            // content model are built from the same minted bytes, so the editor's node ids and the
            // retained model's block ids agree; the minted bytes are what this response returns as
            // Content (the client's retained mount baseline). Fail-closed + best-effort — a non-.docx
            // upload (e.g. a retained .pdf/.txt) or an unreadable source yields Status=Failed/
            // CanEdit=false + a null model (never throws); the client keys off Status/CanEdit, not
            // Html.Length, so this never fails the upload-mount itself (mirrors Load's own contract).
            // Task 050 (spaarkeai-compose-r7, FR-06): pass the sidecar fileName so a PDF upload forks onto
            // the intake leg (bytes-first detection also catches a mis-named .pdf); await the now-async
            // ProjectForMount (the docx path stays synchronous-fast — the PDF branch is the only awaited I/O).
            // FR-A08 (task 044): this door HAS a session (required above), so a PDF upload records the
            // server-side "PDF-sourced" fact and its first save stamps the record Authored. The sibling
            // Browse door below deliberately passes none — it is contracted stateless.
            var mount = await composeService.ProjectForMount(binary, fileName, ct, body.SessionId);
            var projection = mount.Projection;
            binary = mount.Content.ToArray();
            if (projection.Status == ComposeProjectionStatus.Failed)
            {
                logger.LogWarning(
                    "Compose upload-mount: DOCX projection failed for tenant={TenantId} session={SessionId} document={DocumentId} (code={Code}); client will fail closed (read-only / Open in Word) TraceId={TraceId}",
                    tenantId, body.SessionId, body.DocumentId, projection.Warnings.FirstOrDefault()?.Code, httpContext.TraceIdentifier);
            }
            else if (projection.Warnings.Count > 0)
            {
                logger.LogInformation(
                    "Compose upload-mount: DOCX projection partial for tenant={TenantId} session={SessionId} document={DocumentId}; warnings={Warnings}",
                    tenantId, body.SessionId, body.DocumentId,
                    string.Join(",", projection.Warnings.Select(w => $"{w.Code}:{w.Count}")));
            }

            return Results.Ok(new ComposeUploadResponse(
                SessionId: body.SessionId,
                DocumentId: body.DocumentId,
                FileName: fileName,
                ContentType: contentType,
                Content: binary,
                Size: binary.Length,
                Projection: MapProjectionResponse(projection),
                CorrelationId: httpContext.TraceIdentifier,
                // Task 012: the retained canonical model for the imported-save mapper (see
                // LoadComposeDocumentResponse.ContentModel). Built from the SAME minted Content above.
                ContentModel: mount.ContentModel,
                // Task 013 (012-review F7): canonical-projection flatten warnings for the client fold.
                ContentModelWarnings: MapWarningResponses(mount.ContentModelWarnings),
                // Task 050 (FR-06): the PDF-source marker (task 051 keys the honest-lossiness UX + the
                // save-as-docx flow off it). Null for a native docx upload.
                SourceFormat: mount.SourceFormat,
                // FR-S08 (r8 task 015): advertise the enforced limit so the client pre-flights against
                // the SAME number the endpoint checks. One source; the client never hard-codes it.
                MaxDocumentBytes: ComposeSaveLimits.MaxDocumentBytes));
        }
        catch (ComposePdfIntakeException ex)
        {
            // Task 050 (FR-06): the now-async ProjectForMount forks a PDF upload onto the intake leg,
            // so intake unavailability/failure surfaces here too — the SAME honest ProblemDetails the
            // Load door maps (503 retryable-unavailable vs 422 not-projectable), never a generic 500.
            // MUST precede the general Exception catch (ComposePdfIntakeException derives from
            // InvalidOperationException).
            logger.LogWarning(ex,
                "Compose upload-mount: PDF intake refused (unavailable={Unavailable}) tenant={TenantId} session={SessionId} document={DocumentId}. TraceId={TraceId}",
                ex.Unavailable, tenantId, body.SessionId, body.DocumentId, httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: ex.Unavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status422UnprocessableEntity,
                title: ex.Unavailable ? "PDF Intake Unavailable" : "PDF Not Editable",
                detail: ex.Message);
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
    /// POST /api/compose/project — FR-03 (task 011, spaarkeai-compose-fidelity-r4.5, T-2 path-A
    /// resolution): a stateless, read-only bytes-&gt;projection endpoint for the Browse-local-file
    /// door. Unlike <see cref="Upload"/> (which reads PERSISTED retained bytes back out of
    /// <c>ITenantCache</c>), this handler takes the caller-supplied bytes directly off the wire and
    /// renders them — it writes NOTHING (no <c>ITenantCache</c> entry, no SPE call, no
    /// <c>sprk_document</c> row, no session-ledger mutation). A repeated call for the same bytes
    /// leaves zero cumulative server-side state (idempotent, stateless). This preserves the ADR-040 /
    /// R4 I-2 invariant that the client authors no <c>.docx</c> bytes: the server only READS bytes
    /// the client already holds locally and hands back a render; it never stores, echoes back as an
    /// authored artifact, or otherwise retains what it was sent. Fail-closed like Load/Upload:
    /// unreadable bytes yield <c>Status=Failed</c>/<c>CanEdit=false</c> in the 200 response body,
    /// never a 500 (a malformed/non-.docx upload is a normal, expected input, not a server error).
    /// </summary>
    private static async Task<IResult> Project(
        [FromBody] ComposeProjectRequest? body,
        IComposeService composeService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (body is null) return BadRequest("Request body is required.");
        if (body.Content is null || body.Content.Length == 0)
            return BadRequest("content (the document's raw bytes) is required.");

        // #696 (DEF-02): refuse an oversize document BEFORE the synchronous projection, with the same
        // limit and the same voice as the save routes. Two reasons it is a ProblemDetails naming the
        // number rather than a bare transport rejection: a user who opens a 40 MB file needs to know what
        // to do about it, and a door that refused SILENTLY here while the save door explained itself would
        // read as two different products.
        if (body.Content.Length > ComposeSaveLimits.MaxDocumentBytes)
        {
            logger.LogWarning(
                "Compose project refused: document is {Size} bytes, over the {Limit}-byte limit (file={FileName}). TraceId={TraceId}",
                body.Content.Length, ComposeSaveLimits.MaxDocumentBytes, body.FileName, httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Document Too Large",
                detail: $"This document is {body.Content.Length / (1024 * 1024)} MB, and Compose can open documents up to " +
                        $"{ComposeSaveLimits.MaxDocumentDisplay}. Remove or compress large embedded images, or split the " +
                        "document, then try again.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        // The SAME builder instance LoadAsync/Upload use, so Browse renders through the one reader (F-2),
        // not a forked projection path. Task 050 (FR-06): the DOCX render stays pure/synchronous/no-I/O
        // (ADR-007/ADR-013); a PDF source (bytes-first detection via body.FileName + magic bytes) forks
        // onto the ONE ProjectPdfToDocxAsync intake leg — the single reason this handler is now async, and
        // reached ONLY on the PDF branch. This door still persists NOTHING either way.
        // Task 012 (the client cutover): upgraded ProjectDocument → ProjectForMount — mint paraIds
        // FIRST (in-memory; this door still persists NOTHING), then build the HTML projection AND the
        // canonical content model from the same minted bytes so their ids agree. When minting mutated
        // the bytes, the response echoes them (`content`) so the client adopts the id-carrying copy as
        // its retained mount baseline; when nothing needed minting the echo is omitted (the caller's
        // own bytes are already identical — no payload growth).
        try
        {
            var mount = await composeService.ProjectForMount(body.Content, body.FileName, ct);
            var projection = mount.Projection;
            if (projection.Status == ComposeProjectionStatus.Failed)
            {
                logger.LogWarning(
                    "Compose project: DOCX projection failed for file={FileName} (code={Code}); client will fail closed (read-only / Open in Word) TraceId={TraceId}",
                    body.FileName, projection.Warnings.FirstOrDefault()?.Code, httpContext.TraceIdentifier);
            }
            else if (projection.Warnings.Count > 0)
            {
                logger.LogInformation(
                    "Compose project: DOCX projection partial for file={FileName}; warnings={Warnings}",
                    body.FileName, string.Join(",", projection.Warnings.Select(w => $"{w.Code}:{w.Count}")));
            }

            return Results.Ok(new ComposeProjectResponse(
                Projection: MapProjectionResponse(projection),
                CorrelationId: httpContext.TraceIdentifier,
                // Task 012: the retained canonical model + (only when minting mutated the bytes) the
                // minted content echo — see the handler comment above. Still stateless: nothing persisted.
                ContentModel: mount.ContentModel,
                // Echo the mount bytes when they DIFFER from what the caller sent: either paraId minting
                // mutated them (Minted) OR — Task 050 (FR-06) — the source was a PDF that projected into a
                // SYNTHESIZED docx (SourceFormat != null), which the caller does NOT already hold. Without
                // the SourceFormat clause a PDF browse would return a docx projection but no docx bytes
                // (the renderer already mints paraIds, so MintAndPersist is a no-op → Minted=false),
                // leaving the client unable to save the PDF-sourced doc as a docx (the 051 flow).
                Content: (mount.Minted || mount.SourceFormat is not null) ? mount.Content.ToArray() : null,
                // Task 013 (012-review F7): canonical-projection flatten warnings for the client fold.
                ContentModelWarnings: MapWarningResponses(mount.ContentModelWarnings),
                // Task 050 (FR-06): the PDF-source marker (task 051 keys the honest-lossiness UX + the
                // save-as-docx flow off it). Null for a native docx browse.
                SourceFormat: mount.SourceFormat));
        }
        catch (ComposePdfIntakeException ex)
        {
            // Task 050 (FR-06): the now-async ProjectForMount forks a Browse-local PDF onto the intake
            // leg, so intake unavailability/failure surfaces here — the SAME honest ProblemDetails the
            // Load door maps (503 retryable-unavailable vs 422 not-projectable), never a generic 500.
            // (A malformed DOCX still fails CLOSED inside the projection — Status=failed/200 — this
            // catch is only reached on the PDF intake throw path.)
            logger.LogWarning(ex,
                "Compose project: PDF intake refused (unavailable={Unavailable}) file={FileName}. TraceId={TraceId}",
                ex.Unavailable, body.FileName, httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: ex.Unavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status422UnprocessableEntity,
                title: ex.Unavailable ? "PDF Intake Unavailable" : "PDF Not Editable",
                detail: ex.Message);
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
/// <c>docxBytes</c> transient-mount seam, exactly like the Load endpoint's <c>content</c>. Retained
/// so a later Save (create-on-save) can still send the baseline bytes — FR-01 ADDS
/// <see cref="Projection"/> alongside it; it does not replace it.
/// </summary>
public sealed record ComposeUploadResponse(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("documentId")] string DocumentId,
    [property: JsonPropertyName("fileName")] string? FileName,
    [property: JsonPropertyName("contentType")] string ContentType,
    [property: JsonPropertyName("content")] byte[] Content,
    [property: JsonPropertyName("size")] long Size,
    // FR-01 (task 010, spaarkeai-compose-fidelity-r4.5, WS-1 "one reader everywhere"): the server
    // DOCX→editor projection built from these SAME Content bytes via ComposeDocxProjectionBuilder —
    // the IDENTICAL shape LoadComposeDocumentResponse.Projection carries (ComposeProjectionResponse,
    // mapped by the shared MapProjectionResponse helper below). The client upload effect hydrates
    // this into `mountTransient` so the editor mounts via the SAME projection branch as a
    // stored-document Load, instead of the client mammoth fallback (F-2 one reader).
    [property: JsonPropertyName("projection")] ComposeProjectionResponse Projection,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    // Task 012 (the client cutover): retained canonical model for the imported-save mapper — built
    // from the SAME minted Content this response returns. Null when the canonical projection failed.
    [property: JsonPropertyName("contentModel")] ComposeContentModel? ContentModel = null,
    // Task 013 (012-review F7): canonical-projection flatten warnings for the client fold.
    [property: JsonPropertyName("contentModelWarnings")] IReadOnlyList<ComposeProjectionWarningResponse>? ContentModelWarnings = null,
    // Task 050 (FR-06 — PDF import parity): "pdf" when the uploaded source was a PDF (Content is the
    // synthesized docx); null for a native docx upload. Mirrors LoadComposeDocumentResponse.SourceFormat.
    [property: JsonPropertyName("sourceFormat")] string? SourceFormat = null,
    // FR-S08 (r8 task 015): the document-size limit the SERVER enforces, advertised so the client can
    // pre-flight against the SAME number instead of carrying a copy that drifts. The client must never
    // hard-code a limit — when this is absent (older BFF) it does no numeric pre-flight and lets the
    // server refuse honestly, because a guessed limit is exactly how "your file is fine" becomes a
    // rejection. Sourced from ComposeSaveLimits.MaxDocumentBytes; optional/trailing (ADR-040 additive).
    [property: JsonPropertyName("maxDocumentBytes")] long? MaxDocumentBytes = null);

/// <summary>
/// Request body for <c>POST /api/compose/project</c> (FR-03 task 011, spaarkeai-compose-fidelity-r4.5,
/// T-2 path-A resolution). Carries the caller-supplied document bytes to project — NEVER persisted,
/// NEVER written to <c>ITenantCache</c>, NEVER written to SPE. <c>Content</c> deserializes from the
/// wire's base64 string (System.Text.Json <c>byte[]</c> convention), exactly like every other
/// Compose byte-carrying request body (<see cref="ComposeUploadResponse.Content"/>,
/// <c>SaveComposeDocumentBody.Content</c>). <see cref="FileName"/> is optional and used only for
/// diagnostics/logging — it does not affect the projection.
/// </summary>
public sealed record ComposeProjectRequest(
    [property: JsonPropertyName("content")] byte[] Content,
    [property: JsonPropertyName("fileName")] string? FileName = null);

/// <summary>
/// Response shape for <c>POST /api/compose/project</c>. Carries ONLY the projection — unlike
/// <see cref="ComposeUploadResponse"/> there is no echoed <c>content</c>/<c>size</c>/<c>sessionId</c>/
/// <c>documentId</c>, because this door is stateless: the caller already holds the bytes it sent (a
/// Browse-local file never leaves client memory except for this synchronous render), so there is
/// nothing else for the server to hand back.
/// </summary>
public sealed record ComposeProjectResponse(
    [property: JsonPropertyName("projection")] ComposeProjectionResponse Projection,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    // Task 012 (the client cutover): the retained canonical model (null when projection failed) and —
    // ONLY when server-side paraId minting mutated the caller's bytes — the minted content echo the
    // client MUST adopt as its retained mount baseline (so editor node ids, retained-model block ids,
    // and the save-time carrier stay one id universe). Omitted (null) when nothing needed minting:
    // the caller's own bytes are already identical, so no payload growth on the common path. The door
    // remains stateless — nothing is persisted server-side.
    [property: JsonPropertyName("contentModel")] ComposeContentModel? ContentModel = null,
    [property: JsonPropertyName("content")] byte[]? Content = null,
    // Task 013 (012-review F7): canonical-projection flatten warnings for the client fold.
    [property: JsonPropertyName("contentModelWarnings")] IReadOnlyList<ComposeProjectionWarningResponse>? ContentModelWarnings = null,
    // Task 050 (FR-06 — PDF import parity): "pdf" when the browsed source was a PDF (Content is the
    // synthesized docx); null for a native docx browse. Mirrors LoadComposeDocumentResponse.SourceFormat.
    [property: JsonPropertyName("sourceFormat")] string? SourceFormat = null);
