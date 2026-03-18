using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/close-project
///
/// Called by internal users (Core Users / admins) to close a Secure Project.
/// Closing a project cascades revocation of all external access:
///   1. Deactivates all active sprk_externalrecordaccess records for the project
///   2. Removes all external members from the SPE container (if containerId provided)
///   3. Invalidates Redis participation cache for all affected Contacts
///
/// Authentication: Azure AD JWT (RequireAuthorization via the adminGroup in ExternalAccessEndpoints).
/// This is an INTERNAL endpoint — portal users cannot call it.
///
/// Follows ADR-001: Minimal API — no controllers.
/// Follows ADR-008: Authorization applied at route group level in ExternalAccessEndpoints.
/// Follows ADR-009: Redis cache invalidated for each affected Contact.
/// </summary>
public static class ProjectClosureEndpoint
{
    private const string ExternalAccessEntitySet = "sprk_externalrecordaccesses";
    private const string CacheKeyPrefix = "sdap:external:access:";

    /// <summary>
    /// Registers the close-project endpoint on the external-access management group.
    /// </summary>
    public static RouteGroupBuilder MapProjectClosureEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/close-project", Handle)
            .WithName("CloseSecureProject")
            .WithSummary("Close a Secure Project and revoke all external access")
            .WithDescription(
                "Deactivates all active sprk_externalrecordaccess records for the project, " +
                "removes external members from the SPE container (if containerId provided), " +
                "and invalidates the Redis participation cache for all affected Contacts.")
            .Produces<CloseProjectResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    /// <summary>
    /// Handles POST /api/v1/external-access/close-project.
    /// </summary>
    /// <param name="request">The close project request containing ProjectId and optional ContainerId.</param>
    /// <param name="dataverseClient">Dataverse Web API client for querying and updating records.</param>
    /// <param name="speContainerMembership">SPE container membership service for removing external members.</param>
    /// <param name="cache">Distributed Redis cache for invalidating Contact participation entries.</param>
    /// <param name="httpContext">The current HTTP context for trace ID logging.</param>
    /// <param name="logger">Logger for operation tracing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// 200 OK with CloseProjectResponse reporting revoked records and affected contacts.
    /// 400 Bad Request if ProjectId is missing or empty.
    /// 500 Internal Server Error if Dataverse operations fail.
    /// </returns>
    public static async Task<IResult> Handle(
        CloseProjectRequest request,
        DataverseWebApiClient dataverseClient,
        SpeContainerMembershipService speContainerMembership,
        IDistributedCache cache,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (request.ProjectId == Guid.Empty)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "ProjectId is required and must be a valid GUID.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        logger.LogInformation(
            "[CLOSE-PROJECT] Starting project closure: ProjectId={ProjectId}, ContainerId={ContainerId}, TraceId={TraceId}",
            request.ProjectId, request.ContainerId, httpContext.TraceIdentifier);

        // Step 1: Query all active sprk_externalrecordaccess records for the project
        var activeRecords = await QueryActiveAccessRecordsAsync(
            dataverseClient, request.ProjectId, logger, ct);

        if (activeRecords.Count == 0)
        {
            logger.LogInformation(
                "[CLOSE-PROJECT] No active access records found for ProjectId={ProjectId}. Nothing to revoke.",
                request.ProjectId);

            return Results.Ok(new CloseProjectResponse(
                AccessRecordsRevoked: 0,
                SpeContainerMembersRemoved: 0,
                AffectedContactIds: []));
        }

        var affectedContactIds = activeRecords
            .Select(r => r.ContactId)
            .Distinct()
            .ToList();

        logger.LogInformation(
            "[CLOSE-PROJECT] Found {RecordCount} active access records for {ContactCount} contacts on ProjectId={ProjectId}",
            activeRecords.Count, affectedContactIds.Count, request.ProjectId);

        // Step 2: Deactivate all active access records (statecode=1, statuscode=2)
        int revokedCount = await DeactivateAccessRecordsAsync(
            dataverseClient, activeRecords, logger, ct);

        // Step 3: Remove all external members from the SPE container (if containerId provided)
        int speRemovedCount = 0;
        if (!string.IsNullOrWhiteSpace(request.ContainerId))
        {
            speRemovedCount = await speContainerMembership.RemoveAllExternalMembersAsync(
                request.ContainerId, ct);

            logger.LogInformation(
                "[CLOSE-PROJECT] Removed {Count} external SPE members from container {ContainerId}",
                speRemovedCount, request.ContainerId);
        }

        // Step 4: Invalidate Redis cache for all affected Contacts
        await InvalidateContactCachesAsync(cache, affectedContactIds, logger, ct);

