using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Spaarke.Core.Auth;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// The AP export batch routes (ebilling-r1 task 150; FR-41, EB-D-178): list the Approved invoices that can be exported, export a selection as one batch,
/// read a batch; and (task 151a, EB-D-181) download a batch's stored file and mark a Sent to AP invoice Paid (FR-42). Group <c>/api/finance</c>; step 0 on the group, <c>AddFinanceAuthorizationFilter</c> on each route, then the eBilling kill switch.
/// </summary>
/// <remarks>
/// <para><b>Authorization (EB-D-178).</b> The decision lives in ONE place, <see cref="ApExportAuthorization"/>: the caller needs Create on
/// <c>sprk_billingexport</c> and the decide right on every invoice of the batch. The POST and candidates routes declare the table-level part
/// through <see cref="ApExportAuthorization.RouteTargets"/> (refused before the handler runs and nothing is read or written); the per-invoice part is asked by the service on
/// every export. The batch read needs Read on the batch (Dataverse as the caller), uniform 404 otherwise. A later dedicated export right changes
/// that class only.</para>
/// <para><b>Answers.</b> 201 with the batch; 400 <c>ebilling.export.invalid_request</c> / <c>selection_refused</c> (per-invoice reasons, nothing claimed) / <c>batch_total_out_of_range</c> (nothing claimed);
/// 403 <c>ebilling.export.not_permitted</c> (names no invoice); 409 <c>invoice_conflict</c> (nothing claimed); 422 <c>mapping_invalid</c>; 500/503 for faults,
/// each saying whether anything was exported. 503 <c>ebilling.disabled</c> when the module is off.</para>
/// <para><b>File download (151a).</b> <c>GET export-batches/{batchId}/files/{format}</c> serves the stored CSV or JSON exactly as written at export
/// (never regenerated), through the same download pipeline as 090's file route (<see cref="StoredFileEndpoints.StreamServableAsync"/>), as an attachment
/// named <c>ap-export-{batchId}.{format}</c>. Needs Read on the batch (uniform 404 otherwise), the right 090's route already asks for the same bytes.
/// 400 <c>ebilling.export.invalid_format</c>; 404 <c>ebilling.export.file_not_available</c> (no such file on this batch, or the batch did not complete);
/// 409 <c>file_scan_pending</c>; 503 <c>file_store_unavailable</c>.</para>
/// <para><b>Mark Paid (151a).</b> <c>POST invoices/{invoiceId}/mark-paid</c> moves Sent to AP to Paid through <see cref="IInvoiceLifecycleService"/>
/// only. The invoice needs the decide right and <c>finance.read</c> (<see cref="ApExportAuthorization.PaymentRouteTargets"/>; uniform 404 otherwise).
/// Any other current status, including Paid, is 409 with the lifecycle's own reason code (<c>invoice.transition_not_allowed</c>) and
/// <c>currentStatus</c>; 404 uniform for an invoice that vanished; 409 <c>invoice.concurrent_change</c> (retry); 503 <c>ebilling.invoice.payment_unavailable</c>.</para>
/// <para><b>Email to AP and Mark Sent to AP (151c-1a, EB-D-182).</b> <c>POST export-batches/{batchId}/email</c> sends the stored CSV and JSON as the signed-in user through the
/// communication send-as-user path (bytes to the mail channel only; the communication is owned by the batch's team) to the settings' AP export email, and sets
/// the batch's Sent to AP Date once. <c>POST export-batches/{batchId}/mark-sent-to-ap</c> sets the same date for files delivered another way. Both need Read on the
/// batch (uniform 404 otherwise); see <see cref="ApExportDeliveryService"/>. No regarding link to the batch yet (task 151c-2).</para>
/// </remarks>
public static class ApExportEndpoints
{
    private const string BatchRouteKey = "batchId";
    private const string InvoiceRouteKey = "invoiceId";

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
            .AddFinanceAuthorizationFilter(BatchReadTargets, FinanceDenial.UniformNotFound)
            .RequireEbillingEnabled()
            .WithName("GetApExportBatch")
            .WithSummary("Get an AP export batch")
            .WithDescription("Status, count, total, formats, the servable export files (download through the Stored File route) and the invoice ids.")
            .Produces<ApExportBatchDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // GET /api/finance/export-batches/{batchId}/files/{format} - the batch's stored CSV or JSON, as written at export (151a)
        group.MapGet("/export-batches/{batchId:guid}/files/{format}", DownloadFile)
            .AddFinanceAuthorizationFilter(BatchReadTargets, FinanceDenial.UniformNotFound)
            .RequireEbillingEnabled()
            .WithName("DownloadApExportFile")
            .WithSummary("Download an AP export batch's CSV or JSON file")
            .WithDescription(
                "Serves the file stored at export (never regenerated) as an attachment. format is csv or json. Needs Read on the batch (uniform 404 " +
                "otherwise). 404 ebilling.export.file_not_available when the batch has no such servable file (it did not complete, or that format " +
                "was not requested).")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/finance/invoices/{invoiceId}/mark-paid - Sent to AP to Paid, nothing else (151a, FR-42)
        group.MapPost("/invoices/{invoiceId:guid}/mark-paid", MarkPaid)
            .AddFinanceAuthorizationFilter(ctx => ApExportAuthorization.PaymentRouteTargets(ctx, InvoiceRouteKey), FinanceDenial.UniformNotFound)
            .RequireEbillingEnabled()
            .WithName("MarkInvoicePaid")
            .WithSummary("Mark a Sent to AP invoice Paid")
            .WithDescription(
                "Moves the invoice from Sent to AP to Paid through the invoice lifecycle service (payment is the full approved total, one payment, never " +
                "reopened). The caller needs the decide right and Read on the invoice (uniform 404 otherwise). Any other current status, Paid included, " +
                "is 409 invoice.transition_not_allowed with currentStatus and nothing is written.")
            .Produces<InvoiceMarkPaidResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/finance/export-batches/{batchId}/email - Email to AP: the batch's CSV and JSON, as the caller (151c-1a, EB-D-181 / EB-D-182)
        group.MapPost("/export-batches/{batchId:guid}/email", EmailToAp)
            .AddFinanceAuthorizationFilter(BatchReadTargets, FinanceDenial.UniformNotFound)
            .AddEndpointFilter<CommunicationAuthorizationFilter>() // the communication path's own send precondition (an identity with an oid)
            .RequireEbillingEnabled()
            .WithName("EmailApExportBatch")
            .WithSummary("Email an AP export batch's files to AP")
            .WithDescription(
                "Sends the batch's stored CSV and JSON as attachments, as the signed-in user, to the AP export email in the eBilling settings, with " +
                "optional CCs (at most 10) and a message (at most 2000 characters). Needs Read on the batch (uniform 404 otherwise). 409 " +
                "ebilling.export.ap_email_not_configured, file_not_available (the batch did not complete, or a file is missing or unscanned) or " +
                "attachments_too_large; 400 for a bad CC or message; 503 ebilling.export.email_send_failed (nothing recorded). On success the batch's " +
                "Sent to AP Date is set when empty and never overwritten; if that write fails after the send the answer is still 200 with " +
                "sentToApDateSet false.")
            .Produces<ApExportEmailResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/finance/export-batches/{batchId}/mark-sent-to-ap - the files were delivered another way (151c-1a, EB-D-182)
        group.MapPost("/export-batches/{batchId:guid}/mark-sent-to-ap", MarkSentToAp)
            .AddFinanceAuthorizationFilter(BatchReadTargets, FinanceDenial.UniformNotFound)
            .RequireEbillingEnabled()
            .WithName("MarkApExportBatchSentToAp")
            .WithSummary("Mark an AP export batch as sent to AP")
            .WithDescription(
                "Sets the batch's Sent to AP Date to now when it is empty; otherwise leaves it (idempotent). Both answer 200 { sentToApDate, alreadySet }. " +
                "Needs Read on the batch (uniform 404 otherwise). 409 ebilling.export.batch_not_completed when the batch is not Completed.")
            .Produces<ApExportMarkSentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    /// <summary>The route's declaration for the batch read and the file download: Read on the batch named in the path (uniform 404).</summary>
    private static FinanceAuthorizationTargets BatchReadTargets(EndpointFilterInvocationContext ctx) =>
        FinanceAuthorizationTargets.FromRoute(ctx, BatchRouteKey, ApExportAuthorization.BatchEntitySet, "read");

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

