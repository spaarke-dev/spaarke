using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// The Invoice Review "Related work" pane (ebilling-r1 task 107, FR-34), ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// </summary>
/// <remarks>
/// <para><b>One read route, authorized on the invoice.</b> <c>finance.read</c> on <c>sprk_invoices(route invoiceId)</c>, asked of Dataverse AS THE
/// CALLER and failing closed, then the eBilling kill switch (<c>RequireEbillingEnabled</c>, ADR-018) - the flag never replaces authorization.
/// The line must belong to that invoice (uniform 404 otherwise). The core record, the author and the window are read from the line, its
/// timekeeper and the settings by <see cref="InvoiceRelatedWorkService"/>; the request carries no core record, container, author or document id.</para>
/// <para><b>Each document is authorized as the caller.</b> The route authorizes the invoice, not the documents: <see cref="InvoiceRelatedWorkService"/>
/// puts every candidate document to the caller's own Read before it is searched or returned.</para>
/// <para><b>200 unless authorization fails.</b> An empty state or an unavailable source answers inside the 200 body (FR-34); the only 4xx after
/// authorization is the uniform 404 for a line that is not on this invoice.</para>
/// </remarks>
public static class InvoiceRelatedWorkEndpoints
{
    public static IEndpointRouteBuilder MapInvoiceRelatedWorkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/finance")
            .RequireAuthorization()
            .RequireInternalFinanceCaller() // step 0 on every /api/finance route (section 5.3, task 099)
            .WithTags("Finance");

        // GET /api/finance/invoices/{invoiceId}/lines/{lineId}/related-work - the Related work pane for one fee line
        group.MapGet("/invoices/{invoiceId:guid}/lines/{lineId:guid}/related-work", GetRelatedWork)
            .AddFinanceAuthorizationFilter("finance.read", FinanceAuthorizationFilter.InvoiceEntitySet, routeKey: "invoiceId")
            .RequireEbillingEnabled()
            .WithName("GetInvoiceLineRelatedWork")
            .WithSummary("Get the related work product of one invoice fee line")
            .WithDescription(
                "Returns the documents on the line's core record, created within the line date plus or minus the settings window by the line's " +
                "timekeeper (through the timekeeper's linked contact), that the caller can read, ranked by similarity to the line narrative. " +
                "An empty list carries a reason code; an unavailable source carries a code and a message.")
            .Produces<RelatedWorkDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> GetRelatedWork(
        Guid invoiceId, Guid lineId, InvoiceRelatedWorkService service, HttpContext httpContext, CancellationToken ct)
    {
        // The caller's own identity and token: every candidate document is limited to the ones THIS caller can Read. The filter has already
        // refused a caller with no oid; a missing token reaches the service as null and shows nothing (fail closed).
        var caller = new InvoiceContextService.InvoiceCaller(
            CallerResolution.ResolveObjectId(httpContext.User) ?? string.Empty,
            TokenHelper.ExtractBearerTokenOrNull(httpContext));

        // The search is tenant-isolated by the caller's tenant (the same claim the semantic search route reads).
        var tenantId = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

        var related = await service.GetAsync(invoiceId, lineId, caller, tenantId, ct);
        return related is null
            ? FinanceAuthorizationFilter.UniformRecordNotFound(httpContext)
            : TypedResults.Ok(related);
    }
}
