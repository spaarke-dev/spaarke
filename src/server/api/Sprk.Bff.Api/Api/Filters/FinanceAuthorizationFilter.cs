using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

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
/// <para><b>Not finance-only.</b> It is the one filter that authorizes several BODY-declared ids per route, so
/// the analysis routes (task 162) reuse it rather than adding another filter (CLAUDE.md §11).
/// (<c>POST /api/ai/document-intelligence/associate-record</c>, its first non-finance user under task 146 r1, was deleted
/// by task 164.)</para>
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
            var services = context.HttpContext.RequestServices;
            var authService = services.GetRequiredService<AuthorizationService>();
            // Only the Privilege path uses the probe; GetService so a route that never asks one does not
            // depend on it. A Privilege check with no probe denies (see EvaluateAsync).
            var filter = new FinanceAuthorizationFilter(
                authService, resolveTargets, denial, services.GetService<CallerRecordAccessProbe>());
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

    /// <summary>
    /// A TABLE privilege the caller must hold (e.g. Create on <c>sprk_invoice</c>), asked of Dataverse as the
    /// caller through <see cref="CallerRecordAccessProbe.CallerHoldsPrivilegeAsync"/>. There is no record id:
    /// it answers "could this user create one", for a record the app is about to create on their behalf
    /// (task 130, owner decision G5). Build with <see cref="FinanceAuthorizationCheck.CallerPrivilege"/>.
    /// </summary>
    Privilege,
}

/// <summary>One (entity set, id, operation) the caller must be authorized for before the handler runs.</summary>
public sealed record FinanceAuthorizationCheck
{
    public required FinanceCheckPath Path { get; init; }

    /// <summary>A constant entity SET name (one of the <see cref="FinanceAuthorizationFilter"/> constants).</summary>
    public required string EntitySetName { get; init; }

    /// <summary>The record asked about. <see cref="Guid.Empty"/> only on the <see cref="FinanceCheckPath.Privilege"/> path.</summary>
    public required Guid RecordId { get; init; }

    /// <summary>
    /// The <see cref="OperationAccessPolicy"/> key that names the required rights — or, on the
    /// <see cref="FinanceCheckPath.Privilege"/> path, the Dataverse privilege name (which never reaches the policy).
    /// </summary>
    public required string Operation { get; init; }

    /// <summary>Where the id came from (e.g. "body.matterId") — logged on deny, never returned.</summary>
    public required string Source { get; init; }

    /// <summary>
    /// A check that the CALLER holds the Dataverse table privilege <paramref name="privilegeName"/> (any depth),
    /// e.g. <see cref="FinanceAuthorizationFilter.CreateInvoicePrivilege"/>. <paramref name="entitySetName"/>
    /// names the table the privilege governs, for the deny log only.
    /// </summary>
    public static FinanceAuthorizationCheck CallerPrivilege(string privilegeName, string entitySetName, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privilegeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entitySetName);

        return new FinanceAuthorizationCheck
        {
            Path = FinanceCheckPath.Privilege,
            EntitySetName = entitySetName,
            RecordId = Guid.Empty,
            Operation = privilegeName,
            Source = source,
        };
    }
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

    /// <summary><c>sprk_invoice</c>'s set name per live metadata (spaarkedev1, 2026-10-01).</summary>
    public const string InvoiceEntitySet = "sprk_invoices";

    /// <summary>
    /// The Create privilege on <c>sprk_invoice</c>, by its live name (spaarkedev1 <c>privileges</c>, 2026-10-01 —
    /// the table's schema name is <c>sprk_Invoice</c>, so the privilege is NOT all lower case). A misspelt name
    /// answers "not held" for every caller and denies every confirm, which is why it is pinned by a test.
    /// </summary>
    public const string CreateInvoicePrivilege = "prvCreatesprk_Invoice";

    /// <summary>Record-path deny reason (same code OperationAccessRule uses for the document path).</summary>
    public const string InsufficientRightsReasonCode = "sdap.access.deny.insufficient_rights";

    /// <summary>The check itself faulted — denied, never allowed (ADR-003).</summary>
    public const string SystemFailureReasonCode = "sdap.access.error.system_failure";

    /// <summary>The caller lacks a TABLE privilege the operation needs (e.g. Create on sprk_invoice).</summary>
    public const string InsufficientPrivilegeReasonCode = "sdap.access.deny.insufficient_privilege";

    /// <summary>The route declared no resolvable id — denied rather than passed through.</summary>
    public const string NoTargetReasonCode = "sdap.access.deny.no_target";

    /// <summary>
    /// The ONE reasonCode of the uniform 404 — forwards to <see cref="ProblemDetailsHelper.RecordUnavailableReasonCode"/>
    /// (moved there by task 159 so the events surface shares it; value unchanged).
    /// </summary>
    public const string RecordUnavailableReasonCode = ProblemDetailsHelper.RecordUnavailableReasonCode;

    private readonly AuthorizationService _authorizationService;
    private readonly Func<EndpointFilterInvocationContext, FinanceAuthorizationTargets> _resolveTargets;
    private readonly FinanceDenial _denial;
    private readonly CallerRecordAccessProbe? _probe;

    public FinanceAuthorizationFilter(
        AuthorizationService authorizationService,
        Func<EndpointFilterInvocationContext, FinanceAuthorizationTargets> resolveTargets,
        FinanceDenial denial,
        CallerRecordAccessProbe? probe = null)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _resolveTargets = resolveTargets ?? throw new ArgumentNullException(nameof(resolveTargets));
        _denial = denial;
        _probe = probe;
    }

    /// <summary>
    /// The uniform "not found" response shared by the recalculate filter denial AND the recalculate handlers'
    /// <see cref="KeyNotFoundException"/> branch, so the three cases (absent, unreadable, deleted between check
    /// and compute) are byte-identical apart from the correlation id. It never contains the requested id.
    /// Forwards to <see cref="ProblemDetailsHelper.UniformRecordNotFound"/> (moved there by task 159, bytes unchanged).
    /// </summary>
    public static IResult UniformRecordNotFound(HttpContext httpContext) =>
        ProblemDetailsHelper.UniformRecordNotFound(httpContext);

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
        if (check.Path == FinanceCheckPath.Privilege)
        {
            // Fail closed: no probe registered is a configuration fault, never an allow.
            var probe = _probe;
            if (probe is null)
            {
                return SystemFailureReasonCode;
            }

            return await probe.CallerHoldsPrivilegeAsync(callerToken, check.Operation, ct)
                ? null
                : InsufficientPrivilegeReasonCode;
        }

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