    private static async Task<IResult> DownloadFile(
        Guid batchId, string format, ApExportService service, StoredFileService files, HttpContext httpContext, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var normalized = format?.ToLowerInvariant();
        if (normalized is not ("csv" or "json"))
        {
            return InvoiceLineReviewEndpoints.Refused(StatusCodes.Status400BadRequest, ApExportCodes.InvalidFormat, "The format must be csv or json.");
        }

        if (await PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync(httpContext, ct) is not { } systemUserId)
        {
            return ProblemDetailsHelper.Forbidden(
                ApExportCodes.CallerUnresolved, "Your user could not be identified, so nothing was served.", httpContext.TraceIdentifier);
        }

        var logger = loggerFactory.CreateLogger(typeof(ApExportEndpoints).FullName!);
        try
        {
            // The batch read lists a file only for a Completed batch and only when it is Servable (the stored file, never a regenerated one).
            var batch = await service.GetBatchAsync(batchId, ct);
            if (batch is null)
            {
                return FinanceAuthorizationFilter.UniformRecordNotFound(httpContext);
            }

            var listed = batch.Files.FirstOrDefault(f => f.Format == normalized);
            var row = listed is null ? null : await files.ReadRowAsync(listed.FileId, ct);
            if (row is null || row.Purpose != StoredFilePurpose.ExportFile || row.Parent != new StoredFileParent(StoredFileParentKind.BillingExport, batchId))
            {
                return FileNotAvailable(normalized);
            }

            var decision = await files.JudgeServableAsync(row, systemUserId, ct);
            switch (decision.Outcome)
            {
                case StoredFileDownloadOutcome.Serve:
                    var servedType = normalized == "csv" ? "text/csv; charset=utf-8" : "application/json; charset=utf-8";
                    return await StoredFileEndpoints.StreamServableAsync(
                        httpContext, decision.File!, files, logger, servedType, $"ap-export-{batchId:D}.{normalized}", ct);
                case StoredFileDownloadOutcome.ScanPending:
                    return StoredFileAccessFilter.ScanPending(httpContext);
                case StoredFileDownloadOutcome.Unavailable:
                    return StoredFileAccessFilter.Unavailable(httpContext);
                default:
                    return FileNotAvailable(normalized);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "AP export batch {BatchId}: reading the {Format} file faulted (503)", batchId, normalized);
            return StoredFileAccessFilter.Unavailable(httpContext);
        }
    }

