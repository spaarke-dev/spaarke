using Sprk.Bff.Api.Models.SpeAdmin;
using Sprk.Bff.Api.Services.SpeAdmin;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension for applying <see cref="SpeAdminTenantScopeFilter"/> to a route group.
/// </summary>
public static class SpeAdminTenantScopeFilterExtensions
{
    /// <summary>
    /// Confines every endpoint on the group to container type configs inside the caller's business
    /// unit. Apply AFTER <c>AddSpeAdminAuthorizationFilter()</c> — that one decides whether the caller
    /// is an admin at all; this one decides which customers' data that admin may touch.
    /// </summary>
    public static TBuilder AddSpeAdminTenantScopeFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;
            var filter = new SpeAdminTenantScopeFilter(
                services.GetRequiredService<SpeAdminTenantScope>(),
                services.GetService<ILogger<SpeAdminTenantScopeFilter>>());

            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Rejects any SPE Admin request whose <c>configId</c> belongs to a business unit the caller cannot
/// reach.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a filter and not a per-endpoint check.</b> Fifteen endpoint files accept <c>configId</c>.
/// A check written into each is a check that will be missed on the sixteenth — and the failure mode
/// is silent cross-customer disclosure, which no test would notice unless it was written to look for
/// it. One filter on the group cannot be forgotten. ADR-008: authorization belongs in endpoint
/// filters, never global middleware.
/// </para>
/// <para>
/// <b>Where the configId comes from</b> (unified-access-control-r2 task 165). Three places, all read:
/// the query key <c>configId</c>, the route value <c>configId</c>, and a bound request body that
/// implements <see cref="ISpeAdminConfigScopedRequest"/>. Before task 165 the filter read only the
/// first two, and only the route value when the query was empty, so <c>/configs/{id}</c> (route value
/// named <c>id</c>) and the bulk routes (configId in the JSON body) were never confined at all.
/// Endpoint filters run after parameter binding, so the bound body is already in
/// <see cref="EndpointFilterInvocationContext.Arguments"/>; nothing re-reads the request stream.
/// </para>
/// <para>
/// <b>Disagreeing values are refused, 400, before any read.</b> Authorizing one slot while the handler
/// acts on another would turn this filter into an id-substitution primitive: authorize
/// <c>?configId=MINE</c>, act on route/body <c>THEIRS</c>. A slot counts as present when its raw value is
/// non-empty, parseable or not.
/// </para>
/// <para>
/// <b>404, not 403, and the same 404 for "does not exist".</b> "That config exists, but is not yours"
/// confirms another customer exists and leaks a valid identifier. Before task 165 an unknown config
/// passed this filter and the endpoint 404'd in a different shape — itself an oracle. Both now get
/// <see cref="ConfigNotFound"/>, which the config endpoints also use for a config deleted between this
/// filter and the handler.
/// </para>
/// <para>
/// <b>Fails closed: 503.</b> When the boundary cannot be evaluated (a Dataverse read failed) the request
/// is refused, never passed through.
/// </para>
/// <para>
/// <b>Requests with no <c>configId</c> pass through.</b> They are either list endpoints, which apply
/// the same scope to their own query (see <c>ConfigEndpoints</c>), or endpoints that touch no
/// customer-scoped resource. A single present-but-unparseable value also passes through, so the
/// endpoint's own validation returns its 400.
/// </para>
/// </remarks>
public class SpeAdminTenantScopeFilter : IEndpointFilter
{
    /// <summary>Deny code, following <c>{domain}.{area}.{action}.{reason}</c>.</summary>
    internal const string DenyCode = "spe.admin.deny.config_out_of_scope";

    /// <summary>Two configId sources disagree.</summary>
    internal const string AmbiguousCode = "spe.admin.deny.config_id_ambiguous";

    /// <summary>The boundary could not be evaluated.</summary>
    internal const string UnverifiableCode = "spe.admin.deny.scope_unverifiable";

    private const string ConfigIdKey = "configId";

    private readonly SpeAdminTenantScope _tenantScope;
    private readonly ILogger<SpeAdminTenantScopeFilter>? _logger;

