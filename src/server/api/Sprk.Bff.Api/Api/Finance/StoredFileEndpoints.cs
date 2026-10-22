using Azure;
using Microsoft.Net.Http.Headers;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Finance.Files;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// <c>GET /api/finance/files/{fileId}/content</c> (ebilling-r1 task 090; procedure-and-file-model sections 4.3, 5.3): the internal
/// route that serves a Stored File's bytes. Flagged-external users get invoice files through 093's firm route, never this one.
/// </summary>
/// <remarks>
/// <para><b>Order (uac-r2 Q5, F1-3, F1-e, F4-2).</b> <c>RequireAuthorization</c> (group) → <see cref="StoredFileAccessFilterExtensions.AddStoredFileAccessFilter{TBuilder}"/>,
/// which runs every check of the download rule before the handler: (1) step 0 → 403 before any
/// record read; (2) app-only row read → unknown 404, fault 503; (3) the caller's own Read on the parent (gate variant) → else 404;
/// (4) the copy rule → else 404; (5) Servable, own path, scan verdict → 404 / 409 <c>file_scan_pending</c> / 503
/// <c>file_store_unavailable</c> → then the eBilling kill switch (<c>RequireEbillingEnabled</c>, ADR-018), chained AFTER the
/// authorization filter as on every eBilling route, so the flag never replaces authorization. Every 404 is
/// <see cref="ProblemDetailsHelper.UniformRecordNotFound"/>, the same bytes as an unknown id's.</para>
/// <para><b>Handler.</b> Opens the blob pinned to the ETag whose verdict the filter read (a blob changed since answers 409), writes
/// the ONE <see cref="StoredFileAudit.Downloaded"/> event, then streams with the row's content type, <c>nosniff</c>, <c>no-store</c>
/// and a sandbox CSP. No SAS or storage URL reaches the client.</para>
/// </remarks>
public static class StoredFileEndpoints
{
    /// <summary>The 409 errorCode while a file has no usable scan verdict.</summary>
    public const string ScanPendingErrorCode = "file_scan_pending";

    /// <summary>The 503 errorCode when the store (or a read it needs) is unavailable.</summary>
    public const string StoreUnavailableErrorCode = "file_store_unavailable";

    public static IEndpointRouteBuilder MapStoredFileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/finance")
            .RequireAuthorization()
            .RequireInternalFinanceCaller() // step 0 on every /api/finance route (section 5.3, task 099); the filter below runs it
            .WithTags("Finance");

        group.MapGet("/files/{fileId:guid}/content", GetContent)
            .AddStoredFileAccessFilter()
            .RequireEbillingEnabled()
            .WithName("GetStoredFileContent")
            .WithSummary("Download a stored file's bytes (internal callers)")
            .WithDescription(
                "Serves an invoice PDF, supporting file, Procedure source or export file to an internal caller who can Read its parent " +
                "(and its source document, for a copy). 404 for an unknown file and for one the caller may not see; 409 file_scan_pending " +
                "while the malware scan has no verdict.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> GetContent(
        Guid fileId, HttpContext httpContext, StoredFileService service, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(typeof(StoredFileEndpoints).FullName!);

        // The filter stores what it decided to serve; without it (a mapping error) nothing is served.
        if (!httpContext.Items.TryGetValue(StoredFileAccessFilter.ServableKey, out var item)
            || item is not StoredFileServable file || file.FileId != fileId)
        {
            logger.LogError("Stored file {FileId}: the handler ran without the access filter's decision; refusing", fileId);
            return ProblemDetailsHelper.UniformRecordNotFound(httpContext);
        }

        Stream? stream;
        try
        {
            stream = await service.OpenServableAsync(file, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status412PreconditionFailed)
        {
            // Changed since its verdict was read: the new content has none yet.
            return StoredFileAccessFilter.ScanPending(httpContext);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Any other fault (transport, credential, I/O): nothing served, a 503 the client can retry.
            logger.LogError(ex, "Stored file {FileId}: opening the blob faulted (503)", fileId);
            return StoredFileAccessFilter.Unavailable(httpContext);
        }

        if (stream is null)
        {
            return ProblemDetailsHelper.UniformRecordNotFound(httpContext);
        }

        // Before the first byte: the one audit event (O-29), ids only.
        StoredFileAudit.LogDownloaded(
            logger, file.FileId, file.Parent.Table, file.Parent.Id, StoredFileAudit.CallerSystemUser, file.CallerSystemUserId, StoredFileAudit.RouteInternal);

        var headers = httpContext.Response.Headers;
        headers[HeaderNames.XContentTypeOptions] = "nosniff";
        headers[HeaderNames.CacheControl] = "no-store";
        headers[HeaderNames.ContentSecurityPolicy] = "sandbox; default-src 'none'";
        var servedType = SafeContentType(file.ContentType);
        if (servedType == OctetStream)
        {
            // Not a type a client may render: a download, never inline content.
            headers[HeaderNames.ContentDisposition] = "attachment";
        }

        return Results.Stream(stream, servedType);
    }

    internal const string OctetStream = "application/octet-stream";

    /// <summary>
    /// The media types served as themselves (documents, images, plain text, office and archive files). Anything else — HTML, SVG,
    /// XML, script, a malformed value — is served as <see cref="OctetStream"/> with <c>Content-Disposition: attachment</c>, so a
    /// client that renders the response by its type (a <c>blob:</c> URL in the client's own origin, where this route's CSP does not
    /// apply) never renders active content a firm or user uploaded.
    /// </summary>
    private static readonly HashSet<string> InlineTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/png", "image/jpeg", "image/gif", "image/tiff", "image/bmp", "image/webp",
        "text/plain", "text/csv",
        "application/msword", "application/vnd.ms-excel", "application/vnd.ms-powerpoint", "application/vnd.ms-outlook",
        "application/zip",
    };

