using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/close-project
///
/// Called by internal users (Core Users / admins) to close a Secure Project.
/// Closing a project cascades revocation of all external access:
///   1. Deactivates all active sprk_externalrecordaccess records for the project —
///      contact grants AND organization grants
///   2. Removes THOSE GRANTEES' permissions from the project's OWN SPE container — the container the
///      SERVER derives from the project (never a client-supplied id), and only when the project is secure
///   3. Invalidates Redis participation cache for all affected Contacts
///
/// <para><b>Task 166 (route-authorization sweep finding S-39 + amendment (e)).</b> Two defects, one step.
/// (1) The container was <c>CloseProjectRequest.ContainerId</c>, a free body string, while DelegationRuleFilter
/// authorized only <c>ProjectId</c> — so a caller with Write on ANY project could name ANY container. The field is
/// deleted (task 085 precedent) and the container is derived with
/// <see cref="RecordContainerResolver.ResolveForRecordAsync(string, Guid, CancellationToken)"/>: ONLY a
/// <c>ResolvedSecure</c> project's own container is touched; a non-secure project's derived container is the
/// SHARED business-unit container and is never touched. (2) The removal was
/// <c>RemoveAllExternalMembersAsync</c>, which deleted EVERY permission with a user identity — internal users'
/// included. It now removes exactly the permissions of the grantees whose grants this closure revoked (contacts,
/// and every active member of a revoked organization grant), through the same email-keyed
/// <see cref="SpeContainerMembershipService.RemoveMembershipsAsync"/> the single-grant revoke uses.</para>
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
/// Follows ADR-009: Redis cache invalidated for each affected Contact and organization member (task 137).
/// </summary>
public static class ProjectClosureEndpoint
{
    private const string ExternalAccessEntitySet = "sprk_externalrecordaccesses";
    // Cache invalidation (task 137): ExternalParticipationService.InvalidateGrantSetsAsync — the ONE routine,
    // which owns the key, every tenant it can be cached under, and the organization → members expansion.

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
                "removes those grantees' permissions from the project's own SPE container (derived server-side; " +
                "secure projects only — a client-supplied containerId is ignored), " +
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
    /// <param name="request">The close project request containing ProjectId.</param>
    /// <param name="dataverseClient">Dataverse Web API client for querying and updating records.</param>
    /// <param name="speContainerMembership">SPE container membership service for removing the revoked grantees.</param>
    /// <param name="participations">The grant-data service whose single invalidation routine clears every affected
    /// contact's cached grant set — organization members included (task 137).</param>
    /// <param name="containerResolver">Derives the project's own container (task 166) — never the request.</param>
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
        ExternalParticipationService participations,
        RecordContainerResolver containerResolver,
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
            "[CLOSE-PROJECT] Starting project closure: ProjectId={ProjectId}, TraceId={TraceId}",
            request.ProjectId, httpContext.TraceIdentifier);

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

        // Organization grants carry no contact; their organizations are expanded to every ACTIVE member by the
        // cache invalidation in Step 4 (task 137). AffectedContactIds in the response stays the grants' own contacts.
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

        // Step 3: Remove the revoked grantees' permissions from the project's OWN container.
        //
        // Guarded for the same reason enumeration is: container membership IS access, so a failure here
        // leaves external users able to reach the project's files. Letting it escape as an unhandled
        // exception would also discard the fact that N grants WERE revoked, which is the single most
        // useful thing to tell the operator. The failure is recorded and reported after Step 4, so cache
        // invalidation still happens.
        //
        // Task 166 (S-39 + amendment e): WHICH container is the SERVER's answer for THIS project — the
        // body's ContainerId is gone — and WHO is removed is exactly this closure's revoked grantees, never
        // every user permission (which stripped internal users too). See DeriveRecordOwnContainerAsync and
        // RemoveRevokedGranteesAsync.
        var (closureContainerId, containerCleared) = await DeriveRecordOwnContainerAsync(
            containerResolver, "sprk_project", request.ProjectId, logger, ct);

        int speRemovedCount = 0;
        if (closureContainerId is not null)
        {
            try
            {
                var removal = await RemoveRevokedGranteesAsync(
                    speContainerMembership, dataverseClient, closureContainerId, activeRecords, logger, ct);

                speRemovedCount = removal.Removed;
                containerCleared = removal.Complete;

                if (removal.Complete)
                {
                    logger.LogInformation(
                        "[CLOSE-PROJECT] Removed {Count} revoked grantee permission(s) from project {ProjectId}'s " +
                        "own container {ContainerId}; internal users' permissions were not touched.",
                        speRemovedCount, request.ProjectId, closureContainerId);
                }
                else
                {
                    logger.LogError(
                        "[CLOSE-PROJECT] Container {ContainerId} NOT fully cleared of the revoked grantees: " +
                        "{Removed} removed, {Unresolved} could not be confirmed removed and may retain FILE access.",
                        closureContainerId, removal.Removed, removal.Unresolved);
                }
            }
            catch (Exception ex)
            {
                containerCleared = false;
                logger.LogError(ex,
                    "[CLOSE-PROJECT] Failed to clear the revoked grantees from container {ContainerId}. " +
                    "External users may retain FILE access even though {Revoked} grants were revoked.",
                    closureContainerId, revokedCount);
            }
        }

        // Step 4: Invalidate the grant cache of every affected contact — the ONE routine (task 137). Runs
        // unconditionally — it only ever removes access, so it is worth doing even when an earlier step failed.
        // Organization grants expand to every ACTIVE member (they used to wait out the 60-second TTL), and every
        // tenant a grant set can be cached under is cleared, the CIAM one included. Never throws.
        var closedOrganizationIds = activeRecords
            .Where(r => r.IsOrganizationGrant && r.OrganizationId.HasValue)
            .Select(r => r.OrganizationId!.Value)
            .Distinct()
            .ToList();
        await participations.InvalidateGrantSetsAsync(affectedContactIds, closedOrganizationIds, CancellationToken.None);

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
                "project's SPE container could not be determined or could not be cleared of those grantees, " +
                "who may retain file access. Retry the closure.",
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
    /// Task 166 (S-39, and amendment (b) for <c>/revoke</c>): the container a closure or a revoke may touch,
    /// derived by the SERVER from the authorized record — the project being closed, or the grant's root.
    /// </summary>
    /// <returns>
    /// <c>(containerId, true)</c> — the project is SECURE and owns a container: that container, and only it, may
    /// be cleaned. <c>(null, true)</c> — the project is NOT secure (<c>ResolvedFallback</c>: its derived container
    /// is the SHARED business-unit container, and clearing grantees from it could remove access other records
    /// rely on) or has no container (<c>Unresolved</c>): the container step is skipped and the closure is not
    /// failed for it. <c>(null, false)</c> — the decision could not be made (<c>FailClosed</c>, a secure project
    /// whose container is blank, a resolver <see cref="SdapProblemException"/> or any other fault): the closure
    /// reports <see cref="ClosureContainerNotClearedReason"/>, because "we could not tell which container" is not
    /// "nothing to clean".
    /// </returns>
    /// <remarks>
    /// <para>The <see cref="DelegationRuleFilter"/> Write gate on the record has already run (close: the
    /// <c>ProjectId</c>; revoke: the grant row's root), so the record named here is one the caller may act on; the
    /// container follows from it by construction, and the client cannot name a different one (a sent
    /// <c>containerId</c> is an unknown JSON member, ignored).</para>
    /// <para><b>OWN container, never an ancestor's (task 166 r1).</b> This asks
    /// <see cref="RecordContainerResolver.ResolveOwnContainerAsync"/>, not the content-placement question
    /// <see cref="RecordContainerResolver.ResolveForRecordAsync(string, Guid, CancellationToken)"/>: since task 155 the
    /// latter answers a NON-secure project or work assignment filed under a secure root with the ROOT's container,
    /// and stripping this record's revoked grantees from there would remove permissions that a grant on the root
    /// itself may still justify. A record that is not itself secure owns no isolated container, so its step is
    /// skipped; a record whose secure flag is unreadable is "could not be determined".</para>
    /// </remarks>
    internal static async Task<(string? ContainerId, bool Decided)> DeriveRecordOwnContainerAsync(
        RecordContainerResolver containerResolver,
        string entityLogicalName,
        Guid recordId,
        ILogger logger,
        CancellationToken ct)
    {
        ContainerDecision decision;
        try
        {
            decision = await containerResolver.ResolveOwnContainerAsync(entityLogicalName, recordId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[CONTAINER-DERIVE] Could not derive {Entity} {RecordId}'s own container ({ErrorType}); the container "
                + "step cannot run and will be reported as not cleared.",
                entityLogicalName, recordId, ex is SdapProblemException problem ? problem.Code : ex.GetType().Name);
            return (null, false);
        }

        switch (decision.Outcome)
        {
            case ContainerDecisionOutcome.ResolvedSecure when !string.IsNullOrWhiteSpace(decision.ContainerId):
                return (decision.ContainerId, true);

            case ContainerDecisionOutcome.ResolvedFallback:
            case ContainerDecisionOutcome.Unresolved:
                logger.LogInformation(
                    "[CONTAINER-DERIVE] {Entity} {RecordId} is not a secure record with its own container (outcome "
                    + "{Outcome}); the container step is SKIPPED — it owns no isolated container, and neither the "
                    + "shared business-unit container nor a secure ancestor's container is ever swept on its behalf.",
                    entityLogicalName, recordId, decision.Outcome);
                return (null, true);

            default:
                // FailClosed (secure with no container, or an unreadable secure flag), or ResolvedSecure with a
                // blank container id.
                logger.LogError(
                    "[CONTAINER-DERIVE] {Entity} {RecordId}'s container decision is {Outcome} (container id present: "
                    + "{HasContainer}); the container step cannot run and will be reported as not cleared.",
                    entityLogicalName, recordId, decision.Outcome, !string.IsNullOrWhiteSpace(decision.ContainerId));
                return (null, false);
        }
    }

    /// <summary>Outcome of <see cref="RemoveRevokedGranteesAsync"/>.</summary>
    /// <param name="Removed">Permissions actually deleted.</param>
    /// <param name="Unresolved">Grantees whose permission could not be CONFIRMED absent or removed.</param>
    internal readonly record struct GranteeRemoval(int Removed, int Unresolved)
    {
        /// <summary>Every revoked grantee is confirmed to hold no permission on the container.</summary>
        public bool Complete => Unresolved == 0;
    }

    /// <summary>
    /// Task 166 (amendment e): removes from <paramref name="containerId"/> the permissions of EXACTLY the grantees
    /// this closure revoked — each contact grant's contact, and every active member of each organization grant —
    /// matched by the email key membership is written with, through ONE paged read
    /// (<see cref="SpeContainerMembershipService.RemoveMembershipsAsync"/>, the single-grant revoke's mechanism).
    /// </summary>
    /// <remarks>
    /// <para><b>Why not <c>RemoveAllExternalMembersAsync</c> any more.</b> It deleted every permission carrying a
    /// user identity, which on SPE is every individual grant — internal users included (the demo provisioning
    /// path and the SPE admin console both add internal users). Closing a project must revoke external access, not
    /// lock its own attorneys out of the files. The method had no other caller and was deleted.</para>
    /// <para><b>Fail closed, per grantee.</b> A contact with no email, an email that could not be read, an
    /// organization whose membership could not be enumerated or exceeds the sweep bound, and a per-permission
    /// failure each count as UNRESOLVED — "we could not confirm they are gone" — so the closure reports
    /// <see cref="ClosureContainerNotClearedReason"/> rather than a 200. "No permission found" on a FULLY enumerated
    /// container is the healthy broker-only answer and is not a failure.</para>
    /// </remarks>
    internal static async Task<GranteeRemoval> RemoveRevokedGranteesAsync(
        SpeContainerMembershipService speContainerMembership,
        DataverseWebApiClient dataverseClient,
        string containerId,
        IReadOnlyList<ExternalAccessRecord> revokedRecords,
        ILogger logger,
        CancellationToken ct)
    {
        var unresolved = 0;
        var contactIds = new HashSet<Guid>();

        foreach (var record in revokedRecords)
        {
            if (record.ContactId is { } contactId)
            {
                contactIds.Add(contactId);
                continue;
            }

            if (record.OrganizationId is not { } organizationId)
            {
                // An organization grant with no organization names nobody we can find. Unknown, not absent.
                unresolved++;
                continue;
            }

            OrganizationMemberSet members;
            try
            {
                members = await ExternalOrganizationMembership.QueryActiveMembersAsync(dataverseClient, organizationId, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "[CLOSE-PROJECT] Could not enumerate the active members of organization {OrganizationId}; their "
                    + "container permissions cannot be removed and may remain.", organizationId);
                unresolved++;
                continue;
            }

            if (members.ExceededBound)
            {
                logger.LogError(
                    "[CLOSE-PROJECT] Organization {OrganizationId} has more than {Bound} active members; its members' "
                    + "container permissions were NOT swept. Escalate for a bulk cleanup.",
                    organizationId, ExternalOrganizationMembership.MaxMembersPerSweep);
                unresolved++;
                continue;
            }

            foreach (var memberId in members.ContactIds)
            {
                contactIds.Add(memberId);
            }
        }

        var emails = new List<string>(contactIds.Count);
        foreach (var contactId in contactIds)
        {
            string? email;
            try
            {
                email = await RevokeExternalAccessEndpoint.ResolveContactEmailAsync(dataverseClient, contactId, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "[CLOSE-PROJECT] Could not read the email of contact {ContactId}; their container permission cannot "
                    + "be matched and may remain.", contactId);
                unresolved++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                logger.LogWarning(
                    "[CLOSE-PROJECT] Contact {ContactId} has no emailaddress1 — the key container membership is "
                    + "written with — so any permission they hold cannot be matched.", contactId);
                unresolved++;
                continue;
            }

            emails.Add(email);
        }

        if (emails.Count == 0)
        {
            return new GranteeRemoval(0, unresolved);
        }

        var results = await speContainerMembership.RemoveMembershipsAsync(containerId, emails, ct).ConfigureAwait(false);

        var removed = 0;
        foreach (var email in emails.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!results.TryGetValue(email, out var result))
            {
                unresolved++;
                continue;
            }

            if (result.Success)
            {
                removed++;
            }
            else if (result.Error?.StartsWith(SpeContainerMembershipService.NoPermissionFoundError,
                         StringComparison.OrdinalIgnoreCase) != true)
            {
                unresolved++;
            }
        }

        return new GranteeRemoval(removed, unresolved);
    }

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
