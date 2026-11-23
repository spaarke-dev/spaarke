using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// Reject and Return to Firm of an invoice (ebilling-r1 task 104; FR-36, FR-30, FR-35; D-34, D-43), ADR-001 (Minimal API), ADR-008 (endpoint
/// filters), ADR-019 (stable codes). Own endpoints file beside <see cref="InvoiceApprovalEndpoints"/> (103).
/// </summary>
/// <remarks>
/// <para><b>Authorization, two layers.</b> (1) The route filter, asked of Dataverse AS THE CALLER and failing closed
/// (<see cref="FinanceAuthorizationFilterExtensions"/>): <c>ebilling.invoice.decide</c> (Write on the invoice). (2) The service resolves the
/// caller's eligibility LIVE from the invoice's core record (<see cref="EligibleApproverResolver"/>), matched on the caller's own contact
/// (EB-D-171): a caller with Write who is not an eligible approver gets 403 <c>ebilling.approver.not_eligible</c> and nothing is written.
/// The kill switch (<c>RequireEbillingEnabled</c>, ADR-018) follows the authorization filter and never replaces it.</para>
/// <para><b>Nothing is trusted from the request but the reason.</b> The decider is the caller's <c>systemuserid</c> resolved from the caller's
/// own token; the decision id is derived from the approval portion, so every attempt by every approver is one decision.</para>
/// <para><b>Status codes.</b> 200 decided or already decided (a repeat is a no-op, <c>alreadyDecided</c>); 400 <c>ebilling.reason_required</c> /
/// <c>ebilling.reason_too_long</c>; 401; 403 (route, not eligible, identity unproven, or no rights to close the invoice's Signals); 404
/// uniform; 409 (not In Review, held, no or ambiguous approval portion, unknown or already-decided approval, approval changed or claimed by
/// another approver, another decision in progress, transition refused); 500 (no single core record, a decision the writer refuses); 503
/// <c>ebilling.disabled</c>, <c>ebilling.invoice.decision_unavailable</c> (nothing changed) or <c>ebilling.invoice.decision_incomplete</c>
/// (repeat the call to finish).</para>
/// </remarks>
public static class InvoiceRejectReturnEndpoints
{
    public static IEndpointRouteBuilder MapInvoiceRejectReturnEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/finance")
            .RequireAuthorization()
            .RequireInternalFinanceCaller() // step 0 on every /api/finance route (section 5.3, task 099)
            .WithTags("Finance");

        // POST /api/finance/invoices/{invoiceId}/reject - reject the whole invoice (final)
        group.MapPost("/invoices/{invoiceId:guid}/reject", Reject)
            .AddFinanceAuthorizationFilter("ebilling.invoice.decide", FinanceAuthorizationFilter.InvoiceEntitySet, routeKey: "invoiceId")
            .RequireEbillingEnabled()
            .WithName("RejectInvoice")
            .WithSummary("Reject an invoice (final; reason required)")
            .WithDescription(
                "Any one eligible approver rejects the whole invoice. A reason is required (400 ebilling.reason_required). Only from In Review " +
                "(409 otherwise). Moves the invoice to Rejected (Inactive), sets the approval and records one Decision Record naming the " +
                "approver and the reason. A repeat is a no-op.")
            .Produces<InvoiceDecisionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/finance/invoices/{invoiceId}/return - return the invoice to the firm
        group.MapPost("/invoices/{invoiceId:guid}/return", Return)
            .AddFinanceAuthorizationFilter("ebilling.invoice.decide", FinanceAuthorizationFilter.InvoiceEntitySet, routeKey: "invoiceId")
            .RequireEbillingEnabled()
            .WithName("ReturnInvoice")
            .WithSummary("Return an invoice to the firm (reason required)")
            .WithDescription(
                "Any one eligible approver returns the invoice to the firm to correct and resubmit. A reason is required. Only from In Review " +
                "(409 otherwise). Moves the invoice to Returned to Firm (Inactive), sets the approval and records one Decision Record naming " +
                "the approver and the reason. A repeat is a no-op. The firm's accepted resubmission is linked to this invoice.")
            .Produces<InvoiceDecisionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static Task<IResult> Reject(
        Guid invoiceId, InvoiceDecisionRequest? body, InvoiceRejectReturnService service, HttpContext httpContext, CancellationToken ct) =>
        DecideAsync(InvoiceDecisionKind.Reject, invoiceId, body, service, httpContext, ct);

