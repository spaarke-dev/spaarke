using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using static Sprk.Bff.Api.Api.ComposeEndpoints;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Compose <b>persisted-document lifecycle</b> routes: open (<c>GET /documents/{documentSpeId}</c>) and
/// re-run the Document Profile (<c>.../refresh-profile</c>).
///
/// <para><b>Reason to change</b>: the identity + resume contract of a document that already lives in
/// SPE — which session a reopen resumes, which record id a drive-item maps to, and what the open
/// path seeds (change-detection subscription, profile).</para>
///
/// <para><b>RETIRED 2026-10-03 — <c>POST /documents/{documentSpeId}/promote</c></b> (unified-access-control-r2
/// task 166, sweep finding S-63, owner round 10 item 1). It took the TENANT and SESSION from the request
/// body, never resolved the caller, made no OBO read of the drive item, and handed both to
/// <c>IComposeService.PromoteIfEphemeralAsync</c> — which looked the item up app-only (a drive-item → record
/// id oracle), created a root-owned <c>sprk_document</c> app-only for ANY drive-item id (squatting the
/// <c>sprk_graphitemid_uk</c> key), and rebound ANY (tenant, session) pair's document id with no owner check.
/// It had NO client caller (every <c>/promote</c> call in <c>src/</c> is <c>/api/ai/analysis/promote</c>) and
/// is in no published API description, so it was deleted rather than gated. The SERVICE method stays: the
/// save path's first-save promotion uses it internally, behind <c>ComposeSaveEndpoints</c>' session-owner
/// check. Absence is asserted by <c>tests/integration/regression/RouteAuthorization/DeadRouteRetirementTests.cs</c>.</para>
/// </summary>
internal static class ComposeDocumentEndpoints
{
    /// <summary>Maps this cluster's routes onto the shared <c>/api/compose</c> group.</summary>
    internal static RouteGroupBuilder MapComposeDocumentEndpoints(this RouteGroupBuilder group)
    {
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

        // (4) POST /api/compose/documents/{documentSpeId}/promote — RETIRED by uac-r2 task 166 (see the class
        // summary). Do not re-add it: create-on-save promotes internally behind the session-owner check.

        // G10 (FR-09, task 040): the manual "Refresh Profile" leg — re-run the Document Profile on demand.
        // Fire-and-forget best-effort (202): reuses IComposeService.RefreshProfileAsync → the SAME
        // DispatchBackgroundProfile pipeline the save-hook + reload re-trigger use (never a second trigger).
        // Under the authenticated group (OBO); no SPE/Graph type crosses the endpoint (ADR-007).
        //
        // uac-r2 task 166 r1 (amendment d family): the profile is PERSISTED app-only onto the sprk_document row
        // the route names, so the caller must hold WRITE on that row — the route-level document filter decides,
        // before the handler, on the exact id the handler consumes (the route parameter is named {documentId} so
        // DocumentAuthorizationFilter reads it; the URL is unchanged). An unknown id and a forbidden one get the
        // filter's one 403. The tenant is the caller's claim; the body's tenantId is obsolete and never read.
        group.MapPost("/documents/{documentId:guid}/refresh-profile", RefreshProfileAsync)
            .WithName("ComposeRefreshProfile")
            .WithSummary("Re-run the Document Profile for a Compose document on demand (FR-09 / G10)")
            .AddDocumentAuthorizationFilter("write")
            .RequireRateLimiting("ai-context")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status500InternalServerError);

