using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding EntityAccessFilter to Office save endpoints.
/// </summary>
public static class EntityAccessFilterExtensions
{
    /// <summary>
    /// Adds entity access filter that validates user has access to referenced entities in save requests.
    /// Returns 403 Forbidden if user lacks access to the target entity.
    /// Returns 400 Bad Request if the association target type is not supported.
    /// </summary>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// This filter should be applied after OfficeAuthFilter to ensure userId is available.
    /// Extracts target entity from SaveRequest.TargetEntity.
    /// </remarks>
    public static TBuilder AddEntityAccessFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;
            var logger = services.GetService<ILogger<EntityAccessFilter>>();
            var probe = services.GetRequiredService<CallerRecordAccessProbe>();
            var filter = new EntityAccessFilter(probe, logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Authorization filter that validates user has access to referenced entities in save requests.
/// Verifies the user can associate documents with the target account, contact, matter, project, or invoice.
/// </summary>
/// <remarks>
/// <para>
/// Follows ADR-008: Use endpoint filters for resource-level authorization.
/// </para>
/// <para>
/// Entity access is determined by:
/// 1. User has read access to the entity (can view it)
/// 2. User has write/associate permission for document association
/// 3. Entity exists and is active
/// </para>
/// <para>
/// Supported entity types (from spec):
/// - account (Dataverse standard)
/// - contact (Dataverse standard)
/// - sprk_matter (Spaarke custom)
/// - sprk_project (Spaarke custom)
/// - sprk_invoice (Spaarke custom)
/// </para>
///
/// <para><b>FIXED 2026-08-23 (unified-access-control-r2, task 008 follow-up, owner-authorised).</b>
/// This filter used to build a resource id of the form <c>"{entityType}:{entityId}"</c> and pass it to
/// <see cref="AuthorizationService"/>. That bottoms out in <c>DataverseAccessDataSource</c>, which
/// substitutes the value into <c>sprk_documents({resourceId})</c> in BOTH its
/// <c>RetrievePrincipalAccess</c> target and its fallback read probe — so the emitted URL was
/// <c>sprk_documents(sprk_matter:8f3a…)</c>, which is not a document id and not even a GUID. Dataverse
/// rejected it, the lookup failed closed to <see cref="AccessRights.None"/>, and since
/// <c>entity.associate_document</c> requires <see cref="AccessRights.AppendTo"/> the save was refused
/// for EVERY caller. Filing a document against a matter from the Office add-in could not succeed.</para>
///
/// <para>The rights now come from <see cref="CallerRecordAccessProbe"/>, which asks Dataverse about the
/// TARGET ENTITY's own collection (<c>sprk_matters</c>, <c>accounts</c>, …) as the caller (OBO).
/// <see cref="OperationAccessPolicy"/> remains the single authority for WHICH right the operation
/// needs — only the source of the rights changed, so there is still one place that decides what
/// <c>entity.associate_document</c> costs.</para>
///
/// <para>When <c>IAccessDataSource</c> is generalized beyond documents (task 032), this filter should
/// go back through <see cref="AuthorizationService"/> so there is one access path again.</para>
/// </remarks>
public class EntityAccessFilter : IEndpointFilter
{
    private readonly CallerRecordAccessProbe _probe;
    private readonly ILogger<EntityAccessFilter>? _logger;

    // Operation constant for entity association
    private const string AssociateOperation = "entity.associate_document";

    /// <summary>
    /// Association target type → Dataverse entity SET (plural collection) name.
    /// </summary>
    /// <remarks>
    /// <para>A closed map with a fail-closed miss, replacing the previous <c>IsValidEntityType</c> boolean:
    /// validating a type and then resolving its collection are the same question, and keeping them in
    /// one table means a type can never be accepted without a collection to check it against. Short
    /// aliases are retained because the previous implementation accepted them.</para>
    ///
    /// <para><b>This is THE map for logical-name → entity-set on the caller-rights path</b>
    /// (unified-access-control-r2 task 076). It is read by this filter AND by
    /// <see cref="RecordRouteAccessAuthorizationFilter"/> through <see cref="TryResolveEntitySet"/>.
    /// The codebase already carried three logical/short-name → entity-set maps before 076
    /// (here, <c>SemanticSearchAuthorizationFilter.AuthorizableEntitySets</c>, and
    /// <c>RecordSearchAuthorizationFilter</c>'s dynamically-built one), which is already over the
    /// CLAUDE.md §11 line. A FOURTH is not acceptable, so the record-keyed upload route reuses this
    /// one rather than declaring its own — the two filters differ in where they read the target from
    /// and which right they demand, not in what an entity's collection is called.</para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> EntitySetByType =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["account"] = "accounts",
            ["contact"] = "contacts",
            ["sprk_matter"] = "sprk_matters",
            ["matter"] = "sprk_matters",
            ["sprk_project"] = "sprk_projects",
            ["project"] = "sprk_projects",
            ["sprk_invoice"] = "sprk_invoices",
            ["invoice"] = "sprk_invoices"
        };

    /// <summary>
    /// Resolve an entity logical name (or short alias) to its Dataverse entity SET, for
    /// <see cref="CallerRecordAccessProbe.GetCallerRightsAsync"/>, which needs the PLURAL collection.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> and the collection name when the type is one whose per-record access
    /// this codebase can evaluate; <see langword="false"/> otherwise. A <see langword="false"/> return
    /// is a DENIAL at every call site, never a pass-through — see
    /// <see cref="RecordRouteAccessAuthorizationFilter"/>.
    /// </returns>
    /// <remarks>
    /// Exposed (task 076) so the record-keyed upload route can share this table instead of adding a
    /// fourth copy of it. Deliberately a <c>TryResolve</c> rather than an exposed dictionary: handing
    /// out the map would let a caller enumerate it and then decide for itself what a miss means, and
    /// the whole point of the closed-map-with-fail-closed-miss shape is that a miss has exactly one
    /// legal interpretation.
    /// </remarks>
    internal static bool TryResolveEntitySet(string? entityLogicalNameOrAlias, out string entitySet)
    {
        if (!string.IsNullOrWhiteSpace(entityLogicalNameOrAlias)
            && EntitySetByType.TryGetValue(entityLogicalNameOrAlias.Trim(), out var resolved))
        {
            entitySet = resolved;
            return true;
        }

        entitySet = string.Empty;
        return false;
    }

    public EntityAccessFilter(
        CallerRecordAccessProbe probe,
        ILogger<EntityAccessFilter>? logger = null)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Get userId from HttpContext.Items (set by OfficeAuthFilter)
        var userId = httpContext.Items[OfficeAuthFilter.UserIdKey] as string;
        if (string.IsNullOrEmpty(userId))
        {
            _logger?.LogWarning(
                "Entity access check failed: No userId in HttpContext.Items. " +
                "Ensure OfficeAuthFilter runs before EntityAccessFilter. " +
                "CorrelationId: {CorrelationId}",
                httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not established",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_AUTH_003",
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        // Extract target entity from request arguments
        var targetEntity = ExtractTargetEntity(context);
        if (targetEntity == null)
        {
            _logger?.LogDebug(
                "Entity access check: No target entity in request, validation will be handled by endpoint. " +
                "CorrelationId: {CorrelationId}",
                httpContext.TraceIdentifier);

            // Per spec, association is required - but let the endpoint handle validation
            // This filter is for authorization only
            return await next(context);
        }

        // Resolve the target's Dataverse collection. A type with no entry is rejected — validating the
        // type and knowing where to look it up are the same question (see EntitySetByType).
        if (!TryResolveEntitySet(targetEntity.EntityType, out var entitySet))
        {
            _logger?.LogWarning(
                "Entity access check failed: Invalid entity type '{EntityType}'. " +
                "CorrelationId: {CorrelationId}",
                targetEntity.EntityType, httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: $"Invalid association entity type: {targetEntity.EntityType}",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_002",
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        _logger?.LogDebug(
            "Checking entity access for user {UserId} on {EntityType} {EntityId}. " +
            "CorrelationId: {CorrelationId}",
            userId, targetEntity.EntityType, targetEntity.EntityId, httpContext.TraceIdentifier);

        try
        {
            // Ask Dataverse what this CALLER may do to the TARGET ENTITY — in its own collection, not
            // sprk_documents. OperationAccessPolicy still decides which right that buys.
            var rights = await _probe.GetCallerRightsAsync(
                TokenHelper.ExtractBearerTokenOrNull(httpContext),
                entitySet,
                targetEntity.EntityId,
                httpContext.RequestAborted);

            if (!OperationAccessPolicy.HasRequiredRights(rights, AssociateOperation))
            {
                _logger?.LogWarning(
                    "Entity access denied: User {UserId} cannot associate documents with {EntityType} {EntityId} " +
                    "({EntitySet}). Holds {Rights}; requires {Required}. CorrelationId: {CorrelationId}",
                    userId, targetEntity.EntityType, targetEntity.EntityId, entitySet, rights,
                    OperationAccessPolicy.GetRequiredRights(AssociateOperation), httpContext.TraceIdentifier);

                return Results.Problem(
                    statusCode: 403,
                    title: "Forbidden",
                    detail: InsufficientRightsDetail,
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.3",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = AccessDeniedErrorCode,
                        ["reasonCode"] = "insufficient_rights",
                        ["entityType"] = targetEntity.EntityType,
                        ["correlationId"] = httpContext.TraceIdentifier
                    });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex,
                "Entity access check failed for {EntityType} {EntityId}. " +
                "User: {UserId}. CorrelationId: {CorrelationId}",
                targetEntity.EntityType, targetEntity.EntityId, userId, httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "An error occurred during authorization",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "OFFICE_AUTH_ERROR",
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        _logger?.LogDebug(
            "Entity access verified: User {UserId} can associate documents with {EntityType} {EntityId}. " +
            "CorrelationId: {CorrelationId}",
            userId, targetEntity.EntityType, targetEntity.EntityId, httpContext.TraceIdentifier);

        // Store entity info for downstream use
        httpContext.Items["Office.TargetEntityType"] = targetEntity.EntityType;
        httpContext.Items["Office.TargetEntityId"] = targetEntity.EntityId;

        return await next(context);
    }

    /// <summary>
    /// Extract target entity from request arguments.
    /// Supports SaveRequest with TargetEntity property.
    /// </summary>
    private static SaveEntityReference? ExtractTargetEntity(EndpointFilterInvocationContext context)
    {
        foreach (var argument in context.Arguments)
        {
            if (argument is SaveRequest saveRequest && saveRequest.TargetEntity != null)
            {
                return saveRequest.TargetEntity;
            }
        }

        return null;
    }

    /// <summary>
    /// Maps authorization denial reason to appropriate HTTP status and error code.
    /// </summary>
    /// <summary>
    /// The Office error-code taxonomy's "access denied" code. The Outlook/Word task pane keys its
    /// notification TITLE off this (<c>errorMessages.ts</c> <c>ERROR_CODE_MAP</c> → "Access Denied");
    /// the BODY comes from <see cref="InsufficientRightsDetail"/> below.
    /// </summary>
    private const string AccessDeniedErrorCode = "OFFICE_009";

    /// <summary>
    /// What the USER is shown when the association is refused for want of rights.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this is a written sentence and not a code lookup</b> (unified-access-control-r2,
    /// 2026-08-31, owner-directed). This replaced <c>MapAuthorizationDenial</c>, a five-arm switch on a
    /// <c>reasonCode</c> string. That switch had exactly ONE call site passing exactly ONE literal —
    /// <c>"insufficient_rights"</c> — which matched NONE of its named arms, so every real denial fell to
    /// the default and the user was told <i>"Access denied to association target"</i>: internal jargon
    /// that names no record, no missing capability and no remedy. The switch's own better sentence (for
    /// <c>"permission_denied"</c>) was unreachable from that call site. A lookup table with one input and
    /// four dead arms is not a mapping; the message is written here directly instead.</para>
    ///
    /// <para><b>The dead arms were not merely dead — one was a latent disclosure.</b> Its
    /// <c>entity_not_found → 404</c> arm would, if it ever became reachable, have separated "no such
    /// record" from "no access to that record". <see cref="CallerRecordAccessProbe"/> conflates those two
    /// deliberately ("both mean not authorized"), and task 022 removed exactly that separation from bulk
    /// download because it is a record-enumeration oracle. So restoring reason-code branching here needs a
    /// disclosure argument, not just a caller.</para>
    ///
    /// <para><b>Why naming the permission is safe.</b> Record EXISTENCE is not disclosed by this text —
    /// the filter answers 403 whether or not the record exists, precisely because the probe conflates
    /// them. And the caller supplied the record id from a picker that already required access to it. So
    /// naming "Append To" costs no information and is the one word an administrator can act on.</para>
    ///
    /// <para><b>The entity type is deliberately NOT interpolated.</b> It is a logical name
    /// (<c>sprk_matter</c>), not a label, and mapping logical names to display names would be a FOURTH
    /// entity-name table (CLAUDE.md §11 — see <see cref="EntitySetByType"/>'s remarks). The type travels
    /// in the <c>entityType</c> extension for support and telemetry instead of in prose.</para>
    /// </remarks>
    private const string InsufficientRightsDetail =
        "You do not have permission to file documents against this record. Filing a document to a record "
        + "requires the \"Append To\" permission on it, and your security role does not currently grant "
        + "that. Ask an administrator to grant Append To for this record type, or ask the record's owner "
        + "to share the record with you.";
}
