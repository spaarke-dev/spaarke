using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// Assign a firm to a matter, project or work assignment from the Outlook pane (ebilling-r1 task 095g, EB-D-224, EB-D-233..235; notes
/// 095g-assign-firm.md). ADR-001 (Minimal API), ADR-008 (endpoint filters), ADR-019 (stable codes).
/// </summary>
/// <remarks>
/// <para><b>Component Justification (CLAUDE.md §11).</b> (1) Existing: <c>/office/search/entities</c> returns association targets read as the
/// caller, with no organization type and no step 0; the section 8 GET (<c>GET .../core-records/{type}/{id}/firms</c>) lists the record's
/// linked firms only; the Matter Management form and the generic update tools write the slots with no slot rules. (2) Extension: none can
/// carry the internal-only, law-firm-only list or the slot rules, eligibility refusal and revoke confirmation without changing what it is
/// for. (3) Cost of doing nothing: the pane cannot offer an unlinked firm or save one, so EB-D-224 cannot be built.</para>
/// <para><b>Authorization</b>, as the caller and fail closed. Step 0 on the group (an external or row-less caller gets the uniform 404 with
/// zero rights calls). The list asks the caller's Read privilege on <c>sprk_organization</c>; the assign asks Write on the record (a clear)
/// or Write and Append (an add or replace, uac-r2 R1), from one caller snapshot, then the caller's Read and AppendTo on the firm. Both run
/// the filter in <see cref="FinanceDenial.UniformNotFoundFailClosed"/>: a denial is the uniform 404, a rights fault is 503. The eBilling
/// kill switch follows the filter.</para>
/// </remarks>
public static class CoreRecordFirmEndpoints
{
    /// <summary>The caller's Read privilege on <c>sprk_organization</c>, by its live name (the Spaarke roles' own privilege name).</summary>
    internal const string ReadOrganizationPrivilege = "prvReadsprk_Organization";

    internal const int DefaultPageSize = 25;

    public static IEndpointRouteBuilder MapCoreRecordFirmEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/finance")
            .RequireAuthorization()
            .RequireInternalFinanceCaller() // step 0 on every /api/finance route (section 5.3, task 099)
            .WithTags("Finance");

        // GET /api/finance/firms?search=&page=&pageSize= - the active law firms an internal user may assign (design section 1A)
        group.MapGet("/firms", ListFirmsAsync)
            .AddFinanceAuthorizationFilter(ResolveListTargets, FinanceDenial.UniformNotFoundFailClosed)
            .RequireEbillingEnabled()
            .RequireRateLimiting("dataverse-query")
            .WithName("ListAssignableFirms")
            .WithSummary("The active law firms an internal user can assign to a matter, project or work assignment")
            .WithDescription(
                "Internal callers only. Active organizations of the Law Firm type, sorted by name then id, searchable by name. 404 (uniform) " +
                "for an external caller, one with no user row or no Read on organizations; 503 on any fault, never an empty list.")
            .Produces<FirmListResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/finance/core-records/{entityType}/{entityId}/firms - add, replace or clear an assigned firm (design section 1B)
        group.MapPost("/core-records/{entityType}/{entityId:guid}/firms", AssignAsync)
            .AddFinanceAuthorizationFilter(ResolveAssignTargets, FinanceDenial.UniformNotFoundFailClosed)
            .RequireEbillingEnabled()
            .WithName("AssignCoreRecordFirm")
            .WithSummary("Add, replace or clear a firm assigned to a matter, project or work assignment")
            .WithDescription(
                "Body { add, remove, confirmRevoke }: add alone fills slot 1 then slot 2; add and remove replaces; remove alone clears. A " +
                "replace or clear removes the access the assignment gave the old firm and needs confirmRevoke naming it. Refused on a Restricted, " +
                "secure or limited record, an inactive or non-law firm, or a firm on the record's No Access list. The answer carries the " +
                "record's firms, the slots and the access effect.")
            .Produces<CoreRecordFirmAssignResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Target declarations (run after step 0, before any rights query)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>The list: the query shape (400), then the caller's Read privilege on organizations.</summary>
    internal static FinanceAuthorizationTargets ResolveListTargets(EndpointFilterInvocationContext context)
    {
        if (ListQueryErrors(context.HttpContext.Request.Query) is { } errors)
        {
            return FinanceAuthorizationTargets.Reject(ProblemDetailsHelper.ValidationProblem(errors));
        }

        return FinanceAuthorizationTargets.Authorize(FinanceAuthorizationCheck.CallerPrivilege(
            ReadOrganizationPrivilege, FinanceAuthorizationFilter.VendorOrganizationEntitySet, "route.firms"));
    }