        return group;
    }

    private static async Task<IResult> Load(
        string documentSpeId,
        [FromQuery] string driveId,
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

        // Task 059. The Compose document OPEN path took `?tenantId=` and consulted no claim, so a
        // caller could load — and resume the anchored annotations, defined terms and action history
        // of — a session belonging to another tenant, by editing the URL. Callers may still send the
        // parameter (the Compose client passes it as a host prop); it is ignored.
        var tenantId = TenantResolution.ResolveTenantId(httpContext.User);
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "Tenant identity ('tid' claim) not found in authentication token.");
        }

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
                VersionId: result.VersionId,
                FileName: result.FileName,
                Size: result.Size,
                // gaps 4.2/4.4: surface the three collections the (unchanged) service already
                // returns from the resumed/created session — previously dropped before the wire.
                AnchoredAnnotations: result.AnchoredAnnotations,
                DefinedTermsTracking: result.DefinedTermsTracking,
                ActionHistory: result.ActionHistory,
                // task 052 fix (FR-08/FR-24/FR-25 wire gap): ComposeService.LoadAsync has computed
                // ParaIdMap (task 010)/ImportedRevisions (task 050)/ImportedComments (task 051) on
                // LoadComposeDocumentResult since those tasks landed, but this response record never
                // projected them onto the wire — every client Load silently received undefined for
                // all three, so `ComposeEditor`'s paraIdMap/importedRevisions/importedComments props
                // (which the client-side unit tests exercise only via direct prop injection, never via
                // a real Load response) were dead in production. Surfaced by this task's through-the-wire
                // seam test (ADR-038 "unit-green != done" — exactly the gap the vertical-slice-seam
                // KEEP category exists to catch). Additive, camelCase, mirrors the existing client
                // compose-contracts.ts ParaIdMapEntry/ImportedRevision/ImportedComment shapes verbatim.
                ParaIdMap: result.ParaIdMap,
                ImportedRevisions: result.ImportedRevisions,
                ImportedComments: result.ImportedComments,
                // UAT-12 (2026-08-18): honest signal that the annotation read FAILED (so the empty
                // revisions/comments above are a fallback, NOT proof the document is clean).
                AnnotationReadFailed: result.AnnotationReadFailed,
                // Phase-1 mammoth removal: the server-side projection (paraId-tagged HTML + fail-closed status).
                // FR-01 (task 010): mapping extracted to the shared MapProjectionResponse helper so the
                // Upload endpoint (below) reuses the IDENTICAL wire-shape mapping instead of forking it
                // (root CLAUDE.md §11 — extend, don't duplicate).
                Projection: MapProjectionResponse(result.Projection),
                CorrelationId: httpContext.TraceIdentifier,
                // G1 (FR-01, task 020): the persisted authored-vs-imported origin marker (Path A only).
                Origin: result.Origin,
                // Task 012 (the client cutover): the canonical content model the client RETAINS and
                // re-posts (merged with editor state) on an imported dirty save — the render-on-save
                // (a1) request shape. Null when the canonical projection failed (client falls back to
                // the transitional op-log shape). Additive, camelCase (ADR-040).
                ContentModel: result.ContentModel,
                // Task 013 (012-review F7): the canonical projection's flatten warnings - the client
                // folds them into the FIRST model-path save's degradation banner. Additive.
                ContentModelWarnings: MapWarningResponses(result.ContentModelWarnings),
                // Task 040 (FR-06, PDF intake): "pdf" when Content is the docx SYNTHESIZED from the
                // PDF's canonical-model projection; null for a native docx load. Additive (ADR-040).
                SourceFormat: result.SourceFormat,
                // FR-S08 (r8 task 015): advertise the enforced limit so the client pre-flights against
                // the SAME number the endpoint checks. One source; the client never hard-codes it.
                MaxDocumentBytes: ComposeSaveLimits.MaxDocumentBytes));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (ComposePdfIntakeException ex)
        {
            // Task 040 Step-9.5 HIGH-1: honest PDF-intake ProblemDetails — 503 (intake unavailable,
            // retryable) vs 422 (this document is not projectable) — carrying the service's real
            // message instead of collapsing into the generic 500 catch-all. MUST precede the
            // InvalidOperationException catch below (this type derives from it).
            logger.LogWarning(ex, "Compose load: PDF intake refused (unavailable={Unavailable}). TraceId={TraceId}",
                ex.Unavailable, httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: ex.Unavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status422UnprocessableEntity,
                title: ex.Unavailable ? "PDF Intake Unavailable" : "PDF Not Editable",
                detail: ex.Message);
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

    // G10 (FR-09, task 040): the manual "Refresh Profile" leg. Delegates to
    // IComposeService.RefreshProfileAsync → the SAME fire-and-forget DispatchBackgroundProfile pipeline the
    // save-hook + reload re-trigger use (never a second trigger). Best-effort 202 (the profile runs in the
    // background under OBO); a bad request 400s. No SPE/Graph type crosses the endpoint (ADR-007).
    //
    // uac-r2 task 166 r1: the caller's WRITE on the row was decided by the route's document filter before this runs.
    // Two secondary ids used to arrive unauthorized in the body: the TENANT (now the caller's claim) and the SPE item
    // whose "profiled eTag" stamp is written (a cache key that suppresses that item's reload re-trigger). The stamp is
    // now written only for the item the AUTHORIZED row itself points at; any other item id in the body is ignored.
    private static async Task<IResult> RefreshProfileAsync(
        Guid documentId,
        [FromBody] RefreshProfileBody? body,
        IComposeService composeService,
        IDocumentDataverseService documentService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (documentId == Guid.Empty) return BadRequest("documentId is required in the route.");

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
            var stampSpeId = await ResolveAuthorizedStampItemAsync(
                    documentService, documentId, body?.DocumentSpeId, body?.ETag, logger, httpContext, ct)
                .ConfigureAwait(false);

            await composeService.RefreshProfileAsync(
                new RefreshComposeProfileRequest
                {
                    DocumentRecordId = documentId,
                    TenantId = tenantId,
                    // Only the item the authorized row points at may be stamped; null skips the stamp.
                    DocumentSpeId = stampSpeId,
                    ETag = stampSpeId is null ? null : body?.ETag,
                },
                httpContext,
                ct).ConfigureAwait(false);

            logger.LogInformation(
                "Compose refresh-profile: document {DocumentRecordId} — profile re-dispatched (best-effort) TraceId={TraceId}",
                documentId, httpContext.TraceIdentifier);

            return Results.Accepted(value: new { documentRecordId = documentId, correlationId = httpContext.TraceIdentifier });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Compose refresh-profile: unexpected failure for document {DocumentRecordId} TraceId={TraceId}",
                documentId, httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while refreshing the document profile.");
        }
    }

    /// <summary>
    /// uac-r2 task 166 r1: the SPE item id whose "profiled eTag" stamp this refresh may write — the body's id when, and
    /// only when, it is the item the AUTHORIZED <c>sprk_document</c> row points at (<c>sprk_graphitemid</c>, ordinal);
    /// otherwise <see langword="null"/> (no stamp). The stamp is a best-effort optimisation (it spares one redundant
    /// reload re-trigger), so a read fault or a mismatch skips it rather than failing the refresh.
    /// </summary>
    internal static async Task<string?> ResolveAuthorizedStampItemAsync(
        IDocumentDataverseService documentService,
        Guid documentId,
        string? bodySpeId,
        string? bodyETag,
        ILogger logger,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(bodySpeId) || string.IsNullOrWhiteSpace(bodyETag))
        {
            return null;
        }

        try
        {
            var row = await documentService.GetDocumentAsync(documentId.ToString("D"), ct).ConfigureAwait(false);
            if (row is not null && string.Equals(row.GraphItemId, bodySpeId, StringComparison.Ordinal))
            {
                return bodySpeId;
            }

            logger.LogWarning(
                "Compose refresh-profile: the body's SPE item is not the one document {DocumentRecordId} points at; the "
                + "profiled-eTag stamp is SKIPPED (task 166). TraceId={TraceId}",
                documentId, httpContext.TraceIdentifier);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Compose refresh-profile: could not read document {DocumentRecordId} to bind the stamp item; the "
                + "profiled-eTag stamp is SKIPPED. TraceId={TraceId}",
                documentId, httpContext.TraceIdentifier);
        }

        return null;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Request / response DTOs
