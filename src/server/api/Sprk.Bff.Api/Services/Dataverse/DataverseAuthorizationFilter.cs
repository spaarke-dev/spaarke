using Sprk.Bff.Api.Infrastructure.Authentication;
using System.Security.Claims;
using Sprk.Bff.Api.Services.Dataverse.Privileges;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// Strategy describing where the <see cref="DataverseAuthorizationFilter"/> resolves the entity
/// to privilege-check for the current endpoint.
/// </summary>
/// <remarks>
/// The <c>FromFetchXmlBody</c> and <c>FromRouteValueWithRecord</c> members were DELETED with the only
/// two routes that used them, the internal <c>POST /api/dataverse/fetch</c> and
/// <c>GET /api/dataverse/record/{entityLogicalName}/{id}</c> (unified-access-control-r2 task 160, owner
/// round 10 item 1: no caller in the repo, in no published API description). Those routes ran the
/// caller's query app-only, so this entity-level check was their only control (route sweep findings
/// #9 and #10). Do not re-add a FetchXML-body mode for a route that reads app-only: this filter checks a
/// table-level privilege at any depth, and it never consults a record.
/// </remarks>
internal enum EntitySource
{
    /// <summary>Single entity logical name from a route value (default key: <c>entityLogicalName</c>).</summary>
    FromRouteValue,

    /// <summary>
    /// Single entity derived by looking up the SavedQuery from a <c>savedQueryId</c> route value
    /// (the SavedQueryService caches savedquery→entity mapping for fast lookup).
    /// </summary>
    FromSavedQueryEntity
}

/// <summary>
/// Per-endpoint configuration for <see cref="DataverseAuthorizationFilter"/>.
/// </summary>
internal sealed record DataverseAuthorizationFilterOptions(
    EntitySource EntitySource,
    string RouteKey = "entityLogicalName");

/// <summary>
/// Authorization filter for Dataverse projection endpoints (FR-BFF-01..03, FR-BFF-07).
/// Validates that the caller has Read privilege on the Dataverse entity named by the request.
/// </summary>
/// <remarks>
/// <para>
/// ENTITY-LEVEL ONLY. The caller's readable-entity set is built from <c>RetrieveUserPrivileges</c>
/// and keeps any <c>prvRead*</c> privilege at ANY depth (Basic, Local, Deep or Global); no record is
/// consulted. It is a cheap, documented 403 for table metadata and view definitions, and it is NOT a
/// record-level control: a route that returns record DATA must run that read as the caller (or
/// pre-check the exact record) and must not rely on this filter.
/// </para>
/// </remarks>
/// <remarks>
/// <para>
/// Follows ADR-008 (endpoint-filter authorization), ADR-019 (ProblemDetails), ADR-028 (Spaarke Auth v2).
/// See <c>010-authorization-filter-shape.md</c> for the canonical design (sections 1-12).
/// </para>
/// <para>
/// Constructed per-request by <see cref="DataverseAuthorizationFilterExtensions"/> with the
/// per-endpoint <see cref="DataverseAuthorizationFilterOptions"/>. The class is NOT registered in DI
/// directly — its dependency (<see cref="IDataversePrivilegeChecker"/>) is resolved from the
/// request-scoped service provider.
/// </para>
/// </remarks>
internal sealed class DataverseAuthorizationFilter : IEndpointFilter
{
    private readonly IDataversePrivilegeChecker _privilegeChecker;
    private readonly ILogger<DataverseAuthorizationFilter> _logger;
    private readonly DataverseAuthorizationFilterOptions _options;

    // Azure AD claim names (matches DocumentAuthorizationFilter + SemanticSearchAuthorizationFilter precedents).
    private const string OidClaimType = "oid";
    private const string AltOidClaimType = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string TenantIdClaimType = "tid";

