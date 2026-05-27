using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Documents;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Bulk operations on Dataverse-backed documents (FR-BFF-02).
/// </summary>
/// <remarks>
/// <para>
/// Placement Justification (per <c>.claude/constraints/bff-extensions.md</c> §A.1):
/// Bulk download is latency-sensitive (user-facing "Download selected" action in the Documents PCF),
/// requires the same auth scheme + Dataverse lookup + SPE access as existing single-file download
/// endpoints (<c>GET /api/documents/{id}/download</c> in <see cref="FileAccessEndpoints"/>), and streams
/// through BFF-managed Graph credentials and authorization — so it belongs in the BFF, not a separate
/// deployable. Client-side zip in the PCF would buffer all file bytes in browser memory (500 docs × MB =
/// hundreds of MB → kill the tab); server-side streaming via <see cref="SpeFileStore"/> →
/// <see cref="ZipArchive"/> → HTTP response keeps memory bounded. Authorization via the existing
/// endpoint-filter pattern (<see cref="BulkDownloadAuthorizationFilter"/>) reuses BFF auth
/// infrastructure with no parallel auth in PCF. Uses BCL <c>System.IO.Compression.ZipArchive</c>
/// only — no new NuGet packages (ADR-029).
/// </para>
/// </remarks>
public static class DocumentsBulkEndpoints
{
    /// <summary>
    /// Maximum number of document IDs accepted in a single bulk-download request.
    /// Requests exceeding this cap return HTTP 413 Payload Too Large.
    /// </summary>
    public const int MaxDocumentIdsPerRequest = 500;

    /// <summary>
    /// Registers POST /api/documents/bulk-download.
    /// </summary>
    public static IEndpointRouteBuilder MapDocumentsBulkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/documents/bulk-download", BulkDownload)
            .AddBulkDownloadAuthorizationFilter()
            .RequireAuthorization()
            .RequireRateLimiting("graph-read")
            .WithName("BulkDownloadDocuments")
            .WithTags("Documents")
            .WithSummary("Stream a zip of selected documents")
            .WithDescription(
                "Streams a zip archive of the requested documents using app-only SPE access. " +
                "Per-document failures are recorded in a _FAILED.txt manifest inside the zip while " +
                "zipping continues. Total failure (zero accessible documents) returns 4xx ProblemDetails. " +
                $"Maximum {MaxDocumentIdsPerRequest} ids per request.")
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    /// <summary>
    /// Handler for POST /api/documents/bulk-download.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pre-resolve phase: each documentId is looked up in Dataverse and validated. If <b>every</b>
    /// lookup fails, we return ProblemDetails BEFORE flushing the response so callers see a clean
    /// 4xx instead of a partially-written zip. If at least one resolution succeeds, we begin
    /// streaming the zip — at this point response headers are committed and per-document failures
    /// are appended to a <c>_FAILED.txt</c> manifest inside the zip.
    /// </para>
    /// <para>
    /// Streaming: <see cref="ZipArchive"/> wraps <see cref="HttpResponse.Body"/> with
    /// <c>leaveOpen: true</c>. Each entry is written by <c>CopyToAsync</c> from
    /// <see cref="SpeFileStore.DownloadFileAsync"/> directly into the entry stream — no full-file
    /// buffering in memory.
    /// </para>
    /// </remarks>
    private static async Task<IResult> BulkDownload(
        [FromBody] BulkDownloadRequest request,
        IDocumentDataverseService dataverseService,
        SpeFileStore speFileStore,
        ILogger<Program> logger,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var traceId = httpContext.TraceIdentifier;

        // 1. Validate request shape
        if (request is null || request.DocumentIds is null || request.DocumentIds.Count == 0)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Request",
                detail: "Request body must include a non-empty documentIds array.",
                extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
        }

