using Microsoft.AspNetCore.Http.HttpResults;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Services.Finance.Coding;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// AI code-set mapping proposals and their admin confirmation (ebilling-r1 task 048, FR-22), ADR-001 (Minimal API) and
/// ADR-008 (endpoint filters).
/// </summary>
/// <remarks>
/// <para><b>Admin-only, by Dataverse table privilege (D-76).</b> The admin is whoever holds Write on the reference table(s) the route
/// touches, asked of Dataverse AS THE CALLER (<see cref="FinanceAuthorizationCheck.CallerPrivilege"/>, fail closed): the proposals list
/// needs Write on BOTH matter types and practice areas (it is admin data), the confirm route Write on the one table named in its path.
/// No role-name check, no app role. Each route carries its own authorization filter, then the eBilling flag filter
/// (<c>RequireEbillingEnabled</c>, ADR-018): the flag never replaces authorization.</para>
/// <para><b>Nothing is applied unconfirmed (FR-22).</b> The GET writes nothing. <c>CodeSetProposalService.ConfirmAsync</c>, reached
/// only from the POST, is the one writer of <c>sprk_defaultbillingcodeset</c>.</para>
/// <para><b>No UI.</b> BFF endpoints only: the task carries no frontend tag and SpaarkeMaster source is reserved for serial tasks.</para>
/// </remarks>
public static class CodeSetMappingEndpoints
{
    internal const string Route = "/api/finance/code-set-mappings";

    // ADR-019 stable codes (carried in the ProblemDetails errorCode extension).
    public const string CodeForbidden = "ebilling.codeset.forbidden";
    public const string CodeNotFound = "ebilling.codeset.not_found";
    public const string CodeAlreadyMapped = "ebilling.codeset.already_mapped";
    public const string CodeConcurrentChange = "ebilling.codeset.concurrent_change";

