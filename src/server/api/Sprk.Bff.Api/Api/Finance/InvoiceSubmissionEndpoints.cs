using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
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
/// fields. <see cref="ReadSubmissionFormAsync"/> is shared with the external route, so both channels parse identically.</para>
/// <para><b>Response contract (tasks 049 / 051 / 052).</b> 202 with <c>Location</c> = <c>statusUrl</c> and the body
/// <see cref="InvoiceSubmissionAccepted"/>; refusals are ProblemDetails with <c>errorCode</c> (<see cref="InvoiceSubmissionErrors"/>).</para>
/// </remarks>
public static class InvoiceSubmissionEndpoints
{
    internal const string Route = "/api/finance";

    /// <summary>The status route of a submission on the internal surface.</summary>
    internal static string StatusUrl(Guid submissionId) => $"{Route}/invoice-submissions/{submissionId}";

    /// <summary>
    /// The whole-request body cap: the largest configurable file (<see cref="EbillingOptions.MaxSubmissionFileSizeMbCeiling"/>) plus 1 MB of
    /// multipart envelope. Kestrel's default (about 30 MB) would otherwise refuse a configured 50 MB file before the handler; the configured
    /// limit itself is enforced by the handler (413 <see cref="InvoiceSubmissionErrors.TooLarge"/>).
    /// </summary>
    internal const long MaxRequestBodyBytes = (EbillingOptions.MaxSubmissionFileSizeMbCeiling + 1L) * 1024 * 1024;

