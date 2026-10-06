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
    /// Authorize the CALLER against the <c>sprk_event</c> named by the route value <c>id</c>, for
    /// <paramref name="operation"/> (an <see cref="OperationAccessPolicy"/> key: <c>read</c> or <c>write</c>).
    /// </summary>
    public static TBuilder AddEventRecordAccessFilter<TBuilder>(this TBuilder builder, string operation)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
            await Create(context).AuthorizeEventAsync(context, next, operation));

    /// <summary>
    /// Authorize the CALLER's right to attach a child to the regarding PARENT a create/update request names
    /// (<see cref="EventAccessFilter.AttachOperation"/>: AppendTo), BEFORE the server resolves it, reads its
    /// name/number, or stamps its ancestors (existence oracle + data copy).
    /// </summary>
    public static TBuilder AddEventParentAccessFilter<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
            await Create(context).AuthorizeParentAsync(context, next));

    /// <summary>
    /// Require the CALLER to hold the Create privilege on <c>sprk_event</c> (<see cref="EventAccessFilter.CreateEventPrivilege"/>)
    /// before the app creates one on their behalf — there is no record yet, so no record right can answer it
    /// (the <c>FinanceAuthorizationFilter</c> Privilege path, owner decision G5).
    /// </summary>
    public static TBuilder AddEventCreatePrivilegeFilter<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
            await Create(context).AuthorizeCreatePrivilegeAsync(context, next));

    private static EventAccessFilter Create(EndpointFilterInvocationContext context)
    {
        var services = context.HttpContext.RequestServices;
        return new EventAccessFilter(
            services.GetRequiredService<CallerRecordAccessProbe>(),
            services.GetService<ILogger<EventAccessFilter>>());
    }
}

/// <summary>
/// spaarke-ontology-platform-r1 task 097 (security review) — per-record authorization for the
/// <c>/api/v1/events</c> routes, which used to carry only <c>RequireAuthorization()</c> and then act app-only on any
/// event id a caller supplied.
/// </summary>
/// <remarks>
/// <para><b>The established pattern, applied — not a new one.</b> This is the record-route gate the BFF already uses
/// for record routes: <see cref="RecordRouteAccessAuthorizationFilter"/> (record-keyed uploads),
/// <see cref="EntityAccessFilter"/> (Office save), <see cref="TodoSourceAccessFilter"/>,
/// <c>QuickCreateSourceAccessFilter</c> and <see cref="FinanceAuthorizationFilter"/> — an ADR-008 endpoint filter that
/// asks Dataverse, AS THE CALLER, what rights they hold on the record (<see cref="CallerRecordAccessProbe"/>,
/// <c>RetrievePrincipalAccess</c>), and lets <see cref="OperationAccessPolicy"/> decide which right the operation
/// costs: <c>read</c> for GET / logs; <c>write</c> for PUT / complete / cancel / DELETE (a soft delete is a status
/// WRITE); <see cref="AttachOperation"/> (AppendTo) on a create/re-parent's regarding parent; and, for a create, the
/// caller's Create TABLE privilege. The <c>sprk_events</c> collection comes from the shared
/// <see cref="EntityAccessFilter.TryResolveEntitySet"/> table; a regarding parent's from the one regarding catalogue in
/// <see cref="RegardingRecordType"/>.</para>
///
/// <para><b>One deny body, 403, for every record refusal</b> — exactly <see cref="TodoSourceAccessFilter"/>'s
/// reasoning: the probe collapses "no such record", "you may not see it" and "the check could not run" to
/// <see cref="AccessRights.None"/> (Dataverse hides existence under OBO), and a single constant body means no route
/// can be used as an existence oracle. 403 rather than 404 because one answer must cover both cases and 403 asserts
/// nothing about existence. The missing-privilege refusal names its own reason: it is about the caller's role, not
/// about any record, so it discloses nothing.</para>
/// </remarks>
public sealed class EventAccessFilter
{
    internal const string ReadOperation = "read";
    internal const string WriteOperation = "write";

    /// <summary>AppendTo on the regarding parent (task 097 review F2; <see cref="OperationAccessPolicy"/> key).</summary>
    internal const string AttachOperation = "event.attach";

    /// <summary>
    /// The Create privilege on <c>sprk_event</c>, by its live name (spaarkedev1 <c>RetrieveUserSetOfPrivilegesByNames</c>,
    /// 2026-10-06 — the schema name is <c>sprk_Event</c>, so the privilege is not all lower case). A misspelt name
    /// answers "not held" for every caller, which is why a test pins it.
    /// </summary>
    internal const string CreateEventPrivilege = "prvCreatesprk_Event";

