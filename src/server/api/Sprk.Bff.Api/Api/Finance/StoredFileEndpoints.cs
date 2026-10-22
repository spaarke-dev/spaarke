using Azure;
using Microsoft.Net.Http.Headers;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Finance.Files;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// <c>GET /api/finance/files/{fileId}/content</c> (ebilling-r1 task 090; procedure-and-file-model sections 4.3, 5.3): the internal
/// route that serves a Stored File's bytes. Flagged-external users get invoice files through 093's firm route, never this one.
/// </summary>
/// <remarks>
/// <para><b>Order (uac-r2 Q5, F1-3, F1-e, F4-2).</b> <c>RequireAuthorization</c> (group) → <see cref="StoredFileAccessFilterExtensions.AddStoredFileAccessFilter{TBuilder}"/>,
/// which runs every check of <see cref="StoredFileService.AuthorizeDownloadAsync"/> before the handler: (1) step 0 → 403 before any
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
        catch (RequestFailedException ex)
        {
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
        return Results.Stream(stream, SafeContentType(file.ContentType));
    }

    /// <summary>The row's content type when it is a well-formed media type; otherwise <c>application/octet-stream</c>.</summary>
    internal static string SafeContentType(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var parsed) && parsed.MediaType.HasValue && parsed.MediaType.Value!.Contains('/')
            ? parsed.ToString()
            : "application/octet-stream";
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
/// The download's endpoint filter (ADR-008): the whole of <see cref="StoredFileService.AuthorizeDownloadAsync"/> before the handler,
/// rendered as ProblemDetails (ADR-019). Fails closed: an unexpected fault answers 503 and nothing is served.
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
            var service = httpContext.RequestServices.GetRequiredService<StoredFileService>();
            decision = await service.AuthorizeDownloadAsync(
                httpContext.User, TokenHelper.ExtractBearerTokenOrNull(httpContext), fileId, httpContext.RequestAborted);
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
