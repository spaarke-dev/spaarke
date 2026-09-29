using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Services.Compose.Operations;
using Sprk.Bff.Api.Telemetry;
using static Sprk.Bff.Api.Api.ComposeEndpoints;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Compose <b>save</b> routes: <c>POST /documents/{documentSpeId}/save</c> (replace) and
/// <c>POST /documents/create-on-save</c> (transient-create). Both funnel through the one
/// <c>ExecuteSaveAsync</c> path so they can never diverge on the size gate or the outcome mapping.
///
/// <para><b>Reason to change</b>: the save outcome contract — the document-size gate, the terminal
/// <c>ComposeSaveOutcome</c> every client path keys off, and the exception-to-ProblemDetails +
/// telemetry mapping that makes a failed save honest instead of an opaque 500.</para>
/// </summary>
internal static class ComposeSaveEndpoints
{
    /// <summary>Maps this cluster's routes onto the shared <c>/api/compose</c> group.</summary>
    internal static RouteGroupBuilder MapComposeSaveEndpoints(this RouteGroupBuilder group)
    {
        // (3) POST /api/compose/documents/{documentSpeId}/save — save DOCX
        group.MapPost("/documents/{documentSpeId}/save", Save)
            .WithName("ComposeSaveDocument")
            .WithSummary("Save DOCX bytes to SPE (idempotent first-Save promotion per FR-06)")
            // FR-S08 (r8 task 015): raise the request-body cap above the document limit. The document
            // rides base64-encoded inside a JSON envelope, so Kestrel's 30 MB default rejected documents
            // from about 22 MB up — at the TRANSPORT layer, before any handler ran, which is why the user
            // saw an unexplained failure with no message instead of the honest size refusal in
            // ExecuteSaveAsync. Both numbers derive from ComposeSaveLimits so they cannot drift apart.
            .WithMetadata(new RequestSizeLimitAttribute(ComposeSaveLimits.MaxRequestBodyBytes))
            // SPE persistence → ai-persist (20/min) per its documented purpose, not the 5/min upload bucket.
            .RequireRateLimiting("ai-persist")
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
            // FR-S08: the SAME cap as the replace route above — a first save of a large document is the
            // case that hit the old ceiling hardest, since create-on-save always carries the full bytes.
            .WithMetadata(new RequestSizeLimitAttribute(ComposeSaveLimits.MaxRequestBodyBytes))
            .WithSummary("Create a new sprk_document from a transient Compose draft in the client-resolved BU container (FR-05)")
            // SPE persistence → ai-persist (20/min), not the 5/min upload bucket.
            .RequireRateLimiting("ai-persist")
            .Produces<SaveComposeDocumentResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        return group;
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

        // R4 FR-06 (task 032): a save carrying the retired paragraph-diff delta comes from a client build
        // predating the operation-log cutover. Reject it with a ProblemDetails so a stale payload is a clean 400
        // (never a 500, and never a silent edit-drop) — the client must refresh to send the op-log.
        if (body.EditedParagraphs is { Count: > 0 })
            return StaleSavePayload();

        // The client authors no .docx bytes. The replace path resolves the patch BASELINE from EITHER the
        // retained-original 'content' (clean save / same-session fast-path) OR 'baselineVersionId' (the server
        // re-fetches the load-time version). A dirty save additionally carries 'operationLog' (applied onto the
        // baseline by the engine). One of content / baselineVersionId MUST be resolvable.
        //
        // task 039 (UAT round 1+2, born-in-editor 2nd-save fix): a BORN-IN-EDITOR document (blank page /
        // AI-draft — the client holds NO retained original bytes and there is no real SPE baseline version to
        // delta onto) re-authors its whole content via 'contentModel' on EVERY in-session save. That is a valid
        // dirty save: ResolveSaveBaselineAsync (ComposeService.cs) renders the .docx from ContentModel FIRST
        // (mutually exclusive with content / baselineVersionId / operationLog), then the replace branch does
        // ReplaceFileContentAsUserAsync on the EXISTING item — updating in place, never minting a duplicate. The
        // CLIENT gates this: only a doc with no retained original (`!state.docxBytes`) sends contentModel; a
        // loaded/imported doc still sends op-log + baseline → tracked changes (REQ-2 unchanged).
        var hasContent = body.Content is { Length: > 0 };
        var hasBaseline = !string.IsNullOrWhiteSpace(body.BaselineVersionId);
        var hasContentModel = body.ContentModel is { Blocks.Count: > 0 };
        if (!hasContent && !hasBaseline && !hasContentModel)
            return BadRequest("Provide the retained-original 'content' bytes, 'baselineVersionId', or a born-in-editor 'contentModel', so the server can resolve the save baseline the operation log applies onto (the client authors no .docx bytes).");

        logger.LogInformation(
            "Compose save: tenant={TenantId} drive={DriveId} item={DocumentSpeId} session={SessionId} record={DocumentRecordId} contentBytes={SizeBytes} ops={OpCount} comments={CommentCount} TraceId={TraceId}",
            body.TenantId, body.DriveId, documentSpeId, body.SessionId, body.DocumentRecordId, body.Content?.Length ?? 0, body.OperationLog?.Operations.Count ?? 0, body.Comments?.Count ?? 0, httpContext.TraceIdentifier);

        var request = new SaveComposeDocumentRequest
        {
            DriveId = body.DriveId,
            DocumentSpeId = documentSpeId,
            // ContainerId forwarding REMOVED — issue #858. It was already ignored on this (replace)
            // path, and the field no longer exists on SaveComposeDocumentRequest.
            Content = body.Content is null ? ReadOnlyMemory<byte>.Empty : body.Content,
            // Replace path still requires a session (guarded above at the endpoint); non-null here.
            SessionId = body.SessionId!,
            TenantId = body.TenantId,
            DocumentRecordId = body.DocumentRecordId,
            DisplayName = body.DisplayName,
            // R4 FR-06 (task 032): the op-log's base version + the ordered op-log the engine applies onto it.
            BaselineVersionId = body.BaselineVersionId,
            // UAT-25/26 (2026-08-18): the load-time ETag for honest stale-base detection on the save path.
            BaselineETag = body.BaselineETag,
            OperationLog = body.OperationLog,
            Comments = body.Comments,
            ContentModel = body.ContentModel,
            // C2 (UAT 2026-07-20): the client paraId map → stamp minted ids onto the baseline before the engine
            // resolves each op's anchor.
            ParaIdMap = body.ParaIdMap,
            // G7 (task 022): forwarded for symmetry (ignored on the replace path — a promoted doc already has
            // its SPE id; the dedup only runs in the transient-create branch).
            TransientKey = body.TransientKey,
            ForkNew = body.ForkNew,
            // R8 UAT item 8 — without this line the client's revisionReport is dropped silently.
            RevisionReport = body.RevisionReport,
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
        // The `containerId is required` guard is DELETED — issue #858. Requiring it was the contract
        // expression of the defect: the caller had to name a storage container, and the server wrote
        // there. The container is now chosen by ComposeService.ResolveCreateOnSaveContainerAsync from
        // the matter bound to the session (after authorizing the caller against it), or from the acting
        // user's business unit when there is no matter.
        if (string.IsNullOrWhiteSpace(body.TenantId)) return BadRequest("tenantId is required in the request body.");

        // R4 FR-06 (task 032): reject the retired paragraph-diff delta shape (stale client) with a clean 400.
        if (body.EditedParagraphs is { Count: > 0 })
            return StaleSavePayload();

        // sessionId is OPTIONAL on the transient-create path (task 110): a Browse/local-file first
        // Save has no chat session. The FR-07 rebind is skipped server-side when it is absent; the
        // SPE create + sprk_document create + indexing all complete without one.
        // R3 task 027: create-on-save accepts EITHER retained-original Content (browse-local passthrough)
        // OR a born-in-editor ContentModel (AI-draft/blank — the server RENDERS the .docx). One MUST be present.
        var hasContent = body.Content is { Length: > 0 };
        var hasContentModel = body.ContentModel is { Blocks.Count: > 0 };
        if (!hasContent && !hasContentModel)
            return BadRequest("Provide the retained-original 'content' bytes, or a 'contentModel' for a born-in-editor draft (the client no longer authors .docx bytes).");

        logger.LogInformation(
            "Compose create-on-save: tenant={TenantId} session={SessionId} contentBytes={SizeBytes} modelBlocks={BlockCount} TraceId={TraceId}",
            body.TenantId, body.SessionId, body.Content?.Length ?? 0, body.ContentModel?.Blocks.Count ?? 0, httpContext.TraceIdentifier);

        var request = new SaveComposeDocumentRequest
        {
            // DocumentSpeId null → SaveAsync transient-create branch. The drive is derived from the
            // container the SERVER resolves (issue #858) — the client neither supplies nor knows it.
            DocumentSpeId = null,
            DriveId = null,
            Content = body.Content is null ? ReadOnlyMemory<byte>.Empty : body.Content,
            // Empty when no session is bound (Browse/local-file first Save). The service treats an
            // empty/whitespace SessionId as "no session" and skips the FR-07 rebind (task 110).
            SessionId = body.SessionId ?? string.Empty,
            TenantId = body.TenantId,
            DocumentRecordId = null,
            DisplayName = body.DisplayName,
            // R3 FR-01a (task 027): the born-in-editor content model the server RENDERS into high-fidelity bytes.
            ContentModel = body.ContentModel,
            // R4 FR-06 (task 032): a browse-local (non-born-in-editor) create-on-save MAY carry an op-log +
            // comments the engine applies onto the retained bytes; born-in-editor sends neither (the render is
            // the whole document).
            OperationLog = body.OperationLog,
            Comments = body.Comments,
            // C2 (UAT 2026-07-20): the client paraId map — carried for symmetry (the stamper is a no-op on the
            // born-in-editor ContentModel path, where the renderer already mints ids into the bytes it authors).
            ParaIdMap = body.ParaIdMap,
            // R8 UAT item 8: RevisionReport is deliberately NOT mapped here. A revision report summarises
            // tracked changes read from a STORED document, and this route exists for a draft that has no
            // SPE item yet — so it cannot legitimately arrive. The client agrees (the field rides
            // `replaceCommon`, never the create shape). Stated rather than omitted, so a future reader
            // sees a decision instead of the same silent-drop bug this field was added to fix.
            //
            // G7 (task 022): the transient-key dedup identity + Save-New fork flag — the whole point of this
            // route (the transient-create branch). transientKey dedups repeated create-on-save to ONE record;
            // forkNew forces a fresh record ("Save New Document").
            TransientKey = body.TransientKey,
            ForkNew = body.ForkNew,
            // Task 041 B-MED-3 (option C): the source record whose links the new document inherits
            // (PDF-sourced create-on-save — filed alongside the source PDF).
            SourceDocumentRecordId = body.SourceDocumentRecordId,
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
        // FR-S08 (r8 task 015): the document-size gate, on the ONE path both save routes share so the
        // replace and create-on-save routes can never diverge on it. Checked BEFORE SaveAsync, so an
        // oversize document costs no render, no baseline fetch and no byte transfer to storage.
        //
        // `refused-invalid`, correctly: retrying the same request cannot succeed — something about the
        // request has to change first, which is exactly that outcome member's defining property. And it
        // is a ProblemDetails naming the actual limit, never a bare 400/413: the failure this replaces
        // was a transport-level rejection with no body, which told the user nothing about what to do.
        if (request.Content.Length > ComposeSaveLimits.MaxDocumentBytes)
        {
            ComposeSaveTelemetry.RecordSaveOutcome(ComposeSaveOutcome.RefusedInvalid, ComposeSaveTelemetry.CauseTooLarge);
            logger.LogWarning(
                "Compose save refused: document is {Size} bytes, over the {Limit}-byte limit (session={SessionId}). TraceId={TraceId}",
                request.Content.Length, ComposeSaveLimits.MaxDocumentBytes, request.SessionId, httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Document Too Large",
                detail: $"This document is {request.Content.Length / (1024 * 1024)} MB, and Compose can save documents up to " +
                        $"{ComposeSaveLimits.MaxDocumentDisplay}. Nothing was saved and your changes are still here. " +
                        "Remove or compress large embedded images, or split the document, then save again.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        try
        {
            var result = await composeService.SaveAsync(request, httpContext, ct).ConfigureAwait(false);

            // FR-S10 (task 013): every terminal outcome is counted, including the ones that arrive on a
            // 200. `storage-failed` here is NOT hypothetical — it is the container-failure path, which
            // returns rather than throws, and was previously indistinguishable from success in request
            // telemetry. That indistinguishability is why three releases shipped with saves broken.
            ComposeSaveTelemetry.RecordSaveOutcome(result.Outcome, result.Outcome switch
            {
                ComposeSaveOutcome.Persisted => ComposeSaveTelemetry.CauseNone,
                ComposeSaveOutcome.PersistedWithWarnings => ComposeSaveTelemetry.CauseWarnings,
                // FR-S09 item 5 (task 016): `partially-recorded` now has two producers, and they mean
                // different things operationally. A partial APPLY is the user's edits not all landing
                // (recoverable by redoing them); a failed record PROMOTION is our Dataverse write not
                // landing (recoverable by retrying, and a spike means Dataverse is unwell). Collapsing
                // both into one cause would make the counter unable to tell a content problem from an
                // infrastructure problem — which is the whole reason the cause dimension exists.
                ComposeSaveOutcome.PartiallyRecorded => result.PartialApply is { UnresolvedCount: > 0 }
                    ? ComposeSaveTelemetry.CausePartialApply
                    : ComposeSaveTelemetry.CauseRecordPromotion,
                ComposeSaveOutcome.StorageFailed => ComposeSaveTelemetry.CauseContainerStep,
                _ => ComposeSaveTelemetry.CauseNone,
            });

            if (result.Outcome is ComposeSaveOutcome.StorageFailed or ComposeSaveOutcome.PartiallyRecorded)
            {
                logger.LogWarning(
                    "Compose save completed with a non-success outcome {Outcome} for driveItem={DocumentSpeId} (session={SessionId}). TraceId={TraceId}",
                    result.Outcome.ToWireValue(), result.DocumentSpeId, result.SessionId, httpContext.TraceIdentifier);
            }

            return Results.Ok(new SaveComposeDocumentResponse(
                DocumentSpeId: result.DocumentSpeId,
                DriveId: result.DriveId,
                SessionId: result.SessionId,
                DocumentRecordId: result.DocumentRecordId,
                VersionId: result.VersionId,
                ETag: result.ETag,
                Size: result.Size,
                WasPromotedThisSave: result.WasPromotedThisSave,
                CorrelationId: httpContext.TraceIdentifier,
                ReanchorSummary: result.ReanchorSummary,
                // G1 (FR-01, task 020): the ComposeOrigin this save resolved (available for 021's
                // clean-apply engine mode selection without a follow-up Load).
                Origin: result.Origin,
                // Prong 1 (task 055): best-effort partial-apply outcome (null on the clean path).
                PartialApply: result.PartialApply,
                // Task 026 (FR-04): success-with-warnings degradation surface — mapped to the same
                // wire DTO the load path uses (code + count only; the service record's Detail never
                // crosses the wire).
                DegradationWarnings: result.DegradationWarnings?
                    .Select(w => new ComposeProjectionWarningResponse(w.Code, w.Count))
                    .ToList(),
                // Task 012: the post-save canonical model (render-path saves only) — the client adopts it
                // as its new retained loaded model + re-baselines its snapshot. Additive (ADR-040).
                ContentModel: result.ContentModel,
                // FR-S06 (task 013): the terminal outcome, as a stable wire string. The client decides
                // success from THIS, not from the 200 — because a 200 does not imply anything was
                // written (see the container-failure path, which returns StorageFailed on a 200).
                Outcome: result.Outcome.ToWireValue()));
        }
        catch (ArgumentException ex)
        {
            // FR-S10: a malformed request is a terminal save outcome and belongs in the counter — an
            // upstream client regression that starts sending bad requests should be visible as a spike,
            // not just as scattered 400s.
            ComposeSaveTelemetry.RecordSaveOutcome(ComposeSaveOutcome.RefusedInvalid, ComposeSaveTelemetry.CauseBadRequest);
            return BadRequest(ex.Message);
        }
        catch (ComposePdfIntakeException ex)
        {
            // Task 040 Step-9.5 HIGH-2: a save baseline resolved to PDF bytes (rogue/stale caller —
            // the 041 client saves PDF-sourced docs via create-on-save). Refuse with the honest 422
            // instead of a deep OOXML failure surfacing as a generic 500. MUST precede the
            // InvalidOperationException catch below (this type derives from it).
            ComposeSaveTelemetry.RecordSaveOutcome(
                ex.Unavailable ? ComposeSaveOutcome.StorageFailed : ComposeSaveOutcome.RefusedInvalid,
                ComposeSaveTelemetry.CauseBadRequest);
            logger.LogWarning(ex, "Compose save: PDF baseline refused. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: ex.Unavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status422UnprocessableEntity,
                title: ex.Unavailable ? "PDF Intake Unavailable" : "PDF Cannot Be Saved In Place",
                detail: ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            ComposeSaveTelemetry.RecordSaveOutcome(ComposeSaveOutcome.StorageFailed, ComposeSaveTelemetry.CauseNotFound);
            logger.LogWarning(ex, "Compose save: SPE drive-item not found. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Document Not Found",
                detail: $"SPE drive-item '{documentSpeId ?? "(transient create)"}' was not found or could not be written.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
        }
        catch (UnauthorizedAccessException ex)
        {
            ComposeSaveTelemetry.RecordSaveOutcome(ComposeSaveOutcome.RefusedInvalid, ComposeSaveTelemetry.CauseForbidden);
            logger.LogWarning(ex, "Compose save: OBO denied. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: "Caller lacks SPE ACL write permission for this drive-item.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.3");
        }
        catch (Sprk.Bff.Api.Infrastructure.Graph.DocumentLockedByWordException ex)
        {
            // UAT #10/#11 (task 052): the SPE drive-item is held by a Word-for-web CO-AUTHORING lock — the
            // write layer (UploadSessionManager) translates the Graph 423/resourceLocked ODataError into this
            // typed domain exception. Spaarke never does a SharePoint FORMAL checkout, so a 423 here is ALWAYS
            // the co-authoring lock (Word is / was open), NOT a checkout — the copy is honest about that: there
            // is no programmatic release (confirmed against Microsoft WOPI docs), it clears on a clean Word
            // close or SharePoint's ~30-min-from-last-edit timeout. Do NOT say "check it in" (there is nothing
            // to check in) — that misled users who never checked anything out. The client renders a distinct
            // Retry affordance (no fake Unlock button).
            ComposeSaveTelemetry.RecordSaveOutcome(ComposeSaveOutcome.RefusedLocked, ComposeSaveTelemetry.CauseWordLock);
            logger.LogWarning(ex, "Compose save: drive-item locked by Word co-authoring (423). TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status423Locked,
                title: "Open in Word",
                detail: "This document is open in Word — close it there, then click Retry. It also releases " +
                        "automatically within a few minutes. Your Compose changes are safe and still pending.",
                type: "https://tools.ietf.org/html/rfc4918#section-11.3");
        }
        catch (Sprk.Bff.Api.Infrastructure.Graph.GraphThrottledException ex)
        {
            // FR-S09 item 6 (r8 task 016): Microsoft Graph throttled the write (HTTP 429). Before this
            // catch, the throttle arrived as a bare InvalidOperationException, fell through to the final
            // `catch (Exception)`, and became an HTTP 500 reading "Save failed: InvalidOperationException:
            // Service temporarily unavailable due to Graph rate limiting" — a message that tells the user
            // their save hit a server error, when in fact the service is healthy and simply asked them to
            // wait. Graph's own `Retry-After` was discarded on the way.
            //
            // 429, mirrored back with the header, so the caller (and any proxy) sees a standards-shaped
            // rate-limit response rather than a fault. `storage-failed` on the telemetry side: the write
            // ATTEMPT is what failed and nothing was stored — with cause `throttled`, which is what makes
            // a throttling spike distinguishable from a real storage outage in the counter.
            var retryAfterSeconds = (int)Math.Ceiling((ex.RetryAfter ?? TimeSpan.FromSeconds(30)).TotalSeconds);
            ComposeSaveTelemetry.RecordSaveOutcome(ComposeSaveOutcome.StorageFailed, ComposeSaveTelemetry.CauseThrottled);
            logger.LogWarning(ex,
                "Compose save: throttled by Graph (429), retryAfter={RetryAfter}s. TraceId={TraceId}",
                retryAfterSeconds, httpContext.TraceIdentifier);
            httpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            return Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Too Many Requests",
                detail: $"The document service is busy right now, so nothing was saved and nothing was " +
                        $"overwritten. Your changes are still here — try again in about {retryAfterSeconds} seconds.",
                type: "https://tools.ietf.org/html/rfc6585#section-4");
        }
        catch (Sprk.Bff.Api.Infrastructure.Graph.EtagPreconditionFailedException ex)
        {
            // FR-S02 (r8 task 011): the save route NO LONGER RETURNS 412. Concurrency is last-writer-wins
            // with a warning — a document whose stored version merely moved since load now SUCCEEDS and
            // carries a `concurrent-external-change` warning, so the old "reload and reapply" refusal (and
            // its dead client handler) are gone.
            //
            // Reaching here now means something narrower and genuinely transient: a writer landed inside
            // the read-to-write window AND the single rebase retry in ComposeService.ReplaceWithPreconditionAsync
            // also lost — i.e. the document is being written continuously by someone else right now. That is
            // a CONFLICT (409), not a failed precondition the caller can fix by reloading: nothing about the
            // caller's state is stale, and the honest instruction is simply to try again. Their work is
            // intact client-side; the save is a no-op.
            ComposeSaveTelemetry.RecordSaveOutcome(ComposeSaveOutcome.RefusedStale, ComposeSaveTelemetry.CausePrecondition);
            logger.LogWarning(ex, "Compose save: If-Match precondition failed after the rebase retry (409). TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Document Busy",
                detail: "Someone else is saving this document right now, so your save did not go through. " +
                        "Nothing was overwritten and your changes are still here — try saving again in a moment.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.8");
        }
        catch (Sprk.Bff.Api.Services.Compose.ComposeStaleBaselineUnavailableException ex)
        {
            // FR-S07 (r8 task 014): the document's base moved AND the re-anchor could not re-download the
            // current bytes, so the operation log had nothing valid to rebase onto. The save is refused
            // before any write — this replaces a fallback that persisted the LOAD-TIME baseline instead,
            // silently overwriting a newer version with pre-edit content and reporting HTTP 200.
            //
            // `refused-stale`, not `storage-failed`: no write was attempted, so nothing about the storage
            // ATTEMPT failed, and the stored version is untouched. Telling the user their document may be
            // damaged when it is provably intact would be its own dishonest outcome. The failed READ is
            // carried on the telemetry `cause` dimension instead, which is what it is for.
            //
            // 409 rather than 412: nothing about the CALLER's state is stale — reloading would not help,
            // and a re-download failure is usually transient. The honest instruction is to try again.
            ComposeSaveTelemetry.RecordSaveOutcome(ComposeSaveOutcome.RefusedStale, ComposeSaveTelemetry.CauseBaselineDownload);
            logger.LogWarning(ex,
                "Compose save: stale base could not be rebased (reason={Reason}) — refused, nothing written. TraceId={TraceId}",
                ex.Reason, httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Could Not Save Onto the Newer Version",
                detail: "This document changed since you opened it, and we could not read the newer version to " +
                        "merge your changes into it, so nothing was saved and nothing was overwritten. Your " +
                        "changes are still here — try saving again in a moment.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.8");
        }
        catch (ComposePatchException ex)
        {
            // R4 FR-06 (task 032): the Patch Engine refused the operation log / comments (unresolved
            // paraId/anchor, unsupported schema version, opaque-atom or structural refusal). Mapped to a typed
            // ProblemDetails per Kind — never an opaque 500. Nothing partially wrote — Apply throws before the
            // SPE write in ComposeService.SaveAsync.
            ComposeSaveTelemetry.RecordSaveOutcome(ComposeSaveOutcome.RefusedInvalid, ComposeSaveTelemetry.CausePatchRefusal);
            logger.LogWarning(ex, "Compose save: patch-engine refusal ({Kind}). TraceId={TraceId}", ex.Kind, httpContext.TraceIdentifier);
            return MapPatchException(ex, httpContext);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("Found multiple records", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("not defined as keys", StringComparison.OrdinalIgnoreCase)
            || (ex.Message.Contains("sprk_graphitemid", StringComparison.OrdinalIgnoreCase)
                && ex.Message.Contains("Not Active", StringComparison.OrdinalIgnoreCase)))
        {
            // Prod-safety hardening (post-R7 #1): the FR-07(d) atomic upsert on the sprk_graphitemid_uk
            // alternate key fails in two environment-integrity conditions the code cannot self-heal:
            //   (a) the key index is not Active (build Failed) -> "not defined as keys" / "(Not Active)"
            //   (b) the environment carries duplicate sprk_document rows for one graphitemid -> resolve
            //       finds "Found multiple records".
            // Both previously fell through to the opaque 500 below ("Save failed: InvalidOperationException:
            // ..."). Map to an HONEST, actionable ProblemDetails instead. The SPE version already persisted
            // (only the sprk_document row upsert failed) so edits are NOT lost -- the user can retry once an
            // administrator reconciles the data / reactivates the key.
            var keyInactive = ex.Message.Contains("not defined as keys", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("Not Active", StringComparison.OrdinalIgnoreCase);
            // FR-S06 (task 013): `partially-recorded`, NOT `storage-failed`. This block's own comment
            // records why: the SPE version ALREADY PERSISTED and only the sprk_document row upsert
            // failed. The bytes are durable; the identity record is not. Calling that a storage failure
            // would tell the user their document is gone when it is not — the precise class of dishonest
            // outcome the closed enum exists to prevent.
            ComposeSaveTelemetry.RecordSaveOutcome(ComposeSaveOutcome.PartiallyRecorded, ComposeSaveTelemetry.CauseRecordConflict);
            logger.LogError(ex,
                "Compose save: sprk_document identity-key fault (keyInactive={KeyInactive}). TraceId={TraceId}",
                keyInactive, httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: keyInactive ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status409Conflict,
                title: "Document identity unavailable",
                detail: keyInactive
                    ? "This document couldn't be saved because the document-identity index (sprk_graphitemid_uk) " +
                      "is not active in this environment. An administrator needs to reactivate it. Your edits " +
                      "were not lost — retry once it's resolved."
                    : "This document couldn't be saved because it has duplicate identity records in this " +
                      "environment. An administrator needs to reconcile the duplicate documents. Your edits " +
                      "were not lost — retry once it's resolved.",
                type: keyInactive
                    ? "https://tools.ietf.org/html/rfc7231#section-6.6.4"
                    : "https://tools.ietf.org/html/rfc7231#section-6.5.8");
        }
        catch (Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException ex)
        {
            // Issue #858: the server-side container resolution refuses with TYPED problems —
            // compose_record_access_denied (403), compose_host_entity_unsupported /
            // compose_host_record_invalid / acting_user_ambiguous / secure_record_container_missing
            // (409), acting_user_not_resolvable (403). ResolveCreateOnSaveContainerAsync's own contract
            // says these "must reach the client as 403/409 rather than as a save step that didn't
            // work" — and without this arm they fell through to the catch-all below and shipped as the
            // exact opaque 500 ("Save failed: SdapProblemException: …") the DEF-14 regression suite
            // exists to forbid. Verified on the wire 2026-09-01. The /api/compose group carries no
            // exception filter (unlike Office's OfficeExceptionFilter), so the mapping lives here on
            // the one path both save routes share.
            //
            // Telemetry mirrors the UnauthorizedAccessException arm above: the request was REFUSED
            // before any write (nothing stored, nothing overwritten), so the outcome is RefusedInvalid,
            // with the cause split by what the refusal was about.
            ComposeSaveTelemetry.RecordSaveOutcome(
                ComposeSaveOutcome.RefusedInvalid,
                ex.StatusCode == StatusCodes.Status403Forbidden
                    ? ComposeSaveTelemetry.CauseForbidden
                    : ComposeSaveTelemetry.CauseBadRequest);
            logger.LogWarning(ex,
                "Compose save refused: {Code} ({StatusCode}). TraceId={TraceId}",
                ex.Code, ex.StatusCode, httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: ex.StatusCode,
                title: ex.Title,
                detail: ex.Detail,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = ex.Code,
                    ["correlationId"] = httpContext.TraceIdentifier,
                });
        }
        catch (Exception ex)
        {
            ComposeSaveTelemetry.RecordSaveOutcome(ComposeSaveOutcome.StorageFailed, ComposeSaveTelemetry.CauseUnhandled);
            logger.LogError(ex, "Compose save: unexpected failure. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: $"Save failed: {ex.GetType().Name}: {ex.Message}. TraceId={httpContext.TraceIdentifier}");
        }
    }

    // R4 FR-06 (task 032): a stale/legacy SAVE payload (the retired paragraph-diff `editedParagraphs` shape)
    // maps to a clean 400 ProblemDetails — NOT a 500, and NOT a silent edit-drop.
    private static IResult StaleSavePayload() =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Outdated Save Payload",
            detail: "This save used the retired paragraph-diff format ('editedParagraphs'), which the server no " +
                    "longer accepts. Refresh Compose to load the current build — it saves via the operation-log " +
                    "contract ('operationLog'). Your changes were not lost; re-apply and save again.",
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");

    // R4 FR-06 (task 032): map a ComposeShadowPatchEngine refusal to the right ProblemDetails status instead of
    // an opaque 500. Nothing partially wrote — Apply throws before any bytes are returned / any SPE write runs.
    private static IResult MapPatchException(ComposePatchException ex, HttpContext httpContext) =>
        ex.Kind switch
        {
            ComposePatchErrorKind.MalformedDocument => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Document",
                detail: "The document to save could not be read as a valid .docx; nothing was written.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1"),
            ComposePatchErrorKind.UnsupportedSchemaVersion => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Incompatible Save Contract",
                detail: "The operation-log schema version does not match this server. Refresh Compose and save again.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.8"),
            _ => Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Change Could Not Be Applied",
                detail: "A change could not be anchored in the document to save (its target moved or is not " +
                        "editable). Reload the document and reapply your changes — nothing was overwritten.",
                type: "https://tools.ietf.org/html/rfc4918#section-11.2"),
        };
}

// ─────────────────────────────────────────────────────────────────────────────
// Request / response DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Request body for <c>POST /api/compose/documents/{id}/save</c> (replace path) and
/// <c>POST /api/compose/documents/create-on-save</c> (FR-05 transient create path, task 100).
///
/// <para><b><c>containerId</c> was REMOVED from this body — issue #858 (2026-09-01).</b> It carried a
/// client-resolved SPE container that the server then wrote bytes into, with no per-resource
/// authorization anywhere on the path. The container is now chosen server-side by
/// <c>ComposeService.ResolveCreateOnSaveContainerAsync</c>.</para>
///
/// <para><b>Deploy ordering — this change is BFF-safe-first</b>, unlike task 076's upload contract. A
/// client that still sends <c>containerId</c> has it silently ignored (System.Text.Json drops unknown
/// properties), and the server derives the container regardless. So the BFF may ship before the client
/// with no 404s and no broken saves. The client change is cleanup, not a coupled release.</para></summary>
public sealed record SaveComposeDocumentBody(
    /// <summary>Bound ChatSession id. OPTIONAL on the create-on-save (transient Browse/local-file)
    /// path (task 110) — absent when the draft has no chat session; the server skips the FR-07
    /// rebind. Still REQUIRED on the replace path (guarded at that endpoint).</summary>
    [property: JsonPropertyName("sessionId")] string? SessionId,
    [property: JsonPropertyName("tenantId")] string TenantId,
    /// <summary>The retained-original bytes (same-session fast-path baseline / clean-save passthrough).
    /// OPTIONAL as of R3 task 027: a dirty-loaded save may instead send <see cref="BaselineVersionId"/> +
    /// <see cref="EditedParagraphs"/> (the server re-fetches the load-time version), and a born-in-editor
    /// save sends <see cref="ContentModel"/> (the server RENDERS the bytes). The client authors no
    /// <c>.docx</c> bytes — the only bytes it ever sends are this retained original.</summary>
    [property: JsonPropertyName("content")] byte[]? Content = null,
    [property: JsonPropertyName("driveId")] string? DriveId = null,
    // `containerId` DELETED — issue #858. See the record's summary for the deploy-ordering note: an
    // old client still sending it is harmless (unknown JSON properties are ignored).
    [property: JsonPropertyName("documentRecordId")] Guid? DocumentRecordId = null,
    [property: JsonPropertyName("displayName")] string? DisplayName = null,
    /// <summary>R3 FR-06 (task 027): the LOAD-TIME SPE version id (from the Load response) = the op-log's
    /// BASE VERSION. Sent on a dirty-loaded save when the client no longer holds the retained
    /// <see cref="Content"/> bytes so the server re-fetches the load-time version as the patch baseline.</summary>
    [property: JsonPropertyName("baselineVersionId")] string? BaselineVersionId = null,
    /// <summary>UAT-25/26 (2026-08-18): the LOAD-TIME SPE ETag the client's edits are based on. SaveAsync
    /// compares the live ETag against the effective baseline (Compose save-stamp, else this) and refuses the
    /// whole-body ContentModel re-author with a 412 on a mismatch instead of silently overwriting an external
    /// writer. Optional — an older client that omits it keeps the stamp-only check.</summary>
    [property: JsonPropertyName("baselineETag")] string? BaselineETag = null,
    /// <summary>R4 FR-06 (task 032, the write-path cutover): the client's ordered, rebased task-003 OPERATION
    /// LOG for a dirty save. <see cref="IComposeService.SaveAsync"/> applies it via the single
    /// <c>ComposeShadowPatchEngine</c> onto the resolved baseline (ID-anchored, no write-path text-search) —
    /// REPLACES the retired <c>editedParagraphs</c> paragraph-diff payload.</summary>
    [property: JsonPropertyName("operationLog")] ComposeOperationLog? OperationLog = null,
    /// <summary>R4 FR-06 (task 032): optional durable <c>(paraId, range)</c>-anchored comments the engine emits
    /// as native <c>w:comment</c> in the same pass — the text-search-free replacement for the save-path
    /// <c>DocxAnnotation</c> comment payload. Session comments also persist via the FR-29 annotations endpoint;
    /// native OOXML comment/track-change baking is otherwise the push-annotations surface (task 036).</summary>
    [property: JsonPropertyName("comments")] IReadOnlyList<ComposeAnchoredComment>? Comments = null,
    /// <summary>LEGACY (retired R3 dirty-save shape) — detection-only, deserialized as raw JSON so the endpoint
    /// carries no dependency on the retired <c>ComposeEditedParagraph</c> type. A save that still carries this
    /// paragraph-diff delta comes from a client build predating the task-032 operation-log cutover; the endpoint
    /// rejects it with a ProblemDetails (refresh the client) rather than silently dropping the edits. Kept solely
    /// so a stale payload is a clean 400, not a 500. Removed with the client in task 023.</summary>
    [property: JsonPropertyName("editedParagraphs")] IReadOnlyList<System.Text.Json.JsonElement>? EditedParagraphs = null,
    /// <summary>R3 FR-01a (task 027): the paraId-keyed content model for a BORN-IN-EDITOR save (AI-drafted /
    /// blank / browse-local — no retained original). The server RENDERS the high-fidelity <c>.docx</c> from
    /// it (styles + style-linked multi-level numbering + tables + minted paraId). Mutually exclusive with
    /// <see cref="Content"/> / <see cref="BaselineVersionId"/>.</summary>
    [property: JsonPropertyName("contentModel")] ComposeContentModel? ContentModel = null,
    /// <summary>C2 fix (UAT 2026-07-20): the client's ordered load-time paraId map — one entry per editor
    /// paragraph in document order (<c>{ index, paraId, text }</c>). Lets the server stamp minted ids
    /// physically onto the baseline's id-less paragraphs before the synthesizer resolves (see
    /// <see cref="SaveComposeDocumentRequest.ParaIdMap"/> / <c>ComposeBaselineParaIdStamper</c>).
    /// Optional — an older client omits it and the stamp is skipped.</summary>
    [property: JsonPropertyName("paraIdMap")] IReadOnlyList<ComposeBaselineParaId>? ParaIdMap = null,
    /// <summary>G7 (FR-06, task 022): the client-minted stable transient-draft key (<c>crypto.randomUUID()</c>,
    /// minted once at mount) sent on every create-on-save so repeated calls dedup to ONE record via the
    /// <c>sprk_composetransientkey_uk</c> alt-key instead of minting duplicates (the 8-duplicate fix). Null on
    /// the replace path / older clients. See <see cref="SaveComposeDocumentRequest.TransientKey"/>.</summary>
    [property: JsonPropertyName("transientKey")] string? TransientKey = null,
    /// <summary>G7 (FR-06, task 022): the deliberate <b>Save New Document</b> fork — <c>true</c> skips the
    /// transient-key dedup and mints a fresh record. Default <c>false</c> = <b>Save Version</b> (replace/dedup).
    /// See <see cref="SaveComposeDocumentRequest.ForkNew"/>.</summary>
    [property: JsonPropertyName("forkNew")] bool ForkNew = false,
    // Task 041 B-MED-3 (operator resolution 2026-08-07, option C): the SOURCE sprk_document record this
    // create derives from — sent by the client on a PDF-sourced create-on-save so the new Word document
    // INHERITS the source PDF's record links (matter/project/… — filed alongside the PDF). Optional/
    // trailing (ADR-040 additive); null = no inheritance (every pre-existing flow).
    [property: JsonPropertyName("sourceDocumentRecordId")] Guid? SourceDocumentRecordId = null,
    /// <summary>
    /// R8 UAT item 8: the "Include revision report" appendix — the ledgered
    /// <c>compose-summarize-word-changes</c> result plus the document identity the report is scoped to.
    /// When present AND non-empty, <see cref="IComposeService.SaveAsync"/> appends a Document Revision
    /// Report section via <c>ComposeDocumentRenderer.AppendSection</c>. Optional/trailing (ADR-040
    /// additive); null on every ordinary save.
    /// <para>
    /// <b>This field exists because its absence made the whole feature dead.</b> The endpoint maps this
    /// body onto <see cref="SaveComposeDocumentRequest"/> FIELD BY FIELD, and unknown JSON properties are
    /// silently ignored — so a client sending <c>revisionReport</c> against a DTO without it loses the
    /// payload with no error anywhere. See <see cref="SaveComposeDocumentRequest.SummaryPage"/>, which
    /// still has exactly that defect.
    /// </para>
    /// </summary>
    [property: JsonPropertyName("revisionReport")] ComposeRevisionReportInput? RevisionReport = null);

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
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    // FR-08 (task 050): populated ONLY when this save detected a stale base and re-anchored the operation
    // log — AUTO applied; REVIEW/ORPHAN surfaced here so the client can present them. Null on every
    // non-stale save (the common case). Optional/trailing so existing callers deserializing this response
    // are unaffected.
    [property: JsonPropertyName("reanchorSummary")] ReanchorSummary? ReanchorSummary = null,
    // G1 (FR-01, task 020): the ComposeOrigin this save resolved (server-side, from ContentModel
    // presence — never SPE-id/content inference). Populated on EVERY save so the client/a downstream
    // consumer (e.g. task 021's clean-apply engine mode selection) learns the origin without a
    // follow-up Load. On a create-on-save this is also the value persisted onto the new sprk_document
    // row; a replace-path save of an already-promoted document reports the save's resolved
    // discriminant WITHOUT mutating the already-persisted field. Wire values "authored" | "imported".
    // Optional/trailing so existing callers deserializing this response are unaffected.
    [property: JsonPropertyName("origin")] ComposeOrigin? Origin = null,
    // Prong 1 (task 055): populated ONLY when the save hit an op-level anchoring refusal and the service
    // fell back to best-effort per-paragraph recovery — the resolvable paragraphs were applied and the
    // unresolvable ops are listed here so the client can prompt the user to redo just those edits (never
    // silently applied, never silently dropped). Null on the common path (clean batch apply) and on a
    // batch-level refusal (which still fails hard). Optional/trailing so existing callers are unaffected.
    [property: JsonPropertyName("partialApply")] PartialApplySummary? PartialApply = null,
    // Task 026 (FR-04 graceful degradation): render-side degradation warnings (codes + counts) — content
    // the authoring engine simplified/dropped on this save (success-with-warnings; NEVER a 422 for a
    // hard-tier construct). Null/absent when nothing degraded. Optional/trailing so existing callers
    // deserializing this response are unaffected.
    [property: JsonPropertyName("degradationWarnings")] IReadOnlyList<ComposeProjectionWarningResponse>? DegradationWarnings = null,
    // Task 012 (the client cutover): the post-save canonical model (render-path saves only) — null on
    // op-log/clean saves or when the post-save projection failed. Optional/trailing (ADR-040 additive).
    [property: JsonPropertyName("contentModel")] ComposeContentModel? ContentModel = null,
    // FR-S06 (r8 task 013): the TERMINAL OUTCOME of this save, as a stable wire string from the closed
    // ComposeSaveOutcome set. THE CLIENT DECIDES SUCCESS FROM THIS FIELD, NOT FROM THE HTTP STATUS —
    // a 200 does not imply anything was written (the container-failure path returns `storage-failed`
    // on a 200, which is exactly how a total write failure used to render as "Saved ✓").
    //
    // Defaulted to `persisted` so the record's optional-trailing convention holds and an older caller
    // deserializing this response is unaffected. The default is safe HERE and only here: every server
    // construction site passes an explicit value derived from the service result's `required` Outcome,
    // so the default can never be what a real response carries — it exists for wire compatibility, not
    // as a fallback for "we didn't know".
    [property: JsonPropertyName("outcome")] string Outcome = ComposeSaveOutcomes.Persisted);