    public SpeAdminTenantScopeFilter(
        SpeAdminTenantScope tenantScope,
        ILogger<SpeAdminTenantScopeFilter>? logger = null)
    {
        _tenantScope = tenantScope ?? throw new ArgumentNullException(nameof(tenantScope));
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var present = ReadPresentConfigIds(context);

        if (present.Count == 0)
        {
            return await next(context);
        }

        Guid configId;
        if (present.Count == 1)
        {
            if (!Guid.TryParse(present[0], out configId) || configId == Guid.Empty)
            {
                // One unparseable value: the endpoint's own validation answers 400.
                return await next(context);
            }
        }
        else
        {
            var parsed = present
                .Select(raw => Guid.TryParse(raw, out var g) && g != Guid.Empty ? g : (Guid?)null)
                .ToList();

            if (parsed.Any(g => g is null) || parsed.Distinct().Count() != 1)
            {
                _logger?.LogWarning(
                    "SPE Admin tenant scope REFUSED: the request names {Count} configId values that do not agree. " +
                    "Path={Path} TraceId={TraceId}",
                    present.Count, http.Request.Path, http.TraceIdentifier);

                return Refusal(
                    StatusCodes.Status400BadRequest,
                    "Bad Request",
                    "The request names more than one configId (query string, route, body) and they do not agree.",
                    AmbiguousCode,
                    http.TraceIdentifier);
            }

            configId = parsed[0]!.Value;
        }

        var decision = await _tenantScope.DecideConfigAccessAsync(http.User, configId, http.RequestAborted);

        switch (decision)
        {
            case SpeAdminScopeDecision.Permitted:
                return await next(context);

            case SpeAdminScopeDecision.Unverifiable:
                _logger?.LogError(
                    "SPE Admin tenant scope UNVERIFIABLE for config {ConfigId}; refusing. Path={Path} TraceId={TraceId}",
                    configId, http.Request.Path, http.TraceIdentifier);

                return ScopeUnverifiable(http.TraceIdentifier);

            default:
                _logger?.LogWarning(
                    "SPE Admin tenant scope DENIED: config {ConfigId} does not exist or is outside the caller's " +
                    "business units. Path={Path} TraceId={TraceId}",
                    configId, http.Request.Path, http.TraceIdentifier);

                return ConfigNotFound(configId, http.TraceIdentifier);
        }
    }

    /// <summary>
    /// THE "config not found" answer for every SPE admin route: the filter's out-of-scope and
    /// does-not-exist denials, and the config endpoints' own not-found paths. One helper so the answers
    /// are byte-identical apart from the trace id — any difference would tell a caller which case it hit.
    /// </summary>
    public static IResult ConfigNotFound(Guid configId, string traceId) =>
        Refusal(
            StatusCodes.Status404NotFound,
            "Not Found",
            $"Container type config '{configId}' was not found.",
            DenyCode,
            traceId);

    /// <summary>
    /// THE "the boundary could not be evaluated" answer (503). Shared with the config write checks so a
    /// Dataverse fault refuses in one shape wherever it is met.
    /// </summary>
    public static IResult ScopeUnverifiable(string traceId) =>
        Refusal(
            StatusCodes.Status503ServiceUnavailable,
            "Service Unavailable",
            "The configuration's access scope could not be verified. Try again shortly.",
            UnverifiableCode,
            traceId);

    private static IResult Refusal(int status, string title, string detail, string errorCode, string traceId) =>
        TypedResults.Problem(
            detail: detail,
            statusCode: status,
            title: title,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["traceId"] = traceId
            });

    /// <summary>
    /// Every non-empty configId the request carries: each query value, the route value, and the
    /// <see cref="ISpeAdminConfigScopedRequest.ConfigId"/> of each bound body argument.
    /// </summary>
    private static List<string> ReadPresentConfigIds(EndpointFilterInvocationContext context)
    {
        var http = context.HttpContext;
        var present = new List<string>();

        foreach (var value in http.Request.Query[ConfigIdKey])
        {
            if (!string.IsNullOrEmpty(value)) present.Add(value);
        }

        if (http.Request.RouteValues.TryGetValue(ConfigIdKey, out var routeValue)
            && routeValue?.ToString() is { Length: > 0 } routeRaw)
        {
            present.Add(routeRaw);
        }

        foreach (var argument in context.Arguments)
        {
            if (argument is ISpeAdminConfigScopedRequest { ConfigId: { Length: > 0 } bodyRaw })
            {
                present.Add(bodyRaw);
            }
        }

        return present;
    }
}
