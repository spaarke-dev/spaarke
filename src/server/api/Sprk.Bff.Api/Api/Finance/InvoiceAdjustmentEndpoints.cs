using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// Invoice-level adjustments (ebilling-r1 task 102; FR-32, FR-07, D-42, D-74, NFR-09), ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// Same route group, filters and status conventions as <see cref="InvoiceLineReviewEndpoints"/> (task 100).
/// </summary>
/// <remarks>
/// <para><b>Authorization.</b> Every route is authorized on the invoice in the route, asked of Dataverse AS THE CALLER and failing closed
/// (<see cref="FinanceAuthorizationFilterExtensions"/>): the rule list with <c>finance.read</c> (Read), add and remove with
/// <c>ebilling.invoice.decide</c> (Write; D-77). Core-record access reaches the invoice by inheritance (013, ADR-034), so a caller without
/// access to the invoice's matter, project or work assignment is refused with 403 before any service runs. The eBilling kill switch
/// (<c>RequireEbillingEnabled</c>, ADR-018) follows the authorization filter and never replaces it.</para>
/// <para><b>Nothing is trusted from the body.</b> The invoice is the route's; the body carries method, value, reason and an optional rule id
/// (checked against the invoice's own rule options), never an amount or a total (I-1: 016 computes both). The reviewer is the caller's
/// <c>systemuserid</c> from their own token. The adjustment id is the client's and only makes a retry idempotent.</para>
/// <para><b>Status codes.</b> 200 applied; 400 invalid (<c>errorCode</c> says which rule; <c>ebilling.adjustment.negative_total</c> when the
/// approved total would go below zero, FR-32); 401; 403; 404 uniform (invoice or adjustment absent or unreadable); 409 (not In Review, another
/// change in progress, the adjustment id reused, the adjustment changed); 503 <c>ebilling.disabled</c> or a transient failure; 503
/// <c>ebilling.adjustment.incomplete</c> when the line was written but the totals step did not finish (repeat the same request).</para>
/// </remarks>
public static class InvoiceAdjustmentEndpoints
{
    public static IEndpointRouteBuilder MapInvoiceAdjustmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/finance")
            .RequireAuthorization()
            .RequireInternalFinanceCaller() // step 0 on every /api/finance route (section 5.3, task 099)
            .WithTags("Finance");

        // GET /api/finance/invoices/{invoiceId}/adjustment-rules - the rules a reviewer may cite (F-8, D-74)
        group.MapGet("/invoices/{invoiceId:guid}/adjustment-rules", ListRules)
            .AddFinanceAuthorizationFilter("finance.read", FinanceAuthorizationFilter.InvoiceEntitySet, routeKey: "invoiceId")
            .RequireEbillingEnabled()
            .WithName("ListInvoiceAdjustmentRules")
            .WithSummary("List the rules a reviewer may cite on an invoice adjustment")
            .WithDescription(
                "The published billing rules of the invoice's applicable rule sets (the client default and the invoice firm's) in force on the " +
                "invoice date, each with rule set name, version and clause reference. No document link.")
            .Produces<AdjustmentRulesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/finance/invoices/{invoiceId}/adjustments - add an invoice-level adjustment
        group.MapPost("/invoices/{invoiceId:guid}/adjustments", AddAdjustment)
            .AddFinanceAuthorizationFilter("ebilling.invoice.decide", FinanceAuthorizationFilter.InvoiceEntitySet, routeKey: "invoiceId")
            .RequireEbillingEnabled()
            .WithName("AddInvoiceAdjustment")
            .WithSummary("Add an invoice-level adjustment (percent of fees after line adjustments, or a fixed amount)")
            .WithDescription(
                "Method percent (above 0, at most 100) or fixed (an amount above 0), a mandatory reason and an optional rule from the rule list. " +
                "The server computes the amount and the invoice totals; an adjustment that would make the approved total negative is refused " +
                "(400 ebilling.adjustment.negative_total) and nothing is written. Only while the invoice is In Review. Repeating the request with " +
                "the same adjustmentId adds nothing twice.")
            .Produces<AdjustmentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // DELETE /api/finance/invoices/{invoiceId}/adjustments/{lineId} - remove (deactivate) an adjustment
        group.MapDelete("/invoices/{invoiceId:guid}/adjustments/{lineId:guid}", RemoveAdjustment)
            .AddFinanceAuthorizationFilter("ebilling.invoice.decide", FinanceAuthorizationFilter.InvoiceEntitySet, routeKey: "invoiceId")
            .RequireEbillingEnabled()
            .WithName("RemoveInvoiceAdjustment")
            .WithSummary("Remove an invoice-level adjustment")
            .WithDescription(
                "Deactivates the Adjustment line (it is never deleted, NFR-09) and recomputes the invoice totals. Only while the invoice is " +
                "In Review. Repeating the request for an already removed adjustment changes nothing.")
            .Produces<AdjustmentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> ListRules(Guid invoiceId, InvoiceAdjustmentService service, HttpContext httpContext, CancellationToken ct)
    {
        var result = await service.ListRulesAsync(invoiceId, ct);
        return result.IsApplied
            ? TypedResults.Ok(new AdjustmentRulesResponse(result.AsOf, result.Rules ?? []))
            : ToProblem(result.Status, result.Code, result.Detail, httpContext);
    }

