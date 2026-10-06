using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
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
/// Where the container a <c>{containerId}</c> route names lives — read by <see cref="SpeAdminTenantScopeFilter"/> to
/// read its business-unit binding from the right place (unified-access-control-r2 task 165, owner round 20 item 2).
/// Default (no mark): <see cref="Active"/>.
/// </summary>
public enum SpeAdminContainerLocation
{
    /// <summary>An active container: <c>/storage/fileStorage/containers/{id}</c>.</summary>
    Active,

    /// <summary>A soft-deleted container in the recycle bin: <c>/storage/fileStorage/deletedContainers/{id}</c>.</summary>
    RecycleBin
}

/// <summary>
/// Endpoint metadata: the route is an APP-ONLY container-type route (type permissions, consuming-app registrations,
/// register) acting on the container type its <c>{typeId}</c> names — read by <see cref="SpeAdminTenantScopeFilter"/>
/// (owner round 20 item 3's consequence; round 35 item 5). Container-type routes that act with the CALLER's own
/// delegated token carry no mark: Graph decides those.
/// </summary>
/// <remarks>
/// There is no read/write split any more (round 35 item 5): the reads list every customer's consuming app and
/// registrations — the per-customer values round 20 item 3 keeps exclusive — so a read needs what a write needs, the
/// caller reaching EVERY config that carries the type. Owner round 49 item 1: these routes are moreover for a root-unit
/// admin of a Spaarke-operated environment only (<see cref="SpeAdminOptions.PlatformOperatorEnvironment"/>), checked
/// first — the type rule then still requires the type to be the config's own and every config of it reachable.
/// </remarks>
public sealed class SpeAdminContainerTypeScope
{
    /// <summary>The one instance routes carry.</summary>
    public static SpeAdminContainerTypeScope Instance { get; } = new();

    private SpeAdminContainerTypeScope()
    {
    }
}

/// <summary>
/// Endpoint metadata: ONLY a platform operator of a SPAARKE-OPERATED environment — a deployment carrying
/// <see cref="SpeAdminOptions.PlatformOperatorEnvironment"/>, and an SPE admin whose OWN business unit is the root — may
/// call the route (owner round 35 item 4, tightened by round 49 item 1: the security-alerts and secure-score routes read
/// the whole Microsoft 365 tenant through any config's app, and under Model 1 every customer environment has a root
/// admin). Decided by <see cref="SpeAdminTenantScopeFilter"/> before anything is read.
/// </summary>
public sealed class SpeAdminPlatformOperatorOnly
{
    /// <summary>The one instance routes carry.</summary>
    public static SpeAdminPlatformOperatorOnly Instance { get; } = new();

    private SpeAdminPlatformOperatorOnly()
    {
    }
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
    /// Marks a <c>{containerId}</c> route that acts on a container in the RECYCLE BIN, so the group's
    /// <see cref="SpeAdminTenantScopeFilter"/> reads the container's business-unit binding from
    /// <c>deletedContainers</c>. Every <c>{containerId}</c> route is judged per container; an unmarked one reads the
    /// active container (a recycle-bin route left unmarked therefore answers the uniform 404 — it fails closed).
    /// </summary>
    public static TBuilder WithSpeAdminContainerLocation<TBuilder>(
        this TBuilder builder,
        SpeAdminContainerLocation location) where TBuilder : IEndpointConventionBuilder
    {
        return builder.WithMetadata(location);
    }

