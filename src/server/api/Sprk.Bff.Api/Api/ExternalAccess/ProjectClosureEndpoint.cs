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
/// and every active member of a revoked organization grant), through the same identity-keyed
/// <see cref="SpeContainerMembershipService.RemoveMembershipsAsync"/> the single-grant revoke uses. #2104: on a secure
/// project the grantees of grants revoked EARLIER are swept too, so a retry after a container failure is real.</para>
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
        CancellationToken ct,
        // The identity binding store for THIS environment (registered by ExternalAccessModule): a grantee whose binding read
        // EMPTY and who has no email has "nothing to clear" only while the binding columns' read is proven
        // (RevokeExternalAccessEndpoint.ResolveContactMembershipKeyAsync). Optional so direct callers keep compiling; without
        // it that case fails closed.
        IContactIdentityStore? identityStore = null)
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

        if (activeRecords.Count > MaxActiveGrantsPerClosure)
        {
            // #2104 round 2 (F2): the active rows are read as ONE bounded page ($top = bound + 1). More than the bound is
            // not revoked on a partial list (Steps 2-4 stay unreachable, as for an enumeration failure) and is NAMED, not
            // "retry": a retry reads the same rows and meets the same bound.
            logger.LogError(
                "[CLOSE-PROJECT] ProjectId={ProjectId} has more than {Bound} active external grants, more than one closure "
                + "may read. Closure ABORTED with zero grants revoked; an administrator must act.",
                request.ProjectId, MaxActiveGrantsPerClosure);

            return ClosureIncomplete(
                httpContext,
                ClosureTooManyGrantsReason,
                $"Project {request.ProjectId} has more than {MaxActiveGrantsPerClosure} active external grants, more than "
                + "one closure may read. No grants were revoked and external access is UNCHANGED. Retrying the closure "
                + "will not change that. An administrator must deactivate the project's external access records "
                + "(sprk_externalrecordaccess) in Dataverse and remove those grantees' permissions from the project's "
                + "container in the SPE admin app (the container's Permissions).",
                accessRecordsRevoked: 0);
        }

        if (activeRecords.Count == 0)
        {
            // Not an early 200 any more (#2104, pass-1 F1 / pass-2 F3): a closure that revoked its grants but could not
            // clear the container answers container_not_cleared + "Retry the closure" — and that retry finds NO active
            // grant. It must still re-run the container step over the grantees revoked earlier, or "retry" is false.
            logger.LogInformation(
                "[CLOSE-PROJECT] No active access records found for ProjectId={ProjectId}. Nothing to revoke; checking "
                + "the container for grantees revoked earlier.", request.ProjectId);
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
        var (revokedCount, failedCount) = activeRecords.Count == 0
            ? (0, 0)
            : await DeactivateAccessRecordsAsync(dataverseClient, activeRecords, logger, ct);

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
        //
        // #2104 (pass-1 F1 / pass-2 F3): the grantees are this call's revoked rows PLUS every grant the project revoked
        // EARLIER (a previous closure whose container step failed, or a single-grant revoke). Without them a retry —
        // which finds the grants already inactive — would sweep nobody and answer 200 while a grantee kept file access,
        // making "Retry the closure" false. Removal is idempotent: a grantee already gone is NoPermissionFound.
        var (closureContainerId, containerDecided) = await DeriveRecordOwnContainerAsync(
            containerResolver, "sprk_project", request.ProjectId, logger, ct);

        // The revoked history is needed where a container will be swept, and — when nothing is active — to tell a
        // project that never had a grant (nothing to clear, wherever its container is) from one with grantees to clear.
        RevokedGrantHistory? history = null;
        if (closureContainerId is not null || (!containerDecided && activeRecords.Count == 0))
        {
            history = await TryQueryRevokedAccessRecordsAsync(dataverseClient, request.ProjectId, logger, ct);
        }

        var historyRead = history is { Complete: true };
        var ownsNoContainer = containerDecided && closureContainerId is null;
        if (activeRecords.Count == 0 && (ownsNoContainer || history is { Complete: true, Rows.Count: 0 }))
        {
            // Nothing to revoke, and nobody to clear: the project is not secure (its container step is skipped), or it
            // never had a grant. The pre-#2104 answer, unchanged.
            return Results.Ok(new CloseProjectResponse(
                AccessRecordsRevoked: 0,
                SpeContainerMembersRemoved: 0,
                AffectedContactIds: []));
        }

        var containerCleared = ownsNoContainer;
        int speRemovedCount = 0;
        GranteeRemoval? granteeRemoval = null;
        if (closureContainerId is not null)
        {
            try
            {
                var sweep = GranteesToSweep(activeRecords, history?.Rows ?? [], logger);
                var removal = await RemoveRevokedGranteesAsync(
                    speContainerMembership, dataverseClient, closureContainerId, sweep, historyRead, logger, ct, identityStore);

                speRemovedCount = removal.Removed;
                granteeRemoval = removal;
                // The earlier grantees could not be read (or were too many to read): whoever they are, they are not
                // confirmed off the container, unless the container itself holds no contact permission any more (F1).
                containerCleared = removal.ContainerProvenClean || (removal.Complete && historyRead);

                if (containerCleared)
                {
                    logger.LogInformation(
                        "[CLOSE-PROJECT] Removed {Count} revoked grantee permission(s) from project {ProjectId}'s " +
                        "own container {ContainerId} ({NoKey} grantee(s) with no identity key had nothing to clear); " +
                        "internal users' permissions were not touched.",
                        speRemovedCount, request.ProjectId, closureContainerId, removal.NoMembershipKey);
                }
                else
                {
                    logger.LogError(
                        "[CLOSE-PROJECT] Container {ContainerId} NOT fully cleared of the revoked grantees: " +
                        "{Removed} removed, {Unresolved} could not be confirmed removed and may retain FILE access " +
                        "(earlier grants read: {HistoryRead}).",
                        closureContainerId, removal.Removed, removal.Unresolved, historyRead);
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

        // #2104 round 4 (F1): the two "a retry cannot help" claims below (an oversized organization, too many revoked
        // grants) hold only while the container's own answer is definite. When it could not be established — the
        // container question faulted, or the step threw — a retry may yet prove it clean, so neither claim is made and
        // the closure answers the retryable container_not_cleared.
        var containerAnswerDefinite =
            granteeRemoval is
            {
                ContainerState: SpeContainerContactPermissionState.Clean or SpeContainerContactPermissionState.ContactPermissionRemains
            };

        // What the container step established, for every incomplete answer below (pass-2 F2): a partial revocation
        // must not swallow it, and an oversized organization's non-retryable instruction must reach the caller.
        var container = new ContainerStepReport(
            // Null: the project owns no container of its own, so there was nothing of this closure's to clear.
            Cleared: ownsNoContainer ? null : containerCleared,
            // Unknown when the container was never established, the earlier grants could not be read, or the step threw.
            GranteesNotCleared: granteeRemoval is { } counted && (historyRead || counted.ContainerProvenClean)
                ? counted.Unresolved
                : null,
            OrganizationsTooLarge: containerAnswerDefinite ? granteeRemoval!.Value.OrganizationsTooLarge : 0,
            // F2: the project's own container was swept, but its revoked grants exceed one read; not fixed by a retry.
            RevokedGrantsOverLimit: closureContainerId is not null && !containerCleared && history is { OverLimit: true }
                && containerAnswerDefinite,
            // #2104 round 3: grantees whose match was ambiguous or unverifiable — nothing removed; not fixed by a retry.
            PermissionsAmbiguous: granteeRemoval?.PermissionsAmbiguous ?? 0);

        // Step 4: Invalidate the grant cache of every affected contact — the ONE routine (task 137). Runs
        // unconditionally — it only ever removes access, so it is worth doing even when an earlier step failed.
        // Organization grants expand to every ACTIVE member (they used to wait out the 60-second TTL), and every
        // tenant a grant set can be cached under is cleared, the CIAM one included. Never throws. (No active grant was
        // revoked by this call when there were none — nothing to invalidate.)
        if (activeRecords.Count > 0)
        {
            var closedOrganizationIds = activeRecords
                .Where(r => r.IsOrganizationGrant && r.OrganizationId.HasValue)
                .Select(r => r.OrganizationId!.Value)
                .Distinct()
                .ToList();
            await participations.InvalidateGrantSetsAsync(affectedContactIds, closedOrganizationIds, CancellationToken.None);
        }

        // A row we could not deactivate is a participant who still has access. Reporting 200 here would
        // tell the operator the project is closed while it is not — the same false-success shape the
        // ADR-003 constraint forbids for the enumeration failure above. Steps 3 and 4 already ran because
        // both only ever REMOVE access, so running them makes the partial state strictly less open; the
        // failure is reported afterwards. Closure is idempotent (deactivating an inactive row is a no-op),
        // so the correct operator response is simply to retry — and the container's state rides along.
        if (failedCount > 0)
        {
            logger.LogError(
                "[CLOSE-PROJECT] Closure INCOMPLETE for ProjectId={ProjectId}: {Revoked} of {Total} grants " +
                "revoked, {Failed} still ACTIVE. Participants retain access. Retry the closure. Container cleared: " +
                "{ContainerCleared}.",
                request.ProjectId, revokedCount, activeRecords.Count, failedCount, containerCleared);

            return ClosureIncomplete(
                httpContext,
                ClosurePartialRevocationReason,
                $"Revoked {revokedCount} of {activeRecords.Count} external grants for project " +
                $"{request.ProjectId}; {failedCount} could not be deactivated and remain ACTIVE. " +
                "Those participants still have access. Retry the closure." +
                ContainerAddendum(container),
                accessRecordsRevoked: revokedCount,
                container);
        }

        if (container.RevokedGrantsOverLimit)
        {
            // #2104 round 2 (F2): a retry reads the same revoked rows and meets the same bound. Named, not "retry".
            return ClosureIncomplete(
                httpContext,
                ClosureTooManyGrantsReason,
                $"{GrantsClause(request.ProjectId, activeRecords.Count, revokedCount)} "
                + TooManyRevokedGrantsText("this")
                + NonRetryableGranteeSentences(container, "this"),
                accessRecordsRevoked: revokedCount,
                container);
        }

        if (!containerCleared && container.OrganizationsTooLarge > 0)
        {
            // #2103 follow-up: a retry meets the same sweep bound, so "Retry the closure" would be false. Named
            // separately (non-retryable) and still fail-closed. Any OTHER unresolved grantee is mentioned too.
            return ClosureIncomplete(
                httpContext,
                ClosureOrganizationTooLargeReason,
                $"{GrantsClause(request.ProjectId, activeRecords.Count, revokedCount)}" +
                NonRetryableGranteeSentences(container, "this") + OtherUnresolvedSentence(container),
                accessRecordsRevoked: revokedCount,
                container);
        }

        if (!containerCleared && container.PermissionsAmbiguous > 0)
        {
            // #2104 round 3: a grantee's match was ambiguous or unverifiable, so nothing was removed for them — and a retry
            // reads the same permissions and refuses again. Named (non-retryable), still fail-closed. Precedence, the
            // rule above (ClosurePermissionAmbiguousReason): a non-retryable cause wins over a transient one, and the
            // other unresolved grantees are counted in the detail.
            return ClosureIncomplete(
                httpContext,
                ClosurePermissionAmbiguousReason,
                $"{GrantsClause(request.ProjectId, activeRecords.Count, revokedCount)}" +
                NonRetryableGranteeSentences(container, "this") + OtherUnresolvedSentence(container),
                accessRecordsRevoked: revokedCount,
                container);
        }

        if (!containerCleared)
        {
            return ClosureIncomplete(
                httpContext,
                ClosureContainerNotClearedReason,
                $"{GrantsClause(request.ProjectId, activeRecords.Count, revokedCount)} the project's SPE container " +
                "could not be determined or could not be cleared of those grantees, who may retain file access. " +
                "Retry the closure.",
                accessRecordsRevoked: revokedCount,
                container);
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
    /// <para>#2104 (pass-2 F2): every answer that got as far as the container step also carries what that step
    /// established — <c>containerCleared</c> (<c>null</c> when the project owns no container of its own),
    /// <c>granteesNotCleared</c> (<c>null</c> when it could not be counted) and <c>organizationsTooLarge</c> — so a
    /// partial revocation no longer hides an uncleared container or an oversized organization behind "retry".</para>
    /// </remarks>
    private static IResult ClosureIncomplete(
        HttpContext httpContext, string reasonCode, string detail, int accessRecordsRevoked,
        ContainerStepReport? container = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["reasonCode"] = reasonCode,
            ["accessRecordsRevoked"] = accessRecordsRevoked,
            ["traceId"] = httpContext.TraceIdentifier
        };

        if (container is { } step)
        {
            extensions["containerCleared"] = step.Cleared;
            extensions["granteesNotCleared"] = step.GranteesNotCleared;
            extensions["organizationsTooLarge"] = step.OrganizationsTooLarge;
        }

        return Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Project closure incomplete",
            detail: detail,
            type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            extensions: extensions);
    }

    /// <summary>What the container step established, carried by every incomplete answer that reached it.</summary>
    /// <param name="Cleared"><c>true</c>: every revoked grantee is confirmed to hold no permission on the project's own
    /// container. <c>false</c>: not confirmed (container undetermined, a grantee unresolved, earlier grants unreadable,
    /// or the step threw). <c>null</c>: the project is not secure and owns no container, so the step does not apply.</param>
    /// <param name="GranteesNotCleared">Grantees not confirmed removed; <c>null</c> when that could not be counted.</param>
    /// <param name="OrganizationsTooLarge">Revoked organizations over the sweep bound — not fixed by a retry. 0 when the
    /// container's own answer was not established (#2104 round 4): a retry may then prove their members absent.</param>
    /// <param name="RevokedGrantsOverLimit">#2104 round 2 (F2): the project's revoked grants exceed one read
    /// (<see cref="MaxRevokedGrantsPerSweep"/>) and the container definitely holds a permission a contact grant could
    /// account for — not fixed by a retry (round 4: never set when the container's answer was not established). Carried
    /// in the detail, not as an extension.</param>
    /// <param name="PermissionsAmbiguous">#2104 round 3: grantees (counted in <paramref name="GranteesNotCleared"/> too)
    /// whose match was ambiguous or unverifiable, so nothing was removed for them — not fixed by a retry. Carried in the
    /// detail and the reason code, not as an extension.</param>
    internal readonly record struct ContainerStepReport(
        bool? Cleared, int? GranteesNotCleared, int OrganizationsTooLarge, bool RevokedGrantsOverLimit = false,
        int PermissionsAmbiguous = 0);

    /// <summary>The opening clause of a container-step failure: what happened to the grants themselves.</summary>
    private static string GrantsClause(Guid projectId, int activeCount, int revokedCount)
        => activeCount == 0
            ? $"Project {projectId} had no active external grants left to revoke, but"
            : $"All {revokedCount} external grants for project {projectId} were revoked, but";

    /// <summary>The oversized-organization sentence and its NON-retryable administrator instruction (#2103).</summary>
    private static string OrganizationTooLargeSentence(ContainerStepReport container, string retryScope)
        => $"{container.OrganizationsTooLarge} revoked organization(s) have more than " +
           $"{ExternalOrganizationMembership.MaxMembersPerSweep} active members, more than one closure may sweep, so " +
           "their members' SPE container permissions were NOT removed and they may retain file access. Retrying the " +
           $"closure will not change {retryScope}. An administrator must remove those members' permissions from the " +
           "project's container in the SPE admin app (the container's Permissions).";

    /// <summary>#2104 round 2 (F2): the too-many-revoked-grants text and its NON-retryable administrator instruction,
    /// starting lower-case so it continues <see cref="GrantsClause"/>'s "…, but" (round 4);
    /// <see cref="TooManyRevokedGrantsSentence"/> is the same text as a sentence of its own.</summary>
    private static string TooManyRevokedGrantsText(string retryScope)
        => $"the project has more than {MaxRevokedGrantsPerSweep} revoked external grants, more than one closure may "
           + "read, and its SPE container could not be confirmed to hold no permission of theirs, so those grantees may "
           + $"retain file access. Retrying the closure will not change {retryScope}. An administrator must remove those "
           + "grantees' permissions from the project's container in the SPE admin app (the container's Permissions).";

    /// <summary><see cref="TooManyRevokedGrantsText"/> as a sentence of its own (after a full stop).</summary>
    private static string TooManyRevokedGrantsSentence(string retryScope)
    {
        var text = TooManyRevokedGrantsText(retryScope);
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>#2104 round 3: the ambiguous-match sentence and its NON-retryable administrator instruction. A count only —
    /// the detail names no person and no address.</summary>
    private static string PermissionAmbiguousSentence(ContainerStepReport container, string retryScope)
        => $"{container.PermissionsAmbiguous} revoked grantee(s) match more than one permission on the project's SPE "
           + "container, or a permission whose holder cannot be verified, so none of those permissions was removed and "
           + $"they may retain file access. Retrying the closure will not change {retryScope}. An administrator must "
           + "resolve the container's permissions in the SPE admin app (the project's container, its Permissions).";

    /// <summary>
    /// The sentence of every grantee-level cause a retry cannot fix (#2103 oversized organizations, #2104 round 3 ambiguous
    /// matches), each with a leading space — so whichever reason code wins, the detail still names every such cause.
    /// </summary>
    private static string NonRetryableGranteeSentences(ContainerStepReport container, string retryScope)
        => (container.OrganizationsTooLarge > 0 ? " " + OrganizationTooLargeSentence(container, retryScope) : string.Empty)
           + (container.PermissionsAmbiguous > 0 ? " " + PermissionAmbiguousSentence(container, retryScope) : string.Empty);

    /// <summary>Any grantee left unconfirmed besides the oversized organizations and the ambiguous matches.</summary>
    private static string OtherUnresolvedSentence(ContainerStepReport container)
        => container.GranteesNotCleared switch
        {
            null => " Other revoked grantees could not all be confirmed removed either.",
            { } notCleared when notCleared - container.OrganizationsTooLarge - container.PermissionsAmbiguous > 0 =>
                $" {notCleared - container.OrganizationsTooLarge - container.PermissionsAmbiguous} other grantee(s) could "
                + "also not be confirmed removed.",
            _ => string.Empty,
        };

    /// <summary>The container step's state, appended to a partial revocation's detail (pass-2 F2).</summary>
    private static string ContainerAddendum(ContainerStepReport container) => container switch
    {
        { Cleared: null } => string.Empty,
        { Cleared: true } => " The revoked grantees' permissions on the project's SPE container are confirmed removed.",
        { RevokedGrantsOverLimit: true } =>
            " " + TooManyRevokedGrantsSentence("that part") + NonRetryableGranteeSentences(container, "that part"),
        { OrganizationsTooLarge: > 0 } or { PermissionsAmbiguous: > 0 } =>
            NonRetryableGranteeSentences(container, "that part") + OtherUnresolvedSentence(container),
        { GranteesNotCleared: { } notCleared } =>
            $" The project's SPE container could not be cleared of {notCleared} revoked grantee(s), who may also " +
            "retain file access.",
        _ => " The project's SPE container could not be determined or could not be cleared of the revoked grantees, " +
             "who may also retain file access.",
    };

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
    /// <param name="NoMembershipKey">#2103: grantees with no bound object id and no email, to whom no container
    /// permission can be attributed (<see cref="RevokeExternalAccessEndpoint.HasNoContainerMembershipKey"/>). Nothing to
    /// clear; not unresolved.</param>
    /// <param name="OrganizationsTooLarge">Revoked organizations (counted in <paramref name="Unresolved"/> too) whose
    /// active members exceed the sweep bound: their members were not swept, and a retry meets the same bound.</param>
    /// <param name="ContainerState">#2104 round 2 (F1), three-state since round 4: what the removal established about the
    /// container as a whole (<see cref="SpeMembershipRemoval.ContactPermissionState"/>); <see cref="SpeContainerContactPermissionState.Unknown"/>
    /// when the removal threw; <c>null</c> when no removal ran. <see cref="SpeContainerContactPermissionState.Clean"/>
    /// confirms every revoked grantee absent (<see cref="ContainerProvenClean"/>).</param>
    /// <param name="PermissionsAmbiguous">#2104 round 3: grantees (counted in <paramref name="Unresolved"/> too) whose match
    /// was ambiguous or unverifiable (<see cref="SpeContainerMembershipService.IsAmbiguousPermissionMatch"/>): nothing was
    /// removed for them, and a retry reads the same permissions and refuses again.</param>
    internal readonly record struct GranteeRemoval(
        int Removed, int Unresolved, int NoMembershipKey = 0, int OrganizationsTooLarge = 0,
        SpeContainerContactPermissionState? ContainerState = null, int PermissionsAmbiguous = 0)
    {
        /// <summary>Every revoked grantee is confirmed to hold no permission on the container.</summary>
        public bool Complete => Unresolved == 0;

        /// <summary>The container's complete read left no permission a contact grant could account for, so every revoked
        /// grantee (an oversized organization's members, an unreadable identity, the earlier grants that could not be read)
        /// is confirmed absent; <see cref="Unresolved"/> and <see cref="OrganizationsTooLarge"/> are then 0.</summary>
        public bool ContainerProvenClean => ContainerState == SpeContainerContactPermissionState.Clean;
    }

    /// <summary>
    /// Task 166 (amendment e): removes from <paramref name="containerId"/> the permissions of EXACTLY the grantees
    /// this closure revoked — each contact grant's contact, and every active member of each organization grant —
    /// matched by identity (the contact's bound object id, then its email against the permission's email and UPN; a
    /// Spaarke-managed marker grant is never matched), through ONE paged read
    /// (<see cref="SpeContainerMembershipService.RemoveMembershipsAsync"/>, the single-grant revoke's mechanism).
    /// </summary>
    /// <remarks>
    /// <para><b>Why not <c>RemoveAllExternalMembersAsync</c> any more.</b> It deleted every permission carrying a
    /// user identity, which on SPE is every individual grant — internal users included (the demo provisioning
    /// path and the SPE admin console both add internal users). Closing a project must revoke external access, not
    /// lock its own attorneys out of the files. The method had no other caller and was deleted.</para>
    /// <para><b>Fail closed, per grantee.</b> An identity that could not be read, an
    /// organization whose membership could not be enumerated or exceeds the sweep bound, and a per-permission
    /// failure each count as UNRESOLVED — "we could not confirm they are gone" — so the closure reports
    /// <see cref="ClosureContainerNotClearedReason"/> rather than a 200. "No permission found" on a FULLY enumerated
    /// container is the healthy broker-only answer and is not a failure.</para>
    /// <para><b>A grantee with NO identity key (#2103)</b> — no bound object id and no email — is neither: no container
    /// permission can be attributed to them, so there is nothing to clear — the single-grant revoke's decision, shared
    /// through <see cref="RevokeExternalAccessEndpoint.HasNoContainerMembershipKey"/>, not a second copy. It used to
    /// count as unresolved, so closing a project with one such grantee answered container_not_cleared + "retry"
    /// forever.</para>
    /// <para><b>A removal that throws (pass-2 F4)</b> leaves every keyed grantee unresolved and keeps the counts already
    /// gathered — an oversized organization stays classified as such, so the closure still names the non-retryable
    /// leaf instead of a "retry" that cannot succeed.</para>
    /// <para><b>A container that holds no contact permission clears everyone (#2104 round 2, F1).</b> When a grantee could
    /// not be enumerated or keyed (an organization over the sweep bound above all), or <paramref name="sweepComplete"/>
    /// is false (the earlier grants could not be read in full), the one paged read is asked whether any permission a
    /// contact grant could account for remains. If none does, every grantee is confirmed absent and nothing is
    /// unresolved. Otherwise the closure kept answering <c>organization_too_large</c> for EVERY later closure of a
    /// project that ever revoked an organization of more than 200 members, even with nothing of theirs on the
    /// container. An incomplete or failed read proves nothing, so it stays fail closed.</para>
    /// </remarks>
    internal static async Task<GranteeRemoval> RemoveRevokedGranteesAsync(
        SpeContainerMembershipService speContainerMembership,
        DataverseWebApiClient dataverseClient,
        string containerId,
        IReadOnlyList<ExternalAccessRecord> revokedRecords,
        bool sweepComplete,
        ILogger logger,
        CancellationToken ct,
        IContactIdentityStore? identityStore = null)
    {
        var unresolved = 0;
        var noKey = 0;
        var tooLarge = 0;
        var contactIds = new HashSet<Guid>();
        var organizationIds = new HashSet<Guid>();

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

            // The same organization revoked twice (this call's grant and an earlier one) is swept — and counted — once.
            if (!organizationIds.Add(organizationId))
            {
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
                tooLarge++;
                continue;
            }

            foreach (var memberId in members.ContactIds)
            {
                contactIds.Add(memberId);
            }
        }

        var keys = new HashSet<SpeMembershipKey>();
        foreach (var contactId in contactIds)
        {
            SpeMembershipKey key;
            try
            {
                key = await RevokeExternalAccessEndpoint.ResolveContactMembershipKeyAsync(
                        dataverseClient, identityStore, contactId, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "[CLOSE-PROJECT] Could not read the identity of contact {ContactId}; their container permission "
                    + "cannot be matched and may remain.", contactId);
                unresolved++;
                continue;
            }

            if (RevokeExternalAccessEndpoint.HasNoContainerMembershipKey(key))
            {
                logger.LogInformation(
                    "[CLOSE-PROJECT] Contact {ContactId} has no bound object id and no emailaddress1 — the keys a "
                    + "container permission is matched by — so no permission on {ContainerId} can be attributed to them; "
                    + "nothing to clear (#2103).",
                    contactId, containerId);
                noKey++;
                continue;
            }

            keys.Add(key);
        }

        // F1: a grantee who could not be enumerated or keyed, or earlier grants that could not be read, can still be
        // confirmed absent, by the container holding no contact permission at all.
        var confirmNoContactPermission = unresolved > 0 || !sweepComplete;

        if (keys.Count == 0 && !confirmNoContactPermission)
        {
            return new GranteeRemoval(0, unresolved, noKey, tooLarge);
        }

        SpeMembershipRemoval removal;
        try
        {
            removal = await speContainerMembership.RemoveMembershipsAsync(
                containerId, keys, dataverseClient, confirmNoContactPermission, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Pass-2 F4: the counts gathered so far — the oversized organizations above all — survive the throw, so the
            // closure still names the non-retryable leaf. Every keyed grantee is unconfirmed.
            // Round 4 (F1): the container's answer was not established either, so a retry may still prove it clean — the
            // non-retryable claims an oversized organization would otherwise make are not made (ContainerState Unknown).
            logger.LogError(ex,
                "[CLOSE-PROJECT] Removing {Count} revoked grantee(s) from container {ContainerId} failed; none is "
                + "confirmed removed.", keys.Count, containerId);
            return new GranteeRemoval(
                0, unresolved + keys.Count, noKey, tooLarge, ContainerState: SpeContainerContactPermissionState.Unknown);
        }

        var removed = 0;
        var ambiguous = 0;
        foreach (var key in keys)
        {
            if (!removal.Results.TryGetValue(key, out var result))
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
                if (SpeContainerMembershipService.IsAmbiguousPermissionMatch(result))
                {
                    ambiguous++;
                }
            }
        }

        if (removal.ContactPermissionState == SpeContainerContactPermissionState.Clean)
        {
            if (unresolved > 0 || !sweepComplete)
            {
                logger.LogInformation(
                    "[CLOSE-PROJECT] Container {ContainerId} holds no permission a contact grant could account for (read in "
                    + "full; every remaining user permission is Spaarke-managed or an internal user's), so the {Unresolved} grantee(s) that could "
                    + "not be enumerated or keyed ({TooLarge} oversized organization(s)) and any earlier grants not read are "
                    + "confirmed absent (#2104 round 2, F1).",
                    containerId, unresolved, tooLarge);
            }

            return new GranteeRemoval(
                removed, Unresolved: 0, noKey, OrganizationsTooLarge: 0, ContainerState: SpeContainerContactPermissionState.Clean);
        }

        return new GranteeRemoval(
            removed, unresolved, noKey, tooLarge, removal.ContactPermissionState, PermissionsAmbiguous: ambiguous);
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
    /// #2104: the OData filter selecting a project's INACTIVE (revoked) grant rows — the grantees a closure retry, or a
    /// closure after single-grant revokes, must still clear from the project's container. Same lookup field as
    /// <see cref="BuildActiveProjectGrantsFilter"/>.
    /// </summary>
    internal static string BuildRevokedProjectGrantsFilter(Guid projectId)
        => $"_sprk_project_value eq {projectId} and statecode eq 1";

    /// <summary>
    /// #2104: the most revoked rows one closure reads — one Dataverse page. A project with more is not swept on a
    /// partial list: the read reports itself incomplete (fail closed), never a silently truncated set.
    /// </summary>
    internal const int MaxRevokedGrantsPerSweep = 4999;

    /// <summary>
    /// #2104 round 2 (F2): the most ACTIVE rows one closure reads — one Dataverse page, requested as <c>$top</c> =
    /// bound + 1 (<see cref="DataverseWebApiClient.QueryAsync{T}"/> reads one page and never follows
    /// <c>@odata.nextLink</c>). More is never revoked on a truncated list: the closure answers
    /// <see cref="ClosureTooManyGrantsReason"/> with nothing revoked.
    /// </summary>
    internal const int MaxActiveGrantsPerClosure = 4999;

    /// <summary>
    /// #2104: the project's revoked grant rows as read. <paramref name="Complete"/> false: they could not be read in full
    /// and <paramref name="Rows"/> is empty — the caller cannot confirm the container clear of them.
    /// </summary>
    /// <param name="Rows">The revoked rows (empty unless <paramref name="Complete"/>).</param>
    /// <param name="Complete">Every revoked row was read.</param>
    /// <param name="OverLimit">#2104 round 2 (F2): not read in full because there are more than
    /// <see cref="MaxRevokedGrantsPerSweep"/> — a retry meets the same bound (a read fault is retryable and leaves this
    /// false).</param>
    internal readonly record struct RevokedGrantHistory(
        IReadOnlyList<ExternalAccessRecord> Rows, bool Complete, bool OverLimit = false);

    /// <summary>
    /// #2104: reads the project's revoked grant rows (see <see cref="RevokedGrantHistory"/>). Never throws (cancellation
    /// aside).
    /// </summary>
    private static async Task<RevokedGrantHistory> TryQueryRevokedAccessRecordsAsync(
        DataverseWebApiClient dataverseClient,
        Guid projectId,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var rows = await dataverseClient.QueryAsync<ExternalAccessRow>(
                ExternalAccessEntitySet,
                filter: BuildRevokedProjectGrantsFilter(projectId),
                select: ActiveGrantSelect,
                top: MaxRevokedGrantsPerSweep + 1,
                cancellationToken: ct).ConfigureAwait(false);

            if (rows.Count > MaxRevokedGrantsPerSweep)
            {
                logger.LogError(
                    "[CLOSE-PROJECT] Project {ProjectId} has more than {Bound} revoked grants; they are not swept from "
                    + "its container on a partial list, and the container is reported as not cleared unless it holds no "
                    + "contact permission at all.",
                    projectId, MaxRevokedGrantsPerSweep);
                return new RevokedGrantHistory([], Complete: false, OverLimit: true);
            }

            return new RevokedGrantHistory(
                rows
                    .Select(r => new ExternalAccessRecord(
                        r.sprk_externalrecordaccessid ?? Guid.Empty,
                        r._sprk_contact_value,
                        r._sprk_organization_value))
                    .ToList(),
                Complete: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[CLOSE-PROJECT] Could not read the revoked grants of ProjectId={ProjectId}; their grantees cannot be "
                + "confirmed off the project's container.", projectId);
            return new RevokedGrantHistory([], Complete: false);
        }
    }

    /// <summary>
    /// #2104: the grants whose grantees the container step clears — every row this call revoked (or failed to), plus
    /// every revoked row (which, read after Step 2, includes this call's own; <see cref="RemoveRevokedGranteesAsync"/>
    /// sweeps each contact and organization once). A revoked row naming neither a contact nor an organization (its
    /// grantee's record was deleted, clearing the lookup) identifies nobody and is skipped with a warning — no
    /// container permission can be attributed to it, the #2103 "no key" answer; an ACTIVE one is still counted
    /// unresolved, as before.
    /// </summary>
    internal static IReadOnlyList<ExternalAccessRecord> GranteesToSweep(
        IReadOnlyList<ExternalAccessRecord> activeRecords,
        IReadOnlyList<ExternalAccessRecord> revokedEarlier,
        ILogger logger)
    {
        var sweep = new List<ExternalAccessRecord>(activeRecords);

        foreach (var record in revokedEarlier)
        {
            if (record.ContactId is null && record.OrganizationId is null)
            {
                logger.LogWarning(
                    "[CLOSE-PROJECT] Revoked grant {RecordId} names neither a contact nor an organization; no container "
                    + "permission can be attributed to it, so it is not swept.", record.RecordId);
                continue;
            }

            sweep.Add(record);
        }

        return sweep;
    }

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
    /// #2103 follow-up: the NON-retryable leaf — a revoked organization has more active members than one closure may
    /// sweep (<see cref="ExternalOrganizationMembership.MaxMembersPerSweep"/>), so its members' container permissions
    /// were not removed. A retry meets the same bound; the detail tells an administrator what to do instead.
    /// </summary>
    internal const string ClosureOrganizationTooLargeReason = "sdap.closure.incomplete.organization_too_large";

    /// <summary>
    /// #2104 round 2 (F2): the NON-retryable leaf for a project with more grant rows than one closure reads — more than
    /// <see cref="MaxActiveGrantsPerClosure"/> active ones (nothing is revoked), or more than
    /// <see cref="MaxRevokedGrantsPerSweep"/> revoked ones while its container still may hold a permission of theirs. A
    /// retry reads the same rows; the detail tells an administrator what to do instead.
    /// </summary>
    internal const string ClosureTooManyGrantsReason = "sdap.closure.incomplete.too_many_grants";

    /// <summary>
    /// #2104 round 3: the NON-retryable leaf for a match the closure refused to act on — more than one eligible
    /// permission on the project's container matches a revoked grantee, or the one match names no holder object id and
    /// so cannot be told apart from an internal user's (<see cref="SpeContainerMembershipService.IsAmbiguousPermissionMatch"/>).
    /// Nothing was removed for that grantee; a retry reads the same permissions and refuses again. The detail tells an
    /// administrator to resolve the container's permissions in the SPE admin app.
    /// </summary>
    /// <remarks>
    /// <b>Precedence when causes mix</b> — one rule for every non-retryable leaf: a cause a retry cannot fix wins over a
    /// transient one, because a retry could clear only the transient part and the closure still could not complete; the
    /// detail then also counts the other grantees not confirmed removed. Order: <see cref="ClosurePartialRevocationReason"/>
    /// (grants still ACTIVE — a retry does help those; its detail carries every container cause), then
    /// <see cref="ClosureTooManyGrantsReason"/>, <see cref="ClosureOrganizationTooLargeReason"/>, this, and last the
    /// retryable <see cref="ClosureContainerNotClearedReason"/>. Whichever wins, the detail names every non-retryable cause
    /// present.
    /// </remarks>
    internal const string ClosurePermissionAmbiguousReason = "sdap.closure.incomplete.permission_ambiguous";

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
            // F2: one bounded page; Handle reports more than the bound as too_many_grants, never a truncated closure.
            var rows = await dataverseClient.QueryAsync<ExternalAccessRow>(
                ExternalAccessEntitySet,
                filter: BuildActiveProjectGrantsFilter(projectId),
                select: ActiveGrantSelect,
                top: MaxActiveGrantsPerClosure + 1,
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
