using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for attaching <see cref="RecordRouteAccessAuthorizationFilter"/>.
/// </summary>
public static class RecordRouteAccessAuthorizationFilterExtensions
{
    /// <summary>
    /// Authorize the CALLER against the owning Dataverse record named by the route's
    /// <c>{entityLogicalName}</c> / <c>{recordId}</c> values, before the handler runs.
    /// </summary>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <param name="operation">
    /// The <see cref="OperationAccessPolicy"/> key naming the right the route needs. Callers pass
    /// <see cref="RecordRouteAccessAuthorizationFilter.AssociateContentOperation"/> unless they have a
    /// reason not to.
    /// </param>
    public static TBuilder AddRecordRouteAccessAuthorizationFilter<TBuilder>(
        this TBuilder builder,
        string operation) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;
            var probe = services.GetRequiredService<CallerRecordAccessProbe>();
            var logger = services.GetService<ILogger<RecordRouteAccessAuthorizationFilter>>();
            var filter = new RecordRouteAccessAuthorizationFilter(probe, operation, logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// unified-access-control-r2 task 076 — the per-resource authorization decision for the record-keyed
/// upload routes. Asks Dataverse what the CALLER may do to the record named in the ROUTE, and denies
/// before any container resolution or Graph call happens.
///
/// <para><b>Why a route-value variant of <see cref="EntityAccessFilter"/> and not that filter itself.</b>
/// Three of that filter's behaviours are wrong here, and each would be a defect rather than an
/// inconvenience:</para>
/// <list type="number">
///   <item><description>It reads the caller's id from <c>HttpContext.Items[OfficeAuthFilter.UserIdKey]</c>
///   and 401s when absent. That key is set by <c>OfficeAuthFilter</c>, which the OBO upload routes do not
///   and should not carry — so <see cref="EntityAccessFilter"/> would 401 every upload.</description></item>
///   <item><description>It reads the target from a deserialized Office <c>SaveRequest</c> body. The upload
///   routes carry raw bytes as their body; the target is in the URL.</description></item>
///   <item><description>When it finds no target it calls <c>next()</c> — a deliberate FAIL-OPEN, correct
///   for Office (the endpoint validates the association itself) and catastrophic here, where "no target"
///   would mean an unauthorized write into whatever container the server then resolves.</description></item>
/// </list>
///
/// <para><b>What is shared, which is the part CLAUDE.md §11 is about.</b> The mechanism is unchanged:
/// the SAME <see cref="CallerRecordAccessProbe"/> asks Dataverse the same
/// <c>RetrievePrincipalAccess</c> question, and the logical-name → entity-SET table is
/// <see cref="EntityAccessFilter"/>'s, reached through
/// <see cref="EntityAccessFilter.TryResolveEntitySet"/>. This filter declares NO map of its own —
/// there were already three in the codebase before task 076 and a fourth is a review failure.
/// <see cref="OperationAccessPolicy"/> remains the single authority for which right an operation costs;
/// this filter adds no key to that table either, reusing <c>entity.associate_document</c> (see
/// <see cref="AssociateContentOperation"/>).</para>
///
/// <para><b>Fail closed, in all four ways it can fail.</b> No bearer token → deny. Entity logical name
/// absent or unparseable record id → deny. Entity logical name not in the shared map → deny (NOT a
/// pass-through: an entity whose per-record access this codebase cannot evaluate is an entity whose
/// uploads it must not accept). Probe throws → deny. <see cref="CallerRecordAccessProbe"/> itself
/// already collapses every "could not answer" into <see cref="AccessRights.None"/>, so this filter
/// inherits that posture rather than re-deciding it.</para>
///
/// <para><b>ADR-008.</b> This is an endpoint filter, not handler code and not middleware — the decision
/// runs before the handler, so no container is resolved and no bytes reach Graph for a caller who has no
/// access to the owning record.</para>
/// </summary>
public class RecordRouteAccessAuthorizationFilter : IEndpointFilter
{
    /// <summary>
    /// The <see cref="OperationAccessPolicy"/> key for "attach content to this record".
    /// </summary>
    /// <remarks>
    /// <para>Deliberately REUSED, not added. <c>entity.associate_document</c> already exists and already
    /// means exactly this act — <c>OperationAccessPolicy</c>'s own note on it reads: <i>"The authorized
    /// resource is the TARGET entity …, and the operation attaches a document TO it. In Dataverse that is
    /// AppendTo on the target … not Write — saving an email to a matter does not modify the matter's own
    /// fields."</i> Uploading a file destined to become an <c>sprk_document</c> against a matter is the
    /// same act by a different door, so it costs the same right.</para>
    ///
    /// <para>A new key (<c>record.upload_content</c>, say) was considered and rejected: it would put two
    /// keys on one act, and the first time they drifted the Office save path and the wizard upload path
    /// would disagree about who may file a document against a matter.</para>
    ///
    /// <para>The <c>AppendTo</c> mapping is live on this path today — <see cref="EntityAccessFilter"/>
    /// uses the same key through the same probe for <c>POST /api/office/save</c>, and
    /// <see cref="DataverseAccessRightsMapper"/> maps <c>AppendToAccess</c> directly. The caveat recorded
    /// in <c>OperationAccessPolicy</c> about task 005 lifting a Read ceiling applies to
    /// <c>DataverseAccessDataSource</c>'s snapshot path, which this filter does not use.</para>
    /// </remarks>
    public const string AssociateContentOperation = "entity.associate_document";

    /// <summary>Route value carrying the owning record's entity logical name.</summary>
    internal const string EntityLogicalNameRouteValue = "entityLogicalName";

    /// <summary>Route value carrying the owning record's id.</summary>
    internal const string RecordIdRouteValue = "recordId";

    private readonly CallerRecordAccessProbe _probe;
    private readonly string _operation;
    private readonly ILogger<RecordRouteAccessAuthorizationFilter>? _logger;

    public RecordRouteAccessAuthorizationFilter(
        CallerRecordAccessProbe probe,
        string operation,
        ILogger<RecordRouteAccessAuthorizationFilter>? logger = null)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _operation = operation ?? throw new ArgumentNullException(nameof(operation));
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var routeValues = httpContext.Request.RouteValues;

        var entityLogicalName =
            routeValues.TryGetValue(EntityLogicalNameRouteValue, out var rawEntity)
                ? rawEntity?.ToString()
                : null;

        var recordIdText =
            routeValues.TryGetValue(RecordIdRouteValue, out var rawRecordId)
                ? rawRecordId?.ToString()
                : null;

        // A route missing either half of its own key cannot be authorized. This is unreachable through
        // the mapped routes (both segments are required, and {recordId:guid} is constrained), so a hit
        // here means someone attached this filter to a route that does not carry the key — which must
        // deny rather than proceed, or the filter becomes decorative on that route.
        if (string.IsNullOrWhiteSpace(entityLogicalName)
            || !Guid.TryParse(recordIdText, out var recordId)
            || recordId == Guid.Empty)
        {
            _logger?.LogWarning(
                "[RECORD-ROUTE-AUTH] Denying: the route carries no usable owning-record key "
                + "(entityLogicalName='{EntityLogicalName}', recordId='{RecordId}'). "
                + "CorrelationId: {CorrelationId}",
                entityLogicalName, recordIdText, httpContext.TraceIdentifier);

            return ProblemDetailsHelper.Forbidden(
                "owning_record_not_specified",
                "The owning record could not be determined from the request, so access to it cannot be "
                + "evaluated.",
                httpContext.TraceIdentifier);
        }

        // The shared table (EntityAccessFilter.EntitySetByType). A MISS DENIES.
        //
        // The tempting alternative — 400 "unsupported entity type" — is rejected on purpose. This
        // codebase's posture is that an unanswerable access question IS a denial: CallerRecordAccessProbe
        // returns AccessRights.None for every "could not answer", and ISecurableEntityRegistry propagates
        // rather than defaulting to "not secure". An entity type with no entry is an entity whose
        // per-record access nothing here can evaluate, and accepting an upload against it would write
        // bytes into a container on the strength of no decision at all. The actionable detail for whoever
        // is adding a new entity is in the log line, not in a status code that reads as a client bug.
        if (!EntityAccessFilter.TryResolveEntitySet(entityLogicalName, out var entitySet))
        {
            _logger?.LogWarning(
                "[RECORD-ROUTE-AUTH] Denying upload against '{EntityLogicalName}' {RecordId}: that "
                + "entity type is not in EntityAccessFilter's logical-name -> entity-set table, so the "
                + "caller's per-record rights cannot be established. To support it, add it THERE (do not "
                + "add a second table). CorrelationId: {CorrelationId}",
                entityLogicalName, recordId, httpContext.TraceIdentifier);

            return ProblemDetailsHelper.Forbidden(
                "entity_type_not_authorizable",
                $"Per-record access cannot be evaluated for entity type '{entityLogicalName}', so this "
                + "operation is refused.",
                httpContext.TraceIdentifier);
        }

        AccessRights rights;
        try
        {
            rights = await _probe.GetCallerRightsAsync(
                TokenHelper.ExtractBearerTokenOrNull(httpContext),
                entitySet,
                recordId,
                httpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            // The try covers the AUTHORIZATION DECISION ONLY — deliberately not next(). Wrapping the
            // handler would render every downstream fault as an authorization error, which is the defect
            // task 072 removed from DocumentAuthorizationFilter; the resolver's typed 404/409s must reach
            // the global handler intact.
            _logger?.LogError(ex,
                "[RECORD-ROUTE-AUTH] The caller-rights probe threw for {EntitySet}({RecordId}). Denying. "
                + "CorrelationId: {CorrelationId}",
                entitySet, recordId, httpContext.TraceIdentifier);

            return ProblemDetailsHelper.Forbidden(
                "access_check_failed",
                "Access to the owning record could not be verified, so this operation is refused.",
                httpContext.TraceIdentifier);
        }

        if (!OperationAccessPolicy.HasRequiredRights(rights, _operation))
        {
            _logger?.LogWarning(
                "[RECORD-ROUTE-AUTH] Denied: caller may not '{Operation}' on {EntitySet}({RecordId}). "
                + "Holds {Rights}; requires {Required}. CorrelationId: {CorrelationId}",
                _operation, entitySet, recordId, rights,
                OperationAccessPolicy.GetRequiredRights(_operation), httpContext.TraceIdentifier);

            return ProblemDetailsHelper.Forbidden(
                "insufficient_rights",
                "You do not have permission to add content to this record.",
                httpContext.TraceIdentifier);
        }

        _logger?.LogInformation(
            "[RECORD-ROUTE-AUTH] Allowed: caller may '{Operation}' on {EntitySet}({RecordId}).",
            _operation, entitySet, recordId);

        return await next(context);
    }
}