    /// <summary>
    /// Marks an APP-ONLY <c>/containertypes/{typeId}/...</c> route, so the group's <see cref="SpeAdminTenantScopeFilter"/>
    /// applies the container-type rule: the type must be the config's own, and the caller must reach EVERY config that
    /// carries the type — reads included (round 35 item 5). <c>SpeAdminContainerBindingGuardTests</c> fails the build
    /// when an app-only type route lacks the mark.
    /// </summary>
    public static TBuilder WithSpeAdminContainerTypeScope<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.WithMetadata(SpeAdminContainerTypeScope.Instance);
    }

    /// <summary>
    /// Marks a route (or a group) as platform-operator-only (round 35 item 4; round 49 item 1): the group's
    /// <see cref="SpeAdminTenantScopeFilter"/> refuses, with ONE 403 before any other read, every caller unless this
    /// deployment is a Spaarke-operated environment and the caller's own business unit is the root.
    /// </summary>
    public static TBuilder RequireSpeAdminPlatformOperator<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.WithMetadata(SpeAdminPlatformOperatorOnly.Instance);
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
                services.GetService<ILogger<SpeAdminTenantScopeFilter>>(),
                platformOperatorEnvironment: services.GetRequiredService<IOptions<SpeAdminOptions>>().Value.PlatformOperatorEnvironment);

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
/// <para>
/// <b>Tenant-wide and type-wide routes</b> (round 35 items 4 + 5; owner round 49 item 1): a route marked
/// <see cref="SpeAdminPlatformOperatorOnly"/> (the security alerts and secure score) or <see cref="SpeAdminContainerTypeScope"/>
/// (the app-only container-type permission, consumer and register routes) answers for the whole SharePoint Embedded
/// tenant or a whole — possibly shared — container type. It is refused with ONE 403, FIRST, unless this deployment is a
/// Spaarke-operated environment (<see cref="SpeAdminOptions.PlatformOperatorEnvironment"/>) AND the caller's own business
/// unit is the root — under Model 1 every customer environment has a root admin, so the root check alone cannot confine them.
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

    /// <summary>A container the caller may not act on, or that does not exist: one 404 (owner round 20 item 2).</summary>
    internal const string ContainerNotFoundCode = "spe.admin.deny.container_out_of_scope";

    /// <summary>Two containerId sources (route value, body) disagree.</summary>
    internal const string ContainerAmbiguousCode = "spe.admin.deny.container_id_ambiguous";

    /// <summary>An app-only container-type route naming a type that is not the config's own: one 404.</summary>
    internal const string ContainerTypeNotFoundCode = "spe.admin.deny.container_type_out_of_scope";

    /// <summary>A container-type route (read or write) on a type another unreachable config also carries (shared, Model 1).</summary>
    internal const string ContainerTypeSharedCode = "spe.admin.deny.container_type_shared";

    /// <summary>A platform-operator-only route called by an admin whose own business unit is not the root (round 35 item 4).</summary>
    internal const string PlatformOperatorRequiredCode = "spe.admin.deny.platform_operator_required";

    private const string ConfigIdKey = "configId";
    private const string ContainerIdKey = "containerId";
    private const string TypeIdKey = "typeId";
    private const string EnvironmentRouteIdKey = "id";

    private readonly SpeAdminTenantScope _tenantScope;
    private readonly ILogger<SpeAdminTenantScopeFilter>? _logger;
    private readonly bool _platformOperatorEnvironment;

    /// <param name="tenantScope">The boundary.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="platformOperatorEnvironment">
    /// <see cref="SpeAdminOptions.PlatformOperatorEnvironment"/> — whether this deployment is a Spaarke-operated
    /// environment (owner round 49 item 1). Defaults to <c>false</c>: a filter built without it refuses the tenant-wide and
    /// type-wide routes (fail closed).
    /// </param>
    public SpeAdminTenantScopeFilter(
        SpeAdminTenantScope tenantScope,
        ILogger<SpeAdminTenantScopeFilter>? logger = null,
        bool platformOperatorEnvironment = false)
    {
        _tenantScope = tenantScope ?? throw new ArgumentNullException(nameof(tenantScope));
        _logger = logger;
        _platformOperatorEnvironment = platformOperatorEnvironment;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        var metadata = http.GetEndpoint()?.Metadata;
        if (metadata?.GetMetadata<SpeAdminPlatformOperatorOnly>() is not null
            || metadata?.GetMetadata<SpeAdminContainerTypeScope>() is not null)
        {
            // Owner round 49 item 1 (supersedes round 35 item 4's root-only check for these routes): a route whose answer
            // spans the whole SharePoint Embedded tenant (security alerts, secure score) or a whole container type (its
            // app permissions, consuming apps, registration) is for a ROOT-unit admin of a SPAARKE-OPERATED environment
            // only. Decided before anything else is read, so the answer is the same 403 whatever the request names (no
            // configId or type oracle).
            var operatorRefusal = await RequireSpaarkeOperatorAsync(http);
            if (operatorRefusal is not null)
            {
                return operatorRefusal;
            }
        }

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

        // Round 65 item 1: no secret-name rule here any more. SPE Admin authenticates as the BFF's own identity (master
        // bb8ba7251) and resolves no config credential, so round 35 item 3's 409 guarded nothing; it would only have
        // blocked a config such as Model 1's, whose stored name is the literal "null". The 400 on a supplied name stays.
        var decision = await _tenantScope.DecideConfigAccessAsync(http.User, configId, http.RequestAborted);

        switch (decision)
        {
            case SpeAdminScopeDecision.Permitted:
                break;

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

        var containerRefusal = await DecideContainerAsync(context, configId);
        if (containerRefusal is not null)
        {
            return containerRefusal;
        }

        var typeRefusal = await DecideContainerTypeAsync(http, configId);
        if (typeRefusal is not null)
        {
            return typeRefusal;
        }

        return await next(context);
    }

    /// <summary>
    /// The per-container rule (owner round 20 item 2): when the request names a container — the route value
    /// <c>containerId</c> or a bound body implementing <see cref="ISpeAdminContainerScopedRequest"/> — the container's
    /// business-unit binding decides, through the config already confined above. The refusal, or null to continue.
    /// </summary>
    /// <remarks>
    /// A route that names a container but NO usable configId never reaches here: the configId rule passes it to the
    /// handler, whose own 400 ("configId is required") answers before any Graph call — no handler can reach a container
    /// without a config's client.
    /// </remarks>
    private async Task<IResult?> DecideContainerAsync(EndpointFilterInvocationContext context, Guid configId)
    {
        var http = context.HttpContext;
        var present = ReadPresentContainerIds(context);
        if (present.Count == 0)
        {
            return null;
        }

        if (present.Distinct(StringComparer.Ordinal).Count() != 1)
        {
            _logger?.LogWarning(
                "SPE Admin tenant scope REFUSED: the request names {Count} containerId values that do not agree. " +
                "Path={Path} TraceId={TraceId}",
                present.Count, http.Request.Path, http.TraceIdentifier);

            return Refusal(
                StatusCodes.Status400BadRequest,
                "Bad Request",
                "The request names more than one containerId (route, body) and they do not agree.",
                ContainerAmbiguousCode,
                http.TraceIdentifier);
        }

        var containerId = present[0];
        var inRecycleBin = ContainerLocationOf(http) == SpeAdminContainerLocation.RecycleBin;
        var decision = await _tenantScope.DecideContainerAccessAsync(
            http.User, configId, containerId, inRecycleBin, http.RequestAborted);

        switch (decision)
        {
            case SpeAdminScopeDecision.Permitted:
                return null;

            case SpeAdminScopeDecision.Unverifiable:
                _logger?.LogError(
                    "SPE Admin container scope UNVERIFIABLE for container {ContainerId}; refusing. Path={Path} TraceId={TraceId}",
                    containerId, http.Request.Path, http.TraceIdentifier);
                return ScopeUnverifiable(http.TraceIdentifier);

            default:
                _logger?.LogWarning(
                    "SPE Admin container scope DENIED: container {ContainerId} does not exist, is not of config {ConfigId}'s " +
                    "type, or is bound outside the caller's business units. Path={Path} TraceId={TraceId}",
                    containerId, configId, http.Request.Path, http.TraceIdentifier);
                return inRecycleBin
                    ? RecycleBinContainerNotFound(containerId, http.TraceIdentifier)
                    : ContainerNotFound(containerId, http.TraceIdentifier);
        }
    }

    /// <summary>
    /// The container-type rule for a route marked <see cref="SpeAdminContainerTypeScope"/>: the route's <c>typeId</c> must
    /// be the config's own type, and every config carrying it must be reachable — reads and writes alike (round 35 item
    /// 5). The refusal, or null to continue.
    /// </summary>
    private async Task<IResult?> DecideContainerTypeAsync(HttpContext http, Guid configId)
    {
        if (http.GetEndpoint()?.Metadata.GetMetadata<SpeAdminContainerTypeScope>() is null)
        {
            return null;
        }

        var typeId = http.Request.RouteValues.TryGetValue(TypeIdKey, out var raw) ? raw?.ToString() : null;
        var decision = await _tenantScope.DecideContainerTypeAccessAsync(http.User, configId, typeId, http.RequestAborted);

        switch (decision)
        {
            case SpeAdminScopeDecision.Permitted:
                return null;

            case SpeAdminScopeDecision.Unverifiable:
                return ScopeUnverifiable(http.TraceIdentifier);

            case SpeAdminScopeDecision.ContainerTypeShared:
                return ProblemDetailsHelper.Forbidden(
                    ContainerTypeSharedCode,
                    "This container type is shared with a customer you do not administer. Only an administrator who " +
                    "reaches every configuration of the type may read or change its permissions and consuming apps.",
                    http.TraceIdentifier);

            default:
                return ContainerTypeNotFound(typeId ?? string.Empty, http.TraceIdentifier);
        }
    }

    /// <summary>The one detail of the Spaarke-operator refusal (owner round 49 item 1) — every refused caller reads the same words.</summary>
    internal const string SpaarkeOperatorRequiredDetail =
        "Only a platform operator of a Spaarke-operated environment (an administrator in its root business unit) may use " +
        "routes whose answer spans the whole SharePoint Embedded tenant or a whole container type.";

    /// <summary>
    /// THE check for the tenant-wide and type-wide routes (owner round 49 item 1): this deployment must be a Spaarke-operated
    /// environment (<see cref="SpeAdminOptions.PlatformOperatorEnvironment"/>, a deployment setting — read first, no I/O)
    /// AND the caller a root-unit admin (<see cref="RequirePlatformOperatorAsync"/>). Every other caller — a root admin of a
    /// customer environment, a leaf admin of an operator environment — gets the SAME 403. Null to continue.
    /// </summary>
    private async Task<IResult?> RequireSpaarkeOperatorAsync(HttpContext http)
    {
        if (!_platformOperatorEnvironment)
        {
            _logger?.LogWarning(
                "SPE Admin tenant-/type-wide route DENIED: this deployment is not a Spaarke-operated environment " +
                "(SpeAdmin:PlatformOperatorEnvironment is not true) ({DenyCode}). Path={Path} TraceId={TraceId}",
                PlatformOperatorRequiredCode, http.Request.Path, http.TraceIdentifier);
            return ProblemDetailsHelper.Forbidden(PlatformOperatorRequiredCode, SpaarkeOperatorRequiredDetail, http.TraceIdentifier);
        }

        return await RequirePlatformOperatorAsync(http, PlatformOperatorRequiredCode, SpaarkeOperatorRequiredDetail);
    }

    /// <summary>
    /// THE platform-operator check (round 16 item 4's environment write rule; round 49 item 1's tenant-/type-wide routes,
    /// after the deployment check): the caller's OWN business unit must be the root. Anyone else gets ONE 403 with
    /// <paramref name="denyCode"/>; an unreadable scope is the 503. Null to continue.
    /// </summary>
    private async Task<IResult?> RequirePlatformOperatorAsync(HttpContext http, string denyCode, string detail)
    {
        SpeAdminCallerScope scope;
        try
        {
            scope = await _tenantScope.GetCallerScopeAsync(http.User, http.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex,
                "SPE Admin platform-operator check UNVERIFIABLE; refusing. Path={Path} TraceId={TraceId}",
                http.Request.Path, http.TraceIdentifier);
            return ScopeUnverifiable(http.TraceIdentifier);
        }

        if (scope.IsPlatformOperator)
        {
            return null;
        }

        _logger?.LogWarning(
            "SPE Admin platform-operator route DENIED: the caller's business unit is not the root ({DenyCode}). " +
            "Path={Path} TraceId={TraceId}",
            denyCode, http.Request.Path, http.TraceIdentifier);

        return ProblemDetailsHelper.Forbidden(denyCode, detail, http.TraceIdentifier);
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
    /// THE "container not found" answer for every SPE admin route that names an ACTIVE container: the per-container
    /// rule's denial (absent, another type, bound outside the caller's reach) and the container handlers' own not-found
    /// paths. One helper so they are byte-identical apart from the trace id (owner round 20 item 2).
    /// </summary>
    public static IResult ContainerNotFound(string containerId, string traceId) =>
        Refusal(
            StatusCodes.Status404NotFound,
            "Not Found",
            $"Container '{containerId}' was not found.",
            ContainerNotFoundCode,
            traceId);

    /// <summary>
    /// THE "container not found" answer for the routes that act on a container in the RECYCLE BIN — the rule's denial
    /// and the recycle-bin handlers' own not-found paths.
    /// </summary>
    public static IResult RecycleBinContainerNotFound(string containerId, string traceId) =>
        Refusal(
            StatusCodes.Status404NotFound,
            "Not Found",
            $"Container '{containerId}' was not found in the recycle bin.",
            ContainerNotFoundCode,
            traceId);

    /// <summary>THE "container type not found" answer of the app-only container-type rule.</summary>
    public static IResult ContainerTypeNotFound(string typeId, string traceId) =>
        Refusal(
            StatusCodes.Status404NotFound,
            "Not Found",
            $"Container type '{typeId}' was not found.",
            ContainerTypeNotFoundCode,
            traceId);

    private static SpeAdminContainerLocation ContainerLocationOf(HttpContext http) =>
        http.GetEndpoint()?.Metadata.OfType<SpeAdminContainerLocation>().Contains(SpeAdminContainerLocation.RecycleBin) == true
            ? SpeAdminContainerLocation.RecycleBin
            : SpeAdminContainerLocation.Active;

    /// <summary>
    /// Every non-empty containerId the request carries: the route value and the
    /// <see cref="ISpeAdminContainerScopedRequest.ContainerId"/> of each bound body argument.
    /// </summary>
    private static List<string> ReadPresentContainerIds(EndpointFilterInvocationContext context)
    {
        var present = new List<string>();

        if (context.HttpContext.Request.RouteValues.TryGetValue(ContainerIdKey, out var routeValue)
            && routeValue?.ToString() is { Length: > 0 } routeRaw)
        {
            present.Add(routeRaw);
        }

        foreach (var argument in context.Arguments)
        {
            if (argument is ISpeAdminContainerScopedRequest { ContainerId: { } bodyRaw } && !string.IsNullOrWhiteSpace(bodyRaw))
            {
                present.Add(bodyRaw.Trim());
            }
        }

        return present;
    }

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
        if (operation == SpeAdminEnvironmentOperation.Write)
        {
            // Writes depend only on the caller's own unit and the hierarchy — THE platform-operator check, shared with
            // the security routes (round 35 item 4).
            return await RequirePlatformOperatorAsync(
                http,
                EnvironmentWriteDeniedCode,
                "Only a platform operator (an administrator in the root business unit) may create, change or delete SPE environments.");
        }

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
