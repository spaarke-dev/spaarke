using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// The Invoice Review context panel (ebilling-r1 task 106, FR-33), ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// </summary>
/// <remarks>
/// <para><b>One read route, authorized on the invoice.</b> <c>finance.read</c> on <c>sprk_invoices(route invoiceId)</c>, asked of Dataverse AS THE
/// CALLER and failing closed (<see cref="FinanceAuthorizationFilterExtensions.AddFinanceAuthorizationFilter{TBuilder}(TBuilder, string, string, string, FinanceDenial)"/>),
/// then the eBilling kill switch (<c>RequireEbillingEnabled</c>, ADR-018) - the flag never replaces authorization. The firm and the core record
/// the sections are scoped to are read from that invoice by <see cref="InvoiceContextService"/>; the request carries no firm or core record id.</para>
/// <para><b>200 unless authorization fails.</b> A section whose source fails answers with its own unavailable message inside the 200 body (FR-33);
/// the only 4xx after authorization is the uniform 404 for an invoice deleted between the check and the read.</para>
/// </remarks>
public static class InvoiceContextEndpoints
{
    public static IEndpointRouteBuilder MapInvoiceContextEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/finance")
            .RequireAuthorization()
            .RequireInternalFinanceCaller() // step 0 on every /api/finance route (section 5.3, task 099)
            .WithTags("Finance");

        // GET /api/finance/invoices/{invoiceId}/context - the Invoice Review context panel
        group.MapGet("/invoices/{invoiceId:guid}/context", GetInvoiceContext)
            .AddFinanceAuthorizationFilter("finance.read", FinanceAuthorizationFilter.InvoiceEntitySet, routeKey: "invoiceId")
            .RequireEbillingEnabled()
            .WithName("GetInvoiceContext")
            .WithSummary("Get the Invoice Review context panel for an invoice")
            .WithDescription(
                "Returns five independent sections for the invoice's firm and core record: core record status and dates, budget vs spend " +
                "(the existing rollups), other invoices from this firm on this core record, this firm's Reduced / Disallowed history and " +
                "timekeeper rate history. A section whose source is unavailable carries a code and a message; the others still return.")
            .Produces<InvoiceContextDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> GetInvoiceContext(
        Guid invoiceId, InvoiceContextService service, HttpContext httpContext, CancellationToken ct)
    {
        // The caller's own identity and token: the other invoices on the panel are limited to the ones THIS caller can Read. The filter has
        // already refused a caller with no oid; a missing token reaches the service as null and reads nothing (fail closed).
        var caller = new InvoiceContextService.InvoiceCaller(
            CallerResolution.ResolveObjectId(httpContext.User) ?? string.Empty,
            TokenHelper.ExtractBearerTokenOrNull(httpContext));

        var context = await service.GetAsync(invoiceId, caller, ct);
        return context is null
            ? FinanceAuthorizationFilter.UniformRecordNotFound(httpContext)
            : TypedResults.Ok(context);
    }
}
