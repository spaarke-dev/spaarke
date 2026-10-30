using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance;
using Spaarke.Core.Auth;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// The AP export batch routes (ebilling-r1 task 150; FR-41, EB-D-178): list the Approved invoices that can be exported, export a selection as one batch,
/// read a batch. Group <c>/api/finance</c>; step 0 on the group, <c>AddFinanceAuthorizationFilter</c> on each route, then the eBilling kill switch.
/// </summary>
/// <remarks>
/// <para><b>Authorization (EB-D-178).</b> The decision lives in ONE place, <see cref="ApExportAuthorization"/>: the caller needs Create on
/// <c>sprk_billingexport</c> and the decide right on every invoice of the batch. The POST and candidates routes declare the table-level part
/// through <see cref="ApExportAuthorization.RouteTargets"/> (refused before the handler runs and nothing is read or written); the per-invoice part is asked by the service on
/// every export. The batch read needs Read on the batch (Dataverse as the caller), uniform 404 otherwise. A later dedicated export right changes
/// that class only.</para>
/// <para><b>Answers.</b> 201 with the batch; 400 <c>ebilling.export.invalid_request</c> / <c>selection_refused</c> (per-invoice reasons, nothing claimed);
/// 403 <c>ebilling.export.not_permitted</c> (names no invoice); 409 <c>invoice_conflict</c> (nothing claimed); 422 <c>mapping_invalid</c>; 500/503 for faults,
/// each saying whether anything was exported. 503 <c>ebilling.disabled</c> when the module is off.</para>
/// </remarks>
public static class ApExportEndpoints
{
    private const string BatchRouteKey = "batchId";

    public static IEndpointRouteBuilder MapApExportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/finance")
            .RequireAuthorization()
            .RequireInternalFinanceCaller() // step 0 on every /api/finance route (section 5.3, task 099)
            .WithTags("Finance");

