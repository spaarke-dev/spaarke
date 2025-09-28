using Microsoft.Graph;
using Spe.Bff.Api.Infrastructure.Errors;
using Spe.Bff.Api.Infrastructure.Graph;
using Spe.Bff.Api.Infrastructure.Resilience;
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

                // Execute with retry policy
                var pipeline = RetryPolicies.GraphTransient<ContainerDto?>();
                var result = await pipeline.ExecuteAsync(async () =>
                {
                    return await speFileStore.CreateContainerAsync(
                        request.ContainerTypeId,
                        request.DisplayName,
                        request.Description);
                });

                return Results.Created($"/api/containers/{result?.Id}", result);
            }
            catch (ServiceException ex)
            {
                logger.LogError(ex, "Failed to create container");
                return ProblemDetailsHelper.FromGraphException(ex);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error creating container");
                return Results.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while creating the container",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        // TODO: .RequireRateLimiting("graph-write")
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

                var pipeline = RetryPolicies.GraphTransient<IList<ContainerDto>?>();
                var result = await pipeline.ExecuteAsync(async () =>
                {
                    return await speFileStore.ListContainersAsync(containerTypeId.Value);
                });

                return Results.Ok(result);
            }
            catch (ServiceException ex)
            {
                logger.LogError(ex, "Failed to list containers");
                return ProblemDetailsHelper.FromGraphException(ex);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error listing containers");
                return Results.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while listing containers",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        // TODO: .RequireRateLimiting("graph-read")
        .RequireAuthorization("canmanagecontainers");

        // GET /api/containers/{id}/drive - Get container drive (MI)
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

                logger.LogInformation("Getting drive for container {ContainerId}", containerId);

                var pipeline = RetryPolicies.GraphTransient<ContainerDto?>();
                var result = await pipeline.ExecuteAsync(async () =>
                {
                    return await speFileStore.GetContainerDriveAsync(containerId);
                });

                return Results.Ok(result);
            }
            catch (ServiceException ex)
            {
                logger.LogError(ex, "Failed to get container drive");
                return ProblemDetailsHelper.FromGraphException(ex);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error getting container drive");
                return Results.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while getting the container drive",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        // TODO: .RequireRateLimiting("graph-read")
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

                var pipeline = RetryPolicies.GraphTransient<IList<FileHandleDto>?>();
                var result = await pipeline.ExecuteAsync(async () =>
                {
                    return await speFileStore.ListChildrenAsync(driveId, itemId);
                });

                return Results.Ok(result);
            }
            catch (ServiceException ex)
            {
                logger.LogError(ex, "Failed to list drive children");
                return ProblemDetailsHelper.FromGraphException(ex);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error listing drive children");
                return Results.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while listing drive children",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        // TODO: .RequireRateLimiting("graph-read")
        .RequireAuthorization("canmanagecontainers");

        return app;
    }
}