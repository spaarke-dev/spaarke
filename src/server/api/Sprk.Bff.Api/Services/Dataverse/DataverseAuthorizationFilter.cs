using System.Security.Claims;
using Sprk.Bff.Api.Services.Dataverse.FetchXml;
using Sprk.Bff.Api.Services.Dataverse.Privileges;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// Strategy describing where the <see cref="DataverseAuthorizationFilter"/> resolves the entity
/// (or entities) to privilege-check for the current endpoint.
/// </summary>
internal enum EntitySource
{
    /// <summary>Single entity logical name from a route value (default key: <c>entityLogicalName</c>).</summary>
    FromRouteValue,

    /// <summary>
    /// Single entity derived by looking up the SavedQuery from a <c>savedQueryId</c> route value
    /// (the SavedQueryService caches savedquery→entity mapping for fast lookup).
    /// </summary>
    FromSavedQueryEntity,

    /// <summary>
    /// Primary entity plus every <c>&lt;link-entity&gt;</c> inside the FetchXML body (FR-BFF-04).
    /// Requires <see cref="IFetchXmlEntityExtractor"/> to be DI-registered (task 013 owns the impl).
    /// </summary>
    FromFetchXmlBody,

    /// <summary>
    /// Single entity from a route value with the record id passed through to the handler for
    /// downstream existence/row-level validation (FR-BFF-05).
    /// </summary>
    FromRouteValueWithRecord
}

/// <summary>
/// Per-endpoint configuration for <see cref="DataverseAuthorizationFilter"/>.
/// </summary>
internal sealed record DataverseAuthorizationFilterOptions(
    EntitySource EntitySource,
    string RouteKey = "entityLogicalName");

/// <summary>
/// Authorization filter for Dataverse projection endpoints (FR-BFF-01..05, FR-BFF-07).
/// Validates that the caller has Read privilege on every Dataverse entity referenced by the request.
/// </summary>
/// <remarks>
/// <para>
/// Follows ADR-008 (endpoint-filter authorization), ADR-019 (ProblemDetails), ADR-028 (Spaarke Auth v2).
/// See <c>010-authorization-filter-shape.md</c> for the canonical design (sections 1-12).
/// </para>
/// <para>
/// Constructed per-request by <see cref="DataverseAuthorizationFilterExtensions"/> with the
/// per-endpoint <see cref="DataverseAuthorizationFilterOptions"/>. The class is NOT registered in DI
/// directly — its dependencies (<see cref="IDataversePrivilegeChecker"/>, <see cref="IFetchXmlEntityExtractor"/>)
/// are resolved from the request-scoped service provider.
/// </para>
/// </remarks>
internal sealed class DataverseAuthorizationFilter : IEndpointFilter
{
    private readonly IDataversePrivilegeChecker _privilegeChecker;
    private readonly IFetchXmlEntityExtractor? _fetchXmlExtractor;
    private readonly ILogger<DataverseAuthorizationFilter> _logger;
    private readonly DataverseAuthorizationFilterOptions _options;

    // Azure AD claim names (matches DocumentAuthorizationFilter + SemanticSearchAuthorizationFilter precedents).
    private const string OidClaimType = "oid";
    private const string AltOidClaimType = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string TenantIdClaimType = "tid";

    public DataverseAuthorizationFilter(
        IDataversePrivilegeChecker privilegeChecker,
        IFetchXmlEntityExtractor? fetchXmlExtractor,
        ILogger<DataverseAuthorizationFilter> logger,
        DataverseAuthorizationFilterOptions options)
    {
        _privilegeChecker = privilegeChecker ?? throw new ArgumentNullException(nameof(privilegeChecker));
        _fetchXmlExtractor = fetchXmlExtractor; // Optional: only required for EntitySource.FromFetchXmlBody.
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
        var userOidStr = httpContext.User.FindFirst(OidClaimType)?.Value
                         ?? httpContext.User.FindFirst(AltOidClaimType)?.Value
                         ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(userOidStr, out var userOid))
        {
            _logger.LogWarning(
                "Dataverse authorization denied: no/invalid oid claim (correlationId={CorrelationId})",
                httpContext.TraceIdentifier);
            return DataverseProblem(401, "Unauthorized", "User identity not found in authentication token",
                "DV_NO_USER_IDENTITY", httpContext);
        }

        var tenantId = httpContext.User.FindFirst(TenantIdClaimType)?.Value;