        // GET /api/finance/export-batches/candidates - the Approved invoices not yet in a batch that the caller may export
        group.MapGet("/export-batches/candidates", GetCandidates)
            .AddFinanceAuthorizationFilter(_ => ApExportAuthorization.RouteTargets())
            .RequireEbillingEnabled()
            .WithName("GetApExportCandidates")
            .WithSummary("List the invoices that can be exported to AP")
            .WithDescription(
                "Approved invoices with no export batch that the caller may export (Create on the export table and the decide right on the invoice), " +
                "oldest invoice date first. At most 500 are examined; truncated says whether there were more.")
            .Produces<ApExportCandidatesDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/finance/export-batches - export a selection as one batch
        group.MapPost("/export-batches", CreateBatch)
            .AddFinanceAuthorizationFilter(_ => ApExportAuthorization.RouteTargets())
            .RequireEbillingEnabled()
            .WithName("CreateApExportBatch")
            .WithSummary("Export a selection of Approved invoices to AP as one batch")
            .WithDescription(
                "All or nothing. The caller needs Create on the export table and the decide right on every selected invoice (403 otherwise, naming no " +
                "invoice). Only Approved invoices with no batch are accepted (400 with a reason per invoice). The server claims each invoice for this " +
                "batch, writes a CSV (one row per invoice) and a JSON (with lines) as internal-only Stored Files from the mapping in the eBilling " +
                "settings (D-56 default), moves the invoices to Sent to AP and marks the batch Completed. Two batches never hold the same invoice (409).")
            .Produces<ApExportBatchDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // GET /api/finance/export-batches/{batchId} - one batch
        group.MapGet("/export-batches/{batchId:guid}", GetBatch)
            .AddFinanceAuthorizationFilter(
                ctx => FinanceAuthorizationTargets.FromRoute(ctx, BatchRouteKey, ApExportAuthorization.BatchEntitySet, "read"),
                FinanceDenial.UniformNotFound)
            .RequireEbillingEnabled()
            .WithName("GetApExportBatch")
            .WithSummary("Get an AP export batch")
            .WithDescription("Status, count, total, formats, the servable export files (download through the Stored File route) and the invoice ids.")
            .Produces<ApExportBatchDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> GetCandidates(ApExportService service, HttpContext httpContext, CancellationToken ct)
    {
        ApExportCandidatesDto? candidates;
        try
        {
            candidates = await service.ListCandidatesAsync(Caller(httpContext), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return InvoiceLineReviewEndpoints.Refused(StatusCodes.Status503ServiceUnavailable, ApExportCodes.Unavailable, "The candidates could not be read. Try again.");
        }

        return candidates is null
            ? InvoiceLineReviewEndpoints.Refused(StatusCodes.Status503ServiceUnavailable, ApExportCodes.AuthorizationUnavailable, "Your rights could not be confirmed. Try again.")
            : TypedResults.Ok(candidates);
    }

    private static async Task<IResult> CreateBatch(ApExportRequest? request, ApExportService service, HttpContext httpContext, CancellationToken ct)
    {
        if (request is null)
        {
            return InvoiceLineReviewEndpoints.Refused(StatusCodes.Status400BadRequest, ApExportCodes.InvalidRequest, "A request body is required.");
        }

        if (await PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync(httpContext, ct) is not { } systemUserId)
        {
            return ProblemDetailsHelper.Forbidden(
                ApExportCodes.CallerUnresolved, "Your user could not be identified, so nothing was exported.", httpContext.TraceIdentifier);
        }

        var result = await service.CreateBatchAsync(request, Caller(httpContext), systemUserId, ct);
        if (result.Outcome == ApExportOutcome.Created)
        {
            return TypedResults.Created($"/api/finance/export-batches/{result.Batch!.BatchId}", result.Batch);
        }

        return ToProblem(result, httpContext);
    }

    private static async Task<IResult> GetBatch(Guid batchId, ApExportService service, HttpContext httpContext, CancellationToken ct)
    {
        ApExportBatchDto? batch;
        try
        {
            batch = await service.GetBatchAsync(batchId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return InvoiceLineReviewEndpoints.Refused(StatusCodes.Status503ServiceUnavailable, ApExportCodes.Unavailable, "The batch could not be read. Try again.");
        }

        return batch is null ? FinanceAuthorizationFilter.UniformRecordNotFound(httpContext) : TypedResults.Ok(batch);
    }

    private static InvoiceContextService.InvoiceCaller Caller(HttpContext httpContext) => new(
        CallerResolution.ResolveObjectId(httpContext.User) ?? string.Empty,
        TokenHelper.ExtractBearerTokenOrNull(httpContext));

    /// <summary>Maps a refused or unfinished export to one HTTP status and one stable code (ADR-019). A failure that undid itself says nothing was exported.</summary>
    internal static IResult ToProblem(ApExportResult result, HttpContext httpContext) => result.Outcome switch
    {
        ApExportOutcome.NotPermitted => ProblemDetailsHelper.Forbidden(result.Code!, result.Detail, httpContext.TraceIdentifier),
        ApExportOutcome.InvalidRequest => InvoiceLineReviewEndpoints.Refused(StatusCodes.Status400BadRequest, result.Code!, result.Detail),
        ApExportOutcome.SelectionRefused => Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            detail: result.Detail,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = result.Code,
                ["invoices"] = result.Invoices?.Select(i => new { invoiceId = i.InvoiceId, reason = i.Reason }).ToArray() ?? [],
            }),
        ApExportOutcome.MappingInvalid => InvoiceLineReviewEndpoints.Refused(StatusCodes.Status422UnprocessableEntity, result.Code!, result.Detail),
        ApExportOutcome.Conflict => Results.Problem(
            statusCode: StatusCodes.Status409Conflict, detail: result.Detail,
            extensions: new Dictionary<string, object?> { ["errorCode"] = result.Code, ["batchId"] = result.BatchId }),
        ApExportOutcome.Incomplete => Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError, detail: result.Detail,
            extensions: new Dictionary<string, object?> { ["errorCode"] = result.Code, ["batchId"] = result.BatchId }),
        ApExportOutcome.Failed when result.Code == ApExportCodes.FileTooLarge => Results.Problem(
            statusCode: StatusCodes.Status422UnprocessableEntity, detail: result.Detail,
            extensions: new Dictionary<string, object?> { ["errorCode"] = result.Code, ["batchId"] = result.BatchId }),
        _ => Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable, detail: result.Detail,
            extensions: new Dictionary<string, object?> { ["errorCode"] = result.Code, ["batchId"] = result.BatchId }),
    };
}
