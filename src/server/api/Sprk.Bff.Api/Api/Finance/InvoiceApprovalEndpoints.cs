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
/// <para><b>Status codes (approve).</b> 200 approved or already approved (a second approve is a no-op); 401; 403 (route, not eligible, identity
/// unproven, or the bulk accept's Signal rights); 404 uniform; 409 (lines open with counts, held, not In Review, no or ambiguous approval portion,
/// already decided, approval changed, another decision in progress, totals or transition refused); 500 (no core record, a decision the writer
/// refuses); 503 <c>ebilling.disabled</c>, <c>ebilling.approval.unavailable</c> (nothing changed) or <c>ebilling.approval.incomplete</c>
/// (approve again to finish).</para>
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
                "Any one eligible approver approves. Refused while a line is Pending or Flagged (409 with counts) or the invoice is held. Undecided " +
                "Clean lines are accepted in bulk. Moves the invoice to Approved, freezes the approved total on the approval and records one " +
                "Decision Record naming the approver. A second approve is a no-op.")
            .Produces<InvoiceApprovalResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> GetApproval(Guid invoiceId, InvoiceApprovalService service, HttpContext httpContext, CancellationToken ct)
    {
        // An unproven identity is simply "not eligible" here (canApprove false); approve refuses it with 403.
        var caller = await PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync(httpContext, ct);
        InvoiceApprovalStateDto? state;
        try
        {
            state = await service.GetAsync(invoiceId, caller, ct);
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
                state = await service.GetAsync(invoiceId, caller, ct);
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