    public static IEndpointRouteBuilder MapInvoiceSubmissionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route)
            .RequireAuthorization()
            .WithTags("Finance");

        // POST /api/finance/{matters|projects|workassignments}/{id}/invoice-submissions. Three literal chains (not a loop): the
        // RouteAuthorizationGuard scanner reads each route's path and attachments from its own Map{Verb} chain.
        group.MapPost("/matters/{id:guid}/invoice-submissions", SubmitForMatterAsync)
            .AddFinanceAuthorizationFilter(context => ResolveSubmitTargets(context, FinanceAuthorizationFilter.MatterEntitySet))
            .RequireEbillingEnabled()
            .DisableAntiforgery() // multipart upload; auth is the JWT + the caller's own record rights, not a form token
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBodyBytes))
            .WithName("SubmitInvoiceForMatter")
            .WithSubmitContract("matter");

        group.MapPost("/projects/{id:guid}/invoice-submissions", SubmitForProjectAsync)
            .AddFinanceAuthorizationFilter(context => ResolveSubmitTargets(context, FinanceAuthorizationFilter.ProjectEntitySet))
            .RequireEbillingEnabled()
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBodyBytes))
            .WithName("SubmitInvoiceForProject")
            .WithSubmitContract("project");

        group.MapPost("/workassignments/{id:guid}/invoice-submissions", SubmitForWorkAssignmentAsync)
            .AddFinanceAuthorizationFilter(context => ResolveSubmitTargets(context, FinanceAuthorizationFilter.WorkAssignmentEntitySet))
            .RequireEbillingEnabled()
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBodyBytes))
            .WithName("SubmitInvoiceForWorkAssignment")
            .WithSubmitContract("work assignment");

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
            return Refused(StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.FirmInvalid, "firmId is required and must be a GUID.");
        }

        var form = await ReadSubmissionFormAsync(http.Request, service, ct);
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

        return ToResult(outcome, StatusUrl, http.TraceIdentifier);
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
            return ReadFault(ex);
        }
    }

    /// <summary>A read fault of the service (<see cref="InvoiceSubmissionService.ReadUnavailable"/>, 503) in this feature's problem shape.</summary>
    internal static IResult ReadFault(SdapProblemException ex) => Refused(ex.StatusCode, ex.Code, ex.Detail ?? ex.Title);

    // ═══════════════════════════════════════════════════════════════════════════
    // Shared with the external route
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>The parsed multipart body, or the refusal to return instead.</summary>
    internal sealed record SubmissionForm(InvoiceSubmissionFile? File, string? FirmInvoiceNumber, string? PoNumber, IResult? Refusal);

    /// <summary>
    /// Reads the multipart body: <c>file</c>, <c>firmInvoiceNumber</c>, <c>poNumber</c>. A body larger than the configured limit is a 413
    /// before it is parsed; a malformed body is a 400 (the parser's message can echo request content, so it is not returned). Nothing is
    /// validated beyond size and shape here: the service validates the number (D-91), the PDF and the firm, in that order, before any write.
    /// </summary>
    internal static async Task<SubmissionForm> ReadSubmissionFormAsync(HttpRequest request, InvoiceSubmissionService service, CancellationToken ct)
    {
        var maxBytes = service.MaxFileBytes;
        if (request.ContentLength > maxBytes + (1024 * 1024)) // the multipart envelope adds a little; the file itself is checked below
        {
            return new SubmissionForm(null, null, null, ToResult(service.TooLarge(), StatusUrl, request.HttpContext.TraceIdentifier));
        }

        if (!request.HasFormContentType)
        {
            return new SubmissionForm(null, null, null,
                Refused(StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.FileRequired, "The request must be multipart/form-data with a PDF file."));
        }

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
        {
            return new SubmissionForm(null, null, null,
                Refused(StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.FileRequired, "The request body is not valid multipart/form-data."));
        }

        var number = form["firmInvoiceNumber"].ToString();
        var po = form["poNumber"].ToString();
        var part = form.Files.GetFile("file");
        if (part is null || part.Length == 0)
        {
            return new SubmissionForm(null, number, po, null);
        }

        if (part.Length > maxBytes)
        {
            return new SubmissionForm(null, number, po, ToResult(service.TooLarge(), StatusUrl, request.HttpContext.TraceIdentifier));
        }

        byte[] bytes;
        await using (var stream = part.OpenReadStream())
        using (var buffer = new MemoryStream())
        {
            await stream.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }

        return new SubmissionForm(new InvoiceSubmissionFile(part.FileName, part.ContentType, bytes), number, po, null);
    }

    /// <summary>The OpenAPI contract of a submit route (documentation only; authorization is on each route's own chain).</summary>
    internal static RouteHandlerBuilder WithSubmitContract(this RouteHandlerBuilder builder, string coreNoun) => builder
        .Accepts<IFormFile>("multipart/form-data")
        .WithSummary($"Submit an invoice PDF against a {coreNoun}")
        .WithDescription(
            "Stores the PDF in the record's container, creates the document (type Invoice) and the submission (Received, with its " +
            "tracking number) and enqueues processing. Form: file (PDF), firmInvoiceNumber (required), poNumber. Query: firmId. " +
            "Refusals are ProblemDetails with errorCode: 400 not_pdf, file_required, firm_invoice_number_required, firm_invalid; " +
            "403 without the rights the route needs; 413 too_large; 503 ebilling.disabled.")
        .Produces<InvoiceSubmissionAccepted>(StatusCodes.Status202Accepted)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    /// <summary>202 with <c>Location</c> = the channel's status URL, or the refusal as ProblemDetails (ADR-019).</summary>
    internal static IResult ToResult(InvoiceSubmissionOutcome outcome, Func<Guid, string> statusUrl, string traceId) => outcome.Kind switch
    {
        InvoiceSubmissionOutcomeKind.Received => Accepted(outcome.Receipt!, statusUrl(outcome.Receipt!.SubmissionId)),
        InvoiceSubmissionOutcomeKind.OwnerRefused => ProblemDetailsHelper.RecordOwnerRefused(outcome.OwnerRefusal!, "invoice submission", traceId),
        _ => Refused(outcome.StatusCode, outcome.ErrorCode!, outcome.Detail!),
    };

    private static IResult Accepted(InvoiceSubmissionReceipt receipt, string statusUrl) =>
        Results.Accepted(statusUrl, new InvoiceSubmissionAccepted(receipt.SubmissionId, receipt.JobId, receipt.TrackingNumber, statusUrl));

    internal static IResult Refused(int statusCode, string errorCode, string detail) =>
        Results.Problem(
            statusCode: statusCode, detail: detail,
            extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode });
}

/// <summary>The 202 body of a received submission (tasks 051 / 052 rely on these names).</summary>
/// <param name="SubmissionId">The <c>sprk_invoicesubmission</c> id; also the processing job's idempotency key.</param>
/// <param name="JobId">The enqueued processing job.</param>
/// <param name="TrackingNumber">The firm-scoped tracking number (D-87); never null (D-91).</param>
/// <param name="StatusUrl">The status route of this submission on the channel it arrived through.</param>
public sealed record InvoiceSubmissionAccepted(Guid SubmissionId, Guid JobId, string TrackingNumber, string StatusUrl);
