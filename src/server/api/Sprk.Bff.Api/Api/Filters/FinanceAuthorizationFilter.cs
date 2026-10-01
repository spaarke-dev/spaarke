using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding <see cref="FinanceAuthorizationFilter"/> to finance (and scorecard)
/// routes. Follows ADR-008: the per-resource decision is an endpoint filter in each route's own fluent
/// chain, so RouteAuthorizationGuardTests Rule A can see it.
/// </summary>
/// <remarks>
/// <para><b>Each route declares exactly what it authorizes</b> (unified-access-control-r2 task 130,
/// defect C8). The filter used to walk a fallback chain — route matterId → documentId → invoiceId → id →
/// query matterId → documentId → invoiceId — and evaluate whatever it found first against
/// <c>sprk_documents</c>. That made three separate defects:</para>
/// <list type="bullet">
///   <item>the summary route checked a MATTER id as if it were a document, so it denied every caller;</item>
///   <item>confirm/reject read their ids from the BODY, which the chain never looked at, so a caller could
///   append <c>?documentId=&lt;a document I can write&gt;</c> and have the handler act on a different
///   body id;</item>
///   <item>invoice search authorized an optional <c>?documentId=</c> and then ran a tenant-wide search.</item>
/// </list>
/// <para>Now a route supplies either a fixed (route key, entity set, operation) or a resolver that reads the
/// SAME source its handler binds and returns the full list of checks. There is no fallback: a route whose
/// declaration yields no check is denied.</para>
/// </remarks>
public static class FinanceAuthorizationFilterExtensions
{
    /// <summary>
    /// Authorizes <paramref name="operation"/> on the record of the FIXED entity set
    /// <paramref name="entitySetName"/> whose id is the route value <paramref name="routeKey"/>.
    /// </summary>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <param name="operation">The <see cref="OperationAccessPolicy"/> key (e.g. "finance.read").</param>
    /// <param name="entitySetName">
    /// A constant entity SET name — use one of the <see cref="FinanceAuthorizationFilter"/> constants. The
    /// entity type is fixed by the route, never taken from request input, and never pluralized.
    /// </param>
    /// <param name="routeKey">The route parameter the handler binds (e.g. "matterId").</param>
    /// <param name="denial">How a denial is rendered — see <see cref="FinanceDenial"/>.</param>
    public static TBuilder AddFinanceAuthorizationFilter<TBuilder>(
        this TBuilder builder,
        string operation,
        string entitySetName,
        string routeKey,
        FinanceDenial denial = FinanceDenial.Forbidden) where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(entitySetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeKey);