    public static IEndpointRouteBuilder MapCodeSetMappingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route)
            .RequireAuthorization()
            .RequireInternalFinanceCaller() // step 0 on every /api/finance route (section 5.3, task 099)
            .WithTags("Finance");

        // GET /api/finance/code-set-mappings/proposals — AI proposals for every unmapped matter type and practice area
        group.MapGet("/proposals", GetProposals)
            .AddEndpointFilter<CodeSetForbiddenCodeFilter>()
            .AddFinanceAuthorizationFilter(_ => FinanceAuthorizationTargets.Authorize(
                Privilege(CodeSetReferenceKind.MatterType),
                Privilege(CodeSetReferenceKind.PracticeArea)))
            .RequireEbillingEnabled()
            .WithName("GetCodeSetMappingProposals")
            .WithSummary("AI-proposed default code sets for unmapped matter types and practice areas, with coverage")
            .WithDescription(
                "Lists every active matter type and practice area with no default code set as either Proposed (the code set, its " +
                "similarity, the runner-up, the AI review's confidence and reason; Reviewed=false marks an embedding-only fallback) or " +
                "NoneFits (no proposal; the closest set as context; map by hand or add a code set), plus coverage counts (mapped / " +
                "proposed / none-fits) per table. Nothing is applied: confirm a proposal with the POST route.")
            .Produces<CodeSetProposalsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/finance/code-set-mappings/{table}/{id}/confirm — write one mapping
        group.MapPost("/{table}/{id:guid}/confirm", ConfirmMapping)
            .AddEndpointFilter<CodeSetForbiddenCodeFilter>()
            .AddFinanceAuthorizationFilter(ResolveConfirmTargets)
            .RequireEbillingEnabled()
            .WithName("ConfirmCodeSetMapping")
            .WithSummary("Confirm a code set as the default for one matter type or practice area")
            .WithDescription(
                "{table} is 'mattertype' or 'practicearea'. Re-reads the row and refuses with 409 when it already has a default code set; " +
                "otherwise sets sprk_defaultbillingcodeset on exactly that row to the named code set. The write is conditional on the " +
                "row version read, so a mapping another admin saved in the meantime is never overwritten (also 409). A row that is still " +
                "unmapped but keeps changing under the write is refused with 409 ebilling.codeset.concurrent_change; retry.")
            .Produces<CodeSetMappingConfirmedResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static FinanceAuthorizationCheck Privilege(CodeSetReferenceKind kind) =>
        FinanceAuthorizationCheck.CallerPrivilege(
            CodeSetReferenceTables.WritePrivilege(kind), CodeSetReferenceTables.EntitySet(kind), "code set mapping admin");

    /// <summary>
    /// Write on the one table the path names. An unknown <c>{table}</c> is the 404 before any rights query (it asks Dataverse nothing and
    /// reveals only that the segment is not one of the two table names).
    /// </summary>
    internal static FinanceAuthorizationTargets ResolveConfirmTargets(EndpointFilterInvocationContext context)
    {
        var raw = context.HttpContext.Request.RouteValues.TryGetValue("table", out var value) ? value?.ToString() : null;
        return CodeSetReferenceTables.TryParse(raw, out var kind)
            ? FinanceAuthorizationTargets.Authorize(Privilege(kind))
            : FinanceAuthorizationTargets.Reject(Refused(StatusCodes.Status404NotFound, CodeNotFound, "The table must be 'mattertype' or 'practicearea'."));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Handlers
    // ═══════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> GetProposals(CodeSetProposalService service, CancellationToken ct)
    {
        try
        {
            var list = await service.ProposeAsync(ct);
            return Results.Ok(new CodeSetProposalsResponse(list.Proposals, list.Coverage));
        }
        catch (FeatureDisabledException ex)
        {
            return ex.AsFeatureDisabled503();
        }
    }

    private static async Task<IResult> ConfirmMapping(
        string table, Guid id, ConfirmCodeSetMappingRequest request, HttpContext http, CodeSetProposalService service, CancellationToken ct)
    {
        // The filter already refused an unknown table; this keeps the handler honest if it is ever mapped without it.
        if (!CodeSetReferenceTables.TryParse(table, out var kind))
        {
            return Refused(StatusCodes.Status404NotFound, CodeNotFound, "The table must be 'mattertype' or 'practicearea'.");
        }

        Guid? callerOid = Guid.TryParse(CallerResolution.ResolveObjectId(http.User), out var oid) ? oid : null;
        var outcome = await service.ConfirmAsync(kind, id, request.CodeSetId, callerOid, ct);
        return outcome switch
        {
            CodeSetConfirmOutcome.Confirmed => Results.Ok(new CodeSetMappingConfirmedResponse(CodeSetReferenceTables.Segment(kind), id, request.CodeSetId)),
            CodeSetConfirmOutcome.AlreadyMapped => Refused(
                StatusCodes.Status409Conflict, CodeAlreadyMapped, "This row already has a default code set; nothing was changed."),
            CodeSetConfirmOutcome.ConcurrentChange => Refused(
                StatusCodes.Status409Conflict, CodeConcurrentChange,
                "This row is still unmapped but kept changing while the mapping was being saved; nothing was changed. Try again."),
            CodeSetConfirmOutcome.CodeSetNotFound => Refused(
                StatusCodes.Status404NotFound, CodeNotFound, "The code set was not found, is inactive or has no active Task codes."),
            _ => Refused(StatusCodes.Status404NotFound, CodeNotFound, "The matter type or practice area was not found."),
        };
    }

    private static IResult Refused(int statusCode, string errorCode, string detail) =>
        Results.Problem(
            statusCode: statusCode, detail: detail,
            extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode });
}

/// <summary>Body of the confirm route.</summary>
public sealed record ConfirmCodeSetMappingRequest(Guid CodeSetId);

/// <summary>
/// Response of the proposals route (task 054, D-93): one entry per unmapped active row (proposed, none-fits or failed) and the coverage
/// of both tables, so every active matter type and practice area is accounted for as mapped, proposed, none-fits or failed.
/// </summary>
public sealed record CodeSetProposalsResponse(IReadOnlyList<CodeSetProposal> Proposals, CodeSetCoverage Coverage);

/// <summary>Response of a confirmed mapping.</summary>
public sealed record CodeSetMappingConfirmedResponse(string Table, Guid ReferenceId, Guid CodeSetId);

/// <summary>
/// Stamps the stable <see cref="CodeSetMappingEndpoints.CodeForbidden"/> code (ADR-019) on a 403 produced by the route's authorization
/// filter, which carries only a <c>reasonCode</c>. It is chained BEFORE the authorization filter so it sees that filter's result; it
/// decides nothing and never turns a denial into anything else.
/// </summary>
public sealed class CodeSetForbiddenCodeFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var result = await next(context);
        if (result is ProblemHttpResult { StatusCode: StatusCodes.Status403Forbidden } problem)
        {
            problem.ProblemDetails.Extensions["errorCode"] = CodeSetMappingEndpoints.CodeForbidden;
        }

        return result;
    }
}
