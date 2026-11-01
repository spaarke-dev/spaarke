using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// The Invoice Review page's data and line decisions (ebilling-r1 task 100; FR-31, FR-07, D-64, D-75, D-77), ADR-001 (Minimal API) and ADR-008
/// (endpoint filters).
/// </summary>
/// <remarks>
/// <para><b>Authorization.</b> Every route is authorized on the invoice in the route, asked of Dataverse AS THE CALLER and failing closed
/// (<see cref="FinanceAuthorizationFilterExtensions"/>): the read with <c>finance.read</c> (Read), the decisions with <c>ebilling.invoice.decide</c>
/// (Write; D-77: any internal user with Write on the invoice decides lines, not only eligible approvers). Core-record access reaches the invoice by
/// inheritance (013 stamping, ADR-034), so a caller without access to the invoice's matter, project or work assignment is refused with 403 before
/// any service runs. The eBilling kill switch (<c>RequireEbillingEnabled</c>, ADR-018) follows the authorization filter and never replaces it.</para>
/// <para><b>Nothing is trusted from the body.</b> The invoice is the route's; each line id is checked against that invoice's own lines (uniform
/// 404) before anything is written; the decider is the caller's <c>systemuserid</c>, resolved from the caller's own token (074 obligation 2);
/// no request field sets a total or a status (I-1, ADR-002). The decision id is the client's and only makes a retry idempotent.</para>
/// <para><b>Signals.</b> A decision closes the Open and Acknowledged Signals on its lines (D-122) through the app-identity writer, so the
/// service first asks, AS THE CALLER with the caller's own token, for AppendTo on the core record and Write on each such line; a missing right is
/// 403 <c>ebilling.decision.not_authorized</c> and nothing is written.</para>
/// <para><b>Status codes.</b> 200 applied; 400 invalid (<c>errorCode</c> says which rule); 401; 403; 404 uniform (invoice or line absent or
/// unreadable); 409 (not In Review, line Pending or not decidable, totals refused, another decision in progress, a line changed); 503
/// <c>ebilling.disabled</c> or a transient failure; 503 <c>ebilling.decision.incomplete</c> when the lines were written but a later step did not
/// finish (the body names the committed lines to repeat the decision with). Its <c>failedStep</c> is <c>totals</c>, <c>corrections</c>,
/// <c>record</c>, or <c>signals</c> (2026-10-29: a Signal close failed, so a Signal is still Open; the decision stands and the repeat
/// completes the close without a second Decision Record).</para>
/// </remarks>
public static class InvoiceLineReviewEndpoints
{
    public static IEndpointRouteBuilder MapInvoiceLineReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/finance")
            .RequireAuthorization()
            .RequireInternalFinanceCaller() // step 0 on every /api/finance route (section 5.3, task 099)
            .WithTags("Finance");

