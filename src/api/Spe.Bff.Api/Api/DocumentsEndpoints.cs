using Microsoft.Graph;
using Spe.Bff.Api.Infrastructure.Errors;
using Spe.Bff.Api.Infrastructure.Graph;
using Spe.Bff.Api.Models;

namespace Spe.Bff.Api.Api;

/// <summary>
/// Document and container management endpoints following ADR-008.
/// Groups all container operations with consistent error handling.
/// </summary>
public static class DocumentsEndpoints
{
    public static IEndpointRouteBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /api/containers - Create container (MI)
        app.MapPost("/api/containers", async (
            CreateContainerRequest request,
            SpeFileStore speFileStore,
            ILogger<Program> logger,
            HttpContext context) =>
        {
            var traceId = context.TraceIdentifier;

            try
            {
                // Validate request
                if (string.IsNullOrWhiteSpace(request.DisplayName))
                    return ProblemDetailsHelper.ValidationError("DisplayName is required");

                if (request.ContainerTypeId == Guid.Empty)
                    return ProblemDetailsHelper.ValidationError("ContainerTypeId must be a valid GUID");

                logger.LogInformation("Creating container {DisplayName} with type {ContainerTypeId}",
                    request.DisplayName, request.ContainerTypeId);

                var result = await speFileStore.CreateContainerAsync(
                    request.ContainerTypeId,
                    request.DisplayName,
                    request.Description);

                return TypedResults.Created($"/api/containers/{result?.Id}", result);
            }
            catch (ServiceException ex)
            {
                logger.LogError(ex, "Failed to create container");
                return ProblemDetailsHelper.FromGraphException(ex);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error creating container");
                return TypedResults.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while creating the container",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        .RequireRateLimiting("graph-write")
        .RequireAuthorization("canmanagecontainers");

        // GET /api/containers?containerTypeId= - List containers (MI)
        app.MapGet("/api/containers", async (
            Guid? containerTypeId,
            SpeFileStore speFileStore,
            ILogger<Program> logger,
            HttpContext context) =>
        {
            var traceId = context.TraceIdentifier;

            try
            {
                if (!containerTypeId.HasValue || containerTypeId.Value == Guid.Empty)
                {
                    return ProblemDetailsHelper.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["containerTypeId"] = ["containerTypeId query parameter is required and must be a valid GUID"]
                    });
                }

                logger.LogInformation("Listing containers for type {ContainerTypeId}", containerTypeId.Value);

                var result = await speFileStore.ListContainersAsync(containerTypeId.Value);

                return TypedResults.Ok(result);
            }
            catch (ServiceException ex)
            {
                logger.LogError(ex, "Failed to list containers");
                return ProblemDetailsHelper.FromGraphException(ex);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error listing containers");
                return TypedResults.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while listing containers",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        .RequireRateLimiting("graph-read")
        .RequireAuthorization("canmanagecontainers");

        // GET /api/containers/{id}/drive - Get container drive (MI)
        // NOTE: This endpoint expects the SPE Container ID, not the Dataverse Container GUID
        app.MapGet("/api/containers/{containerId}/drive", async (
            string containerId,
            SpeFileStore speFileStore,
            ILogger<Program> logger,
            HttpContext context) =>
        {
            var traceId = context.TraceIdentifier;

            try
            {
                if (string.IsNullOrWhiteSpace(containerId))
                {
                    return ProblemDetailsHelper.ValidationError("containerId is required");
                }

                logger.LogInformation("Getting drive for SPE container {ContainerId}", containerId);

                var result = await speFileStore.GetContainerDriveAsync(containerId);

                return TypedResults.Ok(result);
            }
            catch (ServiceException ex)
            {
                logger.LogError(ex, "Failed to get container drive");
                return ProblemDetailsHelper.FromGraphException(ex);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error getting container drive");
                return TypedResults.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while getting the container drive",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        .RequireRateLimiting("graph-read")
        .RequireAuthorization("canmanagecontainers");

        // GET /api/drives/{driveId}/children - List drive children (MI)
        app.MapGet("/api/drives/{driveId}/children", async (
            string driveId,
            string? itemId,
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

                logger.LogInformation("Listing children for drive {DriveId}, item {ItemId}", driveId, itemId);

                var result = await speFileStore.ListChildrenAsync(driveId, itemId);

                return TypedResults.Ok(result);
            }
            catch (ServiceException ex)
            {
                logger.LogError(ex, "Failed to list drive children");
                return ProblemDetailsHelper.FromGraphException(ex);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error listing drive children");
                return TypedResults.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while listing drive children",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        .RequireRateLimiting("graph-read")
        .RequireAuthorization("canmanagecontainers");

        // GET /api/drives/{driveId}/items/{itemId} - Get file metadata (MI)
        app.MapGet("/api/drives/{driveId}/items/{itemId}", async (
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

                logger.LogInformation("Getting metadata for file {ItemId} in drive {DriveId}", itemId, driveId);

                var result = await speFileStore.GetFileMetadataAsync(driveId, itemId);

                if (result == null)
                {
                    return TypedResults.NotFound();
                }

                return TypedResults.Ok(result);
            }
            catch (ServiceException ex)
            {
                logger.LogError(ex, "Failed to get file metadata");
                return ProblemDetailsHelper.FromGraphException(ex);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error getting file metadata");
                return TypedResults.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while getting file metadata",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        .RequireRateLimiting("graph-read")
        .RequireAuthorization("canmanagecontainers");

        // GET /api/drives/{driveId}/items/{itemId}/content - Download file (MI)
        app.MapGet("/api/drives/{driveId}/items/{itemId}/content", async (
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

                logger.LogInformation("Downloading file {ItemId} from drive {DriveId}", itemId, driveId);

                var stream = await speFileStore.DownloadFileAsync(driveId, itemId);

                if (stream == null)
                {
                    return TypedResults.NotFound();
                }

                // Get file metadata to determine content type and filename
                var metadata = await speFileStore.GetFileMetadataAsync(driveId, itemId);
                var fileName = metadata?.Name ?? "download";

                return TypedResults.File(stream, "application/octet-stream", fileName);
            }
            catch (ServiceException ex)
            {
                logger.LogError(ex, "Failed to download file");
                return ProblemDetailsHelper.FromGraphException(ex);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error downloading file");
                return TypedResults.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while downloading the file",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        .RequireRateLimiting("graph-read")
        .RequireAuthorization("canmanagecontainers");

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

                var result = await speFileStore.UploadSmallAsync(driveId, fileName, stream);

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
            catch (ServiceException ex)
            {
                logger.LogError(ex, "Failed to upload file");
                return ProblemDetailsHelper.FromGraphException(ex);
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

                var deleted = await speFileStore.DeleteFileAsync(driveId, itemId);

                if (!deleted)
                {
                    return TypedResults.NotFound();
                }

                return TypedResults.NoContent();
            }
            catch (ServiceException ex)
            {
                logger.LogError(ex, "Failed to delete file");
                return ProblemDetailsHelper.FromGraphException(ex);
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
