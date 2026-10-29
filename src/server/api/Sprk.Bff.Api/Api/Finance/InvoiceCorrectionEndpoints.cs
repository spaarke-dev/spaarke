using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// Value and code corrections of the Invoice Review page (ebilling-r1 task 101 part B1; FR-31, FR-22, D-64, NFR-09), ADR-001 (Minimal API) and
/// ADR-008 (endpoint filters). Same route group, filters and status conventions as <see cref="InvoiceAdjustmentEndpoints"/> (task 102).
/// </summary>
/// <remarks>
/// <para><b>Authorization.</b> Every route is authorized on the invoice in the route, asked of Dataverse AS THE CALLER and failing closed
/// (<see cref="FinanceAuthorizationFilterExtensions"/>): the corrections list with <c>finance.read</c> (Read), the two corrections with
/// <c>ebilling.invoice.decide</c> (Write; D-77). Core-record access reaches the invoice by inheritance (013, ADR-034), so a caller without
/// access to the invoice's matter, project or work assignment is refused with 403 before any service runs. The eBilling kill switch
/// (<c>RequireEbillingEnabled</c>, ADR-018) follows the authorization filter and never replaces it.</para>
/// <para><b>Nothing is trusted from the body.</b> The invoice and line are the route's; the body names a field and a value (or confirms the
/// value as read), never an outcome, a role, a status or an amount of approval. The reviewer is the caller's <c>systemuserid</c> from their own
/// token. A correction is kept separate from Reduce (task 100): it writes a correction row and a value, never a line decision.</para>
/// <para><b>Status codes.</b> 200 applied (<c>alreadyApplied</c> true when a repeat found everything written); 400 invalid (<c>errorCode</c>
/// says which rule: <c>ebilling.code.not_in_set</c>, <c>ebilling.correction.field_unknown</c>, <c>field_not_applicable</c>,
/// <c>field_not_supported_yet</c>, <c>value_required</c>, <c>value_invalid</c>, <c>nothing_to_confirm</c>); 401; 403; 404 uniform (invoice or
/// line absent or unreadable); 409 (not In Review, line Pending or already decided, no recorded AI source, no code set, the line or invoice
/// changed); 503 <c>ebilling.disabled</c> or a transient failure (repeat the same request: it writes no second row); 503
/// <c>ebilling.correction.incomplete</c> with <c>failedStep</c> <c>findings</c> (the re-evaluation and the findings patch), <c>status</c> (the line
/// status) or <c>reevaluation</c> (any other step) when the value was saved but a step after it did not finish (repeat the same request: it writes no
/// second row and runs the steps again). A code correction is also the re-code path of a Pending line of an uncoded invoice (D-126, B2b-1).</para>
/// </remarks>
public static class InvoiceCorrectionEndpoints
{
    public static IEndpointRouteBuilder MapInvoiceCorrectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/finance")
            .RequireAuthorization()
            .RequireInternalFinanceCaller() // step 0 on every /api/finance route (section 5.3, task 099)
            .WithTags("Finance");

        // POST /api/finance/invoices/{invoiceId}/lines/{lineId}/corrections - correct or confirm one line field
        group.MapPost("/invoices/{invoiceId:guid}/lines/{lineId:guid}/corrections", CorrectLine)
            .AddFinanceAuthorizationFilter("ebilling.invoice.decide", FinanceAuthorizationFilter.InvoiceEntitySet, routeKey: "invoiceId")
            .RequireEbillingEnabled()
            .WithName("CorrectInvoiceLineField")
            .WithSummary("Correct or confirm one extracted value or AI code of an undecided invoice line")
            .WithDescription(
                "Fields: Hours and Rate (fee lines), Amount, Narrative, TaskCode and ActivityCode (fee lines), ExpenseCode (expense lines). Send a value, " +
                "or confirm to keep the value as read. Writes one Corrected or Confirmed individually row (both labels) and updates the value; a code " +
                "must be in the line's resolved code set (400 ebilling.code.not_in_set). Date and timekeeper are refused for now " +
                "(400 ebilling.correction.field_not_supported_yet). Only while the invoice is In Review and the line is Clean or Flagged. " +
                "Never changes the line's status, approved amount or decision. Repeating the request writes no second row.")
            .Produces<CorrectionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/finance/invoices/{invoiceId}/header/corrections - correct or confirm one header field
        group.MapPost("/invoices/{invoiceId:guid}/header/corrections", CorrectHeader)
            .AddFinanceAuthorizationFilter("ebilling.invoice.decide", FinanceAuthorizationFilter.InvoiceEntitySet, routeKey: "invoiceId")
            .RequireEbillingEnabled()
            .WithName("CorrectInvoiceHeaderField")
            .WithSummary("Correct or confirm one extracted header value of an invoice")
            .WithDescription(
                "Fields: FirmInvoiceNumber, BillingPeriodStart, BillingPeriodEnd, Currency, TotalDue. Send a value, or confirm to keep the value as " +
                "read. Writes one Corrected or Confirmed individually row and updates the invoice. The invoice date is refused for now " +
                "(400 ebilling.correction.field_not_supported_yet). Only while the invoice is In Review. Repeating the request writes no second row.")
            .Produces<CorrectionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // GET /api/finance/invoices/{invoiceId}/corrections - the correction rows of this invoice, newest first
        group.MapGet("/invoices/{invoiceId:guid}/corrections", ListCorrections)
            .AddFinanceAuthorizationFilter("finance.read", FinanceAuthorizationFilter.InvoiceEntitySet, routeKey: "invoiceId")
            .RequireEbillingEnabled()
            .WithName("ListInvoiceCorrections")
            .WithSummary("List the correction rows of an invoice")
            .WithDescription(
                "Every correction row of this invoice (any outcome), newest first, for the History pane. Optional sourceCode and sourceVersion " +
                "filter on the analyzer or Action code and version the rows are attributed to. Rows of another invoice never appear.")
            .Produces<CorrectionListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> CorrectLine(
        Guid invoiceId, Guid lineId, CorrectionRequest? body, InvoiceCorrectionService service, HttpContext httpContext, CancellationToken ct)
    {
        if (body is null)
        {
            return InvoiceLineReviewEndpoints.Refused(StatusCodes.Status400BadRequest, CorrectionCodes.InvalidRequest, "A request body is required.");
        }

        if (await ReviewerAsync(httpContext, ct) is not { } reviewer)
        {
            return ReviewerUnresolved(httpContext);
        }

        var result = await service.CorrectAsync(new CorrectionCommand(invoiceId, lineId, body.Field, body.Value, body.Confirm, reviewer), ct);
        return ToResult(result, httpContext);
    }