    private const string OfficeOpenXmlPrefix = "application/vnd.openxmlformats-officedocument.";

    /// <summary>The row's media type (without parameters other than a charset on text) when allow-listed; otherwise <see cref="OctetStream"/>.</summary>
    internal static string SafeContentType(string? contentType)
    {
        if (!MediaTypeHeaderValue.TryParse(contentType, out var parsed) || !parsed.MediaType.HasValue)
        {
            return OctetStream;
        }

        var mediaType = parsed.MediaType.Value!.ToLowerInvariant();
        if (!InlineTypes.Contains(mediaType) && !mediaType.StartsWith(OfficeOpenXmlPrefix, StringComparison.Ordinal))
        {
            return OctetStream;
        }

        return mediaType.StartsWith("text/", StringComparison.Ordinal) && parsed.Charset.HasValue
            ? $"{mediaType}; charset={parsed.Charset.Value}"
            : mediaType;
    }
}

/// <summary>Adds <see cref="StoredFileAccessFilter"/> to the download route.</summary>
public static class StoredFileAccessFilterExtensions
{
    /// <summary>
    /// Runs the download rule before the handler and marks the route as running step 0 (<see cref="StoredFileAccessFilterAttached"/>,
    /// read by FinanceStepZeroCoverageGuardTests and RouteAuthorizationGuardTests).
    /// </summary>
    public static TBuilder AddStoredFileAccessFilter<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(StoredFileAccessFilterAttached.Instance);
        return builder.AddEndpointFilter<TBuilder, StoredFileAccessFilter>();
    }
}

/// <summary>
/// Endpoint metadata: the route runs <see cref="StoredFileAccessFilter"/>, which runs step 0 FIRST (before any record read), so it
/// honours <see cref="FinanceInternalCallersOnly"/> (<see cref="IFinanceStepZeroFilter"/>).
/// </summary>
public sealed class StoredFileAccessFilterAttached : IFinanceStepZeroFilter
{
    public static readonly StoredFileAccessFilterAttached Instance = new();

    private StoredFileAccessFilterAttached()
    {
    }
}

/// <summary>
/// The download's endpoint filter (ADR-008): the whole download rule before the handler (<see cref="DecideAsync"/>), rendered as
/// ProblemDetails (ADR-019). Fails closed: an unexpected fault answers 503 and nothing is served.
/// </summary>
public sealed class StoredFileAccessFilter : IEndpointFilter
{
    /// <summary>The <see cref="HttpContext.Items"/> key of the <see cref="StoredFileServable"/> the handler serves.</summary>
    public static readonly object ServableKey = new();

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var logger = httpContext.RequestServices.GetService<ILogger<StoredFileAccessFilter>>();

        if (!httpContext.Request.RouteValues.TryGetValue("fileId", out var raw) || !Guid.TryParse(raw?.ToString(), out var fileId))
        {
            return ProblemDetailsHelper.UniformRecordNotFound(httpContext);
        }

        StoredFileDownloadDecision decision;
        try
        {
            decision = await DecideAsync(httpContext, fileId, logger);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !httpContext.RequestAborted.IsCancellationRequested)
        {
            logger?.LogError(ex, "Stored file {FileId}: the access checks faulted; refusing (503)", fileId);
            return Unavailable(httpContext);
        }