// ─────────────────────────────────────────────────────────────────────────────

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
    [property: JsonPropertyName("versionId")] string? VersionId,
    [property: JsonPropertyName("fileName")] string? FileName,
    [property: JsonPropertyName("size")] long? Size,
    [property: JsonPropertyName("anchoredAnnotations")] IReadOnlyList<AnchoredAnnotation> AnchoredAnnotations,
    [property: JsonPropertyName("definedTermsTracking")] IReadOnlyList<DefinedTerm> DefinedTermsTracking,
    [property: JsonPropertyName("actionHistory")] IReadOnlyList<ComposeActionHistoryEntry> ActionHistory,
    // task 052 fix: additive wire projection of LoadComposeDocumentResult.ParaIdMap (task 010) /
    // ImportedRevisions (task 050) / ImportedComments (task 051) — computed server-side since those
    // tasks landed but never serialized onto this response until this task's seam test surfaced the
    // gap. Field shapes mirror the client compose-contracts.ts mirrors verbatim (camelCase).
    [property: JsonPropertyName("paraIdMap")] IReadOnlyList<ParaIdMapEntry> ParaIdMap,
    [property: JsonPropertyName("importedRevisions")] IReadOnlyList<ImportedRevision> ImportedRevisions,
    [property: JsonPropertyName("importedComments")] IReadOnlyList<ImportedComment> ImportedComments,
    // UAT-12 (2026-08-18): honest signal that the annotation read FAILED (fallback empties above are NOT
    // proof the doc is clean). Optional — defaults false so an older client ignores it harmlessly.
    [property: JsonPropertyName("annotationReadFailed")] bool AnnotationReadFailed,
    // Phase-1 mammoth removal (design notes/design-server-side-docx-html-conversion.md): the server-side
    // DOCX→editor projection — paraId-tagged HTML + fail-closed status the client mounts instead of running
    // mammoth. The client keys off Projection.status/canEdit, NOT html length.
    [property: JsonPropertyName("projection")] ComposeProjectionResponse Projection,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    // G1 (FR-01, task 020): the persisted authored-vs-imported origin marker (Path A loads only —
    // an existing sprk_document record). Wire values "authored" | "imported" | null (CamelCaseStringEnumConverter).
    // Null on Path B continuation (no record yet) OR a legacy pre-existing record — the client MUST treat
    // null the SAME as "imported" (never strict-equal null to "authored"), per the BINDING null-handling
    // contract (ComposeOrigin remarks). Optional/trailing so existing callers deserializing this response
    // are unaffected.
    [property: JsonPropertyName("origin")] ComposeOrigin? Origin = null,
    // Task 012 (the client cutover): the canonical content model, built from the SAME minted bytes as
    // the HTML projection (paraIds agree). The client retains it and re-posts it — merged with editor
    // state, every server-set field preserved — as the imported dirty save's `contentModel` (+ a
    // baseline source). Null when the canonical projection failed. Optional/trailing (ADR-040 additive).
    [property: JsonPropertyName("contentModel")] ComposeContentModel? ContentModel = null,
    // Task 013 (012-review F7): flatten warnings of the canonical-model projection (codes + counts) -
    // folded by the client into the FIRST model-path save's degradation banner. Optional/trailing.
    [property: JsonPropertyName("contentModelWarnings")] IReadOnlyList<ComposeProjectionWarningResponse>? ContentModelWarnings = null,
    // Task 040 (FR-06, PDF intake): "pdf" when content is the docx SYNTHESIZED from the PDF's
    // canonical-model projection (client keys the honest-lossiness UX + save-as-docx routing off
    // this); null for a native docx load. Optional/trailing (ADR-040 additive).
    [property: JsonPropertyName("sourceFormat")] string? SourceFormat = null,
    // FR-S08 (r8 task 015): the document-size limit the SERVER enforces, advertised so the client can
    // pre-flight against the SAME number instead of carrying a copy that drifts. The client must never
    // hard-code a limit — when this is absent (older BFF) it does no numeric pre-flight and lets the
    // server refuse honestly, because a guessed limit is exactly how "your file is fine" becomes a
    // rejection. Sourced from ComposeSaveLimits.MaxDocumentBytes; optional/trailing (ADR-040 additive).
    [property: JsonPropertyName("maxDocumentBytes")] long? MaxDocumentBytes = null);

/// <summary>Request body for <c>POST /api/compose/documents/{documentId}/refresh-profile</c>
/// (FR-09 / G10). The <c>sprk_documentid</c> rides the route; the body carries an optional SPE
/// pointer/eTag used only to stamp the profiled version so an immediate reopen does not redundantly
/// re-trigger the storm-guarded reload leg — honoured only for the item the authorized row points at
/// (unified-access-control-r2 task 166 r1).</summary>
public sealed record RefreshProfileBody(
    /// <summary>OBSOLETE — never read (unified-access-control-r2 task 166 r1). The tenant is the caller's
    /// <c>tid</c> claim; this property stays only so the shipped client's payload binds.</summary>
    [property: JsonPropertyName("tenantId")] string? TenantId = null,
    [property: JsonPropertyName("documentSpeId")] string? DocumentSpeId = null,
    [property: JsonPropertyName("eTag")] string? ETag = null);
