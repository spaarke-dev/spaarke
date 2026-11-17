using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// The reviewer's firm-notice draft (ebilling-r1 task 131; FR-50, D-60, FR-39), ADR-001 (Minimal API), ADR-008 (endpoint filters), ADR-019 (stable
/// codes). One route, beside the Reject and Return routes it precedes (<see cref="InvoiceRejectReturnEndpoints"/>, 104).
/// </summary>
/// <remarks>
/// <para><b>Authorization.</b> The same route filter 104 puts on Reject and Return for the invoice in the path
/// (<c>ebilling.invoice.decide</c>: Write on the invoice, asked of Dataverse AS THE CALLER, fail closed), behind the group's internal-caller step 0.
/// A caller who may not Reject or Return the invoice may not see the draft. The eBilling kill switch follows the filter and never replaces it.
/// The draft is read-only: nothing is stored, sent or decided. Sending, recipients and storing the sent text are 132's.</para>
/// <para><b>Status codes.</b> 200 the draft (<see cref="FirmNoticeDraftResponse"/>); 400 <c>ebilling.firm-notice.invalid_kind</c> (the kind is not
/// <c>ReturnedToFirm</c> or <c>InvoiceRejected</c>); 401; 403 (the route's filter); 404 uniform (the filter, or no such invoice); 409
/// <c>ebilling.firm-notice.not_applicable</c> (the invoice is in a state this notice does not describe); 503 <c>ebilling.disabled</c> or
/// <c>ebilling.firm-notice.unavailable</c> (a read faulted; nothing drafted).</para>
/// </remarks>
public static class FirmNoticeEndpoints
{
    internal const string InvalidKindCode = "ebilling.firm-notice.invalid_kind";
    internal const string NotApplicableCode = "ebilling.firm-notice.not_applicable";
    internal const string UnavailableCode = "ebilling.firm-notice.unavailable";

    public static IEndpointRouteBuilder MapFirmNoticeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/finance")
            .RequireAuthorization()
            .RequireInternalFinanceCaller() // step 0 on every /api/finance route (section 5.3, task 099)
            .WithTags("Finance");

        // POST /api/finance/invoices/{invoiceId}/firm-notice/draft?kind=ReturnedToFirm|InvoiceRejected - the draft notice for the reviewer to read and edit
        group.MapPost("/invoices/{invoiceId:guid}/firm-notice/draft", Draft)
            .AddFinanceAuthorizationFilter("ebilling.invoice.decide", FinanceAuthorizationFilter.InvoiceEntitySet, routeKey: "invoiceId")
            .RequireEbillingEnabled()
            .WithName("DraftFirmNotice")
            .WithSummary("Draft the notice to the firm for a Return to Firm or a whole-invoice Reject")
            .WithDescription(
                "Returns the draft notice a reviewer reads (and may edit) before Return to Firm or whole-invoice Reject: a subject, the plain-text " +
                "prose, the rendered HTML preview, and where the prose came from (the AI Action, or the fixed template when the Action is unavailable, " +
                "off or its prose failed the checks). Every amount, line reference, clause quote and the findings table comes from the firm-findings " +
                "projection, never from the AI. Nothing is stored or sent. kind is ReturnedToFirm or InvoiceRejected.")
            .Produces<FirmNoticeDraftResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> Draft(
        Guid invoiceId, string? kind, FirmNoticeComposer composer, HttpContext httpContext, CancellationToken ct)
    {
        if (!TryParseKind(kind, out var noticeKind))
        {
            return InvoiceLineReviewEndpoints.Refused(
                StatusCodes.Status400BadRequest, InvalidKindCode, "kind must be ReturnedToFirm or InvoiceRejected.");
        }

        var result = await composer.ComposeForInvoiceAsync(noticeKind, invoiceId, ct);
        httpContext.Response.Headers.CacheControl = "no-store";
        return result.Status switch
        {
            FirmNoticeStatus.Composed => TypedResults.Ok(FirmNoticeDraftResponse.From(result.Draft!)),
            FirmNoticeStatus.NotFound => FinanceAuthorizationFilter.UniformRecordNotFound(httpContext),
            FirmNoticeStatus.NotApplicable => InvoiceLineReviewEndpoints.Refused(
                StatusCodes.Status409Conflict, NotApplicableCode, "The invoice is not in a state this notice describes."),
            _ => InvoiceLineReviewEndpoints.Refused(
                StatusCodes.Status503ServiceUnavailable, UnavailableCode, "The notice could not be drafted right now. Nothing was changed."),
        };
    }

    // Exact names only: a number or an unknown word is refused, never coerced to a kind.
    private static bool TryParseKind(string? kind, out FirmNoticeKind parsed)
    {
        parsed = default;
        switch (kind)
        {
            case nameof(FirmNoticeKind.ReturnedToFirm):
                parsed = FirmNoticeKind.ReturnedToFirm;
                return true;
            case nameof(FirmNoticeKind.InvoiceRejected):
                parsed = FirmNoticeKind.InvoiceRejected;
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// 200 body of the draft route. <paramref name="Source"/> is <c>Action</c> or <c>Template</c>; <paramref name="ActionVersion"/> is set only for
/// <c>Action</c>, and <paramref name="FallbackReason"/> (a stable code) only for <c>Template</c>. <paramref name="Prose"/> is the editable plain
/// text; <paramref name="Html"/> is the preview of the whole notice.
/// </summary>
public sealed record FirmNoticeDraftResponse(
    string Kind, string Subject, string Prose, string Html, string Source, string? ActionVersion, string? FallbackReason)
{
    internal static FirmNoticeDraftResponse From(FirmNoticeDraft draft) => new(
        draft.Kind.ToString(), draft.Subject, draft.Prose, draft.Html, draft.Source.ToString(), draft.ActionVersion, draft.FallbackReason);
}