        switch (decision.Outcome)
        {
            case StoredFileDownloadOutcome.Serve:
                httpContext.Items[ServableKey] = decision.File;
                return await next(context);
            case StoredFileDownloadOutcome.Forbidden:
                logger?.LogWarning("Stored file {FileId}: refused at step 0 ({Reason})", fileId, decision.ReasonCode);
                return ProblemDetailsHelper.Forbidden(decision.ReasonCode!, traceId: httpContext.TraceIdentifier);
            case StoredFileDownloadOutcome.ScanPending:
                return ScanPending(httpContext);
            case StoredFileDownloadOutcome.Unavailable:
                return Unavailable(httpContext);
            default:
                return ProblemDetailsHelper.UniformRecordNotFound(httpContext);
        }
    }

    /// <summary>
    /// The download rule, in order, before the handler (section 4.3, 5.3; uac-r2 Q5, F1-3, F1-e, F4-2): (1) step 0 → 403 before any
    /// record read; (2) the app-only row read, unknown id → 404, fault → 503; (3) the caller's own Read on the parent, gate variant
    /// (<see cref="CallerRecordAccessProbe.GetCallerRightsForAuthorizationGateAsync"/>: no access, unknown and a fault all answer
    /// <see cref="AccessRights.None"/> on one schedule) → else 404; (4) when the row has a source document, the caller's own Read on it
    /// (the copy rule) → else 404; (5) <see cref="StoredFileService.JudgeServableAsync"/>: Servable, own path, scan verdict.
    /// </summary>
    private static async Task<StoredFileDownloadDecision> DecideAsync(HttpContext httpContext, Guid fileId, ILogger? logger)
    {
        var services = httpContext.RequestServices;
        var ct = httpContext.RequestAborted;

        // (1) step 0, before any record read.
        var stepZero = await FinanceAuthorizationFilter.EvaluateStepZeroAsync(httpContext.User, services, logger, ct);
        if (!stepZero.IsInternal || stepZero.SystemUserId is not { } callerSystemUserId)
        {
            return StoredFileDownloadDecision.Forbidden(stepZero.RefusalReasonCode ?? FinanceAuthorizationFilter.SystemFailureReasonCode);
        }

        // (2) the app-only row read (a fault throws: 503 above).
        var service = services.GetRequiredService<StoredFileService>();
        var row = await service.ReadRowAsync(fileId, ct);
        if (row?.Parent is not { } parent)
        {
            return StoredFileDownloadDecision.NotFound;
        }

        // (3) and (4): the CALLER's own Read, never the application's. Resolved as required: StoredFileService (resolved above and
        // as the handler's parameter, which binds before this filter runs) cannot exist without it, so no "not registered" branch
        // could be reached here; a missing registration fails the request before anything is served.
        var probe = services.GetRequiredService<CallerRecordAccessProbe>();

        var token = TokenHelper.ExtractBearerTokenOrNull(httpContext);
        var parentRights = await probe.GetCallerRightsForAuthorizationGateAsync(token, parent.EntitySet, parent.Id, ct);
        if (!parentRights.HasFlag(AccessRights.Read))
        {
            return StoredFileDownloadDecision.NotFound;
        }

        // A copy whose source is a communication but no document has no source this rule can check (design v5.5 retired that
        // shape): refused, never served on the parent's Read alone.
        if (row.SourceCommunicationId is not null && row.SourceDocumentId is null)
        {
            logger?.LogWarning("Stored file {FileId}: a source communication without a source document; refusing (copy rule)", fileId);
            return StoredFileDownloadDecision.NotFound;
        }

        if (row.SourceDocumentId is { } sourceDocument)
        {
            var sourceRights = await probe.GetCallerRightsForAuthorizationGateAsync(token, StoredFileSchema.DocumentEntitySet, sourceDocument, ct);
            if (!sourceRights.HasFlag(AccessRights.Read))
            {
                return StoredFileDownloadDecision.NotFound;
            }
        }

        // (5) Servable, own path, scan verdict.
        return await service.JudgeServableAsync(row, callerSystemUserId, ct);
    }

    /// <summary>409 <c>file_scan_pending</c>.</summary>
    internal static IResult ScanPending(HttpContext httpContext) =>
        Results.Problem(
            title: "File not yet available",
            detail: "The file is waiting for its malware scan. Try again in a few minutes.",
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = StoredFileEndpoints.ScanPendingErrorCode,
                ["correlationId"] = httpContext.TraceIdentifier,
            });

    /// <summary>503 <c>file_store_unavailable</c>.</summary>
    internal static IResult Unavailable(HttpContext httpContext) =>
        Results.Problem(
            title: "File store unavailable",
            detail: "The file could not be read right now. Try again later.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = StoredFileEndpoints.StoreUnavailableErrorCode,
                ["correlationId"] = httpContext.TraceIdentifier,
            });
}