    private static Task<IResult> Return(
        Guid invoiceId, InvoiceDecisionRequest? body, InvoiceRejectReturnService service, HttpContext httpContext, CancellationToken ct) =>
        DecideAsync(InvoiceDecisionKind.Return, invoiceId, body, service, httpContext, ct);

    private static async Task<IResult> DecideAsync(
        InvoiceDecisionKind kind, Guid invoiceId, InvoiceDecisionRequest? body, InvoiceRejectReturnService service, HttpContext httpContext, CancellationToken ct)
    {
        // The decider is the caller (never request input); an unproven identity is refused before anything is read or written.
        if (await PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync(httpContext, ct) is not { } caller)
        {
            return ProblemDetailsHelper.Forbidden(
                InvoiceRejectReturnCodes.DeciderUnresolved, "Your user could not be identified, so the decision was not recorded.", httpContext.TraceIdentifier);
        }

        var token = TokenHelper.ExtractBearerTokenOrNull(httpContext);
        var result = kind == InvoiceDecisionKind.Reject
            ? await service.RejectAsync(invoiceId, caller, body?.Reason, token, ct)
            : await service.ReturnAsync(invoiceId, caller, body?.Reason, token, ct);

        return result.Outcome is InvoiceDecisionOutcome.Decided or InvoiceDecisionOutcome.AlreadyDecided
            ? TypedResults.Ok(new InvoiceDecisionResponse(
                result.Outcome == InvoiceDecisionOutcome.Decided ? "decided" : "alreadyDecided",
                kind == InvoiceDecisionKind.Reject ? "Rejected" : "Returned",
                result.DecisionId, result.DecisionRecordId))
            : ToProblem(result, httpContext);
    }

    /// <summary>Maps a refused or unfinished call to one HTTP status and one stable code (ADR-019).</summary>
    internal static IResult ToProblem(InvoiceDecisionResult result, HttpContext httpContext) => result.Outcome switch
    {
        InvoiceDecisionOutcome.InvoiceNotFound => FinanceAuthorizationFilter.UniformRecordNotFound(httpContext),
        InvoiceDecisionOutcome.NotEligible or InvoiceDecisionOutcome.Forbidden =>
            ProblemDetailsHelper.Forbidden(result.Code, result.Detail ?? "Not allowed.", httpContext.TraceIdentifier),
        InvoiceDecisionOutcome.InvalidRequest => InvoiceLineReviewEndpoints.Refused(StatusCodes.Status400BadRequest, result.Code, result.Detail),
        InvoiceDecisionOutcome.Held or InvoiceDecisionOutcome.NotInReview or InvoiceDecisionOutcome.Conflict or InvoiceDecisionOutcome.Busy =>
            InvoiceLineReviewEndpoints.Refused(StatusCodes.Status409Conflict, result.Code, result.Detail),
        InvoiceDecisionOutcome.Failed => InvoiceLineReviewEndpoints.Refused(StatusCodes.Status500InternalServerError, result.Code, result.Detail),
        InvoiceDecisionOutcome.Incomplete => Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            detail: result.Detail,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = result.Code,
                ["decisionId"] = result.DecisionId,
                ["failedStep"] = result.FailedStep,
            }),
        _ => InvoiceLineReviewEndpoints.Refused(StatusCodes.Status503ServiceUnavailable, result.Code, result.Detail),
    };
}

/// <summary>The body of a reject or return: the reason, required (400 <c>ebilling.reason_required</c> without it).</summary>
public sealed record InvoiceDecisionRequest(string? Reason);

/// <summary>200 body: <c>decided</c> (this call decided) or <c>alreadyDecided</c> (a no-op), the invoice's outcome (<c>Rejected</c> or
/// <c>Returned</c>), the decision and its Decision Record (null on a no-op that did not look it up).</summary>
public sealed record InvoiceDecisionResponse(string Outcome, string Decision, Guid? DecisionId, Guid? DecisionRecordId);
