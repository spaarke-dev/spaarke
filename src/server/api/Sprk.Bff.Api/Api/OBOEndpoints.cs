using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Api;

public static class OBOEndpoints
{
    public static IEndpointRouteBuilder MapOBOEndpoints(this IEndpointRouteBuilder app)
    {
        // GET: list children (as user) with paging, ordering, and metadata
        app.MapGet("/api/obo/containers/{id}/children", async (
            string id,
            int? top,
            int? skip,
            string? orderBy,
            string? orderDir,
            HttpContext ctx,
            [FromServices] SpeFileStore speFileStore,
            CancellationToken ct) =>
        {
            try
            {
                var parameters = new Sprk.Bff.Api.Models.ListingParameters(
                    Top: top ?? 50,
                    Skip: skip ?? 0,
                    OrderBy: orderBy ?? "name",
                    OrderDir: orderDir ?? "asc"
                );

                var result = await speFileStore.ListChildrenAsUserAsync(ctx, id, parameters, ct);
                return TypedResults.Ok(result);
            }
            catch (UnauthorizedAccessException)
            {
                return TypedResults.Unauthorized();
            }
            catch (ODataError ex)
            {
                return ProblemDetailsHelper.FromGraphException(ex);
            }
        }).RequireRateLimiting("graph-read");


        // PUT: small upload (as user)
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

                // Stream directly to Graph SDK (no memory buffering)
                var item = await speFileStore.UploadSmallAsUserAsync(ctx, id, path, req.Body, ct);

                logger.LogInformation("OBO upload successful - DriveItemId: {ItemId}", item?.Id);
                return item is null ? TypedResults.NotFound() : TypedResults.Ok(item);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError(ex, "OBO upload unauthorized");
                return TypedResults.Unauthorized();
            }
            catch (ODataError ex)
            {
                logger.LogError(ex, "OBO upload failed - Graph API error: {Message}", ex.Message);
                return ProblemDetailsHelper.FromGraphException(ex);
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
        }).RequireRateLimiting("graph-write");


        // POST: create upload session (as user)
        app.MapPost("/api/obo/drives/{driveId}/upload-session", async (
            string driveId,
            string path,
            string? conflictBehavior,
            HttpContext ctx,
            [FromServices] SpeFileStore speFileStore,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return ProblemDetailsHelper.ValidationError("path query parameter is required");
            }

            try
            {
                var behavior = Sprk.Bff.Api.Models.ConflictBehaviorExtensions.ParseConflictBehavior(conflictBehavior);
                var session = await speFileStore.CreateUploadSessionAsUserAsync(ctx, driveId, path, behavior, ct);

                return session == null
                    ? TypedResults.Problem(statusCode: 500, title: "Failed to create upload session")
                    : TypedResults.Ok(session);
            }
            catch (UnauthorizedAccessException)
            {
                return TypedResults.Unauthorized();
            }
            catch (ODataError ex)
            {
                return ProblemDetailsHelper.FromGraphException(ex);
            }
        }).RequireRateLimiting("graph-write");

        // PUT: upload chunk (as user)
        app.MapPut("/api/obo/upload-session/chunk", async (
            HttpRequest request,
            HttpContext ctx,
            [FromServices] SpeFileStore speFileStore,
            CancellationToken ct) =>
        {
            // Get required headers
            var uploadSessionUrl = request.Headers["Upload-Session-Url"].FirstOrDefault();
            var contentRange = request.Headers["Content-Range"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(uploadSessionUrl))
            {
                return ProblemDetailsHelper.ValidationError("Upload-Session-Url header is required");
            }

            if (string.IsNullOrWhiteSpace(contentRange))
            {
                return ProblemDetailsHelper.ValidationError("Content-Range header is required");
            }

            try
            {
                var userToken = TokenHelper.ExtractBearerToken(ctx);

                // Read chunk data from request body
                using var ms = new MemoryStream();
                await request.Body.CopyToAsync(ms, ct);
                var chunkData = ms.ToArray();

                if (chunkData.Length == 0)
                {
                    return ProblemDetailsHelper.ValidationError("Request body cannot be empty");
                }

                var result = await speFileStore.UploadChunkAsUserAsync(userToken, uploadSessionUrl, contentRange, chunkData, ct);

                return result.StatusCode switch
                {
                    200 => TypedResults.Ok(result.CompletedItem), // Upload complete
                    201 => TypedResults.Created("", result.CompletedItem), // Upload complete
                    202 => TypedResults.Accepted("", result.CompletedItem), // More chunks expected
                    400 => TypedResults.BadRequest("Invalid chunk or Content-Range"),
                    413 => TypedResults.Problem(statusCode: 413, title: "Chunk too large"),
                    499 => TypedResults.Problem(statusCode: 499, title: "Client closed request"),
                    _ => TypedResults.Problem(statusCode: 500, title: "Upload failed")
                };
            }
            catch (UnauthorizedAccessException)
            {
                return TypedResults.Unauthorized();
            }
            catch (ODataError ex)
            {
                return ProblemDetailsHelper.FromGraphException(ex);
            }
            catch (Exception)
            {
                return TypedResults.Problem(statusCode: 500, title: "Upload chunk failed");
            }
        }).RequireRateLimiting("graph-write");

        // PATCH: update item (rename/move)
        app.MapPatch("/api/obo/drives/{driveId}/items/{itemId}", async (
            string driveId,
            string itemId,
            UpdateFileRequest request,
            HttpContext ctx,
            [FromServices] SpeFileStore speFileStore,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return ProblemDetailsHelper.ValidationError("itemId is required");
            }

            if (request == null || (string.IsNullOrEmpty(request.Name) && string.IsNullOrEmpty(request.ParentReferenceId)))
            {
                return ProblemDetailsHelper.ValidationError("At least one of 'name' or 'parentReferenceId' must be provided");
            }

            try
            {
                var updatedItem = await speFileStore.UpdateItemAsUserAsync(ctx, driveId, itemId, request, ct);

                return updatedItem == null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(updatedItem);
            }
            catch (UnauthorizedAccessException)
            {
                return TypedResults.Unauthorized();
            }
            catch (ODataError ex)
            {
                return ProblemDetailsHelper.FromGraphException(ex);
            }
        }).RequireRateLimiting("graph-write");


        // GET: download content with range support (enhanced)
        app.MapGet("/api/obo/drives/{driveId}/items/{itemId}/content", async (
            string driveId,
            string itemId,
            HttpRequest request,
            HttpContext ctx,
            [FromServices] SpeFileStore speFileStore,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return ProblemDetailsHelper.ValidationError("itemId is required");
            }

            try
            {
                // Parse Range header
                var rangeHeader = request.Headers["Range"].FirstOrDefault();
                var range = Sprk.Bff.Api.Models.RangeHeader.Parse(rangeHeader);

                // Parse If-None-Match header (for ETag-based caching)
                var ifNoneMatch = request.Headers["If-None-Match"].FirstOrDefault();

                var fileContent = await speFileStore.DownloadFileWithRangeAsUserAsync(ctx, driveId, itemId, range, ifNoneMatch, ct);

                if (fileContent == null)
                {
                    return range != null
                        ? TypedResults.Problem(statusCode: 416, title: "Range Not Satisfiable") // 416
                        : TypedResults.NotFound();
                }

                // Handle ETag match (304 Not Modified)
                if (fileContent.ContentLength == 0 && fileContent.Content == Stream.Null)
                {
                    return TypedResults.StatusCode(304); // Not Modified
                }

                var response = fileContent.IsRangeRequest
                    ? TypedResults.Stream(fileContent.Content, fileContent.ContentType, enableRangeProcessing: true)
                    : TypedResults.Stream(fileContent.Content, fileContent.ContentType);

                // Set headers
                ctx.Response.Headers.ETag = $"\"{fileContent.ETag}\"";
                ctx.Response.Headers.AcceptRanges = "bytes";

                if (fileContent.IsRangeRequest)
                {
                    ctx.Response.StatusCode = 206; // Partial Content
                    ctx.Response.Headers.ContentRange = fileContent.ContentRangeHeader;
                }

                return response;
            }
            catch (UnauthorizedAccessException)
            {
                return TypedResults.Unauthorized();
            }
            catch (ODataError ex)
            {
                return ProblemDetailsHelper.FromGraphException(ex);
            }
            catch (Exception)
            {
                return TypedResults.Problem(statusCode: 500, title: "Download failed");
            }
        }).RequireRateLimiting("graph-read");

        // DELETE: delete item (as user)
        app.MapDelete("/api/obo/drives/{driveId}/items/{itemId}", async (
            string driveId,
            string itemId,
            HttpContext ctx,
            [FromServices] SpeFileStore speFileStore,
            CancellationToken ct) =>
        {
            try
            {
                await speFileStore.DeleteItemAsUserAsync(ctx, driveId, itemId, ct);
                return TypedResults.NoContent();
            }
            catch (UnauthorizedAccessException)
            {
                return TypedResults.Unauthorized();
            }
            catch (ODataError ex)
            {
                return ProblemDetailsHelper.FromGraphException(ex);
            }
            catch (Exception)
            {
                return TypedResults.Problem(statusCode: 500, title: "Delete failed");
            }
        }).RequireRateLimiting("graph-write");

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