    private static async Task<IResult> CorrectHeader(
        Guid invoiceId, CorrectionRequest? body, InvoiceCorrectionService service, HttpContext httpContext, CancellationToken ct)
    {
        if (body is null)
        {
            return InvoiceLineReviewEndpoints.Refused(StatusCodes.Status400BadRequest, CorrectionCodes.InvalidRequest, "A request body is required.");
        }

        if (await ReviewerAsync(httpContext, ct) is not { } reviewer)
        {
            return ReviewerUnresolved(httpContext);
        }

        var result = await service.CorrectAsync(new CorrectionCommand(invoiceId, null, body.Field, body.Value, body.Confirm, reviewer), ct);
        return ToResult(result, httpContext);
    }

    private static async Task<IResult> ListCorrections(
        Guid invoiceId, string? sourceCode, string? sourceVersion, InvoiceCorrectionService service, CancellationToken ct)
    {
        var rows = await service.ListAsync(invoiceId, sourceCode, sourceVersion, ct);
        return rows is null
            ? InvoiceLineReviewEndpoints.Refused(StatusCodes.Status503ServiceUnavailable, CorrectionCodes.Unavailable, "The corrections could not be read. Try again.")
            : TypedResults.Ok(new CorrectionListResponse(rows));
    }

    /// <summary>The caller's <c>systemuserid</c> from the caller's own token (<c>WhoAmI</c> on the OBO token); null means the identity is unproven (fail closed).</summary>
    private static async Task<Guid?> ReviewerAsync(HttpContext httpContext, CancellationToken ct) =>
        await PlaybookAuthorizationFilter.ResolveCallerSystemUserIdAsync(httpContext, ct);

    private static IResult ReviewerUnresolved(HttpContext httpContext) =>
        ProblemDetailsHelper.Forbidden(CorrectionCodes.ReviewerUnresolved, "Your user could not be identified, so the correction was not made.", httpContext.TraceIdentifier);

    internal static IResult ToResult(CorrectionResult result, HttpContext httpContext) => result.IsApplied
        ? TypedResults.Ok(new CorrectionResponse(result.Correction!, result.AlreadyApplied))
        : ToProblem(result, httpContext);

    /// <summary>Maps a service outcome to one HTTP status and one stable code (ADR-019).</summary>
    private static IResult ToProblem(CorrectionResult result, HttpContext httpContext)
    {
        var (status, code, detail) = (result.Status, result.Code, result.Detail);
        return status == CorrectionStatus.Incomplete
            ? Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                detail: detail,
                extensions: new Dictionary<string, object?> { ["errorCode"] = code, ["failedStep"] = result.FailedStep })
            : ToProblem(status, code, detail, httpContext);
    }

    private static IResult ToProblem(CorrectionStatus status, string code, string? detail, HttpContext httpContext) => status switch
    {
        CorrectionStatus.InvalidRequest or CorrectionStatus.CodeNotInSet => InvoiceLineReviewEndpoints.Refused(StatusCodes.Status400BadRequest, code, detail),
        CorrectionStatus.InvoiceNotFound or CorrectionStatus.LineNotFound => FinanceAuthorizationFilter.UniformRecordNotFound(httpContext),
        CorrectionStatus.NotInReview or CorrectionStatus.LineNotCorrectable or CorrectionStatus.NoProvenance
            or CorrectionStatus.CodeSetUnavailable or CorrectionStatus.Changed => InvoiceLineReviewEndpoints.Refused(StatusCodes.Status409Conflict, code, detail),
        CorrectionStatus.OwnerUnresolved => InvoiceLineReviewEndpoints.Refused(StatusCodes.Status500InternalServerError, code, detail),
        _ => InvoiceLineReviewEndpoints.Refused(StatusCodes.Status503ServiceUnavailable, code, detail),
    };
}

/// <summary>
/// Body of both correction routes: the field (<c>Hours</c>, <c>Amount</c>, <c>TaskCode</c>, <c>TotalDue</c> and so on) and either the final
/// <c>value</c> as text or <c>confirm</c> true to keep the value as read. There is no outcome, role or reviewer in the body.
/// </summary>
public sealed record CorrectionRequest(string? Field, string? Value, bool Confirm = false);

/// <summary>200 body of a correction: the correction row (the one written, or the existing one when the repeat found it) and whether everything was already in place.</summary>
public sealed record CorrectionResponse(CorrectionDto Correction, bool AlreadyApplied);

/// <summary>200 body of the list route: the invoice's correction rows, newest first.</summary>
public sealed record CorrectionListResponse(IReadOnlyList<CorrectionDto> Corrections);
