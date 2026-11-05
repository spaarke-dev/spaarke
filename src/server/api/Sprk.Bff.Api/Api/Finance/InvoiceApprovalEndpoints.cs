using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// The approval state and the approve operation of an invoice (ebilling-r1 task 103; FR-35, FR-11, I-2, D-40, D-72, D-78), ADR-001 (Minimal API),
/// ADR-008 (endpoint filters), ADR-019 (stable codes).
/// </summary>
/// <remarks>
/// <para><b>Authorization, two layers.</b> (1) The route filter, asked of Dataverse AS THE CALLER and failing closed
/// (<see cref="FinanceAuthorizationFilterExtensions"/>): the state with <c>finance.read</c> (Read on the invoice), approve with
/// <c>ebilling.invoice.decide</c> (Write on the invoice). (2) For approve, the service resolves the caller's eligibility LIVE from the invoice's
/// core record (<see cref="EligibleApproverResolver"/>): a caller with Write who is not an eligible approver gets 403
/// <c>ebilling.approver.not_eligible</c> and nothing is written. The stored eligible-approver set never grants. The kill switch
/// (<c>RequireEbillingEnabled</c>, ADR-018) follows the authorization filter and never replaces it.</para>
/// <para><b>Nothing is trusted from the request.</b> Approve takes no body: the decider is the caller's <c>systemuserid</c> resolved from the
/// caller's own token, and the decision id is derived from the approval portion so every attempt by every approver is one decision.</para>
/// <para><b>Status codes (approve).</b> 200 approved or already approved (a second approve is a no-op); 401; 403 (route, not eligible by the
/// caller's own contact or no contact (EB-D-171), identity unproven, or the rights to close the invoice's or the bulk accept's Signals); 404
/// uniform; 409 (lines open with counts, held, not In Review, no or ambiguous approval portion, unknown approval status, already decided,
/// approval changed or claimed by another approver, another person's unrecorded line decision, another decision in progress, a correction
/// that did not finish (<c>ebilling.approval.corrections_pending</c>), totals or transition refused); 500 (no single core record, a decision the writer refuses); 503 <c>ebilling.disabled</c>,
/// <c>ebilling.approval.unavailable</c> (nothing changed) or <c>ebilling.approval.incomplete</c> (approve again to finish).</para>
/// </remarks>
public static class InvoiceApprovalEndpoints
{
    public static IEndpointRouteBuilder MapInvoiceApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/finance")
            .RequireAuthorization()
            .RequireInternalFinanceCaller() // step 0 on every /api/finance route (section 5.3, task 099)
            .WithTags("Finance");

