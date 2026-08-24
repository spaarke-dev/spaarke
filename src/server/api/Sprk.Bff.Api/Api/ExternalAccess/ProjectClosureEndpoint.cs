using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/close-project
///
/// Called by internal users (Core Users / admins) to close a Secure Project.
/// Closing a project cascades revocation of all external access:
///   1. Deactivates all active sprk_externalrecordaccess records for the project —
///      contact grants AND organization grants
///   2. Removes all external members from the SPE container (if containerId provided)
///   3. Invalidates Redis participation cache for all affected Contacts
///
/// <para><b>The cascade never reports a success it did not achieve</b> (task 016, finding A-12, spec
/// FR-15). If the grants cannot be enumerated, or any row cannot be deactivated, the response is a
/// 500 ProblemDetails carrying a machine-readable reason code — because an operator who sees 200 stops
/// checking, and the participants they believe were cut off still have access. Closure is idempotent,
/// so the correct response to that failure is simply to retry.</para>
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
    // Cache key components for invalidation. BOUND to ExternalParticipationService (the read/store side,
    // the single source of truth) so a version bump there stays in sync here automatically. Task 073 #7
    // fix: the prior hard-coded `CacheVersion = 1` silently missed the v2/v3 stored key, so the
    // cascade-revoke invalidation on project closure relied on the 60s TTL. Per-Contact participation
    // cache — not an authz decision (ADR-009); tenant scope is derived from the caller's 'tid' claim.
    private const string ExternalAccessResource = ExternalParticipationService.ExternalAccessResource;
    private const int CacheVersion = ExternalParticipationService.CacheVersion;

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
        ITenantCache cache,
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

        // Step 1: Query all active sprk_externalrecordaccess records for the project.
        //
        // A failure here means we do not know WHICH grants exist, so we cannot claim any were revoked.
        // Per this task's ADR-003 constraint the closure must surface the failure and be retried — the
        // one outcome that must never happen is a success response while grants are still active, since
        // an operator who sees 200 stops looking. Steps 2-4 stay unreachable until enumeration succeeds.
        IReadOnlyList<ExternalAccessRecord> activeRecords;
        try
        {
            activeRecords = await QueryActiveAccessRecordsAsync(
                dataverseClient, request.ProjectId, logger, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[CLOSE-PROJECT] Enumeration failed for ProjectId={ProjectId} — closure ABORTED with " +
                "zero grants revoked. Access is unchanged; retry the closure.",
                request.ProjectId);

            return ClosureIncomplete(
                httpContext,
                ClosureEnumerationFailedReason,
                $"Could not enumerate active external grants for project {request.ProjectId}. " +
                "No grants were revoked and external access is UNCHANGED. Retry the closure.",
                accessRecordsRevoked: 0);
        }

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

        // Organization grants carry no contact, so they contribute nothing to the per-contact cache
        // invalidation below — see InvalidateContactCachesAsync for why that gap is bounded.
        var affectedContactIds = activeRecords
            .Where(r => r.ContactId.HasValue)
            .Select(r => r.ContactId!.Value)
            .Distinct()
            .ToList();

        var organizationGrantCount = activeRecords.Count(r => r.IsOrganizationGrant);

        logger.LogInformation(
            "[CLOSE-PROJECT] Found {RecordCount} active access records on ProjectId={ProjectId} " +
            "({ContactCount} contacts, {OrgGrantCount} organization grants)",
            activeRecords.Count, request.ProjectId, affectedContactIds.Count, organizationGrantCount);

        // Step 2: Deactivate all active access records (statecode=1, statuscode=2)
        var (revokedCount, failedCount) = await DeactivateAccessRecordsAsync(
            dataverseClient, activeRecords, logger, ct);

        // Step 3: Remove all external members from the SPE container (if containerId provided).
        //
        // Guarded for the same reason enumeration is: container membership IS access, so a failure here
        // leaves external users able to reach the project's files. Letting it escape as an unhandled
        // exception would also discard the fact that N grants WERE revoked, which is the single most
        // useful thing to tell the operator. The failure is recorded and reported after Step 4, so cache
        // invalidation still happens.
        //
        // ⚠️ This guard cannot fire today, and that is a SEPARATE defect (filed onto task 017):
        // SpeContainerMembershipService.ListExternalMembersAsync catches every exception and returns [],
        // so RemoveAllExternalMembersAsync answers "0 removed" whether the container was genuinely empty
        // or Graph was unreachable — and closure reports 200 while external users may still hold file
        // permission. Fixing that belongs with the SPE revoke work; the guard is kept so that fix lands
        // as a typed response here rather than a raw 500.
        int speRemovedCount = 0;
        bool containerCleared = true;
        if (!string.IsNullOrWhiteSpace(request.ContainerId))
        {
            try
            {
                speRemovedCount = await speContainerMembership.RemoveAllExternalMembersAsync(
                    request.ContainerId, ct);

                logger.LogInformation(
                    "[CLOSE-PROJECT] Removed {Count} external SPE members from container {ContainerId}",
                    speRemovedCount, request.ContainerId);
            }
            catch (Exception ex)
            {
                containerCleared = false;
                logger.LogError(ex,
                    "[CLOSE-PROJECT] Failed to clear external members from container {ContainerId}. " +
                    "External users may retain FILE access even though {Revoked} grants were revoked.",
                    request.ContainerId, revokedCount);
            }
        }

        // Step 4: Invalidate Redis cache for all affected Contacts. Runs unconditionally — it only ever
        // removes access, so it is worth doing even when an earlier step failed.
        var tenantId = ExtractTenantId(httpContext);
        await InvalidateContactCachesAsync(cache, tenantId, affectedContactIds, logger, ct);

        // A row we could not deactivate is a participant who still has access. Reporting 200 here would
        // tell the operator the project is closed while it is not — the same false-success shape the
        // ADR-003 constraint forbids for the enumeration failure above. Steps 3 and 4 already ran because
        // both only ever REMOVE access, so running them makes the partial state strictly less open; the
        // failure is reported afterwards. Closure is idempotent (deactivating an inactive row is a no-op),
        // so the correct operator response is simply to retry.
        if (failedCount > 0)
        {
            logger.LogError(
                "[CLOSE-PROJECT] Closure INCOMPLETE for ProjectId={ProjectId}: {Revoked} of {Total} grants " +
                "revoked, {Failed} still ACTIVE. Participants retain access. Retry the closure.",
                request.ProjectId, revokedCount, activeRecords.Count, failedCount);

            return ClosureIncomplete(
                httpContext,
                ClosurePartialRevocationReason,
                $"Revoked {revokedCount} of {activeRecords.Count} external grants for project " +
                $"{request.ProjectId}; {failedCount} could not be deactivated and remain ACTIVE. " +
                "Those participants still have access. Retry the closure.",
                accessRecordsRevoked: revokedCount);
        }

        if (!containerCleared)
        {
            return ClosureIncomplete(
                httpContext,
                ClosureContainerNotClearedReason,
                $"All {revokedCount} external grants for project {request.ProjectId} were revoked, but the " +
                $"SPE container '{request.ContainerId}' could not be cleared of external members, who may " +
                "retain file access. Retry the closure.",
                accessRecordsRevoked: revokedCount);
        }

        logger.LogInformation(
            "[CLOSE-PROJECT] Project closure complete: ProjectId={ProjectId}, " +
            "AccessRecordsRevoked={Revoked}, SpeRemovedCount={SpeRemoved}, AffectedContacts={Contacts}, " +
            "OrganizationGrants={OrgGrants}",
            request.ProjectId, revokedCount, speRemovedCount, affectedContactIds.Count, organizationGrantCount);

        return Results.Ok(new CloseProjectResponse(
            AccessRecordsRevoked: revokedCount,
            SpeContainerMembersRemoved: speRemovedCount,
            AffectedContactIds: affectedContactIds));
    }

    /// <summary>
    /// The single "closure did not complete" shape: 500 + ProblemDetails carrying a machine-readable
    /// reason code (ADR-003) and the correlation id (ADR-019).
    /// </summary>
    /// <remarks>
    /// <c>accessRecordsRevoked</c> is surfaced as an extension rather than dropped, because "we revoked
    /// none of them" and "we revoked eleven of twelve" call for different operator responses, and a bare
    /// 500 cannot distinguish them.
    /// </remarks>
    private static IResult ClosureIncomplete(
        HttpContext httpContext, string reasonCode, string detail, int accessRecordsRevoked)
        => Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Project closure incomplete",
            detail: detail,
            type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            extensions: new Dictionary<string, object?>
            {
                ["reasonCode"] = reasonCode,
                ["accessRecordsRevoked"] = accessRecordsRevoked,
                ["traceId"] = httpContext.TraceIdentifier
            });

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Builds the OData filter selecting a project's ACTIVE grant rows for cascade-revoke.
    ///
    /// <para>
    /// Bug fix (task 070): the grant table's project lookup value field is <c>_sprk_project_value</c>
    /// (attribute <c>sprk_project</c>), NOT <c>_sprk_projectid_value</c>. The prior name matched ZERO rows
    /// (invalid field), so close-project silently revoked nothing. Verified live against
    /// <c>sprk_externalrecordaccess</c> metadata and mirrors task 028's working read-side filter.
    /// </para>
    ///
    /// Internal (not private) so the test assembly can regression-guard the exact field name.
    /// </summary>
    internal static string BuildActiveProjectGrantsFilter(Guid projectId)
        => $"_sprk_project_value eq {projectId} and statecode eq 0";

    /// <summary>
    /// The columns the cascade reads from <c>sprk_externalrecordaccess</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Bug fix (task 016, finding A-12).</b> This previously selected
    /// <c>_sprk_contactid_value</c> — an attribute that does not exist. Live metadata for
    /// <c>sprk_externalrecordaccess</c> declares the lookup <c>sprk_contact</c>, which Dataverse projects
    /// as <c>_sprk_contact_value</c>; there is no <c>sprk_contactid</c> at all. A <c>$select</c> naming a
    /// nonexistent column returns 400, so EVERY closure failed and revoked nothing. Task 070 had already
    /// corrected the sibling project lookup here (<c>_sprk_projectid_value</c> →
    /// <c>_sprk_project_value</c>) and left this one on the same stale <c>*id_value</c> form.</para>
    ///
    /// <para>The schema docs under <c>src/solutions/.../sprk_externalrecordaccess/views-schema.md</c>
    /// still say <c>sprk_contactid</c> and are wrong; the runtime read path
    /// (<see cref="ExternalParticipationService"/>) and live metadata agree on
    /// <c>_sprk_contact_value</c>.</para>
    ///
    /// <para><c>_sprk_organization_value</c> is selected so organization grants can be identified and
    /// logged, not merely swept anonymously.</para>
    ///
    /// Internal (not private) so the test assembly can regression-guard the exact column names —
    /// the failure mode is silent (a wrong name reads as "no grants to revoke", per task 070).
    /// </remarks>
    internal const string ActiveGrantSelect =
        "sprk_externalrecordaccessid,_sprk_contact_value,_sprk_organization_value";

    internal const string ClosureEnumerationFailedReason = "sdap.closure.incomplete.enumeration_failed";
    internal const string ClosurePartialRevocationReason = "sdap.closure.incomplete.partial_revocation";
    internal const string ClosureContainerNotClearedReason = "sdap.closure.incomplete.container_not_cleared";

    /// <summary>
    /// Queries all active sprk_externalrecordaccess records for the given project — contact grants AND
    /// organization grants.
    /// </summary>
    /// <remarks>
    /// <para><b>Bug fix (task 016, finding A-12), second half.</b> The projection previously required
    /// <c>_sprk_contactid_value.HasValue</c>, which discards every row with no contact — and a row with no
    /// contact is exactly how this schema represents an ORGANIZATION grant (the discriminator
    /// <see cref="ExternalGrantKey"/> and <see cref="ExternalParticipationService"/> both key on). So even
    /// with the column name corrected, closing a project would have left every organization grant active,
    /// and every member of those organizations with access to the closed project.</para>
    ///
    /// <para>The only row now discarded is one with no usable id: it cannot be addressed by a PATCH, so it
    /// cannot be deactivated. Discarding it silently would be a false success, so it is counted as a
    /// deactivation failure by the caller instead — see <see cref="DeactivateAccessRecordsAsync"/>.</para>
    ///
    /// <para>Exceptions propagate to <see cref="Handle"/>, which converts them into an explicit
    /// "closure incomplete" response. They are logged here because this is where the projectId and the
    /// emitted query are in scope.</para>
    /// </remarks>
    private static async Task<IReadOnlyList<ExternalAccessRecord>> QueryActiveAccessRecordsAsync(
        DataverseWebApiClient dataverseClient,
        Guid projectId,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var rows = await dataverseClient.QueryAsync<ExternalAccessRow>(
                ExternalAccessEntitySet,
                filter: BuildActiveProjectGrantsFilter(projectId),
                select: ActiveGrantSelect,
                cancellationToken: ct);

            return rows
                .Select(r => new ExternalAccessRecord(
                    r.sprk_externalrecordaccessid ?? Guid.Empty,
                    r._sprk_contact_value,
                    r._sprk_organization_value))
                .ToList();
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
    /// </summary>
    /// <returns>
    /// How many rows were deactivated, and how many were NOT. A non-zero failure count means
    /// participants retain access and the closure must not be reported as complete.
    /// </returns>
    /// <remarks>
    /// One row's failure does not abort the sweep: every other participant should still lose access, and
    /// stopping at the first error would leave MORE access standing than continuing. But the failures are
    /// counted and returned rather than swallowed — the prior version discarded them and returned only
    /// the success count, so a closure that revoked 2 of 12 grants still answered <c>200 OK</c>.
    /// </remarks>
    private static async Task<(int Revoked, int Failed)> DeactivateAccessRecordsAsync(
        DataverseWebApiClient dataverseClient,
        IReadOnlyList<ExternalAccessRecord> records,
        ILogger logger,
        CancellationToken ct)
    {
        int revokedCount = 0;
        int failedCount = 0;

        // Deactivate payload: statecode=1 (Inactive), statuscode=2 (Inactive)
        var deactivatePayload = new Dictionary<string, object>
        {
            ["statecode"] = 1,
            ["statuscode"] = 2
        };

        foreach (var record in records)
        {
            // A row that arrived without an id cannot be addressed by a PATCH, so it cannot be
            // deactivated. It is a failure, not a row to skip quietly — the grant is still active.
            if (record.RecordId == Guid.Empty)
            {
                failedCount++;
                logger.LogError(
                    "[CLOSE-PROJECT] Active grant returned with no usable record id ({Grantee}) — it " +
                    "cannot be deactivated and REMAINS ACTIVE.",
                    record.GranteeDescription);
                continue;
            }

            try
            {
                await dataverseClient.UpdateAsync(
                    ExternalAccessEntitySet,
                    record.RecordId,
                    deactivatePayload,
                    ct);

                revokedCount++;
                logger.LogDebug(
                    "[CLOSE-PROJECT] Deactivated access record {RecordId} ({Grantee})",
                    record.RecordId, record.GranteeDescription);
            }
            catch (Exception ex)
            {
                failedCount++;
                logger.LogError(ex,
                    "[CLOSE-PROJECT] Failed to deactivate access record {RecordId} ({Grantee}). " +
                    "It REMAINS ACTIVE. Continuing with the rest.",
                    record.RecordId, record.GranteeDescription);
            }
        }

        return (revokedCount, failedCount);
    }

    /// <summary>
    /// Invalidates Redis participation cache entries for all affected Contacts.
    /// Uses fire-and-forget per contact to avoid blocking the response on cache errors.
    /// </summary>
    /// <remarks>
    /// <para><b>Organization grants are not eagerly invalidated.</b> The participation cache is keyed per
    /// contact, and an organization grant names no contact — invalidating its members would require an
    /// organization → members expansion that does not exist on this path today. Members therefore fall
    /// back to the ADR-009 TTL (60s, <c>ExternalParticipationService.CacheTtl</c>) instead of clearing
    /// immediately.</para>
    ///
    /// <para>That is a bounded, self-healing staleness window on a cache — not retained authorization: the
    /// grant row itself is already inactive, so nothing re-populates the entry. Building the expansion
    /// here would add a new query surface for a ≤60s window (CLAUDE.md §11), and closure is an
    /// administrative action, not a hot path. Worth revisiting only if the TTL is ever raised.</para>
    /// </remarks>
    private static async Task InvalidateContactCachesAsync(
        ITenantCache cache,
        string? tenantId,
        IReadOnlyList<Guid> contactIds,
        ILogger logger,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(tenantId))
        {
            logger.LogWarning(
                "[CLOSE-PROJECT] No tenant claim found — skipping cache invalidation for {Count} Contacts",
                contactIds.Count);
            return;
        }

        foreach (var contactId in contactIds)
        {
            try
            {
                await cache.RemoveAsync(
                    tenantId, ExternalAccessResource, contactId.ToString(), CacheVersion,
                    ct: ct);
                logger.LogDebug(
                    "[CLOSE-PROJECT] Invalidated Redis cache for Contact {ContactId}", contactId);
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

    /// <summary>
    /// Extracts the Azure AD tenant ID ('tid' claim) from the authenticated HttpContext.
    /// Returns null when no claim is present (in which case cache invalidation is skipped).
    /// </summary>
    private static string? ExtractTenantId(HttpContext httpContext)
        => httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

    // =========================================================================
    // Types
    // =========================================================================

    /// <summary>
    /// One active grant the cascade must deactivate, whoever holds it.
    /// </summary>
    /// <param name="RecordId">The row id, or <see cref="Guid.Empty"/> if Dataverse returned none.</param>
    /// <param name="ContactId">The grantee contact — <c>null</c> for an organization grant.</param>
    /// <param name="OrganizationId">
    /// The organization. On a contact grant this is the contact's firm recorded as metadata; on an
    /// organization grant it is the grantee. <see cref="ExternalGrantKey"/> documents the distinction.
    /// </param>
    /// <remarks>
    /// Internal (not private) so the test assembly can drive the cascade through the
    /// <see cref="DataverseWebApiClient"/> virtual seam. While this type was <c>private</c> no test could
    /// name <c>QueryAsync&lt;ExternalAccessRow&gt;</c>, which is why A-12 survived to production and why
    /// the pre-existing "propagates exception" unit test asserted nothing (ADR-038 §4 / ban B8: use
    /// <c>InternalsVisibleTo</c>, never reflection).
    /// </remarks>
    internal sealed record ExternalAccessRecord(Guid RecordId, Guid? ContactId, Guid? OrganizationId)
    {
        /// <summary>An organization grant is the row with no contact at all.</summary>
        public bool IsOrganizationGrant => ContactId is null;

        /// <summary>Log-safe description of who holds this grant.</summary>
        public string GranteeDescription => IsOrganizationGrant
            ? $"organization {OrganizationId}"
            : $"contact {ContactId}";
    }

    /// <summary>
    /// Dataverse OData row for sprk_externalrecordaccess. Used only for deserialization here.
    /// Field names mirror the <c>_value</c> projections named in <see cref="ActiveGrantSelect"/>.
    /// </summary>
    internal sealed class ExternalAccessRow
    {
        public Guid? sprk_externalrecordaccessid { get; set; }
        public Guid? _sprk_contact_value { get; set; }
        public Guid? _sprk_organization_value { get; set; }
    }
}