        return builder.AddFinanceAuthorizationFilter(
            context => FinanceAuthorizationTargets.FromRoute(context, routeKey, entitySetName, operation),
            denial);
    }

    /// <summary>
    /// Authorizes every check the <paramref name="resolveTargets"/> declaration returns for this request.
    /// Use for routes whose ids come from the request BODY or QUERY: the resolver reads the same source the
    /// handler binds, validates it, and names each (entity set, id, operation) the handler acts on.
    /// </summary>
    public static TBuilder AddFinanceAuthorizationFilter<TBuilder>(
        this TBuilder builder,
        Func<EndpointFilterInvocationContext, FinanceAuthorizationTargets> resolveTargets,
        FinanceDenial denial = FinanceDenial.Forbidden) where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(resolveTargets);

        return builder.AddEndpointFilter(async (context, next) =>
        {
            var authService = context.HttpContext.RequestServices.GetRequiredService<AuthorizationService>();
            var filter = new FinanceAuthorizationFilter(authService, resolveTargets, denial);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>How a <see cref="FinanceAuthorizationFilter"/> denial is rendered.</summary>
public enum FinanceDenial
{
    /// <summary>403 ProblemDetails carrying a machine-readable <c>reasonCode</c> (ADR-003 / ADR-019).</summary>
    Forbidden,

    /// <summary>
    /// The uniform 404 from <see cref="FinanceAuthorizationFilter.UniformRecordNotFound"/> — IDENTICAL for an
    /// absent record and for one the caller may not read, so the status is not an existence oracle. Used by
    /// the recalculate routes, whose handlers return the same response for a record that disappears between
    /// the check and the compute.
    /// </summary>
    UniformNotFound,
}

/// <summary>Which authorization path a <see cref="FinanceAuthorizationCheck"/> takes.</summary>
public enum FinanceCheckPath
{
    /// <summary>
    /// The existing document path: <see cref="AuthorizationService.AuthorizeAsync"/> with the rule chain,
    /// against <c>sprk_documents</c>. Used for Write on a document (confirm / reject).
    /// </summary>
    Document,

    /// <summary>
    /// The entity-generic path: <see cref="AuthorizationService.GetCallerRecordAccessAsync"/> against the
    /// named entity set, then <see cref="OperationAccessPolicy.HasRequiredRights"/>.
    /// </summary>
    Record,
}

/// <summary>One (entity set, id, operation) the caller must be authorized for before the handler runs.</summary>
public sealed record FinanceAuthorizationCheck
{
    public required FinanceCheckPath Path { get; init; }

    /// <summary>A constant entity SET name (one of the <see cref="FinanceAuthorizationFilter"/> constants).</summary>
    public required string EntitySetName { get; init; }

    public required Guid RecordId { get; init; }

    /// <summary>The <see cref="OperationAccessPolicy"/> key that names the required rights.</summary>
    public required string Operation { get; init; }

    /// <summary>Where the id came from (e.g. "body.matterId") — logged on deny, never returned.</summary>
    public required string Source { get; init; }
}

/// <summary>
/// The result of a route's authorization declaration: the checks to run, or a validation rejection
/// (400) that is returned BEFORE any Dataverse call.
/// </summary>
public sealed class FinanceAuthorizationTargets
{
    private FinanceAuthorizationTargets() { }

    public IReadOnlyList<FinanceAuthorizationCheck> Checks { get; private init; } = Array.Empty<FinanceAuthorizationCheck>();

    public IResult? Rejection { get; private init; }

    public static FinanceAuthorizationTargets Authorize(params FinanceAuthorizationCheck[] checks) =>
        new() { Checks = checks };

    public static FinanceAuthorizationTargets Reject(IResult rejection) =>
        new() { Rejection = rejection ?? throw new ArgumentNullException(nameof(rejection)) };

    /// <summary>
    /// A single Record-path check on the route value <paramref name="routeKey"/>. An unreadable or empty id
    /// yields NO check, which the filter denies — never a pass-through.
    /// </summary>
    internal static FinanceAuthorizationTargets FromRoute(
        EndpointFilterInvocationContext context, string routeKey, string entitySetName, string operation)
    {
        if (context.HttpContext.Request.RouteValues.TryGetValue(routeKey, out var raw)
            && Guid.TryParse(raw?.ToString(), out var id)
            && id != Guid.Empty)
        {
            return Authorize(new FinanceAuthorizationCheck
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = entitySetName,
                RecordId = id,
                Operation = operation,
                Source = "route." + routeKey,
            });
        }

        return Authorize();
    }
}

/// <summary>
/// Endpoint filter that authorizes finance and scorecard operations AS THE CALLER, on exactly the record(s)
/// the route's handler acts on, before any Dataverse read or write. Fails closed (ADR-003): a missing
/// token, an unresolvable caller, a rights fault, or any exception denies. There is no app-only path.
/// Follows ADR-008 (endpoint filter) and ADR-019 (ProblemDetails with a reasonCode).
/// </summary>
public class FinanceAuthorizationFilter : IEndpointFilter
{
    // ── Entity SET names. Every finance route has a FIXED entity type, so these are per-route constants
    //    (the ProvisionProjectEndpoint.ProjectEntitySet precedent), NOT a logical-name → set map. Each value
    //    was read from live EntityDefinitions metadata (spaarkedev1, 2026-09-30) and is pinned by
    //    FinanceEndpointsAuthorizationContractTests. Never derive one by pluralizing a logical name: a
    //    wrong set fails closed as a deny that is indistinguishable from "no access", which hides the bug.
    public const string MatterEntitySet = "sprk_matters";
    public const string ProjectEntitySet = "sprk_projects";
    public const string DocumentEntitySet = "sprk_documents";

    /// <summary>sprk_invoice.sprk_vendororg targets <c>sprk_organization</c>; its set name per live metadata.</summary>
    public const string VendorOrganizationEntitySet = "sprk_organizations";

    /// <summary>Record-path deny reason (same code OperationAccessRule uses for the document path).</summary>
    public const string InsufficientRightsReasonCode = "sdap.access.deny.insufficient_rights";

    /// <summary>The check itself faulted — denied, never allowed (ADR-003).</summary>
    public const string SystemFailureReasonCode = "sdap.access.error.system_failure";

    /// <summary>The route declared no resolvable id — denied rather than passed through.</summary>
    public const string NoTargetReasonCode = "sdap.access.deny.no_target";

    /// <summary>
    /// The ONE reasonCode of the uniform 404. Deliberately the same for an absent record and for an
    /// unreadable one; distinguishing them in any channel would confirm the existence of records the caller
    /// cannot see.
    /// </summary>
    public const string RecordUnavailableReasonCode = "sdap.access.deny.record_unavailable";

    private readonly AuthorizationService _authorizationService;
    private readonly Func<EndpointFilterInvocationContext, FinanceAuthorizationTargets> _resolveTargets;
    private readonly FinanceDenial _denial;

    public FinanceAuthorizationFilter(
        AuthorizationService authorizationService,
        Func<EndpointFilterInvocationContext, FinanceAuthorizationTargets> resolveTargets,
        FinanceDenial denial)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _resolveTargets = resolveTargets ?? throw new ArgumentNullException(nameof(resolveTargets));
        _denial = denial;
    }

    /// <summary>
    /// The uniform "not found" response shared by the recalculate filter denial AND the recalculate handlers'
    /// <see cref="KeyNotFoundException"/> branch, so the three cases (absent, unreadable, deleted between check
    /// and compute) are byte-identical apart from the correlation id. It never contains the requested id.
    /// </summary>
    public static IResult UniformRecordNotFound(HttpContext httpContext) =>
        Results.Problem(
            title: "Not Found",
            detail: "The requested record was not found.",
            statusCode: StatusCodes.Status404NotFound,
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            extensions: new Dictionary<string, object?>
            {
                ["reasonCode"] = RecordUnavailableReasonCode,
                ["correlationId"] = httpContext.TraceIdentifier,
            });

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var logger = httpContext.RequestServices.GetService<ILogger<FinanceAuthorizationFilter>>();

        // Entra `oid`, not `sub` — see CallerResolution (UAT 2026-08-26 / D-6 class). A caller with no
        // resolvable identity is unauthenticated for this purpose; that answer does not depend on the record,
        // so a 401 here is not an existence oracle.
        var userId = CallerResolution.ResolveObjectId(httpContext.User);
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        FinanceAuthorizationTargets targets;
        try
        {
            targets = _resolveTargets(context);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Finance authorization: the route's target declaration threw for caller {UserId}; denying", userId);
            return Deny(httpContext, SystemFailureReasonCode);
        }

        // A validation rejection (400) is returned BEFORE any rights query — an empty or missing id must not
        // spend a Dataverse round trip, and must not be turned into a 403 by filter ordering.
        if (targets.Rejection is not null)
        {
            return targets.Rejection;
        }

        if (targets.Checks.Count == 0)
        {
            logger?.LogWarning("Finance authorization DENIED for caller {UserId}: the route declared no resolvable target", userId);
            return Deny(httpContext, NoTargetReasonCode);
        }

        // Fail closed: a null token is forwarded as null, and both paths deny on it (AuthorizeAsync returns
        // no_caller_token; GetCallerRecordAccessAsync returns AccessRights.None). There is no app-only branch.
        var callerToken = TokenHelper.ExtractBearerTokenOrNull(httpContext);
        var ct = httpContext.RequestAborted;

        foreach (var check in targets.Checks)
        {
            string? denyReason;
            try
            {
                denyReason = await EvaluateAsync(check, userId, callerToken, httpContext.TraceIdentifier, ct);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex,
                    "Finance authorization check faulted for caller {UserId} on {EntitySet}({RecordId}) [{Source}] " +
                    "operation {Operation}; denying (fail closed)",
                    userId, check.EntitySetName, check.RecordId, check.Source, check.Operation);
                denyReason = SystemFailureReasonCode;
            }

            if (denyReason is not null)
            {
                logger?.LogWarning(
                    "Finance authorization DENIED: caller {UserId} on {EntitySet}({RecordId}) [{Source}] " +
                    "operation {Operation} reason {Reason}",
                    userId, check.EntitySetName, check.RecordId, check.Source, check.Operation, denyReason);
                return Deny(httpContext, denyReason);
            }
        }

        return await next(context);
    }

    /// <summary>Returns <c>null</c> when allowed, otherwise the deny reason code.</summary>
    private async Task<string?> EvaluateAsync(
        FinanceAuthorizationCheck check, string userId, string? callerToken, string correlationId, CancellationToken ct)
    {
        if (check.RecordId == Guid.Empty)
        {
            return NoTargetReasonCode;
        }

        switch (check.Path)
        {
            case FinanceCheckPath.Document:
            {
                var result = await _authorizationService.AuthorizeAsync(new AuthorizationContext
                {
                    UserId = userId,
                    ResourceId = check.RecordId.ToString(),
                    Operation = check.Operation,
                    CorrelationId = correlationId,
                    UserAccessToken = callerToken,
                }, ct);

                return result.IsAllowed ? null : result.ReasonCode;
            }

            case FinanceCheckPath.Record:
            {
                var snapshot = await _authorizationService.GetCallerRecordAccessAsync(
                    userId, check.EntitySetName, check.RecordId, callerToken, ct);

                return OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, check.Operation)
                    ? null
                    : InsufficientRightsReasonCode;
            }

            default:
                return NoTargetReasonCode;
        }
    }

    private IResult Deny(HttpContext httpContext, string reasonCode) =>
        _denial == FinanceDenial.UniformNotFound
            ? UniformRecordNotFound(httpContext)
            : ProblemDetailsHelper.Forbidden(reasonCode, traceId: httpContext.TraceIdentifier);
}
