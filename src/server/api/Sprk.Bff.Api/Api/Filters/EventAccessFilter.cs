using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using ApiCreateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.CreateEventRequest;
using ApiUpdateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.UpdateEventRequest;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>Extension methods for attaching <see cref="EventAccessFilter"/>.</summary>
public static class EventAccessFilterExtensions
{
    /// <summary>
    /// Authorize the CALLER against the <c>sprk_event</c> named by the route's <c>{id}</c>, for
    /// <paramref name="operation"/> (an <see cref="OperationAccessPolicy"/> key: <c>read</c> or <c>write</c>).
    /// </summary>
    public static TBuilder AddEventRecordAccessFilter<TBuilder>(this TBuilder builder, string operation)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;
            var filter = new EventAccessFilter(
                services.GetRequiredService<CallerRecordAccessProbe>(),
                services.GetService<ILogger<EventAccessFilter>>());
            return await filter.AuthorizeEventAsync(context, next, operation);
        });

    /// <summary>
    /// Authorize the CALLER's Read right on the regarding PARENT a create/update request names, BEFORE the server
    /// resolves it, reads its name/number, or stamps its ancestors (review: existence oracle + data copy).
    /// </summary>
    public static TBuilder AddEventParentAccessFilter<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;
            var filter = new EventAccessFilter(
                services.GetRequiredService<CallerRecordAccessProbe>(),
                services.GetService<ILogger<EventAccessFilter>>());
            return await filter.AuthorizeParentAsync(context, next);
        });
}

/// <summary>
/// spaarke-ontology-platform-r1 task 097 (security review) — per-record authorization for the
/// <c>/api/v1/events</c> routes, which used to carry only <c>RequireAuthorization()</c> and then act app-only on any
/// event id a caller supplied.
/// </summary>
/// <remarks>
/// <para><b>The established pattern, applied — not a new one.</b> This is the record-route gate the BFF already uses
/// for record routes: <see cref="RecordRouteAccessAuthorizationFilter"/> (record-keyed uploads),
/// <see cref="EntityAccessFilter"/> (Office save), <see cref="TodoSourceAccessFilter"/> and
/// <c>QuickCreateSourceAccessFilter</c> — an ADR-008 endpoint filter that asks Dataverse, AS THE CALLER, what rights
/// they hold on the record (<see cref="CallerRecordAccessProbe"/>, <c>RetrievePrincipalAccess</c>), and lets
/// <see cref="OperationAccessPolicy"/> decide which right the operation costs (<c>read</c> for GET / logs,
/// <c>write</c> for PUT / complete / cancel / DELETE — DELETE is a soft delete, i.e. a status WRITE). The
/// <c>sprk_events</c> collection comes from the shared <see cref="EntityAccessFilter.TryResolveEntitySet"/> table; a
/// regarding parent's from the one regarding catalogue in <see cref="RegardingRecordType"/>.</para>
///
/// <para><b>One deny body, 403, for every refusal</b> — exactly <see cref="TodoSourceAccessFilter"/>'s reasoning:
/// the probe collapses "no such record", "you may not see it" and "the check could not run" to
/// <see cref="AccessRights.None"/> (Dataverse hides existence under OBO), and a single constant body means no route
/// can be used as an existence oracle. 403 rather than 404 because one answer must cover both cases and 403
/// asserts nothing about existence.</para>
/// </remarks>
public sealed class EventAccessFilter
{
    internal const string ReadOperation = "read";
    internal const string WriteOperation = "write";

    /// <summary>The ONE reason code for every refusal (see remarks: a varying code would be an existence oracle).</summary>
    internal const string DeniedReasonCode = "event_access_denied";

    private const string DeniedDetail =
        "You do not have permission to do this to the event or to the record it refers to, or those records are not "
        + "available.";

    private readonly CallerRecordAccessProbe _probe;
    private readonly ILogger<EventAccessFilter>? _logger;

    public EventAccessFilter(CallerRecordAccessProbe probe, ILogger<EventAccessFilter>? logger = null)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _logger = logger;
    }

    internal async ValueTask<object?> AuthorizeEventAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next, string operation)
    {
        var httpContext = context.HttpContext;
        var eventId = context.Arguments.OfType<Guid>().FirstOrDefault();
        if (eventId == Guid.Empty || !EntityAccessFilter.TryResolveEntitySet("sprk_event", out var entitySet))
        {
            _logger?.LogWarning("[EVENT-AUTH] Denying: no event id on the route. CorrelationId: {CorrelationId}",
                httpContext.TraceIdentifier);
            return Deny(httpContext);
        }

        return await IsAllowedAsync(httpContext, entitySet, eventId, operation)
            ? await next(context)
            : Deny(httpContext);
    }

    internal async ValueTask<object?> AuthorizeParentAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var (type, id) = context.Arguments switch
        {
            var a when a.OfType<ApiCreateEventRequest>().FirstOrDefault() is { } c => (c.RegardingRecordType, c.RegardingRecordId),
            var a when a.OfType<ApiUpdateEventRequest>().FirstOrDefault() is { } u => (u.RegardingRecordType, u.RegardingRecordId),
            _ => ((int?)null, (Guid?)null),
        };

        // No parent named → nothing is read or stamped → nothing to authorize. A half-specified parent is the
        // handler's 400, which writes nothing.
        if (type is null || id is null)
            return await next(context);

        // A MISS DENIES: a type whose collection is unknown cannot have its access evaluated.
        if (RegardingRecordType.GetEntitySetName(type.Value) is not { } parentSet || id.Value == Guid.Empty)
        {
            _logger?.LogWarning("[EVENT-AUTH] Denying: regarding type {Type} cannot be authorized. CorrelationId: {CorrelationId}",
                type, httpContext.TraceIdentifier);
            return Deny(httpContext);
        }

        return await IsAllowedAsync(httpContext, parentSet, id.Value, ReadOperation)
            ? await next(context)
            : Deny(httpContext);
    }

    private async Task<bool> IsAllowedAsync(HttpContext httpContext, string entitySet, Guid recordId, string operation)
    {
        AccessRights rights;
        try
        {
            rights = await _probe.GetCallerRightsAsync(
                TokenHelper.ExtractBearerTokenOrNull(httpContext), entitySet, recordId, httpContext.RequestAborted);
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Covers the AUTHORIZATION DECISION only — never next(), so downstream faults are not relabelled.
            _logger?.LogError(ex, "[EVENT-AUTH] The caller-rights probe threw for {EntitySet}({RecordId}). Denying. "
                + "CorrelationId: {CorrelationId}", entitySet, recordId, httpContext.TraceIdentifier);
            return false;
        }

        if (OperationAccessPolicy.HasRequiredRights(rights, operation))
            return true;

        _logger?.LogWarning("[EVENT-AUTH] Denied: caller may not '{Operation}' {EntitySet}({RecordId}); holds {Rights}. "
            + "CorrelationId: {CorrelationId}", operation, entitySet, recordId, rights, httpContext.TraceIdentifier);
        return false;
    }

    private static IResult Deny(HttpContext httpContext) =>
        ProblemDetailsHelper.Forbidden(DeniedReasonCode, DeniedDetail, httpContext.TraceIdentifier);
}
