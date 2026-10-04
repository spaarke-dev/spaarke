using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Models.SpeAdmin;
using Sprk.Bff.Api.Services.SpeAdmin;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// What an SPE environment route does to <c>sprk_speenvironment</c>. Carried as endpoint metadata
/// (<see cref="SpeAdminTenantScopeFilterExtensions.WithSpeAdminEnvironmentScope{TBuilder}"/>) and decided by
/// <see cref="SpeAdminTenantScopeFilter"/> (unified-access-control-r2 task 165, round 16 item 4; folded into
/// the existing filter by round 20 item 4).
/// </summary>
public enum SpeAdminEnvironmentOperation
{
    /// <summary>Reads one environment named by the route value <c>id</c>.</summary>
    Read,

    /// <summary>Creates, changes or deletes an environment.</summary>
    Write
}

/// <summary>
/// Extensions for applying <see cref="SpeAdminTenantScopeFilter"/> to a route group, and for marking an
/// environment route so that filter applies the environment rule to it.
/// </summary>
public static class SpeAdminTenantScopeFilterExtensions
{
    /// <summary>
    /// Marks an <c>/api/spe/environments</c> route with what it does to an environment. The group's
    /// <see cref="SpeAdminTenantScopeFilter"/> reads the mark and applies the environment rule (writes: platform
    /// operator only; by-id reads: an environment a reachable config links). A route on the group without the
    /// mark gets only the configId rule — so every environment route MUST carry it (the
    /// <c>SpeAdminEnvironmentScopeTests</c> drive each one through the real pipeline).
    /// </summary>
    public static TBuilder WithSpeAdminEnvironmentScope<TBuilder>(
        this TBuilder builder,
        SpeAdminEnvironmentOperation operation) where TBuilder : IEndpointConventionBuilder
    {
        return builder.WithMetadata(operation);
    }

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
/// <para>
/// <b>Environment routes</b> (unified-access-control-r2 task 165, round 16 item 4; folded into this filter by
/// round 20 item 4 — one SPE-admin scope filter, no second class). An environment is not a config and has no
/// business-unit column: it is shared tenant infrastructure (in Model 1 one environment serves every customer).
/// A route marked with <see cref="SpeAdminEnvironmentOperation"/> metadata gets the environment rule BEFORE the
/// configId rule:
/// <b>Write</b> (POST, PUT, DELETE) is for a platform operator only — an admin whose OWN business unit is the
/// root; anyone else gets ONE 403 whatever id they name, decided before any environment is read.
/// <b>Read</b> by id is for an environment linked by a config the caller can reach (a platform operator reads
/// all); any other id — unreadable or nonexistent — gets ONE 404 (<see cref="EnvironmentNotFound"/>), the same
/// answer the handlers give for an id that does not exist. The list route trims its own result (the
/// <c>ListConfigsAsync</c> list precedent) and carries no mark. An unreadable reach is 503
/// (<see cref="ScopeUnverifiable"/>); a Read mark on a route with no <c>id</c> value is a mis-wiring and is
/// refused with the same 503.
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

    /// <summary>An environment the caller cannot read, or that does not exist: one 404.</summary>
    internal const string EnvironmentNotFoundCode = "spe.admin.deny.environment_out_of_scope";

    /// <summary>An environment write by an admin who is not a platform operator.</summary>
    internal const string EnvironmentWriteDeniedCode = "spe.admin.deny.environment_write_requires_platform_operator";

    private const string ConfigIdKey = "configId";
    private const string EnvironmentRouteIdKey = "id";

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

        if (EnvironmentOperationOf(http) is { } environmentOperation)
        {
            var environmentRefusal = await DecideEnvironmentAsync(http, environmentOperation);
            if (environmentRefusal is not null)
            {
                return environmentRefusal;
            }
        }

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

    /// <summary>
    /// THE "environment not found" answer: the environment rule's unreadable/nonexistent denial and the
    /// environment handlers' own not-found paths. One helper so they are byte-identical apart from the trace id.
    /// </summary>
    public static IResult EnvironmentNotFound(Guid environmentId, string traceId) =>
        Refusal(
            StatusCodes.Status404NotFound,
            "Not Found",
            $"SPE environment '{environmentId}' was not found.",
            EnvironmentNotFoundCode,
            traceId);

    /// <summary>
    /// The environment operation a route is marked with, or null when it is not an environment route. A route
    /// carrying both marks is treated as a Write (the stricter rule).
    /// </summary>
    private static SpeAdminEnvironmentOperation? EnvironmentOperationOf(HttpContext http)
    {
        var marks = http.GetEndpoint()?.Metadata.OfType<SpeAdminEnvironmentOperation>().ToList();
        if (marks is null || marks.Count == 0)
        {
            return null;
        }

        return marks.Contains(SpeAdminEnvironmentOperation.Write)
            ? SpeAdminEnvironmentOperation.Write
            : SpeAdminEnvironmentOperation.Read;
    }

    /// <summary>
    /// The environment rule (round 16 item 4): the refusal, or null when the request may continue to the
    /// configId rule.
    /// </summary>
    private async Task<IResult?> DecideEnvironmentAsync(HttpContext http, SpeAdminEnvironmentOperation operation)
    {
        SpeAdminEnvironmentReach reach;
        try
        {
            reach = await _tenantScope.GetEnvironmentReachAsync(http.User, http.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex,
                "SPE Admin environment scope UNVERIFIABLE; refusing. Path={Path} TraceId={TraceId}",
                http.Request.Path, http.TraceIdentifier);
            return ScopeUnverifiable(http.TraceIdentifier);
        }

        if (operation == SpeAdminEnvironmentOperation.Write)
        {
            if (reach.CanWrite)
            {
                return null;
            }

            _logger?.LogWarning(
                "SPE Admin environment write DENIED: the caller's business unit is not the root. Path={Path} TraceId={TraceId}",
                http.Request.Path, http.TraceIdentifier);

            return ProblemDetailsHelper.Forbidden(
                EnvironmentWriteDeniedCode,
                "Only a platform operator (an administrator in the root business unit) may create, change or delete SPE environments.",
                http.TraceIdentifier);
        }

        // Read by id. The route constraint is {id:guid}, so an unparseable value never reaches here; a
        // missing one is a mis-wiring and is refused, never passed through.
        if (!http.Request.RouteValues.TryGetValue(EnvironmentRouteIdKey, out var raw)
            || !Guid.TryParse(raw?.ToString(), out var environmentId))
        {
            _logger?.LogError(
                "SPE Admin environment scope: Read mark on a route with no {{id}} value; refusing. Path={Path}",
                http.Request.Path);
            return ScopeUnverifiable(http.TraceIdentifier);
        }

        if (reach.CanRead(environmentId))
        {
            return null;
        }

        _logger?.LogWarning(
            "SPE Admin environment scope DENIED: environment {EnvironmentId} is not linked by a config the caller reaches " +
            "(or does not exist). Path={Path} TraceId={TraceId}",
            environmentId, http.Request.Path, http.TraceIdentifier);

        return EnvironmentNotFound(environmentId, http.TraceIdentifier);
    }

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