        // GET /api/finance/invoices/{invoiceId}/approval - the approval state for the caller
        group.MapGet("/invoices/{invoiceId:guid}/approval", GetApproval)
            .AddFinanceAuthorizationFilter("finance.read", FinanceAuthorizationFilter.InvoiceEntitySet, routeKey: "invoiceId")
            .RequireEbillingEnabled()
            .WithName("GetInvoiceApproval")
            .WithSummary("Get the approval state of an invoice")
            .WithDescription(
                "Returns the approval status, the eligible approvers (resolved live from the core record's approver roles), whether the caller " +
                "may approve, the open-line counts (I-2), the undecided Clean lines an approve accepts, and who decided and when. Rechecks the " +
                "stored eligible approvers while the approval is pending.")
            .Produces<InvoiceApprovalStateDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/finance/invoices/{invoiceId}/approve - approve the invoice
        group.MapPost("/invoices/{invoiceId:guid}/approve", Approve)
            .AddFinanceAuthorizationFilter("ebilling.invoice.decide", FinanceAuthorizationFilter.InvoiceEntitySet, routeKey: "invoiceId")
            .RequireEbillingEnabled()
            .WithName("ApproveInvoice")
            .WithSummary("Approve an invoice")
            .WithDescription(
                "Any one eligible approver approves. Refused while a line is Pending or Flagged (409 with counts), the invoice is held, or a saved " +
                "correction did not finish (409, repeat it). Undecided " +
                "Clean lines are accepted in bulk. Moves the invoice to Approved, freezes the approved total on the approval and records one " +
                "Decision Record naming the approver. A second approve is a no-op.")
            .Produces<InvoiceApprovalResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/finance/invoices/{invoiceId}/firm - Set firm on a held no-firm invoice (task 095d-1b). Two filters, both as the caller:
        // Write on the invoice (uniform 404), then Read and AppendTo on the body's firm (403). The kill switch follows them.
        group.MapPost("/invoices/{invoiceId:guid}/firm", AssignFirm)
            .AddFinanceAuthorizationFilter("ebilling.invoice.assign_firm", FinanceAuthorizationFilter.InvoiceEntitySet, routeKey: "invoiceId", FinanceDenial.UniformNotFound)
            .AddFinanceAuthorizationFilter(ResolveAssignFirmTargets)
            .RequireEbillingEnabled()
            .WithName("AssignInvoiceFirm")
            .WithSummary("Set the firm of an invoice submitted without one")
            .WithDescription(
                "The reviewer names a firm linked to the invoice's core record. Refused unless the invoice is In Review, has no firm and is held " +
                "for one. In one transaction: the submission is renumbered into the firm's scope, the firm and the new number are set on the " +
                "invoice, its lines, its approval and its files, the hold is cleared and the decision is stored; then the Decision Record is " +
                "written. If the record is not written, 503 ebilling.assign_firm.incomplete names the failedStep; repeating with the same firm " +
                "finishes it without applying again (and is otherwise a no-op); with another firm it is refused.")
            .Produces<AssignFirmResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    /// <summary>
    /// Task 095d-1b: the Set-firm route's firm checks, from the BOUND body: Read (<c>read</c>) and AppendTo (<c>finance.attach_invoice</c>,
    /// the invoice's firm lookup attaches to it) on the firm, as the caller. A body that names no firm is the 400 before any rights query on it.
    /// </summary>
    internal static FinanceAuthorizationTargets ResolveAssignFirmTargets(EndpointFilterInvocationContext context)
    {
        var request = context.Arguments.OfType<AssignFirmRequest>().FirstOrDefault();
        if (request?.FirmId is not { } firmId || firmId == Guid.Empty)
        {
            return FinanceAuthorizationTargets.Reject(
                InvoiceLineReviewEndpoints.Refused(StatusCodes.Status400BadRequest, AssignFirmCodes.FirmRequired, "A firm is required."));
        }

        return FinanceAuthorizationTargets.Authorize(
            new FinanceAuthorizationCheck
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = FinanceAuthorizationFilter.VendorOrganizationEntitySet,
                RecordId = firmId,
                Operation = "read",
                Source = "body.firmId",
            },
            new FinanceAuthorizationCheck
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = FinanceAuthorizationFilter.VendorOrganizationEntitySet,
                RecordId = firmId,
                Operation = "finance.attach_invoice",
                Source = "body.firmId",
            });
    }

    /// <summary>
    /// The service is <c>[FromServices]</c> so a host that maps this file without registering it still starts (the approval contract hosts);
    /// the route then answers 500 only when it is called.
    /// </summary>
    private static async Task<IResult> AssignFirm(
        Guid invoiceId, [FromBody] AssignFirmRequest request, [FromServices] InvoiceAssignFirmService service, HttpContext httpContext, CancellationToken ct)
    {
        if (await PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync(httpContext, ct) is not { } caller)
        {
            return ProblemDetailsHelper.Forbidden(
                AssignFirmCodes.DeciderUnresolved, "Your user could not be identified, so the firm was not set.", httpContext.TraceIdentifier);
        }

        var result = await service.SetFirmAsync(
            invoiceId, request.FirmId ?? Guid.Empty, request.Note, caller, TokenHelper.ExtractBearerTokenOrNull(httpContext), ct);

        return result switch
        {
            { Outcome: AssignFirmOutcome.Assigned or AssignFirmOutcome.AlreadyAssigned } => TypedResults.Ok(new AssignFirmResponse(
                result.Outcome == AssignFirmOutcome.Assigned ? "assigned" : "alreadyAssigned",
                result.DecisionId, result.TrackingNumber, result.PreviousTrackingNumber, result.RulesRerun)),
            // 095d-1c: the firm is set, its Decision Record is not. The step and the fixed sentence only: no id, no exception text.
            { Outcome: AssignFirmOutcome.Incomplete } => Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                detail: result.Detail,
                extensions: new Dictionary<string, object?> { ["errorCode"] = result.Code, ["failedStep"] = result.FailedStep }),
            { Status: StatusCodes.Status404NotFound } => FinanceAuthorizationFilter.UniformRecordNotFound(httpContext),
            // Rights the caller lacks on the core record: the body of the route's own firm-rights refusal (the filter's 403), so a caller without
            // those rights is told the same thing whatever firm it names - one the filter refuses (missing or unreadable) or one the service reaches.
            { Status: StatusCodes.Status403Forbidden, Code: AssignFirmCodes.NotAuthorized } =>
                ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.InsufficientRightsReasonCode, traceId: httpContext.TraceIdentifier),
            { Status: StatusCodes.Status403Forbidden } =>
                ProblemDetailsHelper.Forbidden(result.Code, result.Detail ?? "Not allowed.", httpContext.TraceIdentifier),
            _ => InvoiceLineReviewEndpoints.Refused(result.Status, result.Code, result.Detail),
        };
    }

    private static async Task<IResult> GetApproval(Guid invoiceId, InvoiceApprovalService service, HttpContext httpContext, CancellationToken ct)
    {
        // An unproven identity is simply "not eligible" here (canApprove false); approve refuses it with 403.
        var caller = await PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync(httpContext, ct);
        InvoiceApprovalStateDto? state;
        try
        {
            state = await service.GetAsync(invoiceId, caller, DecideCaller(httpContext), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return InvoiceLineReviewEndpoints.Refused(StatusCodes.Status503ServiceUnavailable, InvoiceApprovalCodes.Unavailable, "The approval state could not be read. Try again.");
        }

        return state is null ? FinanceAuthorizationFilter.UniformRecordNotFound(httpContext) : TypedResults.Ok(state);
    }

    private static async Task<IResult> Approve(Guid invoiceId, InvoiceApprovalService service, HttpContext httpContext, CancellationToken ct)
    {
        if (await PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync(httpContext, ct) is not { } caller)
        {
            return ProblemDetailsHelper.Forbidden(
                InvoiceApprovalCodes.DeciderUnresolved, "Your user could not be identified, so the invoice was not approved.", httpContext.TraceIdentifier);
        }

        var result = await service.ApproveAsync(invoiceId, caller, TokenHelper.ExtractBearerTokenOrNull(httpContext), ct);
        if (result.Outcome is ApprovalOutcome.Approved or ApprovalOutcome.AlreadyApproved)
        {
            // The current state, best effort: the approval already succeeded or was a no-op, so a failed read never turns it into an error.
            InvoiceApprovalStateDto? state = null;
            try
            {
                state = await service.GetAsync(invoiceId, caller, DecideCaller(httpContext), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                state = null;
            }

            return TypedResults.Ok(new InvoiceApprovalResponse(
                result.Outcome == ApprovalOutcome.Approved ? "approved" : "alreadyApproved", result.DecisionId, result.DecisionRecordId, state));
        }

        return ToProblem(result, httpContext);
    }

    /// <summary>F4: the caller's own identity and token, so <c>canApprove</c> asks the decide right (Write on the invoice) of Dataverse as the
    /// caller, as the review read's <c>canDecide</c>. A missing token reaches the service as null and answers no (fail closed).</summary>
    private static InvoiceContextService.InvoiceCaller DecideCaller(HttpContext httpContext) => new(
        CallerResolution.ResolveObjectId(httpContext.User) ?? string.Empty,
        TokenHelper.ExtractBearerTokenOrNull(httpContext));

    /// <summary>Maps a refused or unfinished approve to one HTTP status and one stable code (ADR-019).</summary>
    internal static IResult ToProblem(ApprovalResult result, HttpContext httpContext) => result.Outcome switch
    {
        ApprovalOutcome.InvoiceNotFound => FinanceAuthorizationFilter.UniformRecordNotFound(httpContext),
        ApprovalOutcome.NotEligible or ApprovalOutcome.Forbidden =>
            ProblemDetailsHelper.Forbidden(result.Code, result.Detail ?? "Not allowed.", httpContext.TraceIdentifier),
        ApprovalOutcome.InvalidRequest => InvoiceLineReviewEndpoints.Refused(StatusCodes.Status400BadRequest, result.Code, result.Detail),
        ApprovalOutcome.LinesOpen => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            detail: result.Detail,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = result.Code,
                ["pendingLines"] = result.OpenLines?.Pending ?? 0,
                ["flaggedLines"] = result.OpenLines?.Flagged ?? 0,
            }),
        ApprovalOutcome.Held or ApprovalOutcome.NotInReview or ApprovalOutcome.Conflict or ApprovalOutcome.Busy =>
            InvoiceLineReviewEndpoints.Refused(StatusCodes.Status409Conflict, result.Code, result.Detail),
        ApprovalOutcome.Failed => InvoiceLineReviewEndpoints.Refused(StatusCodes.Status500InternalServerError, result.Code, result.Detail),
        ApprovalOutcome.Incomplete => Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            detail: result.Detail,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = result.Code,
                ["decisionId"] = result.DecisionId,
                ["failedStep"] = result.FailedStep,
                // The bulk accept stopped after its lines were written: repeat it through lines/decisions with these ids and this id.
                ["lineDecisionId"] = result.LineDecisionId,
                ["committedLineIds"] = result.CommittedLineIds ?? [],
            }),
        _ => InvoiceLineReviewEndpoints.Refused(StatusCodes.Status503ServiceUnavailable, result.Code, result.Detail),
    };
}

/// <summary>200 body of approve: <c>approved</c> (this call approved) or <c>alreadyApproved</c> (a no-op), the decision and its record, and the
/// current state (null when it could not be read after the approval).</summary>
public sealed record InvoiceApprovalResponse(string Outcome, Guid? DecisionId, Guid? DecisionRecordId, InvoiceApprovalStateDto? State);

/// <summary>Body of Set firm (task 095d-1b): the firm (required) and an optional note (at most 2000 characters).</summary>
public sealed record AssignFirmRequest(Guid? FirmId, string? Note);

/// <summary>200 body of Set firm: <c>assigned</c> (this call applied it) or <c>alreadyAssigned</c> (a repeat: nothing applied), the decision id,
/// the new and the previous tracking number, and <c>rulesRerun</c> (<c>pending</c>: the re-run is 095d-2). Since 095d-1c a 200 means the
/// Decision Record exists (written now or found).</summary>
public sealed record AssignFirmResponse(string Outcome, Guid? DecisionId, string? TrackingNumber, string? PreviousTrackingNumber, string? RulesRerun);
