// teams-app-r1 Task 022 (2026-08-04) — Accessible-record-set enforcement filter (ADR-008).
//
// The single enforcement POINT for the workforce collaboration surface: given the WorkforcePrincipal
// resolved by task 020's WorkforceCallerAuthorizationFilter (on HttpContext.Items), it composes the
// principal's accessible-record set (design §5 / spec FR-06, via IAccessibleRecordSetService) and
// DENIES any request for a record NOT in that set — for EVERY principal type. This generalizes the
// CIAM-only ExternalCallerContext.HasProjectAccess record∈set check to all three principal planes.
//
// ADR-008: per-endpoint filter — no global middleware; runs AFTER routing so it has the record-id
//          route value + the resolved principal.
// ADR-019: ProblemDetails on every 401/403.
//
// Attach order on a collaboration endpoint (record-scoped):
//   .AddWorkforceCallerAuthorizationFilter()                 // task 020 — resolves the principal
//   .AddAccessibleRecordSetAuthorizationFilter("sprk_project", "projectId")  // task 022 — enforces
//
// The record-scoped collaboration endpoints that ATTACH this gate are added by task 030 (broker
// document access). This task delivers the gate + its extension so 030 attaches it without inventing
// a second enforcement mechanism.

using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// Extension that adds accessible-record-set enforcement to a record-scoped collaboration endpoint.
/// </summary>
public static class AccessibleRecordSetAuthorizationFilterExtensions
{
    /// <summary>Machine-readable deny code (auth.md <c>{domain}.{area}.{action}.{reason}</c>).</summary>
    public const string DenyRecordNotAccessible = "sdap.access.deny.record_not_in_accessible_set";

    /// <summary>Deny code when the pipeline is misconfigured (no resolved principal).</summary>
    public const string DenyPrincipalMissing = "sdap.access.deny.principal_not_resolved";

    /// <summary>
    /// Adds the accessible-record-set enforcement filter to an endpoint.
    /// </summary>
    /// <param name="builder">The endpoint builder.</param>
    /// <param name="entityType">The Dataverse entity logical name the route id targets
    /// (e.g. <c>sprk_project</c>). Binds the composed set to the right entity plane.</param>
    /// <param name="recordIdRouteKey">The route-value key carrying the record GUID
    /// (default <c>recordId</c>).</param>
    public static TBuilder AddAccessibleRecordSetAuthorizationFilter<TBuilder>(
        this TBuilder builder, string entityType, string recordIdRouteKey = "recordId")
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordIdRouteKey);

        return builder.AddEndpointFilter(async (context, next) =>
        {
            var service = context.HttpContext.RequestServices
                .GetRequiredService<IAccessibleRecordSetService>();
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILogger<AccessibleRecordSetAuthorizationFilter>>();

            var filter = new AccessibleRecordSetAuthorizationFilter(
                service, logger, entityType, recordIdRouteKey);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Endpoint filter (ADR-008) enforcing <c>record ∈ accessible(principal)</c> for the workforce
/// collaboration surface. A record outside the composed set is DENIED (403, no body) — not merely
/// omitted from a list.
/// </summary>
public sealed class AccessibleRecordSetAuthorizationFilter : IEndpointFilter
{
    private readonly IAccessibleRecordSetService _service;
    private readonly ILogger<AccessibleRecordSetAuthorizationFilter> _logger;
    private readonly string _entityType;
    private readonly string _recordIdRouteKey;

    public AccessibleRecordSetAuthorizationFilter(
        IAccessibleRecordSetService service,
        ILogger<AccessibleRecordSetAuthorizationFilter> logger,
        string entityType,
        string recordIdRouteKey)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _entityType = string.IsNullOrWhiteSpace(entityType)
            ? throw new ArgumentException("entityType required.", nameof(entityType))
            : entityType;
        _recordIdRouteKey = string.IsNullOrWhiteSpace(recordIdRouteKey)
            ? throw new ArgumentException("recordIdRouteKey required.", nameof(recordIdRouteKey))
            : recordIdRouteKey;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // The WorkforceCallerAuthorizationFilter (task 020) must have run first and placed the
        // resolved principal on HttpContext.Items. Its absence is a pipeline misconfiguration —
        // fail CLOSED (401), never fall through to an unscoped request.
        if (httpContext.Items[WorkforcePrincipal.HttpContextItemsKey] is not WorkforcePrincipal principal)
        {
            _logger.LogWarning(
                "[WF-AUTHZ] No resolved workforce principal on the request — the accessible-record-set " +
                "filter requires AddWorkforceCallerAuthorizationFilter() to run first. Denying (fail-closed).");
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "Workforce principal was not resolved for this request.",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                extensions: new Dictionary<string, object?>
                {
                    ["reasonCode"] = AccessibleRecordSetAuthorizationFilterExtensions.DenyPrincipalMissing,
                });
        }

        // Extract + parse the target record id from the route. A missing/unparseable id is a client
        // contract error (the route is authored by task 030 with a GUID id), not an authz decision.
        var raw = httpContext.Request.RouteValues.TryGetValue(_recordIdRouteKey, out var v)
            ? v?.ToString()
            : null;

        if (!Guid.TryParse(raw, out var recordId) || recordId == Guid.Empty)
        {
            _logger.LogWarning(
                "[WF-AUTHZ] Missing/invalid record id (route key '{Key}', value '{Value}') for {EntityType}.",
                _recordIdRouteKey, raw, _entityType);
            return ProblemDetailsHelper.ValidationError(
                $"A valid record identifier is required in route value '{_recordIdRouteKey}'.");
        }

        var accessible = await _service
            .IsRecordAccessibleAsync(principal, _entityType, recordId, httpContext.RequestAborted)
            .ConfigureAwait(false);

        if (!accessible)
        {
            // DENY — short-circuit with 403 + no body (no bytes for a record outside the set).
            _logger.LogWarning(
                "[WF-AUTHZ] DENY {Kind} (systemuser={SystemUserId}, contact={ContactId}) → {EntityType} {RecordId}: " +
                "record not in composed accessible set.",
                principal.Kind, principal.SystemUserId, principal.ContactId, _entityType, recordId);
            return ProblemDetailsHelper.Forbidden(
                AccessibleRecordSetAuthorizationFilterExtensions.DenyRecordNotAccessible);
        }

        _logger.LogInformation(
            "[WF-AUTHZ] ALLOW {Kind} → {EntityType} {RecordId}: record in composed accessible set.",
            principal.Kind, _entityType, recordId);

        return await next(context);
    }
}
