using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse.Privileges;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>How a <c>/api/v1/external</c> endpoint meets the workforce ceiling (unified-access-control-r2 task 186).</summary>
internal enum WorkforceCeilingCoverage
{
    /// <summary>The route's requirement set is fixed; <see cref="WorkforceCeilingEndpointFilter"/> checks it.</summary>
    Route,

    /// <summary>The route's set depends on the request (a route value or the bound body); the filter resolves and checks it.</summary>
    RouteResolved,

    /// <summary>The handler checks through <see cref="WorkforceRoleCeilingGate"/> because the table is known only there.</summary>
    InHandler,

    /// <summary>Not a record operation (identity, bootstrap, schema); the reason is written on the route.</summary>
    Exempt,
}

/// <summary>
/// The workforce-ceiling declaration a <c>/api/v1/external</c> endpoint carries (review round 1 F1). Every endpoint under
/// that prefix carries exactly one; <c>WorkforceCeilingCoverageTests</c> (ArchTests) fails the build for one that does not.
/// </summary>
internal sealed class WorkforceCeilingMetadata
{
    private WorkforceCeilingMetadata(
        WorkforceCeilingCoverage coverage,
        IReadOnlyList<WorkforceCeilingRequirement> requirements,
        Func<EndpointFilterInvocationContext, IReadOnlyList<WorkforceCeilingRequirement>?>? resolve,
        string? reason)
    {
        Coverage = coverage;
        Requirements = requirements;
        Resolve = resolve;
        Reason = reason;
    }

    public WorkforceCeilingCoverage Coverage { get; }

    /// <summary>The fixed set (<see cref="WorkforceCeilingCoverage.Route"/>); empty otherwise.</summary>
    public IReadOnlyList<WorkforceCeilingRequirement> Requirements { get; }

    /// <summary>
    /// The per-request set (<see cref="WorkforceCeilingCoverage.RouteResolved"/>). <see langword="null"/> from it means
    /// the request names nothing the route serves (an unsupported root, an unparseable record type): the handler refuses
    /// such a request with its own 4xx before reading or writing anything, so the filter lets it through.
    /// </summary>
    public Func<EndpointFilterInvocationContext, IReadOnlyList<WorkforceCeilingRequirement>?>? Resolve { get; }

    /// <summary>Why the route is checked in its handler, or exempt.</summary>
    public string? Reason { get; }

    public static WorkforceCeilingMetadata ForRoute(IReadOnlyList<WorkforceCeilingRequirement> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        if (requirements.Count == 0)
            throw new ArgumentException("A route's workforce-ceiling requirement set cannot be empty.", nameof(requirements));
        return new(WorkforceCeilingCoverage.Route, requirements, null, null);
    }

    public static WorkforceCeilingMetadata ForRequest(
        Func<EndpointFilterInvocationContext, IReadOnlyList<WorkforceCeilingRequirement>?> resolve) =>
        new(WorkforceCeilingCoverage.RouteResolved, Array.Empty<WorkforceCeilingRequirement>(),
            resolve ?? throw new ArgumentNullException(nameof(resolve)), null);

    public static WorkforceCeilingMetadata Declared(WorkforceCeilingCoverage coverage, string reason)
    {
        if (coverage is not (WorkforceCeilingCoverage.InHandler or WorkforceCeilingCoverage.Exempt))
            throw new ArgumentOutOfRangeException(nameof(coverage), coverage, "Only InHandler and Exempt carry a reason.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("An in-handler or exempt declaration states its reason.", nameof(reason));
        return new(coverage, Array.Empty<WorkforceCeilingRequirement>(), null, reason);
    }
}

/// <summary>Marks an endpoint whose group runs <see cref="WorkforceCeilingEndpointFilter"/> (checked by the coverage guard).</summary>
internal sealed class WorkforceCeilingFilterAttached
{
    public static readonly WorkforceCeilingFilterAttached Instance = new();

    private WorkforceCeilingFilterAttached()
    {
    }
}

/// <summary>Declares an endpoint's workforce ceiling (review round 1 F1, CLAUDE.md §16: declarative, checked by one filter).</summary>
/// <remarks>
/// <b>Per route only (review round 2).</b> The four declarations extend <see cref="RouteHandlerBuilder"/>, the builder a
/// single <c>Map*</c> call returns, never <see cref="RouteGroupBuilder"/>: a group-wide declaration would satisfy the
/// coverage guard for every route the group maps, including one added later that needs a different set. Enforced by: the
/// type (these four extensions do not compile on a group) and the source-scan guard
/// <c>WorkforceCeilingMetadataConstructionGuardTests</c> (ArchTests), which fails the build when server code outside this
/// file calls a <see cref="WorkforceCeilingMetadata"/> factory, so <c>group.WithMetadata(WorkforceCeilingMetadata.Declared(…))</c>
/// cannot be written around the extensions. The factories stay <see langword="internal"/> because the unit and arch tests
/// build declarations to prove the filter and the coverage guard.
/// </remarks>
internal static class WorkforceCeilingEndpointExtensions
{
    /// <summary>The route needs every privilege in <paramref name="requirements"/> on the Workforce role.</summary>
    public static RouteHandlerBuilder RequireWorkforceCeiling(
        this RouteHandlerBuilder builder, IReadOnlyList<WorkforceCeilingRequirement> requirements) =>
        builder.WithMetadata(WorkforceCeilingMetadata.ForRoute(requirements));