        // GET /api/finance/invoices/{invoiceId}/review - the Invoice Review read model
        group.MapGet("/invoices/{invoiceId:guid}/review", GetReview)
            .AddFinanceAuthorizationFilter("finance.read", FinanceAuthorizationFilter.InvoiceEntitySet, routeKey: "invoiceId")
            .RequireEbillingEnabled()
            .WithName("GetInvoiceReview")
            .WithSummary("Get the Invoice Review read model for an invoice")
            .WithDescription(
                "Returns the header, the stored totals, every line with its status and findings (policy, version, clause, values read, suggested " +
                "adjustment) and the extraction page and box of each field, the invoice-level findings and the Decision Record history. " +
                "Built with a fixed number of Dataverse reads however many lines the invoice has.")
            .Produces<InvoiceReviewDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/finance/invoices/{invoiceId}/lines/{lineId}/decision - one decision on one line
        group.MapPost("/invoices/{invoiceId:guid}/lines/{lineId:guid}/decision", DecideLine)
            .AddFinanceAuthorizationFilter("ebilling.invoice.decide",FinanceAuthorizationFilter.InvoiceEntitySet, routeKey: "invoiceId")
            .RequireEbillingEnabled()
            .WithName("DecideInvoiceLine")
            .WithSummary("Accept, reduce or disallow one invoice line")
            .WithDescription(
                "Accept (approved = billed), Reduce (approvedAmount and reason required; between 0 and the billed amount) or Disallow (reason " +
                "required; approved 0). Only while the invoice is In Review; a Pending line cannot be decided. Recomputes the invoice totals and " +
                "records the decision. Repeating the request with the same decisionId records nothing twice.")
            .Produces<LineDecisionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/finance/invoices/{invoiceId}/lines/decisions - one action and reason on selected lines
        group.MapPost("/invoices/{invoiceId:guid}/lines/decisions", DecideLines)
            .AddFinanceAuthorizationFilter("ebilling.invoice.decide",FinanceAuthorizationFilter.InvoiceEntitySet, routeKey: "invoiceId")
            .RequireEbillingEnabled()
            .WithName("DecideInvoiceLines")
            .WithSummary("Apply one action and reason to several invoice lines")
            .WithDescription(
                "The selected lines get the same action and reason; a Reduce carries its own approvedAmount per line (apply a suggestion to n " +
                "lines). All or nothing: one invalid line writes nothing. An Accept writes the 'Accepted in bulk' correction rows (never a label).")
            .Produces<LineDecisionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/finance/invoices/{invoiceId}/lines/accept-clean - Accept all Clean
        group.MapPost("/invoices/{invoiceId:guid}/lines/accept-clean", AcceptClean)
            .AddFinanceAuthorizationFilter("ebilling.invoice.decide",FinanceAuthorizationFilter.InvoiceEntitySet, routeKey: "invoiceId")
            .RequireEbillingEnabled()
            .WithName("AcceptCleanInvoiceLines")
            .WithSummary("Accept every Clean line of an invoice")
            .WithDescription(
                "Accepts exactly the lines that are Clean when the request runs, at their billed amount. Flagged, Pending and already decided " +
                "lines are untouched. Writes the 'Accepted in bulk' correction rows (never a label).")
            .Produces<LineDecisionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> GetReview(
        Guid invoiceId, InvoiceReviewReadService service, HttpContext httpContext, CancellationToken ct)
    {
        // The caller's own identity and token: canDecide is asked of Dataverse as the caller. The filter has already refused a caller with no oid.
        var caller = new InvoiceContextService.InvoiceCaller(
            CallerResolution.ResolveObjectId(httpContext.User) ?? string.Empty,
            TokenHelper.ExtractBearerTokenOrNull(httpContext));

        var review = await service.GetAsync(invoiceId, caller, ct);
        return review is null
            ? FinanceAuthorizationFilter.UniformRecordNotFound(httpContext)
            : TypedResults.Ok(review);
    }

    private static async Task<IResult> DecideLine(
        Guid invoiceId, Guid lineId, SingleLineDecisionRequest? body, InvoiceLineDecisionService service, HttpContext httpContext, CancellationToken ct)
    {
        if (body is null)
        {
            return Refused(StatusCodes.Status400BadRequest, LineDecisionCodes.InvalidRequest, "A request body is required.");
        }

        if (!TryParseAction(body.Action, out var action))
        {
            return Refused(StatusCodes.Status400BadRequest, LineDecisionCodes.InvalidRequest, "The action is accept, reduce or disallow.");
        }

        if (await DeciderAsync(httpContext, ct) is not { } decider)
        {
            return DeciderUnresolved(httpContext);
        }

        var result = await service.DecideAsync(
            new LineDecisionCommand(
                invoiceId, body.DecisionId, action, body.Reason,
                [new LineDecisionLine(lineId, body.ApprovedAmount)], decider, Bulk: false, TokenHelper.ExtractBearerTokenOrNull(httpContext)),
            ct);
        return ToResult(result, httpContext, body.DecisionId);
    }

    private static async Task<IResult> DecideLines(
        Guid invoiceId, MultiLineDecisionRequest? body, InvoiceLineDecisionService service, HttpContext httpContext, CancellationToken ct)
    {
        if (body is null)
        {
            return Refused(StatusCodes.Status400BadRequest, LineDecisionCodes.InvalidRequest, "A request body is required.");
        }

        if (!TryParseAction(body.Action, out var action))
        {
            return Refused(StatusCodes.Status400BadRequest, LineDecisionCodes.InvalidRequest, "The action is accept, reduce or disallow.");
        }

        if (body.Lines is null)
        {
            return Refused(StatusCodes.Status400BadRequest, LineDecisionCodes.InvalidRequest, "At least one line is required.");
        }

        if (await DeciderAsync(httpContext, ct) is not { } decider)
        {
            return DeciderUnresolved(httpContext);
        }

        var result = await service.DecideAsync(
            new LineDecisionCommand(
                invoiceId, body.DecisionId, action, body.Reason,
                body.Lines.Select(l => new LineDecisionLine(l.LineId, l.ApprovedAmount)).ToList(), decider, Bulk: true, TokenHelper.ExtractBearerTokenOrNull(httpContext)),
            ct);
        return ToResult(result, httpContext, body.DecisionId);
    }