    private static async Task<IResult> EmailToAp(
        Guid batchId, ApExportEmailRequest? request, ApExportDeliveryService service, HttpContext httpContext, CancellationToken ct)
    {
        if (await PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync(httpContext, ct) is not { } systemUserId)
        {
            return ProblemDetailsHelper.Forbidden(
                ApExportCodes.CallerUnresolved, "Your user could not be identified, so nothing was sent.", httpContext.TraceIdentifier);
        }

        var result = await service.EmailAsync(batchId, request, systemUserId, httpContext, ct);
        return result.Outcome == ApExportDeliveryOutcome.Done ? TypedResults.Ok(result.Email) : DeliveryProblem(result, httpContext);
    }

    private static async Task<IResult> MarkSentToAp(Guid batchId, ApExportDeliveryService service, HttpContext httpContext, CancellationToken ct)
    {
        var result = await service.MarkSentAsync(batchId, ct);
        return result.Outcome == ApExportDeliveryOutcome.Done ? TypedResults.Ok(result.MarkSent) : DeliveryProblem(result, httpContext);
    }

    /// <summary>One HTTP status and one stable code per refused delivery (ADR-019). A missing batch is the uniform record-not-found body.</summary>
    private static IResult DeliveryProblem(ApExportDeliveryResult result, HttpContext httpContext) => result.Outcome switch
    {
        ApExportDeliveryOutcome.NotFound => FinanceAuthorizationFilter.UniformRecordNotFound(httpContext),
        ApExportDeliveryOutcome.InvalidRequest => InvoiceLineReviewEndpoints.Refused(StatusCodes.Status400BadRequest, result.Code!, result.Detail),
        ApExportDeliveryOutcome.ApEmailNotConfigured or ApExportDeliveryOutcome.FileNotAvailable or ApExportDeliveryOutcome.BatchNotCompleted
            or ApExportDeliveryOutcome.AttachmentsTooLarge => InvoiceLineReviewEndpoints.Refused(StatusCodes.Status409Conflict, result.Code!, result.Detail),
        _ => InvoiceLineReviewEndpoints.Refused(StatusCodes.Status503ServiceUnavailable, result.Code!, result.Detail),
    };