    /// <summary>The route's privileges depend on the request; <paramref name="resolve"/> derives them before the handler.</summary>
    public static RouteHandlerBuilder RequireWorkforceCeiling(
        this RouteHandlerBuilder builder,
        Func<EndpointFilterInvocationContext, IReadOnlyList<WorkforceCeilingRequirement>?> resolve) =>
        builder.WithMetadata(WorkforceCeilingMetadata.ForRequest(resolve));

    /// <summary>The handler applies the ceiling through <see cref="WorkforceRoleCeilingGate"/>; <paramref name="reason"/> says how.</summary>
    public static RouteHandlerBuilder WorkforceCeilingInHandler(this RouteHandlerBuilder builder, string reason) =>
        builder.WithMetadata(WorkforceCeilingMetadata.Declared(WorkforceCeilingCoverage.InHandler, reason));

    /// <summary>Not a record operation; <paramref name="reason"/> says why the ceiling does not apply.</summary>
    public static RouteHandlerBuilder ExemptFromWorkforceCeiling(this RouteHandlerBuilder builder, string reason) =>
        builder.WithMetadata(WorkforceCeilingMetadata.Declared(WorkforceCeilingCoverage.Exempt, reason));

    /// <summary>
    /// Runs <see cref="WorkforceCeilingEndpointFilter"/> on every endpoint of <paramref name="builder"/> (the external
    /// group). Attach it AFTER <c>AddCallerPrincipalAuthorizationFilter</c>: group filters run in the order they are added,
    /// and the ceiling needs the resolved principal. Route-level filters and the handler run after it, so it decides before
    /// any record read.
    /// </summary>
    public static TBuilder AddWorkforceCeilingFilter<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(WorkforceCeilingFilterAttached.Instance).AddEndpointFilter<TBuilder, WorkforceCeilingEndpointFilter>();
}

/// <summary>
/// The ONE place the workforce ceiling is enforced for a declared route (unified-access-control-r2 task 186, review round
/// 1 F1). It reads the endpoint's <see cref="WorkforceCeilingMetadata"/> and, for a workforce-contact caller only
/// (<see cref="WorkforceRoleCeilingGate.Applies"/>), asks <see cref="WorkforceRoleCeilingGate.DenyAsync"/> before the
/// handler runs. Systemusers and CIAM partners pass through without a role read.
/// </summary>
/// <remarks>
/// <para><b>Fail closed.</b> A workforce contact on an endpoint with NO declaration is refused
/// (<see cref="WorkforceRoleCeilingGate.DenyBaselinePrivilegeMissing"/>): a route that forgot to declare is denied, not
/// waved through. The coverage guard makes that state unmergeable; this is the runtime backstop.</para>
/// <para>CLAUDE.md §11. <b>Existing:</b> <see cref="WorkforceRoleCeilingGate"/> (the decision) and
/// <c>CallerPrincipalAuthorizationFilter</c> (the principal). <b>Extension:</b> the gate stays the single decision point;
/// this only attaches it once per group instead of once per handler. <b>Cost of doing nothing:</b> a route added to the
/// group without a hand-written hook skips the ceiling silently (review round 1 F1 found 40-odd).</para>
/// </remarks>
internal sealed class WorkforceCeilingEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        if (httpContext.Items[CallerPrincipal.HttpContextItemsKey] is not CallerPrincipal principal
            || !WorkforceRoleCeilingGate.Applies(principal))
        {
            // No principal: the handler answers its own 500. Not a workforce contact: the ceiling does not apply.
            return await next(context).ConfigureAwait(false);
        }

        var services = httpContext.RequestServices;
        var logger = services.GetRequiredService<ILogger<WorkforceCeilingEndpointFilter>>();
        var declaration = httpContext.GetEndpoint()?.Metadata.GetMetadata<WorkforceCeilingMetadata>();

        IReadOnlyList<WorkforceCeilingRequirement>? required;
        switch (declaration?.Coverage)
        {
            case WorkforceCeilingCoverage.Route:
                required = declaration.Requirements;
                break;

            case WorkforceCeilingCoverage.RouteResolved:
                required = declaration.Resolve!(context);
                if (required is null)
                    return await next(context).ConfigureAwait(false);
                break;

            case WorkforceCeilingCoverage.InHandler:
            case WorkforceCeilingCoverage.Exempt:
                return await next(context).ConfigureAwait(false);

            default:
                logger.LogError(
                    "[WF-CEILING] DENY contact {ContactId}: endpoint {Endpoint} declares no workforce ceiling.",
                    principal.ContactId, httpContext.GetEndpoint()?.DisplayName);
                return ProblemDetailsHelper.Forbidden(
                    WorkforceRoleCeilingGate.DenyBaselinePrivilegeMissing, "Your account's access does not include this action.");
        }

        var denied = await WorkforceRoleCeilingGate
            .DenyAsync(principal, services.GetRequiredService<IDataversePrivilegeChecker>(), required, logger,
                httpContext.RequestAborted)
            .ConfigureAwait(false);
        return denied ?? await next(context).ConfigureAwait(false);
    }
}