        // 2. Enforce hard cap (413 above MaxDocumentIdsPerRequest)
        if (request.DocumentIds.Count > MaxDocumentIdsPerRequest)
        {
            logger.LogWarning(
                "Bulk download rejected: {Count} ids exceeds cap of {Max}. TraceId: {TraceId}",
                request.DocumentIds.Count, MaxDocumentIdsPerRequest, traceId);

            return Results.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Payload Too Large",
                detail: $"Request contains {request.DocumentIds.Count} document IDs which exceeds the maximum of {MaxDocumentIdsPerRequest} per request.",
                extensions: new Dictionary<string, object?>
                {
                    ["traceId"] = traceId,
                    ["maxDocumentIds"] = MaxDocumentIdsPerRequest,
                    ["requestedCount"] = request.DocumentIds.Count
                });
        }

        // 3. Pre-resolve every documentId in Dataverse (catches total-failure BEFORE flushing zip)
        var resolved = new List<ResolvedDocument>(request.DocumentIds.Count);
        var failedItems = new List<FailedItem>();

        foreach (var rawId in request.DocumentIds)
        {
            if (string.IsNullOrWhiteSpace(rawId))
            {
                failedItems.Add(new FailedItem(rawId ?? "(empty)", null, "Document ID was null or empty"));
                continue;
            }

            if (!Guid.TryParse(rawId, out _))
            {
                failedItems.Add(new FailedItem(rawId, null, "Document ID is not a valid GUID"));
                continue;
            }

            try
            {
                var document = await dataverseService.GetDocumentAsync(rawId, ct);

                if (document is null)
                {
                    failedItems.Add(new FailedItem(rawId, null, "Document not found in Dataverse"));
                    continue;
                }

                var driveId = document.GraphDriveId;
                var itemId = document.GraphItemId;
                var fileName = document.FileName ?? document.Name ?? $"{rawId}.bin";

                if (string.IsNullOrWhiteSpace(driveId) || string.IsNullOrWhiteSpace(itemId))
                {
                    failedItems.Add(new FailedItem(rawId, fileName, "Document has no file attached (missing SPE pointers)"));
                    continue;
                }

                resolved.Add(new ResolvedDocument(
                    rawId,
                    driveId!,
                    itemId!,
                    SanitizeZipEntryName(fileName),
                    document.MatterId));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Bulk download: Dataverse lookup failed for document {DocumentId}. TraceId: {TraceId}",
                    rawId, traceId);
                failedItems.Add(new FailedItem(rawId, null, $"Dataverse lookup failed: {ex.Message}"));
            }
        }

        // 4. Total failure: every requested document failed to resolve — return 4xx BEFORE flushing
        if (resolved.Count == 0)
        {
            logger.LogWarning(
                "Bulk download: all {Count} requested documents failed to resolve. TraceId: {TraceId}",
                failedItems.Count, traceId);

            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "No Accessible Documents",
                detail: "None of the requested documents could be accessed. See failedItems for per-document reasons.",
                extensions: new Dictionary<string, object?>
                {
                    ["traceId"] = traceId,
                    ["requestedCount"] = request.DocumentIds.Count,
                    ["failedItems"] = failedItems.Select(f => new
                    {
                        documentId = f.DocumentId,
                        fileName = f.FileName,
                        reason = f.Reason
                    }).ToList()
                });
        }

        // 5. Begin streaming zip — at this point we commit to 200 OK + application/zip headers
        var matterIdOrBulk = ResolveMatterIdOrBulk(resolved);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var zipFileName = $"documents-{matterIdOrBulk}-{timestamp}.zip";

        var response = httpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "application/zip";
        response.Headers.ContentDisposition = $"attachment; filename=\"{zipFileName}\"";
        // Suppress any inherited content-length — we are streaming
        response.Headers.ContentLength = null;

        logger.LogInformation(
            "Bulk download: streaming {Resolved} document(s) ({FailedPreflight} pre-flight failure(s)) as {ZipFileName}. TraceId: {TraceId}",
            resolved.Count, failedItems.Count, zipFileName, traceId);

        // Use leaveOpen: true so the underlying response stream is not closed prematurely by ZipArchive disposal.
        using (var zip = new ZipArchive(response.Body, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Track used entry names to avoid collisions in the zip when filenames repeat.
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var doc in resolved)
            {
                ct.ThrowIfCancellationRequested();

                var entryName = DeduplicateEntryName(doc.FileName, usedNames);

                try
                {
                    var contentStream = await speFileStore.DownloadFileAsync(doc.DriveId, doc.ItemId, ct);

                    if (contentStream is null)
                    {
                        failedItems.Add(new FailedItem(doc.DocumentId, doc.FileName, "SPE download returned null stream"));
                        logger.LogWarning(
                            "Bulk download: null stream for document {DocumentId} (drive {DriveId} item {ItemId}). TraceId: {TraceId}",
                            doc.DocumentId, doc.DriveId, doc.ItemId, traceId);
                        continue;
                    }

                    var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                    await using (var entryStream = entry.Open())
                    await using (contentStream)
                    {
                        await contentStream.CopyToAsync(entryStream, ct);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Capture per-document failure and continue — do NOT abort the whole zip.
                    failedItems.Add(new FailedItem(doc.DocumentId, doc.FileName, $"Download failed: {ex.Message}"));
                    logger.LogWarning(ex,
                        "Bulk download: failed to add document {DocumentId} to zip. TraceId: {TraceId}",
                        doc.DocumentId, traceId);
                }
            }

            // 6. Write _FAILED.txt manifest if any per-doc failures occurred (pre-flight or in-flight)
            if (failedItems.Count > 0)
            {
                try
                {
                    var manifestEntry = zip.CreateEntry("_FAILED.txt", CompressionLevel.Optimal);
                    await using var manifestStream = manifestEntry.Open();
                    var manifestText = BuildFailedManifest(failedItems, traceId);
                    var bytes = Encoding.UTF8.GetBytes(manifestText);
                    await manifestStream.WriteAsync(bytes, 0, bytes.Length, ct);
                }
                catch (Exception ex)
                {
                    // Best-effort manifest write — do not fail the entire response if the manifest entry
                    // itself fails to write (response headers already flushed).
                    logger.LogError(ex,
                        "Bulk download: failed to write _FAILED.txt manifest. TraceId: {TraceId}",
                        traceId);
                }
            }
        }

        logger.LogInformation(
            "Bulk download complete: {Succeeded} succeeded, {Failed} failed. TraceId: {TraceId}",
            resolved.Count - failedItems.Count(f => resolved.Any(r => r.DocumentId == f.DocumentId)),
            failedItems.Count, traceId);

        return Results.Empty;
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the shared matter ID if every resolved document is from the same matter,
    /// otherwise <c>"bulk"</c>. Used to label the downloaded zip filename per FR-BFF-02.
    /// </summary>
    private static string ResolveMatterIdOrBulk(IReadOnlyList<ResolvedDocument> resolved)
    {
        var matterIds = resolved
            .Where(d => !string.IsNullOrWhiteSpace(d.MatterId))
            .Select(d => d.MatterId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matterIds.Count == 1)
        {
            return SanitizeFilenameSegment(matterIds[0]);
        }

        return "bulk";
    }

    /// <summary>
    /// Sanitize characters that are illegal or problematic inside a zip entry path.
    /// Preserves UTF-8 file names but strips path separators and control characters.
    /// </summary>
    private static string SanitizeZipEntryName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "unnamed.bin";
        }

        // Strip any directory components — entries are flat
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = fileName;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (ch < 0x20 || Array.IndexOf(invalid, ch) >= 0)
            {
                sb.Append('_');
            }
            else
            {
                sb.Append(ch);
            }
        }

        var result = sb.ToString().Trim();
        return string.IsNullOrEmpty(result) ? "unnamed.bin" : result;
    }

    /// <summary>
    /// Sanitize a string for safe inclusion in a Content-Disposition filename.
    /// </summary>
    private static string SanitizeFilenameSegment(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append('-');
            }
        }
        var s = sb.ToString();
        return string.IsNullOrEmpty(s) ? "bulk" : s;
    }

    /// <summary>
    /// Ensure each zip entry has a unique name. Append <c>" (n)"</c> before the extension
    /// when collisions occur.
    /// </summary>
    private static string DeduplicateEntryName(string proposedName, HashSet<string> usedNames)
    {
        if (usedNames.Add(proposedName))
        {
            return proposedName;
        }

        var name = Path.GetFileNameWithoutExtension(proposedName);
        var ext = Path.GetExtension(proposedName);

        for (var i = 1; i < 10_000; i++)
        {
            var candidate = $"{name} ({i}){ext}";
            if (usedNames.Add(candidate))
            {
                return candidate;
            }
        }

        // Fall back to GUID suffix
        var fallback = $"{name}-{Guid.NewGuid():N}{ext}";
        usedNames.Add(fallback);
        return fallback;
    }

    /// <summary>
    /// Build the <c>_FAILED.txt</c> manifest body included in the zip when one or more documents fail.
    /// </summary>
    private static string BuildFailedManifest(IReadOnlyList<FailedItem> failedItems, string traceId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Bulk download report");
        sb.AppendLine("=====================");
        sb.AppendLine($"Generated (UTC): {DateTimeOffset.UtcNow:O}");
        sb.AppendLine($"Trace ID:        {traceId}");
        sb.AppendLine($"Failed count:    {failedItems.Count}");
        sb.AppendLine();
        sb.AppendLine("The following documents could not be added to this archive:");
        sb.AppendLine();

        var index = 1;
        foreach (var item in failedItems)
        {
            sb.AppendLine($"{index}. documentId: {item.DocumentId}");
            sb.AppendLine($"   fileName:   {(string.IsNullOrEmpty(item.FileName) ? "(unknown)" : item.FileName)}");
            sb.AppendLine($"   reason:     {item.Reason}");
            sb.AppendLine();
            index++;
        }

        return sb.ToString();
    }

    // -----------------------------------------------------------------------------------------
    // Local DTOs (file-scoped)
    // -----------------------------------------------------------------------------------------

    private sealed record ResolvedDocument(
        string DocumentId,
        string DriveId,
        string ItemId,
        string FileName,
        string? MatterId);

    private sealed record FailedItem(
        string DocumentId,
        string? FileName,
        string Reason);
}