    private static async Task<IResult> AddAdjustment(
        Guid invoiceId, AddAdjustmentRequest? body, InvoiceAdjustmentService service, HttpContext httpContext, CancellationToken ct)
    {
        if (body is null)
        {
            return InvoiceLineReviewEndpoints.Refused(StatusCodes.Status400BadRequest, AdjustmentCodes.InvalidRequest, "A request body is required.");
        }

        if (await ReviewerAsync(httpContext, ct) is not { } reviewer)
        {
            return ReviewerUnresolved(httpContext);
        }

        var result = await service.AddAsync(
            new AddAdjustmentCommand(invoiceId, body.AdjustmentId, body.Method, body.Value, body.Reason, body.RuleId, reviewer), ct);
        return ToResult(result, httpContext);
    }

    private static async Task<IResult> RemoveAdjustment(
        Guid invoiceId, Guid lineId, InvoiceAdjustmentService service, HttpContext httpContext, CancellationToken ct)
    {
        if (await ReviewerAsync(httpContext, ct) is not { } reviewer)
        {
            return ReviewerUnresolved(httpContext);
        }

        var result = await service.RemoveAsync(new RemoveAdjustmentCommand(invoiceId, lineId, reviewer), ct);
        return ToResult(result, httpContext);
    }

    /// <summary>The caller's <c>systemuserid</c> from the caller's own token (<c>WhoAmI</c> on the OBO token); null means the identity is unproven (fail closed).</summary>
    private static async Task<Guid?> ReviewerAsync(HttpContext httpContext, CancellationToken ct) =>
        await PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync(httpContext, ct);

    private static IResult ReviewerUnresolved(HttpContext httpContext) =>
        ProblemDetailsHelper.Forbidden(AdjustmentCodes.ReviewerUnresolved, "Your user could not be identified, so the change was not made.", httpContext.TraceIdentifier);

    private static IResult ToResult(AdjustmentResult result, HttpContext httpContext) => result.IsApplied
        ? TypedResults.Ok(new AdjustmentResponse(result.Adjustment, result.RemovedLineId, result.Totals, result.AlreadyApplied))
        : ToProblem(result.Status, result.Code, result.Detail, httpContext);

    /// <summary>Maps a service outcome to one HTTP status and one stable code (ADR-019).</summary>
    private static IResult ToProblem(AdjustmentStatus status, string code, string? detail, HttpContext httpContext) => status switch
    {
        AdjustmentStatus.InvalidRequest or AdjustmentStatus.NegativeTotal => InvoiceLineReviewEndpoints.Refused(StatusCodes.Status400BadRequest, code, detail),
        AdjustmentStatus.InvoiceNotFound or AdjustmentStatus.AdjustmentNotFound => FinanceAuthorizationFilter.UniformRecordNotFound(httpContext),
        AdjustmentStatus.NotInReview or AdjustmentStatus.TotalsRefused or AdjustmentStatus.IdReused or AdjustmentStatus.Busy or AdjustmentStatus.Changed
            => InvoiceLineReviewEndpoints.Refused(StatusCodes.Status409Conflict, code, detail),
        AdjustmentStatus.OwnerUnresolved => InvoiceLineReviewEndpoints.Refused(StatusCodes.Status500InternalServerError, code, detail),
        _ => InvoiceLineReviewEndpoints.Refused(StatusCodes.Status503ServiceUnavailable, code, detail),
    };
}

/// <summary>Body of the add route: <c>method</c> is <c>percent</c> or <c>fixed</c>; there is no amount and no total.</summary>
public sealed record AddAdjustmentRequest(Guid AdjustmentId, string? Method, decimal? Value, string? Reason, Guid? RuleId);

/// <summary>200 body of an add or a remove: the adjustment (add) or the removed line id (remove), and the invoice totals as 016 stored them.</summary>
public sealed record AdjustmentResponse(AdjustmentDto? Adjustment, Guid? RemovedLineId, ReviewTotalsDto? Totals, bool AlreadyApplied);

/// <summary>200 body of the rule list: the date the rules were judged on and the options.</summary>
public sealed record AdjustmentRulesResponse(DateOnly? AsOf, IReadOnlyList<AdjustmentRuleOptionDto> Rules);