        // Step 2: Resolve entity (or entities) to check.
        IReadOnlyList<string> entities;
        try
        {
            entities = ResolveEntities(context);
        }
        catch (FetchXmlParseException ex)
        {
            _logger.LogWarning(
                "Dataverse authorization denied: malformed FetchXML (correlationId={CorrelationId}, reason={Reason})",
                httpContext.TraceIdentifier, ex.Message);
            return DataverseProblem(400, "Bad Request", "FetchXML payload could not be parsed",
                "DV_FETCHXML_MALFORMED", httpContext);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                "Dataverse authorization denied: cannot resolve target entity (correlationId={CorrelationId}, reason={Reason})",
                httpContext.TraceIdentifier, ex.Message);
            return DataverseProblem(400, "Bad Request", ex.Message, "DV_NO_TARGET_ENTITY", httpContext);
        }

        if (entities.Count == 0 || entities.Any(string.IsNullOrWhiteSpace))
        {
            return DataverseProblem(400, "Bad Request", "Target entity not resolvable from request",
                "DV_NO_TARGET_ENTITY", httpContext);
        }

        // Step 3: Privilege check.
        // For a single entity we call HasReadPrivilegeAsync directly. For multi-entity (FetchXML) we
        // hydrate the readable-entity set once via GetReadableEntitiesAsync and check set membership
        // in-process — this yields a single Dataverse call regardless of FetchXML breadth (FR-BFF-04 <500ms p50).
        var distinct = entities.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        List<string> denied;
        if (distinct.Count == 1)
        {
            var allowed = await _privilegeChecker.HasReadPrivilegeAsync(userOid, distinct[0], ct);
            denied = allowed ? new List<string>() : new List<string> { distinct[0] };
        }
        else
        {
            var readable = await _privilegeChecker.GetReadableEntitiesAsync(userOid, ct);
            denied = distinct.Where(e => !readable.Contains(e)).ToList();
        }

        if (denied.Count > 0)
        {
            _logger.LogWarning(
                "Dataverse authorization denied: user={UserOid}, tenant={TenantId}, deniedEntities={DeniedEntities}, entitySource={EntitySource}, correlationId={CorrelationId}",
                userOid, tenantId, denied, _options.EntitySource, httpContext.TraceIdentifier);

            // Per design §4 Step 3: include only the FIRST denied entity in the detail to avoid
            // information disclosure about other entities referenced in the query.
            return DataverseProblem(
                403,
                "Forbidden",
                $"Read privilege denied on entity '{denied[0]}'",
                "DV_PRIVILEGE_DENIED",
                httpContext);
        }

        // Step 4: Log success + delegate.
        _logger.LogInformation(
            "Dataverse authorization granted: user={UserOid}, tenant={TenantId}, entities={Entities}, entitySource={EntitySource}, correlationId={CorrelationId}",
            userOid, tenantId, distinct, _options.EntitySource, httpContext.TraceIdentifier);

        return await next(context);
    }

    /// <summary>
    /// Resolves the set of entities to privilege-check based on the configured <see cref="EntitySource"/>.
    /// </summary>
    private IReadOnlyList<string> ResolveEntities(EndpointFilterInvocationContext context)
    {
        var routeValues = context.HttpContext.Request.RouteValues;

        switch (_options.EntitySource)
        {
            case EntitySource.FromRouteValue:
            case EntitySource.FromRouteValueWithRecord:
                {
                    var entityLogicalName = routeValues[_options.RouteKey]?.ToString();
                    if (string.IsNullOrWhiteSpace(entityLogicalName))
                    {
                        throw new InvalidOperationException(
                            $"Route value '{_options.RouteKey}' is missing or empty");
                    }
                    return new[] { entityLogicalName.Trim().ToLowerInvariant() };
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

            case EntitySource.FromFetchXmlBody:
                {
                    if (_fetchXmlExtractor is null)
                    {
                        throw new InvalidOperationException(
                            "EntitySource.FromFetchXmlBody requires IFetchXmlEntityExtractor to be DI-registered");
                    }

                    var fetchXml = ExtractFetchXmlFromArguments(context);
                    if (string.IsNullOrWhiteSpace(fetchXml))
                    {
                        throw new InvalidOperationException("FetchXML payload not found in request");
                    }

                    var extracted = _fetchXmlExtractor.ExtractEntities(fetchXml);
                    return extracted.ToList();
                }

            default:
                throw new InvalidOperationException(
                    $"Unsupported EntitySource: {_options.EntitySource}");
        }
    }

    /// <summary>
    /// Finds a string-typed FetchXML payload in the endpoint arguments. Mirrors the
    /// <c>ExtractSearchRequest</c> pattern in <c>SemanticSearchAuthorizationFilter</c>.
    /// </summary>
    private static string? ExtractFetchXmlFromArguments(EndpointFilterInvocationContext context)
    {
        // Task 013 will define the actual request DTO shape. For now the filter looks for either
        // a property named FetchXml on any argument (via reflection-free duck check on common shapes)
        // or a raw string argument. Task 013 may refine this once the FetchRequest record exists.
        foreach (var arg in context.Arguments)
        {
            if (arg is null) continue;

            if (arg is string s)
            {
                return s;
            }

            // Common shape: record / class with a FetchXml property.
            var prop = arg.GetType().GetProperty("FetchXml");
            if (prop is not null && prop.PropertyType == typeof(string))
            {
                var value = prop.GetValue(arg) as string;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
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
    /// <param name="routeKey">Route key for <see cref="EntitySource.FromRouteValue"/> / <c>FromRouteValueWithRecord</c> (default: <c>entityLogicalName</c>).</param>
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
            var extractor = sp.GetService<IFetchXmlEntityExtractor>(); // Optional — only used by FromFetchXmlBody.
            var logger = sp.GetRequiredService<ILogger<DataverseAuthorizationFilter>>();

            var filter = new DataverseAuthorizationFilter(checker, extractor, logger, options);
            return await filter.InvokeAsync(context, next);
        });
    }
}