    /// <summary>The ONE reason code for every record refusal (a varying code would be an existence oracle).</summary>
    internal const string DeniedReasonCode = "event_access_denied";

    /// <summary>The caller's role lacks Create on sprk_event (same code as <see cref="FinanceAuthorizationFilter"/>).</summary>
    internal const string InsufficientPrivilegeReasonCode = FinanceAuthorizationFilter.InsufficientPrivilegeReasonCode;

    /// <summary>The route value the event id is read from — never a handler argument (review F5).</summary>
    internal const string EventIdRouteValue = "id";

    private const string DeniedDetail =
        "You do not have permission to do this to the event or to the record it refers to, or those records are not "
        + "available.";

    private const string PrivilegeDeniedDetail = "Your security role does not allow you to create events.";

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

        // Review F5: the id the ROUTE names (RecordRouteAccessAuthorizationFilter's source), not "the first Guid
        // argument", which would silently authorize the wrong record if a handler ever took another Guid first.
        if (!httpContext.Request.RouteValues.TryGetValue(EventIdRouteValue, out var raw)
            || !Guid.TryParse(raw?.ToString(), out var eventId)
            || eventId == Guid.Empty
            || !EntityAccessFilter.TryResolveEntitySet("sprk_event", out var entitySet))
        {
            _logger?.LogWarning("[EVENT-AUTH] Denying: no event id on the route. CorrelationId: {CorrelationId}",
                httpContext.TraceIdentifier);
            return Deny(httpContext);
        }

        var rights = await GetRightsAsync(httpContext, entitySet, eventId);
        return OperationAccessPolicy.HasRequiredRights(rights, operation)
            ? await next(context)
            : Denied(httpContext, operation, entitySet, eventId, rights);
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

        // No parent named → nothing is read, attached or stamped → nothing to authorize. A half-specified parent is
        // the handler's 400, which writes nothing.
        if (type is null || id is null)
            return await next(context);

        // A MISS DENIES: a type whose collection is unknown cannot have its access evaluated.
        if (RegardingRecordType.GetEntitySetName(type.Value) is not { } parentSet || id.Value == Guid.Empty)
        {
            _logger?.LogWarning("[EVENT-AUTH] Denying: regarding type {Type} cannot be authorized. CorrelationId: {CorrelationId}",
                type, httpContext.TraceIdentifier);
            return Deny(httpContext);
        }

        var rights = await GetRightsAsync(httpContext, parentSet, id.Value);
        return OperationAccessPolicy.HasRequiredRights(rights, AttachOperation)
            ? await next(context)
            : Denied(httpContext, AttachOperation, parentSet, id.Value, rights);
    }

    internal async ValueTask<object?> AuthorizeCreatePrivilegeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        bool holds;
        try
        {
            holds = await _probe.CallerHoldsPrivilegeAsync(
                TokenHelper.ExtractBearerTokenOrNull(httpContext), CreateEventPrivilege, httpContext.RequestAborted);
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[EVENT-AUTH] The privilege check threw for {Privilege}. Denying. CorrelationId: {CorrelationId}",
                CreateEventPrivilege, httpContext.TraceIdentifier);
            holds = false;
        }

        if (holds)
            return await next(context);

        _logger?.LogWarning("[EVENT-AUTH] Denied: caller does not hold {Privilege}. CorrelationId: {CorrelationId}",
            CreateEventPrivilege, httpContext.TraceIdentifier);
        return ProblemDetailsHelper.Forbidden(InsufficientPrivilegeReasonCode, PrivilegeDeniedDetail, httpContext.TraceIdentifier);
    }

    /// <summary>The caller's rights on one record; any fault is <see cref="AccessRights.None"/> (fail closed).</summary>
    private async Task<AccessRights> GetRightsAsync(HttpContext httpContext, string entitySet, Guid recordId)
    {
        try
        {
            return await _probe.GetCallerRightsAsync(
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
            return AccessRights.None;
        }
    }

    private IResult Denied(HttpContext httpContext, string operation, string entitySet, Guid recordId, AccessRights rights)
    {
        _logger?.LogWarning("[EVENT-AUTH] Denied: caller may not '{Operation}' {EntitySet}({RecordId}); holds {Rights}. "
            + "CorrelationId: {CorrelationId}", operation, entitySet, recordId, rights, httpContext.TraceIdentifier);
        return Deny(httpContext);
    }

    private static IResult Deny(HttpContext httpContext) =>
        ProblemDetailsHelper.Forbidden(DeniedReasonCode, DeniedDetail, httpContext.TraceIdentifier);
}
