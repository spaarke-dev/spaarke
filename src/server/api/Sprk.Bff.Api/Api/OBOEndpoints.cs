using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Graph;
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
/// WHY THE REMAINING THREE ROUTES ARE STILL UNGATED (escalated, NOT an oversight).
/// The three routes below CREATE content. At the moment of authorization no `sprk_document` row exists
/// — every wizard's ordering is `uploadFilesToSpe` THEN `createDocumentRecords` — so there is nothing
/// for `RetrievePrincipalAccess` to answer about, and their authorization object is the OWNING RECORD /
/// container, not a document. Adding <see cref="Api.Filters.DocumentAuthorizationFilter"/> here would
/// resolve `{id}` to a container id, return None, and deny 100% of uploads.
///
/// That seam is owned by tasks 075 (record-aware container resolver) + 076 (route every call site
/// through it), and must land together with task 073, which gates the app-only twin
/// `PUT /api/containers/{containerId}/files/{*path}` — both container-upload routes should end up
/// behind ONE decision. Task 074's route-authorization ArchTest must carry a NAMED WAIVER for these
/// three until then.
///
/// Full caller inventory + per-route reasoning:
/// `projects/unified-access-control-r2/notes/task-071-obo-route-retirement.md`.
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
        // ⚠️ WHAT THIS DOES NOT FIX. Files >= 4 MiB have NO working upload path, before or after
        // this deletion. PathValidator.SmallUploadMaxBytes caps the small route at 4 MiB
        // (enforced at UploadSessionManager.cs:131) and the chunked path was the only alternative.
        // The client's own SdapApiClient.uploadFile routes >= 4 MiB to the dead path, so large
        // uploads fail today with a misleading 'Failed to get container drive'. Deleting makes the
        // failure honest; it does not make large uploads work. A record-keyed upload-session route
        // is follow-up work and is NOT in task 076's scope — see
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

    // Minimal, local validation to avoid dependency on other files.
    private static (bool ok, string? error) ValidatePathForOBO(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (false, "path is required");
        if (path.EndsWith("/", StringComparison.Ordinal)) return (false, "path must not end with '/'");
        if (path.Contains("..")) return (false, "path must not contain '..'");
        foreach (var ch in path) if (char.IsControl(ch)) return (false, "path contains control characters");
        if (path.Length > 1024) return (false, "path too long");
        return (true, null);
    }
}