    private static async Task<IResult> AcceptClean(
        Guid invoiceId, AcceptCleanRequest? body, InvoiceLineDecisionService service, HttpContext httpContext, CancellationToken ct)
    {
        if (body is null)
        {
            return Refused(StatusCodes.Status400BadRequest, LineDecisionCodes.InvalidRequest, "A request body is required.");
        }

        if (await DeciderAsync(httpContext, ct) is not { } decider)
        {
            return DeciderUnresolved(httpContext);
        }

        var result = await service.AcceptCleanAsync(invoiceId, body.DecisionId, decider, ct, TokenHelper.ExtractBearerTokenOrNull(httpContext));
        return ToResult(result, httpContext, body.DecisionId);
    }

    /// <summary>The caller's <c>systemuserid</c> from the caller's own token (<c>WhoAmI</c> on the OBO token); null means the identity is unproven (fail closed).</summary>
    private static async Task<Guid?> DeciderAsync(HttpContext httpContext, CancellationToken ct) =>
        await PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync(httpContext, ct);

    private static IResult DeciderUnresolved(HttpContext httpContext) =>
        ProblemDetailsHelper.Forbidden(LineDecisionCodes.DeciderUnresolved, "Your user could not be identified, so the decision was not made.", httpContext.TraceIdentifier);

    private static bool TryParseAction(string? text, out LineDecisionAction action)
    {
        action = default;
        return !string.IsNullOrWhiteSpace(text) && Enum.TryParse(text.Trim(), ignoreCase: true, out action) && Enum.IsDefined(action);
    }

    /// <summary>Maps a service outcome to one HTTP status and one stable code (ADR-019).</summary>
    internal static IResult ToResult(LineDecisionResult result, HttpContext httpContext, Guid decisionId) => result.Status switch
    {
        LineDecisionStatus.Applied => TypedResults.Ok(new LineDecisionResponse(decisionId, result.Lines ?? [], result.Totals)),
        LineDecisionStatus.InvalidRequest => Refused(StatusCodes.Status400BadRequest, result.Code, result.Detail),
        LineDecisionStatus.InvoiceNotFound or LineDecisionStatus.LineNotFound => FinanceAuthorizationFilter.UniformRecordNotFound(httpContext),
        LineDecisionStatus.NotInReview or LineDecisionStatus.LineNotDecidable or LineDecisionStatus.TotalsRefused
            or LineDecisionStatus.Busy or LineDecisionStatus.Changed => Refused(StatusCodes.Status409Conflict, result.Code, result.Detail),
        LineDecisionStatus.OwnerUnresolved => Refused(StatusCodes.Status500InternalServerError, result.Code, result.Detail),
        LineDecisionStatus.Forbidden => Refused(StatusCodes.Status403Forbidden, result.Code, result.Detail),
        LineDecisionStatus.Incomplete => Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            detail: result.Detail,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = result.Code,
                ["decisionId"] = decisionId,
                ["failedStep"] = result.FailedStep,
                ["committedLineIds"] = result.CommittedLineIds ?? [],
            }),
        _ => Refused(StatusCodes.Status503ServiceUnavailable, result.Code, result.Detail),
    };

    internal static IResult Refused(int statusCode, string errorCode, string? detail) =>
        Results.Problem(
            statusCode: statusCode, detail: detail,
            extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode });
}

/// <summary>Body of the single-line decision route. <paramref name="Action"/> is <c>accept</c>, <c>reduce</c> or <c>disallow</c>.</summary>
public sealed record SingleLineDecisionRequest(Guid DecisionId, string? Action, string? Reason, decimal? ApprovedAmount);

public sealed record MultiLineDecisionLine(Guid LineId, decimal? ApprovedAmount);

/// <summary>Body of the multi-line route: one action and reason, a per-line amount for a Reduce.</summary>
public sealed record MultiLineDecisionRequest(Guid DecisionId, string? Action, string? Reason, IReadOnlyList<MultiLineDecisionLine>? Lines);

/// <summary>Body of Accept all Clean.</summary>
public sealed record AcceptCleanRequest(Guid DecisionId);

/// <summary>200 body of a decision: the lines decided (with their new state) and the invoice totals as 016 stored them (null when nothing was decided).</summary>
public sealed record LineDecisionResponse(Guid DecisionId, IReadOnlyList<DecidedLineDto> Lines, ReviewTotalsDto? Totals);
