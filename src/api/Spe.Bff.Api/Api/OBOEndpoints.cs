using Spe.Bff.Api.Infrastructure.Graph;
using Spe.Bff.Api.Infrastructure.Errors;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Services;
using Spe.Bff.Api.Models;

namespace Api;

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
            IOboSpeService oboSvc,
            CancellationToken ct) =>
        {
            var bearer = GetBearer(ctx);
            if (string.IsNullOrEmpty(bearer)) return Results.Unauthorized();

            try
            {
                var parameters = new Spe.Bff.Api.Models.ListingParameters(
                    Top: top ?? 50,
                    Skip: skip ?? 0,
                    OrderBy: orderBy ?? "name",
                    OrderDir: orderDir ?? "asc"
                );

                var result = await oboSvc.ListChildrenAsync(bearer, id, parameters, ct);
                return Results.Ok(result);
            }
            catch (ServiceException ex)
            {
                return ProblemDetailsHelper.FromGraphException(ex);
            }
        }).RequireRateLimiting("graph-read");


        // PUT: small upload (as user)
        app.MapPut("/api/obo/containers/{id}/files/{*path}", async (
            string id, string path, HttpRequest req, HttpContext ctx,
            IOboSpeService oboSvc,
            CancellationToken ct) =>
        {
            var (ok, err) = ValidatePathForOBO(path);
            if (!ok) return Results.ValidationProblem(new Dictionary<string, string[]> { ["path"] = new[] { err! } });

            var bearer = GetBearer(ctx);
            if (string.IsNullOrEmpty(bearer)) return Results.Unauthorized();
            try
            {
                using var ms = new MemoryStream();
                await req.Body.CopyToAsync(ms, ct);
                ms.Position = 0;
                var item = await oboSvc.UploadSmallAsync(bearer, id, path, ms, ct);
                return item is null ? Results.NotFound() : Results.Ok(item);
            }
            catch (ServiceException ex)
            {
                return ProblemDetailsHelper.FromGraphException(ex);
            }
        }).RequireRateLimiting("graph-write");


        // POST: create upload session (as user)
        app.MapPost("/api/obo/drives/{driveId}/upload-session", async (
            string driveId,
            string path,
            string? conflictBehavior,
            HttpContext ctx,
            IOboSpeService oboSvc,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return ProblemDetailsHelper.ValidationError("path query parameter is required");
            }

            var bearer = GetBearer(ctx);
            if (string.IsNullOrEmpty(bearer)) return Results.Unauthorized();

            try
            {
                var behavior = Spe.Bff.Api.Models.ConflictBehaviorExtensions.ParseConflictBehavior(conflictBehavior);
                var session = await oboSvc.CreateUploadSessionAsync(bearer, driveId, path, behavior, ct);

                return session == null
                    ? Results.Problem(statusCode: 500, title: "Failed to create upload session")
                    : Results.Ok(session);
            }
            catch (ServiceException ex)
            {
                return ProblemDetailsHelper.FromGraphException(ex);
            }
        }).RequireRateLimiting("graph-write");

        // PUT: upload chunk (as user)
        app.MapPut("/api/obo/upload-session/chunk", async (
            HttpRequest request,
            HttpContext ctx,
            IOboSpeService oboSvc,
            CancellationToken ct) =>
        {
            var bearer = GetBearer(ctx);
            if (string.IsNullOrEmpty(bearer)) return Results.Unauthorized();

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
                // Read chunk data from request body
                using var ms = new MemoryStream();
                await request.Body.CopyToAsync(ms, ct);
                var chunkData = ms.ToArray();

                if (chunkData.Length == 0)
                {
                    return ProblemDetailsHelper.ValidationError("Request body cannot be empty");
                }

                var result = await oboSvc.UploadChunkAsync(bearer, uploadSessionUrl, contentRange, chunkData, ct);

                return result.StatusCode switch
                {
                    200 => Results.Ok(result.CompletedItem), // Upload complete
                    201 => Results.Created("", result.CompletedItem), // Upload complete
                    202 => Results.Accepted(), // More chunks expected
                    400 => Results.BadRequest("Invalid chunk or Content-Range"),
                    413 => Results.Problem(statusCode: 413, title: "Chunk too large"),
                    499 => Results.Problem(statusCode: 499, title: "Client closed request"),
                    _ => Results.Problem(statusCode: 500, title: "Upload failed")
                };
            }
            catch (ServiceException ex)
            {
                return ProblemDetailsHelper.FromGraphException(ex);
            }
            catch (Exception)
            {
                return Results.Problem(statusCode: 500, title: "Upload chunk failed");
            }
        }).RequireRateLimiting("graph-write");

        // PATCH: update item (rename/move)
        app.MapPatch("/api/obo/drives/{driveId}/items/{itemId}", async (
            string driveId,
            string itemId,
            UpdateFileRequest request,
            HttpContext ctx,
            IOboSpeService oboSvc,
            CancellationToken ct) =>
        {
            var bearer = GetBearer(ctx);
            if (string.IsNullOrEmpty(bearer)) return Results.Unauthorized();

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
                var updatedItem = await oboSvc.UpdateItemAsync(bearer, driveId, itemId, request, ct);

                return updatedItem == null
                    ? Results.NotFound()
                    : Results.Ok(updatedItem);
            }
            catch (ServiceException ex)
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
            IOboSpeService oboSvc,
            CancellationToken ct) =>
        {
            var bearer = GetBearer(ctx);
            if (string.IsNullOrEmpty(bearer)) return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(itemId))
            {
                return ProblemDetailsHelper.ValidationError("itemId is required");
            }

            try
            {
                // Parse Range header
                var rangeHeader = request.Headers["Range"].FirstOrDefault();
                var range = Spe.Bff.Api.Models.RangeHeader.Parse(rangeHeader);

                // Parse If-None-Match header (for ETag-based caching)
                var ifNoneMatch = request.Headers["If-None-Match"].FirstOrDefault();

                var fileContent = await oboSvc.DownloadContentWithRangeAsync(bearer, driveId, itemId, range, ifNoneMatch, ct);

                if (fileContent == null)
                {
                    return range != null
                        ? Results.Problem(statusCode: 416, title: "Range Not Satisfiable") // 416
                        : Results.NotFound();
                }

                // Handle ETag match (304 Not Modified)
                if (fileContent.ContentLength == 0 && fileContent.Content == Stream.Null)
                {
                    return Results.StatusCode(304); // Not Modified
                }

                var response = fileContent.IsRangeRequest
                    ? Results.Stream(fileContent.Content, fileContent.ContentType, enableRangeProcessing: true)
                    : Results.Stream(fileContent.Content, fileContent.ContentType);

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
            catch (ServiceException ex)
            {
                return ProblemDetailsHelper.FromGraphException(ex);
            }
            catch (Exception)
            {
                return Results.Problem(statusCode: 500, title: "Download failed");
            }
        }).RequireRateLimiting("graph-read");

        // DELETE: delete item (as user)
        app.MapDelete("/api/obo/drives/{driveId}/items/{itemId}", async (
            string driveId,
            string itemId,
            HttpContext ctx,
            IOboSpeService oboSpeService,
            CancellationToken ct) =>
        {
            var bearer = GetBearer(ctx);
            if (string.IsNullOrWhiteSpace(bearer))
            {
                return Results.Unauthorized();
            }

            try
            {
                await oboSpeService.DeleteItemAsync(bearer, driveId, itemId, ct);
                return Results.NoContent();
            }
            catch (ServiceException ex)
            {
                return ProblemDetailsHelper.FromGraphException(ex);
            }
            catch (Exception)
            {
                return Results.Problem(statusCode: 500, title: "Delete failed");
            }
        }).RequireRateLimiting("graph-write");

        return app;
    }

    private static string? GetBearer(HttpContext ctx)
    {
        var h = ctx.Request.Headers.Authorization.ToString();
        const string p = "Bearer ";
        return !string.IsNullOrWhiteSpace(h) && h.StartsWith(p, StringComparison.OrdinalIgnoreCase)
            ? h[p.Length..].Trim()
            : null;
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