        logger.LogInformation(
            "[CLOSE-PROJECT] Project closure complete: ProjectId={ProjectId}, " +
            "AccessRecordsRevoked={Revoked}, SpeRemovedCount={SpeRemoved}, AffectedContacts={Contacts}",
            request.ProjectId, revokedCount, speRemovedCount, affectedContactIds.Count);

        return Results.Ok(new CloseProjectResponse(
            AccessRecordsRevoked: revokedCount,
            SpeContainerMembersRemoved: speRemovedCount,
            AffectedContactIds: affectedContactIds));
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Queries all active sprk_externalrecordaccess records for the given project.
    /// </summary>
    private static async Task<IReadOnlyList<ExternalAccessRecord>> QueryActiveAccessRecordsAsync(
        DataverseWebApiClient dataverseClient,
        Guid projectId,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var filter = $"_sprk_projectid_value eq {projectId} and statecode eq 0";
            var select = "sprk_externalrecordaccessid,_sprk_contactid_value";

            var rows = await dataverseClient.QueryAsync<ExternalAccessRow>(
                ExternalAccessEntitySet,
                filter: filter,
                select: select,
                cancellationToken: ct);

            var records = rows
                .Where(r => r.sprk_externalrecordaccessid.HasValue && r._sprk_contactid_value.HasValue)
                .Select(r => new ExternalAccessRecord(
                    r.sprk_externalrecordaccessid!.Value,
                    r._sprk_contactid_value!.Value))
                .ToList();

            return records;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[CLOSE-PROJECT] Error querying active access records for ProjectId={ProjectId}",
                projectId);
            throw;
        }
    }

    /// <summary>
    /// Deactivates all given access records by setting statecode=1, statuscode=2 via PATCH.
    /// Continues processing remaining records even if individual updates fail.
    /// </summary>
    private static async Task<int> DeactivateAccessRecordsAsync(
        DataverseWebApiClient dataverseClient,
        IReadOnlyList<ExternalAccessRecord> records,
        ILogger logger,
        CancellationToken ct)
    {
        int revokedCount = 0;

        // Deactivate payload: statecode=1 (Inactive), statuscode=2 (Inactive)
        var deactivatePayload = new Dictionary<string, object>
        {
            ["statecode"] = 1,
            ["statuscode"] = 2
        };

        foreach (var record in records)
        {
            try
            {
                await dataverseClient.UpdateAsync(
                    ExternalAccessEntitySet,
                    record.RecordId,
                    deactivatePayload,
                    ct);

                revokedCount++;
                logger.LogDebug(
                    "[CLOSE-PROJECT] Deactivated access record {RecordId} for Contact {ContactId}",
                    record.RecordId, record.ContactId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "[CLOSE-PROJECT] Failed to deactivate access record {RecordId} for Contact {ContactId}. Continuing.",
                    record.RecordId, record.ContactId);
                // Continue processing remaining records even if one fails
            }
        }

        return revokedCount;
    }

    /// <summary>
    /// Invalidates Redis participation cache entries for all affected Contacts.
    /// Uses fire-and-forget per contact to avoid blocking the response on cache errors.
    /// </summary>
    private static async Task InvalidateContactCachesAsync(
        IDistributedCache cache,
        IReadOnlyList<Guid> contactIds,
        ILogger logger,
        CancellationToken ct)
    {
        foreach (var contactId in contactIds)
        {
            var cacheKey = $"{CacheKeyPrefix}{contactId}";
            try
            {
                await cache.RemoveAsync(cacheKey, ct);
                logger.LogDebug(
                    "[CLOSE-PROJECT] Invalidated Redis cache for Contact {ContactId} (key: {CacheKey})",
                    contactId, cacheKey);
            }
            catch (Exception ex)
            {
                // Non-critical — stale cache will expire within 60s per ADR-009 TTL
                logger.LogWarning(ex,
                    "[CLOSE-PROJECT] Failed to invalidate Redis cache for Contact {ContactId}. " +
                    "Cache will expire naturally (ADR-009 TTL: 60s).",
                    contactId);
            }
        }
    }

    // =========================================================================
    // Private types
    // =========================================================================

    /// <summary>A resolved external access record with its ID and Contact ID.</summary>
    private sealed record ExternalAccessRecord(Guid RecordId, Guid ContactId);

    /// <summary>
    /// Dataverse OData row for sprk_externalrecordaccess.
    /// Used only for deserialization within this endpoint.
    /// </summary>
    private sealed class ExternalAccessRow
    {
        public Guid? sprk_externalrecordaccessid { get; set; }
        public Guid? _sprk_contactid_value { get; set; }
    }
}
