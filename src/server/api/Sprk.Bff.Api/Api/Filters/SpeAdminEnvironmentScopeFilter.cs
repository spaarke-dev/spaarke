using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.SpeAdmin;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>What an SPE environment route does to <c>sprk_speenvironment</c>.</summary>
public enum SpeAdminEnvironmentOperation
{
    /// <summary>Reads one environment named by the route value <c>id</c>.</summary>
    Read,

    /// <summary>Creates, changes or deletes an environment.</summary>
    Write
}

/// <summary>
/// Extension for applying <see cref="SpeAdminEnvironmentScopeFilter"/> to one environment route.
/// </summary>
public static class SpeAdminEnvironmentScopeFilterExtensions
{
    /// <summary>
    /// Confines the route to what the caller may do with SPE environments. Runs after the <c>/api/spe</c>
    /// group's role and tenant-scope filters (group filters run first).
    /// </summary>
    public static TBuilder AddSpeAdminEnvironmentScopeFilter<TBuilder>(
        this TBuilder builder,
        SpeAdminEnvironmentOperation operation) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;
            var filter = new SpeAdminEnvironmentScopeFilter(
                services.GetRequiredService<SpeAdminTenantScope>(),
                operation,
                services.GetService<ILogger<SpeAdminEnvironmentScopeFilter>>());

            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// The business-unit boundary for <c>/api/spe/environments</c> (unified-access-control-r2 task 165, round 16
/// item 4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why environments need their own rule.</b> <c>SpeAdminTenantScopeFilter</c> confines a <c>configId</c>;
/// an environment is not a config and has no business-unit column. It is shared tenant infrastructure (in
/// Model 1 one environment serves every customer). Before this filter any SPE admin, in any business unit,
/// could read, edit and delete every environment — including repointing the tenant id and root site URL that
/// every other customer's configs resolve through.
/// </para>
/// <para>
/// <b>Writes</b> (POST, PUT, DELETE) are for a platform operator only: an admin whose OWN business unit is the
/// root. Anyone else gets ONE 403 whatever id they name, decided before any environment is read, so the answer
/// says nothing about which environments exist.
/// </para>
/// <para>
/// <b>Reads</b> by id are for an environment linked by a config the caller can reach (a platform operator
/// reads all). Any other id — unreadable or nonexistent — gets ONE 404 (<see cref="EnvironmentNotFound"/>),
/// the same answer the handlers give for an id that does not exist. The list route trims its own result
/// (the <c>ListConfigsAsync</c> list precedent) and carries no filter.
/// </para>
/// <para>
/// <b>Fails closed.</b> When the reach cannot be read the answer is 503
/// (<see cref="SpeAdminTenantScopeFilter.ScopeUnverifiable"/>), never a pass-through.
/// </para>
/// </remarks>
public sealed class SpeAdminEnvironmentScopeFilter : IEndpointFilter
{
    /// <summary>An environment the caller cannot read, or that does not exist: one 404.</summary>
    internal const string NotFoundCode = "spe.admin.deny.environment_out_of_scope";

    /// <summary>An environment write by an admin who is not a platform operator.</summary>
    internal const string WriteDeniedCode = "spe.admin.deny.environment_write_requires_platform_operator";

    private const string RouteIdKey = "id";

    private readonly SpeAdminTenantScope _tenantScope;
    private readonly SpeAdminEnvironmentOperation _operation;
    private readonly ILogger<SpeAdminEnvironmentScopeFilter>? _logger;

    public SpeAdminEnvironmentScopeFilter(
        SpeAdminTenantScope tenantScope,
        SpeAdminEnvironmentOperation operation,
        ILogger<SpeAdminEnvironmentScopeFilter>? logger = null)
    {
        _tenantScope = tenantScope ?? throw new ArgumentNullException(nameof(tenantScope));
        _operation = operation;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

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
            return SpeAdminTenantScopeFilter.ScopeUnverifiable(http.TraceIdentifier);
        }

        if (_operation == SpeAdminEnvironmentOperation.Write)
        {
            if (reach.CanWrite)
            {
                return await next(context);
            }

            _logger?.LogWarning(
                "SPE Admin environment write DENIED: the caller's business unit is not the root. Path={Path} TraceId={TraceId}",
                http.Request.Path, http.TraceIdentifier);

            return ProblemDetailsHelper.Forbidden(
                WriteDeniedCode,
                "Only a platform operator (an administrator in the root business unit) may create, change or delete SPE environments.",
                http.TraceIdentifier);
        }

        // Read by id. The route constraint is {id:guid}, so an unparseable value never reaches here; a
        // missing one is a mis-wiring and is refused, never passed through.
        if (!http.Request.RouteValues.TryGetValue(RouteIdKey, out var raw)
            || !Guid.TryParse(raw?.ToString(), out var environmentId))
        {
            _logger?.LogError(
                "SPE Admin environment scope: Read filter on a route with no {{id}} value; refusing. Path={Path}",
                http.Request.Path);
            return SpeAdminTenantScopeFilter.ScopeUnverifiable(http.TraceIdentifier);
        }

        if (reach.CanRead(environmentId))
        {
            return await next(context);
        }

        _logger?.LogWarning(
            "SPE Admin environment scope DENIED: environment {EnvironmentId} is not linked by a config the caller reaches " +
            "(or does not exist). Path={Path} TraceId={TraceId}",
            environmentId, http.Request.Path, http.TraceIdentifier);

        return EnvironmentNotFound(environmentId, http.TraceIdentifier);
    }

    /// <summary>
    /// THE "environment not found" answer: the filter's unreadable/nonexistent denial and the environment
    /// handlers' own not-found paths. One helper so they are byte-identical apart from the trace id.
    /// </summary>
    public static IResult EnvironmentNotFound(Guid environmentId, string traceId) =>
        TypedResults.Problem(
            detail: $"SPE environment '{environmentId}' was not found.",
            statusCode: StatusCodes.Status404NotFound,
            title: "Not Found",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = NotFoundCode,
                ["traceId"] = traceId
            });
}