    public DataverseAuthorizationFilter(
        IDataversePrivilegeChecker privilegeChecker,
        ILogger<DataverseAuthorizationFilter> logger,
        DataverseAuthorizationFilterOptions options)
    {
        _privilegeChecker = privilegeChecker ?? throw new ArgumentNullException(nameof(privilegeChecker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var ct = httpContext.RequestAborted;

        // Step 1: Identity extraction.
        var userOidStr = CallerResolution.ResolveObjectId(httpContext.User);

        if (!Guid.TryParse(userOidStr, out var userOid))
        {
            _logger.LogWarning(
                "Dataverse authorization denied: no/invalid oid claim (correlationId={CorrelationId})",
                httpContext.TraceIdentifier);
            return DataverseProblem(401, "Unauthorized", "User identity not found in authentication token",
                "DV_NO_USER_IDENTITY", httpContext);
        }

        var tenantId = httpContext.User.FindFirst(TenantIdClaimType)?.Value;

        // Step 2: Resolve the entity to check.
        string entity;
        try
        {
            entity = ResolveEntity(context);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                "Dataverse authorization denied: cannot resolve target entity (correlationId={CorrelationId}, reason={Reason})",
                httpContext.TraceIdentifier, ex.Message);
            return DataverseProblem(400, "Bad Request", ex.Message, "DV_NO_TARGET_ENTITY", httpContext);
        }

        if (string.IsNullOrWhiteSpace(entity))
        {
            return DataverseProblem(400, "Bad Request", "Target entity not resolvable from request",
                "DV_NO_TARGET_ENTITY", httpContext);
        }

        // Step 3: Privilege check (entity-level; see the class remarks).
        var allowed = await _privilegeChecker.HasReadPrivilegeAsync(userOid, entity, ct);

        if (!allowed)
        {
            _logger.LogWarning(
                "Dataverse authorization denied: user={UserOid}, tenant={TenantId}, deniedEntity={DeniedEntity}, entitySource={EntitySource}, correlationId={CorrelationId}",
                userOid, tenantId, entity, _options.EntitySource, httpContext.TraceIdentifier);

            return DataverseProblem(
                403,
                "Forbidden",
                $"Read privilege denied on entity '{entity}'",
                "DV_PRIVILEGE_DENIED",
                httpContext);
        }

        // Step 4: Log success + delegate.
        _logger.LogInformation(
            "Dataverse authorization granted: user={UserOid}, tenant={TenantId}, entity={Entity}, entitySource={EntitySource}, correlationId={CorrelationId}",
            userOid, tenantId, entity, _options.EntitySource, httpContext.TraceIdentifier);

        return await next(context);
    }

    /// <summary>
    /// Resolves the entity to privilege-check based on the configured <see cref="EntitySource"/>.
    /// </summary>
    private string ResolveEntity(EndpointFilterInvocationContext context)
    {
        var routeValues = context.HttpContext.Request.RouteValues;

        switch (_options.EntitySource)
        {
            case EntitySource.FromRouteValue:
                {
                    var entityLogicalName = routeValues[_options.RouteKey]?.ToString();
                    if (string.IsNullOrWhiteSpace(entityLogicalName))
                    {
                        throw new InvalidOperationException(
                            $"Route value '{_options.RouteKey}' is missing or empty");
                    }
                    return entityLogicalName.Trim().ToLowerInvariant();
                }

            case EntitySource.FromSavedQueryEntity:
                {
                    // The savedquery→entity lookup happens inside the endpoint handler (SavedQueryService
                    // already caches savedquery payloads, including the EntityName). To keep the filter
                    // synchronous and avoid a second cache lookup, the savedquery endpoints use
                    // FromRouteValue with the entity-list endpoint OR rely on per-handler privilege checks
                    // for the by-id endpoint.
                    //
                    // For the by-id endpoint, the filter cannot resolve the entity without a Dataverse
                    // round-trip. The chosen design (per task 010 §4 Step 2) is to defer the resolution to
                    // the handler: the filter validates identity + tenant, the handler calls
                    // SavedQueryService.GetSavedQueryAsync (which is cached), and the handler performs the
                    // privilege check using IDataversePrivilegeChecker once the entityName is known.
                    //
                    // Returning a marker entity here would be a leaky abstraction. Instead we mark this
                    // path as "deferred-to-handler" by returning a synthetic placeholder that the handler
                    // recognises and replaces. The filter's job for FromSavedQueryEntity is reduced to
                    // identity check + audit logging; the actual privilege gate lives in the handler.
                    //
                    // Implementation choice (recorded as a deviation in 011-deviations.md): the filter
                    // is NOT applied to the by-id endpoint with FromSavedQueryEntity. The handler calls
                    // IDataversePrivilegeChecker directly after loading the savedquery. Tasks 015-016
                    // integration tests will validate this path.
                    throw new InvalidOperationException(
                        "EntitySource.FromSavedQueryEntity must be handled by the endpoint (see handler-side privilege check)");
                }

            default:
                throw new InvalidOperationException(
                    $"Unsupported EntitySource: {_options.EntitySource}");
        }
    }

    /// <summary>
    /// Standardised ProblemDetails response builder per the §7 error catalog in
    /// <c>010-authorization-filter-shape.md</c>.
    /// </summary>
    private static IResult DataverseProblem(
        int status,
        string title,
        string detail,
        string errorCode,
        HttpContext httpContext) =>
        Results.Problem(
            statusCode: status,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["correlationId"] = httpContext.TraceIdentifier
            });
}

/// <summary>
/// Extension methods that wire <see cref="DataverseAuthorizationFilter"/> onto specific endpoints.
/// </summary>
internal static class DataverseAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds the Dataverse authorization filter to an endpoint.
    /// </summary>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <param name="entitySource">Where the filter finds the entity(ies) to privilege-check.</param>
    /// <param name="routeKey">Route key for <see cref="EntitySource.FromRouteValue"/> (default: <c>entityLogicalName</c>).</param>
    public static TBuilder AddDataverseAuthorizationFilter<TBuilder>(
        this TBuilder builder,
        EntitySource entitySource,
        string routeKey = "entityLogicalName") where TBuilder : IEndpointConventionBuilder
    {
        var options = new DataverseAuthorizationFilterOptions(entitySource, routeKey);

        return builder.AddEndpointFilter(async (context, next) =>
        {
            var sp = context.HttpContext.RequestServices;
            var checker = sp.GetRequiredService<IDataversePrivilegeChecker>();
            var logger = sp.GetRequiredService<ILogger<DataverseAuthorizationFilter>>();

            var filter = new DataverseAuthorizationFilter(checker, logger, options);
            return await filter.InvokeAsync(context, next);
        });
    }
}
