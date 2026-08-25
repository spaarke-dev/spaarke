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
/// <b>404, not 403.</b> "That config exists, but is not yours" confirms another customer exists and
/// leaks a valid identifier. Absence is the safer answer, and it matches the shape endpoints already
/// return for a config that genuinely does not exist.
/// </para>
/// <para>
/// <b>Requests with no <c>configId</c> pass through.</b> They are either list endpoints, which apply
/// the same scope to their own query (see <c>ConfigEndpoints</c>), or endpoints that touch no
/// customer-scoped resource. This filter deliberately does not invent a scope for a request that
/// names no config.
/// </para>
/// </remarks>
public class SpeAdminTenantScopeFilter : IEndpointFilter
{
    /// <summary>Deny code, following <c>{domain}.{area}.{action}.{reason}</c>.</summary>
    private const string DenyCode = "spe.admin.deny.config_out_of_scope";

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

        if (!TryReadConfigId(http, out var configId))
        {
            return await next(context);
        }

        var permitted = await _tenantScope.CanAccessConfigAsync(
            http.User, configId, http.RequestAborted);

        if (!permitted)
        {
            _logger?.LogWarning(
                "SPE Admin tenant scope DENIED: config {ConfigId} is outside the caller's business units. " +
                "Path={Path} TraceId={TraceId}",
                configId, http.Request.Path, http.TraceIdentifier);

            return TypedResults.Problem(
                detail: $"Container type config '{configId}' was not found.",
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = DenyCode,
                    ["traceId"] = http.TraceIdentifier
                });
        }

        return await next(context);
    }

    /// <summary>
    /// Reads <c>configId</c> from the query string, then the route values.
    /// </summary>
    /// <remarks>
    /// Every SPE Admin endpoint passes it as <c>?configId=</c> today; route values are checked too so
    /// that a future endpoint using <c>/{configId}</c> is covered without anyone having to remember
    /// to update this filter. A value that is present but unparseable is left alone — the endpoint's
    /// own validation returns the 400, and rejecting here would give a misleading 404.
    /// </remarks>
    private static bool TryReadConfigId(HttpContext http, out Guid configId)
    {
        configId = Guid.Empty;

        var raw = http.Request.Query["configId"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(raw) &&
            http.Request.RouteValues.TryGetValue("configId", out var routeValue))
        {
            raw = routeValue?.ToString();
        }

        return !string.IsNullOrWhiteSpace(raw)
            && Guid.TryParse(raw, out configId)
            && configId != Guid.Empty;
    }
}