    /// <summary>
    /// The assign: the entity type and the body shape (400), then ONE Record check on the route record as the caller - Write and Append for
    /// an add or replace (<c>ebilling.record.assign_firm</c>, uac-r2 R1), Write for a pure clear (<c>ebilling.record.remove_firm</c>).
    /// </summary>
    internal static FinanceAuthorizationTargets ResolveAssignTargets(EndpointFilterInvocationContext context)
    {
        var route = context.HttpContext.Request.RouteValues;
        if (!InvoiceSubmissionFromEmailHttp.TryCoreType(route["entityType"]?.ToString(), out _, out var entitySet))
        {
            return FinanceAuthorizationTargets.Reject(ProblemDetailsHelper.ValidationProblem(
                new Dictionary<string, string[]> { ["entityType"] = new[] { "entityType must be Matter, Project or WorkAssignment." } }));
        }

        var request = context.Arguments.OfType<CoreRecordFirmAssignRequest>().FirstOrDefault();
        if (BodyErrors(request) is { } errors)
        {
            return FinanceAuthorizationTargets.Reject(ProblemDetailsHelper.ValidationProblem(errors));
        }

        if (!Guid.TryParse(route["entityId"]?.ToString(), out var id) || id == Guid.Empty)
        {
            return FinanceAuthorizationTargets.Authorize(); // no check: the filter denies
        }

        // One check, so Write and Append come from ONE caller snapshot (uac-r2 R1).
        return FinanceAuthorizationTargets.Authorize(request!.Add is not null
            ? new FinanceAuthorizationCheck
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = entitySet,
                RecordId = id,
                Operation = "ebilling.record.assign_firm",
                Source = "route.entityId",
            }
            : new FinanceAuthorizationCheck
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = entitySet,
                RecordId = id,
                Operation = "ebilling.record.remove_firm",
                Source = "route.entityId",
            });
    }

    internal static Dictionary<string, string[]>? ListQueryErrors(IQueryCollection query)
    {
        var errors = new Dictionary<string, string[]>();
        var search = query["search"].ToString();
        if (search.Length > CoreRecordFirmAssignmentService.SearchMaxLength)
        {
            errors["search"] = new[] { $"search must be at most {CoreRecordFirmAssignmentService.SearchMaxLength} characters." };
        }

        if (query.ContainsKey("page") && (!int.TryParse(query["page"].ToString(), out var page) || page < 1))
        {
            errors["page"] = new[] { "page must be 1 or more." };
        }

        if (query.ContainsKey("pageSize")
            && (!int.TryParse(query["pageSize"].ToString(), out var size) || size < 1 || size > CoreRecordFirmAssignmentService.PageSizeMax))
        {
            errors["pageSize"] = new[] { $"pageSize must be between 1 and {CoreRecordFirmAssignmentService.PageSizeMax}." };
        }

        return errors.Count == 0 ? null : errors;
    }

    internal static Dictionary<string, string[]>? BodyErrors(CoreRecordFirmAssignRequest? request)
    {
        if (request is null)
        {
            return new Dictionary<string, string[]> { ["$"] = new[] { "A request body is required." } };
        }

        var errors = new Dictionary<string, string[]>();
        if (request.Add == Guid.Empty)
        {
            errors["add"] = new[] { "add must be a firm id." };
        }

        if (request.Remove == Guid.Empty)
        {
            errors["remove"] = new[] { "remove must be a firm id." };
        }

        if (request.ConfirmRevoke == Guid.Empty)
        {
            errors["confirmRevoke"] = new[] { "confirmRevoke must be a firm id." };
        }

        if (request.Add is null && request.Remove is null)
        {
            errors["$"] = new[] { "Name a firm to add, a firm to remove, or both." };
        }
        else if (request.Add is { } add && add == request.Remove)
        {
            errors["$"] = new[] { "add and remove must be different firms." };
        }

        return errors.Count == 0 ? null : errors;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Handlers
    // ═══════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> ListFirmsAsync(
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromServices] CoreRecordFirmAssignmentService service,
        CancellationToken ct)
    {
        var result = await service.ListAsync(search, page ?? 1, pageSize ?? DefaultPageSize, ct);
        return result.Page is { } body
            ? TypedResults.Ok(body)
            : InvoiceLineReviewEndpoints.Refused(StatusCodes.Status503ServiceUnavailable, CoreRecordFirmCodes.Unavailable, "The firms could not be read right now. Try again.");
    }

    private static async Task<IResult> AssignAsync(
        string entityType,
        Guid entityId,
        [FromBody] CoreRecordFirmAssignRequest request,
        [FromServices] CoreRecordFirmAssignmentService service,
        HttpContext http,
        CancellationToken ct)
    {
        // The filter has admitted the caller; the grantor is the token's own oid, never a body or route value (uac-r2 Q5).
        var callerOid = CallerResolution.ResolveObjectId(http.User);
        if (string.IsNullOrEmpty(callerOid) || !InvoiceSubmissionFromEmailHttp.TryCoreType(entityType, out var kind, out _))
        {
            return FinanceAuthorizationFilter.UniformRecordNotFound(http);
        }

        var logger = http.RequestServices.GetService<ILogger<CoreRecordFirmAssignmentService>>();
        var result = await service.AssignFirmAsync(
            new InvoiceCoreRecord(kind, entityId), request.Add, request.Remove, request.ConfirmRevoke, callerOid,
            (firmId, token) => CallerFirmRightsAsync(http, firmId, logger, token), ct);

        return ToResult(result, http);
    }

    /// <summary>
    /// Step 3: the caller's Read (<c>read</c>) and AppendTo (<c>finance.attach_invoice</c>, the firm lookup attaches the record to it) on the
    /// firm, through the Finance filter's own check (as the caller, no app-only branch). <c>true</c> both held, <c>false</c> one denied (an
    /// unknown firm answers no rights), <c>null</c> no answer.
    /// </summary>
    private static async Task<bool?> CallerFirmRightsAsync(HttpContext http, Guid firmId, ILogger? logger, CancellationToken ct)
    {
        foreach (var operation in new[] { "read", "finance.attach_invoice" })
        {
            var verdict = await FinanceAuthorizationFilter.EvaluateCheckForCallerAsync(http, new FinanceAuthorizationCheck
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = FinanceAuthorizationFilter.VendorOrganizationEntitySet,
                RecordId = firmId,
                Operation = operation,
                Source = "body.add",
            }, logger, ct);

            if (verdict == FinanceCheckVerdict.Faulted)
            {
                return null;
            }

            if (verdict == FinanceCheckVerdict.Denied)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Maps the service's answer to one status and one stable code (ADR-019). Every refusal wrote nothing.</summary>
    internal static IResult ToResult(CoreRecordFirmAssignResult result, HttpContext http)
    {
        if (result is { Status: StatusCodes.Status200OK, Body: { } body })
        {
            return TypedResults.Ok(body);
        }

        if (result.Status == StatusCodes.Status404NotFound)
        {
            return FinanceAuthorizationFilter.UniformRecordNotFound(http);
        }

        var extensions = new Dictionary<string, object?> { ["errorCode"] = result.Code };
        if (result.Assigned is { } assigned)
        {
            extensions["assigned"] = assigned;
        }

        if (result.Revokes is { } revokes)
        {
            extensions["revokes"] = revokes;
        }

        return Results.Problem(statusCode: result.Status, detail: result.Detail, extensions: extensions);
    }
}

/// <summary>Body of the firm-assign route: firm ids only, never slot numbers.</summary>
/// <param name="Add">The firm to assign (a grant), alone or with <paramref name="Remove"/> (a replace).</param>
/// <param name="Remove">The firm to take off the record (a clear), alone or with <paramref name="Add"/> (a replace).</param>
/// <param name="ConfirmRevoke">Must equal <paramref name="Remove"/> when the call would take a firm off the record.</param>
public sealed record CoreRecordFirmAssignRequest(Guid? Add, Guid? Remove, Guid? ConfirmRevoke);
