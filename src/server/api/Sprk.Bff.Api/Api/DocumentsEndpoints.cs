using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Document and container management endpoints following ADR-008.
/// Groups all container operations with consistent error handling.
/// </summary>
public static class DocumentsEndpoints
{
    public static IEndpointRouteBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder app)
    {
        // ── 6 endpoints REMOVED 2026-08-25 (spaarke-auth-v4-dataverse-MI task 090, obligation 031-A) ──
        // POST/GET /api/containers, GET /api/containers/{containerId}/drive, and the three
        // GET /api/drives/{driveId}/... reads were deleted as dead surface.
        //
        // They were guarded by RequireAuthorization("canmanagecontainers") -> ResourceAccessRequirement
        // ("create_container"): a PER-RESOURCE requirement applied to COLLECTION endpoints that carry no
        // resource to evaluate. That is structurally unsatisfiable, not a misconfiguration, so all six
        // returned 403 to every caller, always -- confirmed live during task 031 §5.3/5.4.
        //
        // Deleted rather than re-guarded because: the source marked them "Legacy Compatibility"; a repo
        // sweep found ZERO callers (the upload adapters call /api/containers/{entityName}/{entityId},
        // a different route in UploadEndpoints); and the capability already exists on live surfaces --
        // /api/spe/containers/* (ContainerItemEndpoints), /api/obo/containers/* (OBOEndpoints) and the
        // SpeAdmin container endpoints.
        //
        // NOTE: task 031 booked this as "two dead endpoints". It was SIX -- the same under-counting bias
        // recorded in the project lessons-learned §4. Re-derive counts; never inherit them.
        //
        // The two endpoints BELOW are deliberately retained: they use canwritefiles on routes that DO
        // carry a {driveId} resource, so their per-resource check is satisfiable.

        // PUT /api/drives/{driveId}/upload - Upload file (MI)
        app.MapPut("/api/drives/{driveId}/upload", async (
            string driveId,
            string fileName,
            HttpRequest request,
            SpeFileStore speFileStore,
            ILogger<Program> logger,
            HttpContext context) =>
        {
            var traceId = context.TraceIdentifier;

            try
            {
                if (string.IsNullOrWhiteSpace(driveId))
                {
                    return ProblemDetailsHelper.ValidationError("driveId is required");
                }

                if (string.IsNullOrWhiteSpace(fileName))
                {
                    return ProblemDetailsHelper.ValidationError("fileName is required");
                }

                logger.LogInformation("Uploading file {FileName} to drive {DriveId}", fileName, driveId);

                using var stream = request.Body;

                // SANITIZED 2026-08-29. `fileName` is a QUERY-STRING parameter, so it is fully
                // client-controlled, and it becomes the whole SPE upload path — Graph creates every
                // '/'-delimited segment of that path as a folder. This route also writes app-only (MI), so
                // no container ACL constrains where it lands; it is the same client-controlled path
                // injection as ChatWordExportEndpoints, on a route that is additionally already flagged as
                // a live authorization hole (see SpeWriteSinkContainerProvenanceGuardTests row 4, task 083).
                // Sanitizing does NOT close that authorization hole — it closes the folder-minting half.
                var uploadPath = SpeUploadPath.SanitizeFileName(fileName);

                var localStream = stream;
                var result = await GraphCallScope.Run(
                    () => speFileStore.UploadSmallAsync(driveId, uploadPath, localStream),
                    "file.upload.small");

                if (result == null)
                {
                    return TypedResults.Problem(
                        statusCode: 500,
                        title: "Upload Failed",
                        detail: "Failed to upload file to SPE",
                        extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
                }

                return TypedResults.Created($"/api/drives/{driveId}/items/{result.Id}", result);
            }
            catch (SpaarkeStorageException ex)
            {
                logger.LogError(ex, "Failed to upload file");
                return ex.ToProblemDetails();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error uploading file");
                return TypedResults.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while uploading the file",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        .RequireRateLimiting("graph-write")
        .RequireAuthorization("canwritefiles");

        // DELETE /api/drives/{driveId}/items/{itemId} - Delete file (MI)
        app.MapDelete("/api/drives/{driveId}/items/{itemId}", async (
            string driveId,
            string itemId,
            SpeFileStore speFileStore,
            ILogger<Program> logger,
            HttpContext context) =>
        {
            var traceId = context.TraceIdentifier;

            try
            {
                if (string.IsNullOrWhiteSpace(driveId))
                {
                    return ProblemDetailsHelper.ValidationError("driveId is required");
                }

                if (string.IsNullOrWhiteSpace(itemId))
                {
                    return ProblemDetailsHelper.ValidationError("itemId is required");
                }

                logger.LogInformation("Deleting file {ItemId} from drive {DriveId}", itemId, driveId);

                var deleted = await GraphCallScope.Run(
                    () => speFileStore.DeleteFileAsync(driveId, itemId),
                    "file.delete");

                if (!deleted)
                {
                    return TypedResults.NotFound();
                }

                return TypedResults.NoContent();
            }
            catch (SpaarkeStorageException ex)
            {
                logger.LogError(ex, "Failed to delete file");
                return ex.ToProblemDetails();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error deleting file");
                return TypedResults.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while deleting the file",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        .RequireRateLimiting("graph-write")
        .RequireAuthorization("canwritefiles");

        return app;
    }
}