    private static IResult FileNotAvailable(string format) =>
        InvoiceLineReviewEndpoints.Refused(StatusCodes.Status404NotFound, ApExportCodes.FileNotAvailable, $"This batch has no {format} file to download.");

    private static async Task<IResult> MarkPaid(Guid invoiceId, IInvoiceLifecycleService lifecycle, HttpContext httpContext, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        InvoiceTransitionResult result;
        try
        {
            result = await lifecycle.TransitionAsync(invoiceId, InvoiceLifecycleStatus.Paid, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            loggerFactory.CreateLogger(typeof(ApExportEndpoints).FullName!).LogError(ex, "Mark Paid: the move of invoice {InvoiceId} to Paid faulted (503)", invoiceId);
            return InvoiceLineReviewEndpoints.Refused(
                StatusCodes.Status503ServiceUnavailable, InvoicePaymentCodes.Unavailable,
                "The payment could not be recorded. Check the invoice's status and try again.");
        }

        switch (result.Outcome)
        {
            case InvoiceTransitionOutcome.Transitioned:
                return TypedResults.Ok(new InvoiceMarkPaidResponse(invoiceId, nameof(InvoiceLifecycleStatus.Paid)));

            case InvoiceTransitionOutcome.AlreadyInTarget:
                // Mark Paid moves Sent to AP to Paid and nothing else: an invoice that is already Paid was not moved by this call, so it is refused like
                // every other status (FR-42), with the lifecycle's own code for "this move is not allowed from here".
                return NotMarkable(InvoiceTransitionReasons.NotAllowed, InvoiceLifecycleStatus.Paid, "The invoice is already Paid.");

            case InvoiceTransitionOutcome.Refused when result.Reason == InvoiceTransitionReasons.NotFound:
                return FinanceAuthorizationFilter.UniformRecordNotFound(httpContext);

            default:
                return NotMarkable(
                    result.Reason ?? InvoiceTransitionReasons.NotAllowed, result.Current,
                    result.Reason == InvoiceTransitionReasons.ConcurrentChange
                        ? "The invoice changed while it was being updated. Nothing was changed; try again."
                        : "Only an invoice that is Sent to AP can be marked Paid. Nothing was changed.");
        }
    }

    /// <summary>409 with the lifecycle's reason code as <c>errorCode</c> and the invoice's current status (the lifecycle's closed set of names, absent when unknown).</summary>
    private static IResult NotMarkable(string reason, InvoiceLifecycleStatus? current, string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status409Conflict, detail: detail,
            extensions: new Dictionary<string, object?> { ["errorCode"] = reason, ["currentStatus"] = current?.ToString() });

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
        ApExportOutcome.TotalOutOfRange => InvoiceLineReviewEndpoints.Refused(StatusCodes.Status400BadRequest, result.Code!, result.Detail),
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
