using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// The internal invoice submission routes (ebilling-r1 task 044, FR-17 internal channel, D-24), ADR-001 (Minimal API) and ADR-008
/// (endpoint filters).
/// </summary>
/// <remarks>
/// <para><b>Authorization.</b> Each route carries its own <see cref="FinanceAuthorizationFilter"/> AS THE CALLER, then the eBilling flag
/// (<c>RequireEbillingEnabled</c>, ADR-018): the flag never replaces authorization. A submit needs Read and AppendTo on the core record
/// the route names (<c>finance.submit_invoice</c>) and AppendTo on the firm the query names (<c>finance.attach_invoice</c>, the
/// sprk_vendororg reasoning of the invoice confirm route): the application then writes rows whose lookups point at both. The status
/// route needs Read on the submission itself, and answers an unreadable and an absent row with the same 404.</para>
/// <para><b>Inputs.</b> The firm is the QUERY parameter <c>firmId</c> (so the filter and the handler read the same source, the Finance
/// doctrine); the PDF is the multipart <c>file</c> part; <c>firmInvoiceNumber</c> (required, D-91) and <c>poNumber</c> are form
/// fields. <see cref="InvoiceSubmissionHttp.ReadSubmissionFormAsync"/> is shared with the external route, so both channels parse identically.</para>
/// <para><b>Response contract (tasks 049 / 051 / 052).</b> 202 with <c>Location</c> = <c>statusUrl</c> and the body
/// <see cref="InvoiceSubmissionAccepted"/>; refusals are ProblemDetails with <c>errorCode</c> (<see cref="InvoiceSubmissionErrors"/>).</para>
/// </remarks>
public static class InvoiceSubmissionEndpoints
{
    internal const string Route = "/api/finance";

    /// <summary>The status route of a submission on the internal surface.</summary>
    internal static string StatusUrl(Guid submissionId) => $"{Route}/invoice-submissions/{submissionId}";

