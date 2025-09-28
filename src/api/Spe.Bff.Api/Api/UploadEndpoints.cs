using Microsoft.Graph;
using Spe.Bff.Api.Infrastructure.Errors;
using Spe.Bff.Api.Infrastructure.Graph;
using Spe.Bff.Api.Infrastructure.Resilience;
using Spe.Bff.Api.Infrastructure.Validation;

namespace Spe.Bff.Api.Api;

/// <summary>
/// File upload endpoints following ADR-008.
/// Groups all upload operations with consistent error handling and validation.
/// </summary>
public static class UploadEndpoints
{
    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        // PUT /api/containers/{id}/files/{*path} - Upload small file (MI)
        app.MapPut("/api/containers/{containerId}/files/{*path}", async (
            string containerId,
            string path,
            HttpRequest req,
            SpeFileStore speFileStore,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct) =>
        {
            var traceId = context.TraceIdentifier;

            try
            {
                // Validate inputs
                if (string.IsNullOrWhiteSpace(containerId))
                {
                    return ProblemDetailsHelper.ValidationError("containerId is required");
                }

                var (ok, err) = PathValidator.ValidatePath(path);
                if (!ok)
                {
                    return ProblemDetailsHelper.ValidationProblem(new() { ["path"] = new[] { err! } });
                }

                if (req.ContentLength == null || req.ContentLength == 0)
                {
                    return ProblemDetailsHelper.ValidationError("Request body cannot be empty");
                }

                logger.LogInformation("Uploading file {Path} to container {ContainerId}", path, containerId);

                using var ms = new MemoryStream();
                await req.Body.CopyToAsync(ms, ct);
                ms.Position = 0;

                var item = await RetryPolicies.GraphTransient().ExecuteAsync(() =>
                    speFileStore.UploadSmallAsync(containerId, path, ms, ct));

                return Results.Ok(item);
            }
            catch (ServiceException ex)
            {
                logger.LogError(ex, "Failed to upload file");
                return ProblemDetailsHelper.FromGraphException(ex);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error uploading file");
                return Results.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while uploading the file",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        // TODO: .RequireRateLimiting("graph-write")
        .RequireAuthorization("canwritefiles");

        // POST /api/containers/{containerId}/upload - Create upload session (MI)
        app.MapPost("/api/containers/{containerId}/upload", async (
            string containerId,
            string path,
            SpeFileStore speFileStore,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct) =>
        {
            var traceId = context.TraceIdentifier;

            try
            {
                // Validate inputs
                if (string.IsNullOrWhiteSpace(containerId))
                {
                    return ProblemDetailsHelper.ValidationError("containerId is required");
                }

                var (ok, err) = PathValidator.ValidatePath(path);
                if (!ok)
                {
                    return ProblemDetailsHelper.ValidationProblem(new() { ["path"] = new[] { err! } });
                }

                logger.LogInformation("Creating upload session for {Path} in container {ContainerId}", path, containerId);

                var session = await RetryPolicies.GraphTransient().ExecuteAsync(() =>
                    speFileStore.CreateUploadSessionAsync(containerId, path, ct));

                return Results.Ok(session);
            }
            catch (ServiceException ex)
            {
                logger.LogError(ex, "Failed to create upload session");
                return ProblemDetailsHelper.FromGraphException(ex);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error creating upload session");
                return Results.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while creating the upload session",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        // TODO: .RequireRateLimiting("graph-write")
        .RequireAuthorization("canwritefiles");

        // PUT /api/upload-session/chunk - Upload file chunk
        app.MapPut("/api/upload-session/chunk", async (
            HttpRequest req,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct) =>
        {
            var traceId = context.TraceIdentifier;

            try
            {
                // Validate upload session URL from headers
                if (!req.Headers.TryGetValue("Upload-Session-Url", out var sessionUrlValues) ||
                    string.IsNullOrWhiteSpace(sessionUrlValues.FirstOrDefault()))
                {
                    return ProblemDetailsHelper.ValidationError("Upload-Session-Url header is required");
                }

                var sessionUrl = sessionUrlValues.First()!;

                // Validate Content-Range header
                if (!req.Headers.TryGetValue("Content-Range", out var rangeValues) ||
                    string.IsNullOrWhiteSpace(rangeValues.FirstOrDefault()))
                {
                    return ProblemDetailsHelper.ValidationError("Content-Range header is required");
                }

                if (req.ContentLength == null || req.ContentLength == 0)
                {
                    return ProblemDetailsHelper.ValidationError("Request body cannot be empty");
                }

                logger.LogInformation("Uploading chunk to session {SessionUrl}", sessionUrl);

                // For now, return a placeholder response
                // In a real implementation, this would use the Graph SDK to upload the chunk
                return Results.Json(new
                {
                    ExpirationDateTime = DateTimeOffset.UtcNow.AddHours(1),
                    NextExpectedRanges = new[] { $"{req.ContentLength}-" }
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error uploading chunk");
                return Results.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while uploading the chunk",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        // TODO: .RequireRateLimiting("graph-write")
        .RequireAuthorization("canwritefiles");

        return app;
    }
}