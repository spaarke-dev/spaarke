using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// OBO (delegated) SPE upload surface.
///
/// RETIRED 2026-08-26 by unified-access-control-r2 task 071 — FOUR drive/container-keyed routes were
/// DELETED because they read or mutated EXISTING SPE content with no per-document authorization
/// decision, and gated document-id-keyed equivalents already ship:
///
///   GET    /api/obo/containers/{id}/children              -> no caller ever existed; also error-OPEN
///                                                            (turned a Graph 404 into 200 {"items":[]}).
///   PATCH  /api/obo/drives/{driveId}/items/{itemId}       -> DocumentOperationsEndpoints (gated "write")
///   GET    /api/obo/drives/{driveId}/items/{itemId}/content -> FileAccessEndpoints (8 routes, gated "read")
///   DELETE /api/obo/drives/{driveId}/items/{itemId}       -> DocumentOperationsEndpoints (gated "delete")
///
/// These were OBO, so SPE denied any caller lacking a container permission — and under the broker-only
/// decision no user is ever granted one. They were a BYPASS BY CONSTRUCTION of the per-document gate,
/// not a live hole. Do NOT re-add them: the id-keyed routes above are the supported surface.
///
/// THE UPLOAD SURFACE AS OF TASK 076 (option C) — three routes, two contracts.
///
///   PUT  /api/obo/containers/{id}/files/{*path}                          LEGACY, container-keyed, UNGATED
///   PUT  /api/obo/records/{entityLogicalName}/{recordId}/files/{*path}   TARGET, record-keyed, GATED
///   POST /api/obo/records/{entityLogicalName}/{recordId}/upload-session  TARGET, record-keyed, GATED
///
/// The record-keyed pair is the contract every client should move to: the caller names the OWNING
/// RECORD, the server authorizes the caller against it via
/// <see cref="Api.Filters.RecordRouteAccessAuthorizationFilter"/>, and only then resolves the container
/// from that same record through task 075's `RecordContainerResolver`. The authorization key and the
/// container are the same value by construction. A secure record resolves to its OWN container or FAILS
/// CLOSED; everything else resolves to the RECORD's own `owningbusinessunit` container. No caller-supplied
/// container is accepted, and there is no parameter through which one could be.
///
/// WHY THE LEGACY ROUTE SURVIVES (escalated, NOT an oversight — and not "unfinished").
/// It CREATES content, so at authorization time no `sprk_document` row exists to authorize against —
/// attaching <see cref="Api.Filters.DocumentAuthorizationFilter"/> would resolve `{id}` to a container id,
/// return None, and deny 100% of uploads. Task 076 built the record-keyed replacement for exactly that
/// reason. It could not DELETE this route, because three live client upload paths have no owning record at
/// the moment the bytes move:
///
///   · EmailComposer's local-attachment upload — the email may have no persisted regarding yet, and the
///     `sprk_document` is created afterwards, deliberately unassociated.
///   · The Analysis wizard's standalone document — uploaded, then linked to an `sprk_analysis` created later.
///   · DocumentUploadWizard's "skip associate" mode — the user explicitly declines a parent.
///
/// Each is a MODELLING gap (bytes before record), not a routing gap. Adding a container parameter to the
/// record-keyed routes "just for those three" is option (B), which the owner rejected. Resolving it means
/// either creating the owning record before the bytes, or a server-issued upload ticket — an owner
/// decision. Until then this route keeps its Pending waiver in `RouteAuthorizationGuardTests`, and that
/// waiver MUST NOT be converted to Permanent: it is a work item, not an exemption.
///
/// Full reasoning + the classified caller inventory:
/// `projects/unified-access-control-r2/notes/task-076-record-keyed-upload-contract.md`.
/// Prior retirement inventory: `projects/unified-access-control-r2/notes/task-071-obo-route-retirement.md`.
/// </summary>
public static class OBOEndpoints
{
    public static IEndpointRouteBuilder MapOBOEndpoints(this IEndpointRouteBuilder app)
    {
        // PUT: small upload (as user). Post-upload RAG indexing is triggered by the
        // wizard client via `@spaarke/sdap-client.SdapApiClient.indexFile()` after a
        // successful PUT — see project `sdap-client-shared-library-fix-r1` and the
        // canonical pattern used by DocumentUploadWizard's `triggerRagIndexing`.
        app.MapPut("/api/obo/containers/{id}/files/{*path}", async (
            string id, string path, HttpRequest req, HttpContext ctx,
            [FromServices] SpeFileStore speFileStore,
            [FromServices] ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var (ok, err) = ValidatePathForOBO(path);
            if (!ok) return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["path"] = new[] { err! } });

            try
            {
                logger.LogInformation("OBO upload starting - Container: {ContainerId}, Path: {Path}", id, path);

                // Resolve container ID to drive ID (SPE container IDs != drive IDs)
                var driveId = await GraphCallScope.Run(
                    () => speFileStore.ResolveDriveIdAsync(id, ct),
                    "obo.driveid.resolve");
                logger.LogDebug("Resolved container {ContainerId} to drive {DriveId}", id, driveId);

                // Stream directly to Graph SDK (no memory buffering)
                var item = await GraphCallScope.Run(
                    () => speFileStore.UploadSmallAsUserAsync(ctx, driveId, path, req.Body, ct),
                    "obo.upload.small");

                logger.LogInformation("OBO upload successful - DriveItemId: {ItemId}", item?.Id);

                return item is null ? TypedResults.NotFound() : TypedResults.Ok(item);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError(ex, "OBO upload unauthorized");
                return TypedResults.Unauthorized();
            }
            catch (SpaarkeStorageException ex)
            {
                logger.LogError(ex, "OBO upload failed - Graph API error: {Message}", ex.Message);
                return ex.ToProblemDetails();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OBO upload failed - Unexpected error: {Message}", ex.Message);
                return TypedResults.Problem(
                    title: "Upload failed",
                    detail: $"An unexpected error occurred: {ex.Message}",
                    statusCode: 500
                );
            }
        }).RequireRateLimiting("graph-write").RequireAuthorization();

        // ═════════════════════════════════════════════════════════════════════════════════════════
        // RECORD-KEYED UPLOAD (unified-access-control-r2 task 076, option C) — the TARGET contract.
        //
        // The caller names the OWNING RECORD; the server resolves the container from that same
        // record, through task 075's RecordContainerResolver, AFTER authorizing the caller against
        // it. The authorization key and the container are therefore the same value by construction,
        // and no code path lets them disagree — which is the entire point. Compare the route above,
        // which takes a caller-NAMED container and writes bytes into it with no per-resource decision.
        //
        // ⚠️ THE CONTAINER-KEYED ROUTE ABOVE IS STILL PRESENT, AND THAT IS DELIBERATE, NOT UNFINISHED.
        // Three live client upload paths have NO owning record at the moment the bytes move
        // (EmailComposer's local-attachment upload, the Analysis wizard's standalone document, and
        // DocumentUploadWizard's "skip associate" mode), so deleting the old route would break them
        // rather than migrate them. Giving the new route a container parameter "just for those" is
        // option (B), which was rejected. That modelling gap is an OWNER decision — see
        // projects/unified-access-control-r2/notes/task-076-record-keyed-upload-contract.md
        // "ESCALATION". Until it is resolved the old route keeps its Pending waiver in
        // RouteAuthorizationGuardTests; it is NOT converted to Permanent.
        // ═════════════════════════════════════════════════════════════════════════════════════════

        // PUT: small upload (< 4 MiB) against the owning record.
        app.MapPut("/api/obo/records/{entityLogicalName}/{recordId:guid}/files/{*path}", async (
            string entityLogicalName, Guid recordId, string path, HttpRequest req, HttpContext ctx,
            [FromServices] SpeFileStore speFileStore,
            [FromServices] RecordContainerResolver containerResolver,
            [FromServices] ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var (ok, err) = ValidatePathForOBO(path);
            if (!ok) return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["path"] = new[] { err! } });

            try
            {
                logger.LogInformation(
                    "OBO record-keyed upload starting - {Entity} {RecordId}, Path: {Path}",
                    entityLogicalName, recordId, path);

                // The container comes from the record, never from the caller. The two-argument
                // overload derives the non-secure default from the RECORD's own owningbusinessunit —
                // do NOT pass a fallback here, or the caller regains the ability to choose.
                //
                // Throws SdapProblemException for every refusal, which the global handler renders as
                // canonical ProblemDetails (ADR-019): secure_record_container_missing (409, a secure
                // record with no container of its own — FAIL CLOSED, never a fallback),
                // container_record_not_found (404), container_ownership_ambiguous /
                // container_ownership_indeterminate (409).
                var decision = await containerResolver.ResolveForRecordAsync(entityLogicalName, recordId, ct);

                if (decision.Outcome == ContainerDecisionOutcome.Unresolved || decision.ContainerId is null)
                {
                    // Non-secure record whose owning business unit has no container stamped. Benign
                    // for the ingest paths that may skip, but an upload cannot skip — there is
                    // nowhere to put the bytes — so it is reported rather than silently dropped.
                    logger.LogWarning(
                        "OBO record-keyed upload refused - no container could be derived for non-secure "
                        + "{Entity} {RecordId} (its owning business unit has no sprk_containerid).",
                        entityLogicalName, recordId);

                    return TypedResults.Problem(
                        title: "No storage container is configured",
                        detail: "No SharePoint Embedded container could be derived for this record's "
                                + "business unit, so there is nowhere to store the file.",
                        statusCode: 409);
                }

                // Resolve container ID to drive ID (SPE container IDs != drive IDs)
                var driveId = await GraphCallScope.Run(
                    () => speFileStore.ResolveDriveIdAsync(decision.ContainerId, ct),
                    "obo.driveid.resolve");

                // Stream directly to Graph SDK (no memory buffering)
                var item = await GraphCallScope.Run(
                    () => speFileStore.UploadSmallAsUserAsync(ctx, driveId, path, req.Body, ct),
                    "obo.upload.small");

                logger.LogInformation("OBO record-keyed upload successful - DriveItemId: {ItemId}", item?.Id);

                return item is null ? TypedResults.NotFound() : TypedResults.Ok(item);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError(ex, "OBO record-keyed upload unauthorized");
                return TypedResults.Unauthorized();
            }
            catch (SpaarkeStorageException ex)
            {
                logger.LogError(ex, "OBO record-keyed upload failed - Graph API error: {Message}", ex.Message);
                return ex.ToProblemDetails();
            }
            catch (SdapProblemException)
            {
                // The resolver's refusals are the contract, not faults. Rethrow so the global handler
                // renders the typed code/status — swallowing them into the 500 below would turn
                // "this secure record has no container" into "something went wrong".
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OBO record-keyed upload failed - Unexpected error: {Message}", ex.Message);
                return TypedResults.Problem(
                    title: "Upload failed",
                    detail: $"An unexpected error occurred: {ex.Message}",
                    statusCode: 500
                );
            }
        })
        .AddRecordRouteAccessAuthorizationFilter(RecordRouteAccessAuthorizationFilter.AssociateContentOperation)
        .RequireRateLimiting("graph-write")
        .RequireAuthorization();

        // POST: upload session for files >= 4 MiB, against the owning record.
        //
        // WHY THIS EXISTS (task 076). Files >= 4 MiB had NO working upload path at all: the small
        // route is capped at PathValidator.SmallUploadMaxBytes (enforced in
        // UploadSessionManager.UploadSmallAsUserAsync), and the chunked OBO pair that nominally served
        // them was deleted earlier in this same task because it was dead by 404 — its client began
        // with GET /api/obo/containers/{id}/drive, a route mapped nowhere. This restores the
        // capability on the record-keyed contract rather than reviving the container-keyed one.
        //
        // The response carries Graph's own upload-session URL, which the client then PUTs chunks to
        // DIRECTLY — exactly as the previous client did, and as Graph's large-file protocol requires.
        // The BFF deliberately does not proxy the chunks: doing so would need per-session server-side
        // state, and a memory-backed store would break across App Service instances (chunk N landing
        // on an instance that never saw the session) while a distributed one would put a new
        // conditionally-registered dependency under an unconditionally-mapped route (CLAUDE.md §10
        // F.1). The authorization decision is made ONCE, here, against the owning record — which is
        // where this task's invariant lives.
        app.MapPost("/api/obo/records/{entityLogicalName}/{recordId:guid}/upload-session", async (
            string entityLogicalName, Guid recordId, [FromQuery] string path, HttpContext ctx,
            [FromServices] SpeFileStore speFileStore,
            [FromServices] RecordContainerResolver containerResolver,
            [FromServices] ILogger<Program> logger,
            CancellationToken ct,
            [FromQuery] string? conflictBehavior = null) =>
        {
            var (ok, err) = ValidatePathForOBO(path);
            if (!ok) return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["path"] = new[] { err! } });

            if (!TryParseConflictBehavior(conflictBehavior, out var behavior))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["conflictBehavior"] = new[] { "conflictBehavior must be one of: fail, replace, rename" }
                });
            }

            try
            {
                logger.LogInformation(
                    "OBO record-keyed upload session starting - {Entity} {RecordId}, Path: {Path}",
                    entityLogicalName, recordId, path);

                // Identical resolution to the small route, deliberately — one contract, two sizes.
                var decision = await containerResolver.ResolveForRecordAsync(entityLogicalName, recordId, ct);

                if (decision.Outcome == ContainerDecisionOutcome.Unresolved || decision.ContainerId is null)
                {
                    logger.LogWarning(
                        "OBO record-keyed upload session refused - no container could be derived for "
                        + "non-secure {Entity} {RecordId}.",
                        entityLogicalName, recordId);

                    return TypedResults.Problem(
                        title: "No storage container is configured",
                        detail: "No SharePoint Embedded container could be derived for this record's "
                                + "business unit, so there is nowhere to store the file.",
                        statusCode: 409);
                }

                var driveId = await GraphCallScope.Run(
                    () => speFileStore.ResolveDriveIdAsync(decision.ContainerId, ct),
                    "obo.driveid.resolve");

                var session = await GraphCallScope.Run(
                    () => speFileStore.CreateUploadSessionAsUserAsync(ctx, driveId, path, behavior, ct),
                    "obo.upload.session.create");

                if (session is null)
                {
                    return TypedResults.Problem(
                        title: "Upload session could not be created",
                        detail: "SharePoint Embedded did not return an upload session for this file.",
                        statusCode: 502);
                }

                logger.LogInformation(
                    "OBO record-keyed upload session created for {Entity} {RecordId}, expires {Expires}",
                    entityLogicalName, recordId, session.ExpirationDateTime);

                return TypedResults.Ok(session);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError(ex, "OBO record-keyed upload session unauthorized");
                return TypedResults.Unauthorized();
            }
            catch (SpaarkeStorageException ex)
            {
                logger.LogError(ex, "OBO record-keyed upload session failed - Graph API error: {Message}", ex.Message);
                return ex.ToProblemDetails();
            }
            catch (SdapProblemException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OBO record-keyed upload session failed - Unexpected error: {Message}", ex.Message);
                return TypedResults.Problem(
                    title: "Upload session failed",
                    detail: $"An unexpected error occurred: {ex.Message}",
                    statusCode: 500
                );
            }
        })
        .AddRecordRouteAccessAuthorizationFilter(RecordRouteAccessAuthorizationFilter.AssociateContentOperation)
        .RequireRateLimiting("graph-write")
        .RequireAuthorization();

        // ─────────────────────────────────────────────────────────────────────────────────────────
        // DELETED 2026-08-27 (task 076): POST /api/obo/drives/{driveId}/upload-session and
        // PUT /api/obo/upload-session/chunk — the chunked OBO pair.
        //
        // DELETED rather than converted to the record-keyed contract, because the path was DEAD and
        // giving a dead path a new contract is worse than removing it. Verified first-hand rather
        // than taken from the task POML:
        //
        //   1. The pair's only client was Spaarke.SdapClient's UploadOperation.createUploadSession,
        //      which FIRST called `GET /api/obo/containers/{id}/drive` to obtain a drive id.
        //   2. That route is mapped NOWHERE in the BFF — grep of src/server/** returns three prose
        //      comments and zero Map* calls. So createUploadSession threw 'Failed to get container
        //      drive' on the 404 and never reached the upload-session call at all.
        //   3. The chunk route was deader still: even that client never called it. Its uploadChunk
        //      PUT went straight to Graph's own `session.uploadUrl`, not to the BFF.
        //
        // ✅ RESOLVED IN THE SAME TASK (updated 2026-08-28). At the time of the deletion above this
        // block read "Files >= 4 MiB have NO working upload path, before or after this deletion …
        // a record-keyed upload-session route is follow-up work and is NOT in task 076's scope."
        // The owner directed otherwise, and it is now IN scope and done: see
        // POST /api/obo/records/{entityLogicalName}/{recordId}/upload-session above. It reaches the
        // same live UploadSessionManager.CreateUploadSessionAsUserAsync the deleted pair used, minus
        // the GET /api/obo/containers/{id}/drive hop that never existed.
        //
        // ⚠️ Still true on the CLIENT: no shipped client calls the new route yet, so >= 4 MiB uploads
        // continue to fail — now with an accurate message naming the route
        // (Spaarke.SdapClient UploadOperation.LARGE_FILE_UNSUPPORTED) instead of a misleading
        // 'Failed to get container drive'. The client cutover is blocked on the §5 escalation in
        // projects/unified-access-control-r2/notes/task-076-record-keyed-upload-contract.md.
        //
        // Both routes carried Pending waivers in RouteAuthorizationGuardTests owned by "073/075/076";
        // those are deleted with them, because the routes are gone — not because the rule relaxed.
        // ─────────────────────────────────────────────────────────────────────────────────────────

        // ─────────────────────────────────────────────────────────────────────────────────────────
        // DELETED 2026-08-26 (task 071): PATCH /api/obo/drives/{driveId}/items/{itemId},
        // GET /api/obo/drives/{driveId}/items/{itemId}/content, and
        // DELETE /api/obo/drives/{driveId}/items/{itemId}.
        //
        // All three reached EXISTING SPE content keyed by (driveId, itemId) with no per-document
        // authorization decision. Zero production callers (grep-evidenced). Use the gated
        // document-id-keyed routes instead: FileAccessEndpoints (read) and
        // DocumentOperationsEndpoints (write / delete). See the class summary above.
        // ─────────────────────────────────────────────────────────────────────────────────────────

        return app;
    }

    /// <summary>
    /// Parse the optional <c>conflictBehavior</c> query value. Absent defaults to
    /// <see cref="ConflictBehavior.Rename"/> — the behaviour the previous chunked client requested,
    /// and the only one that cannot destroy an existing file. An UNRECOGNISED value is rejected rather
    /// than silently defaulting: a caller who asks for <c>fail</c> and gets <c>rename</c> because they
    /// typo'd it has had its conflict policy quietly inverted.
    /// </summary>
    private static bool TryParseConflictBehavior(string? raw, out ConflictBehavior behavior)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            behavior = ConflictBehavior.Rename;
            return true;
        }

        return Enum.TryParse(raw.Trim(), ignoreCase: true, out behavior)
               && Enum.IsDefined(behavior);
    }

    // Minimal, local validation to avoid dependency on other files.
    /// <summary>
    /// Validates the caller-supplied <c>{*path}</c> of the three OBO upload routes.
    /// </summary>
    /// <remarks>
    /// <para><b>This route is deliberately NOT sanitized, unlike every other SPE upload site in the BFF
    /// (reviewed 2026-08-29).</b> Everywhere else the server constructs the path and the "file name" is just
    /// a name, so <c>SpeUploadPath.SanitizeFileName</c> strips separators. Here <c>{*path}</c> is a wildcard
    /// route where the caller may legitimately address a location inside a container it already holds, and
    /// silently rewriting it would move a caller's bytes without telling them. So this REJECTS (400) rather
    /// than rewrites.</para>
    ///
    /// <para><b>What the pre-2026-08-29 version did and did not do.</b> It correctly blocked traversal
    /// (<c>..</c>), control characters, a trailing <c>/</c>, blank, and &gt;1024 chars. It did NOT block a
    /// LEADING <c>/</c>, EMPTY segments (<c>a//b</c>), a bare <c>.</c> segment, or any Windows/SharePoint
    /// invalid character in a segment — notably <c>'\\'</c>, which several SharePoint surfaces read as a
    /// separator and which <c>Path.GetInvalidFileNameChars()</c> does NOT report on the linux-x64 runtime
    /// this publishes to. Those four gaps are closed below, per-SEGMENT, which is what preserves the
    /// sub-path capability while making each segment a valid name.</para>
    ///
    /// <para><b>And it does NOT prevent folder creation — by design.</b> A multi-segment path here still
    /// makes Graph create the intermediate folders. That is the documented capability of this route and is
    /// why <c>tests/Spaarke.ArchTests/SpeUploadPathIsFlatGuardTests.cs</c> excludes this file by name.</para>
    ///
    /// <para>⚠️ <b>Reported finding, NOT acted on here.</b> That capability is currently DORMANT: all three
    /// client callers send a single file name —
    /// <c>Spaarke.SdapClient/src/operations/UploadOperation.ts</c> (<c>encodeURIComponent(file.name)</c>),
    /// <c>Spaarke.UI.Components/src/services/EntityCreationService.ts</c>, and
    /// <c>services/document-upload/types.ts</c>, which documents the parameter as <c>{fileName}</c>. That is
    /// the same "dormant client-supplied path plumbing" shape the 2026-08-28 change DELETED at three other
    /// sites (SaveRequest.FolderPath, UploadFinalizationPayload.FolderPath, OfficeStorageUploader.folderPath).
    /// Retiring it here is an owner call, not a guard's, because this is a public route contract — so it is
    /// left intact and recorded instead. Related: EntityCreationService interpolates the file name into the
    /// URL WITHOUT <c>encodeURIComponent</c>, so a '/' in a user's file name silently becomes extra route
    /// segments there. After this change that request gets a clean 400 instead of minting folders.</para>
    /// </remarks>
    private static (bool ok, string? error) ValidatePathForOBO(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (false, "path is required");
        if (path.Length > 1024) return (false, "path too long");
        if (path.StartsWith("/", StringComparison.Ordinal)) return (false, "path must not start with '/'");
        if (path.EndsWith("/", StringComparison.Ordinal)) return (false, "path must not end with '/'");
        if (path.Contains("..")) return (false, "path must not contain '..'");
        foreach (var ch in path) if (char.IsControl(ch)) return (false, "path contains control characters");

        // Per-SEGMENT validation. Splitting on '/' keeps the sub-path capability intact; requiring each
        // segment to be a valid NAME is what the previous whole-string checks never did.
        foreach (var segment in path.Split('/'))
        {
            if (!SpeUploadPath.IsSafeSegment(segment))
            {
                return (false,
                    "each '/'-separated segment of path must be a valid file or folder name: non-empty, "
                    + "not '.' or '..', and free of the characters < > : \" \\ | ? *");
            }
        }

        return (true, null);
    }
}