    public static IEndpointRouteBuilder MapInvoiceSubmissionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route)
            .RequireAuthorization()
            .RequireInternalFinanceCaller() // step 0 on every /api/finance route (section 5.3, task 099)
            .WithTags("Finance");

        // POST /api/finance/{matters|projects|workassignments}/{id}/invoice-submissions. Three literal chains (not a loop): the
        // RouteAuthorizationGuard scanner reads each route's path and attachments from its own Map{Verb} chain.
        group.MapPost("/matters/{id:guid}/invoice-submissions", SubmitForMatterAsync)
            .AddFinanceAuthorizationFilter(context => ResolveSubmitTargets(context, FinanceAuthorizationFilter.MatterEntitySet))
            .RequireEbillingEnabled()
            .DisableAntiforgery() // multipart upload; auth is the JWT + the caller's own record rights, not a form token
            .WithMetadata(new RequestSizeLimitAttribute(InvoiceSubmissionHttp.MaxRequestBodyBytes))
            .WithName("SubmitInvoiceForMatter")
            .WithSubmitContract("matter");

        group.MapPost("/projects/{id:guid}/invoice-submissions", SubmitForProjectAsync)
            .AddFinanceAuthorizationFilter(context => ResolveSubmitTargets(context, FinanceAuthorizationFilter.ProjectEntitySet))
            .RequireEbillingEnabled()
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(InvoiceSubmissionHttp.MaxRequestBodyBytes))
            .WithName("SubmitInvoiceForProject")
            .WithSubmitContract("project");

        group.MapPost("/workassignments/{id:guid}/invoice-submissions", SubmitForWorkAssignmentAsync)
            .AddFinanceAuthorizationFilter(context => ResolveSubmitTargets(context, FinanceAuthorizationFilter.WorkAssignmentEntitySet))
            .RequireEbillingEnabled()
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(InvoiceSubmissionHttp.MaxRequestBodyBytes))
            .WithName("SubmitInvoiceForWorkAssignment")
            .WithSubmitContract("work assignment");

        // POST /api/finance/invoice-submissions/from-email (ebilling-r1 task 095, model §9.1, contract notes/095-from-email-contract.md).
        // Filters in the constrained order: step 0 and the body-shape 400 and the record and firm rights (the Finance filter, as the
        // caller: Read + AppendTo on the core record, AppendTo on the firm), the Office caller, EntityAccessFilter on the same core record
        // (the save files the .eml there), the Office Save rate limit, the version-save gate (a no-op for this body), the eBilling kill
        // switch, then the X-Idempotency-Key response cache bound to K. The handler is InvoiceSubmissionFromEmailHttp.
        group.MapPost("/invoice-submissions/from-email", InvoiceSubmissionFromEmailHttp.SubmitFromEmailAsync)
            .AddFinanceAuthorizationFilter(InvoiceSubmissionFromEmailHttp.ResolveFromEmailTargets)
            .AddOfficeAuthFilter()
            .AddEntityAccessFilter()
            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.Save)
            .AddOfficeVersionSaveAuthorizationFilter()
            .RequireEbillingEnabled()
            .AddIdempotencyFilter(
                bindClientKeyTo: InvoiceSubmissionFromEmailHttp.IdempotencyBinding,
                mayReplayResponse: InvoiceSubmissionFromEmailHttp.MayReplayResponse)
            .Accepts<FromEmailSubmissionRequest>("application/json")
            .WithName("SubmitInvoiceFromEmail")
            .WithSummary("Submit an invoice from an Outlook email: save the email alone and submit its marked attachment")
            .Produces<InvoiceSubmissionFromEmailAccepted>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // GET /api/finance/core-records/{entityType}/{entityId}/firms (task 095, contract section 8): the firms the submit accepts.
        group.MapGet("/core-records/{entityType}/{entityId:guid}/firms", InvoiceSubmissionFromEmailHttp.GetFirmsAsync)
            .AddFinanceAuthorizationFilter(InvoiceSubmissionFromEmailHttp.ResolveFirmsTargets, FinanceDenial.UniformNotFound)
            .RequireEbillingEnabled()
            .WithName("GetCoreRecordFirms")
            .WithSummary("The active firms linked to a matter, project or work assignment")
            .Produces<IReadOnlyList<CoreRecordFirm>>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // GET /api/finance/invoice-submissions/{id}
        group.MapGet("/invoice-submissions/{id:guid}", GetAsync)
            .AddFinanceAuthorizationFilter("finance.read", FinanceAuthorizationFilter.InvoiceSubmissionEntitySet, "id", FinanceDenial.UniformNotFound)
            .RequireEbillingEnabled()
            .WithName("GetInvoiceSubmission")
            .WithSummary("Read one invoice submission's status")
            .Produces<InvoiceSubmissionView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Authorization declaration — reads the SAME route and query values the handler binds
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Read + AppendTo on the route's core record, and AppendTo on the query's firm. An unreadable route id yields no record check, which
    /// the filter denies; a malformed firmId adds no firm check (the handler answers 400 <see cref="InvoiceSubmissionErrors.FirmInvalid"/>),
    /// so a caller without rights on the record is always answered 403 first.
    /// </summary>
    internal static FinanceAuthorizationTargets ResolveSubmitTargets(EndpointFilterInvocationContext context, string entitySet)
    {
        var request = context.HttpContext.Request;
        if (!Guid.TryParse(request.RouteValues["id"]?.ToString(), out var id) || id == Guid.Empty)
        {
            return FinanceAuthorizationTargets.Authorize();
        }

        var checks = new List<FinanceAuthorizationCheck>
        {
            new()
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = entitySet,
                RecordId = id,
                Operation = "finance.submit_invoice",
                Source = "route.id",
            },
        };

        if (Guid.TryParse(request.Query["firmId"].ToString(), out var firmId) && firmId != Guid.Empty)
        {
            checks.Add(new FinanceAuthorizationCheck
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = FinanceAuthorizationFilter.VendorOrganizationEntitySet,
                RecordId = firmId,
                Operation = "finance.attach_invoice",
                Source = "query.firmId",
            });
        }

        return FinanceAuthorizationTargets.Authorize(checks.ToArray());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Handlers
    // ═══════════════════════════════════════════════════════════════════════════

    private static Task<IResult> SubmitForMatterAsync(Guid id, HttpContext http, InvoiceSubmissionService service, CancellationToken ct) =>
        SubmitAsync(new InvoiceCoreRecord(InvoiceCoreRecordKind.Matter, id), http, service, ct);

    private static Task<IResult> SubmitForProjectAsync(Guid id, HttpContext http, InvoiceSubmissionService service, CancellationToken ct) =>
        SubmitAsync(new InvoiceCoreRecord(InvoiceCoreRecordKind.Project, id), http, service, ct);

    private static Task<IResult> SubmitForWorkAssignmentAsync(Guid id, HttpContext http, InvoiceSubmissionService service, CancellationToken ct) =>
        SubmitAsync(new InvoiceCoreRecord(InvoiceCoreRecordKind.WorkAssignment, id), http, service, ct);

    private static async Task<IResult> SubmitAsync(InvoiceCoreRecord core, HttpContext http, InvoiceSubmissionService service, CancellationToken ct)
    {
        if (!Guid.TryParse(http.Request.Query["firmId"].ToString(), out var firmId) || firmId == Guid.Empty)
        {
            return InvoiceSubmissionHttp.Refused(StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.FirmInvalid, "firmId is required and must be a GUID.");
        }

        var form = await InvoiceSubmissionHttp.ReadSubmissionFormAsync(http.Request, service, StatusUrl, ct);
        if (form.Refusal is { } refusal)
        {
            return refusal;
        }

        Guid? callerOid = Guid.TryParse(CallerResolution.ResolveObjectId(http.User), out var oid) ? oid : null;
        var outcome = await service.SubmitAsync(new InvoiceSubmissionRequest
        {
            FirmId = firmId,
            CoreRecord = core,
            Channel = InvoiceSubmissionChannel.Internal,
            FirmInvoiceNumber = form.FirmInvoiceNumber,
            PoNumber = form.PoNumber,
            File = form.File,
            CallerObjectId = callerOid,
            CorrelationId = http.TraceIdentifier,
        }, ct);

        return InvoiceSubmissionHttp.ToResult(outcome, StatusUrl, http.TraceIdentifier);
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext http, InvoiceSubmissionService service, CancellationToken ct)
    {
        try
        {
            var view = await service.GetAsync(id, ct);
            return view is null ? FinanceAuthorizationFilter.UniformRecordNotFound(http) : Results.Ok(view);
        }
        catch (SdapProblemException ex)
        {
            return InvoiceSubmissionHttp.ReadFault(ex);
        }
    }
}

/// <summary>The 202 body of a received submission (tasks 051 / 052 rely on these names).</summary>
/// <param name="SubmissionId">The <c>sprk_invoicesubmission</c> id; also the processing job's idempotency key.</param>
/// <param name="JobId">The enqueued processing job.</param>
/// <param name="TrackingNumber">The firm-scoped tracking number (D-87); never null (D-91).</param>
/// <param name="StatusUrl">The status route of this submission on the channel it arrived through.</param>
public sealed record InvoiceSubmissionAccepted(Guid SubmissionId, Guid JobId, string TrackingNumber, string StatusUrl);
