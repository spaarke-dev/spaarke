// Graph, Azure.Identity and HTTP-header usings were dropped by task 017: this endpoint no longer talks to
// Graph at all. Its forked SPE matcher (finding A-13) was deleted in favour of
// SpeContainerMembershipService, which owns that conversation.
using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/revoke
///
/// Revokes an external Contact's access to a Secure Project by:
///   1. Deactivating the sprk_externalrecordaccess record in Dataverse (statecode=1, statuscode=2).
///   2. Removing the Contact from the SPE container permissions.
///   3. If the Contact has no remaining active participations, removing the "Secure Project Participant" web role.
///   4. Invalidating the contact's participation cache in Redis.
///
/// ADR-001: Minimal API — no controllers.
/// ADR-008: Endpoint filter for internal caller check (RequireAuthorization).
/// ADR-009: Redis cache invalidation after revoke (key: sdap:external:access:{contactId}).
/// ADR-010: Concrete DI injections.
/// </summary>
public static class RevokeExternalAccessEndpoint
{
    private const string AccessEntitySet = "sprk_externalrecordaccesses";
    // Cache key components for invalidation. BOUND to ExternalParticipationService (the read/store side,
    // the single source of truth) so a version bump there stays in sync here automatically. Task 073 #7
    // fix: the prior hard-coded `CacheVersion = 1` silently missed the v2/v3 stored key, so revoke
    // invalidation relied on the 60s TTL. Per-Contact participation cache — not an authz decision
    // (ADR-009); tenant scope is derived from the caller's 'tid' claim.
    private const string ExternalAccessResource = ExternalParticipationService.ExternalAccessResource;
    private const int CacheVersion = ExternalParticipationService.CacheVersion;

    /// <summary>
    /// Registers the revoke endpoint on the external-access group.
    /// </summary>
    public static RouteGroupBuilder MapRevokeExternalAccessEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/revoke", RevokeAccessAsync)
            .WithName("RevokeExternalAccess")
            .WithSummary("Revoke external access from a Contact for a Secure Project")
            .WithDescription(
                "Deactivates the sprk_externalrecordaccess record, removes the Contact from the SPE container, " +
                "and optionally removes the Power Pages web role if no other active participations remain. " +
                "Invalidates the contact's Redis participation cache after revoking.")
            .Produces<RevokeAccessResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    // =========================================================================
    // Handler
    // =========================================================================

    /// <summary>
    /// Internal (not private) so the test assembly can exercise the PRODUCTION handler directly per
    /// <c>InternalsVisibleTo("Sprk.Bff.Api.Tests")</c> — the same convention used across this codebase,
    /// and no reflection into a private member (ADR-038 §7 ban B8). The revoke sweep's correctness is a
    /// privilege-retention question; it must be tested against the real handler, not a re-implementation.
    /// </summary>
    internal static async Task<IResult> RevokeAccessAsync(
        RevokeAccessRequest request,
        DataverseWebApiClient dataverseClient,
        SpeContainerMembershipService speContainerMembership,
        ITenantCache cache,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // ── Validation ───────────────────────────────────────────────────────
        if (request.AccessRecordId == Guid.Empty)
            return ProblemDetailsHelper.ValidationError("AccessRecordId is required and must be a valid GUID.");

        // ContactId is OPTIONAL (task 073 #7): revoke is authoritative by AccessRecordId (root- AND
        // grantee-agnostic). A per-contact revoke SHOULD still pass ContactId so its participation cache
        // is invalidated immediately (Step 3); an ORGANIZATION-grant revoke has no single grantee contact
        // — it passes an empty ContactId, the org row is deactivated by AccessRecordId, and affected
        // members refresh within the 60s participation TTL.
        //
        // Note (task 070): ProjectId is NOT required — revoke deactivates by AccessRecordId and is
        // root-agnostic (works for a project/matter/work-assignment grant alike). The field is retained
        // on the DTO for back-compat but no longer gates the request.

        logger.LogInformation(
            "[EXT-REVOKE] Revoking access record {AccessRecordId} for Contact {ContactId}",
            request.AccessRecordId, request.ContactId);

        // ── Step 1: Deactivate EVERY active row for this logical grant ───────
        //
        // Revoke used to deactivate exactly ONE row, by AccessRecordId (finding A-11). Because /grant
        // created unconditionally, two identical grants produced two active rows — so revoking "the"
        // grant left a sibling standing and access survived revocation. The participation surface could
        // not reveal it either: QueryGrantSetAsync collapses duplicates with GroupBy(root).Max(level) and
        // never returns access-record ids.
        //
        // The revocation target now identifies a LOGICAL grant (root × grantee), and every active row on
        // that key is deactivated. Task 010 / spec FR-09.
        int deactivatedCount;
        // Hoisted out of the try (task 020): the SPE step needs to know WHICH grantee this grant names.
        // An organization grant confers access on every active member, so its container cleanup is a
        // many-identity sweep — and the organization id lives on the row, not on the request.
        ExternalGrantKey grantKey;
        try
        {
            var targetRow = await ExternalGrantLifecycle.RetrieveRowAsync(dataverseClient, request.AccessRecordId, ct);

            if (targetRow is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found",
                    detail: $"Access record '{request.AccessRecordId}' was not found.");
            }

            var key = ExternalGrantLifecycle.DeriveKey(targetRow);

            if (key is null)
            {
                // FAIL LOUDLY. Per this task's ADR-003 constraint, /revoke must never report success
                // while any matching active row remains unqueried — and a row with no derivable root or
                // grantee has no queryable siblings. Deactivating only the target would be precisely the
                // silent partial revocation A-11 describes, so refusing is the fail-closed answer.
                logger.LogError(
                    "[EXT-REVOKE] Access record {AccessRecordId} has no derivable grant key (no root " +
                    "and/or no grantee lookup). Refusing to revoke: sibling rows cannot be identified, so " +
                    "success cannot be guaranteed.", request.AccessRecordId);

                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Internal Server Error",
                    detail: $"Access record '{request.AccessRecordId}' is missing the root or grantee lookup " +
                            "needed to identify every row of this grant. No rows were deactivated.",
                    extensions: new Dictionary<string, object?> { ["traceId"] = httpContext.TraceIdentifier });
            }

            grantKey = key.Value;

            // Sweep by KEY, not by id — this is what makes the revoke complete. Note the target row is
            // swept too when active, and that an ALREADY-INACTIVE target still sweeps live siblings:
            // "the row you named is already off" is not the same as "this grant confers nothing".
            var activeRows = await ExternalGrantLifecycle.QueryActiveRowsAsync(dataverseClient, key.Value, ct);

            deactivatedCount = await ExternalGrantLifecycle.DeactivateAsync(
                dataverseClient, activeRows.Select(r => r.Id), logger, ct);

            logger.LogInformation(
                "[EXT-REVOKE] Revoked grant {Key}: deactivated {Count} active row(s) (target {AccessRecordId}).",
                key.Value, deactivatedCount, request.AccessRecordId);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"Access record '{request.AccessRecordId}' was not found.");
        }
        catch (Exception ex)
        {
            // Covers the sibling-row query AND any partial sweep. Reporting success here would be the
            // worst outcome available: the caller believes access is gone while rows remain active.
            logger.LogError(ex,
                "[EXT-REVOKE] Failed to revoke grant for access record {AccessRecordId}. Some rows may " +
                "remain active.", request.AccessRecordId);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to deactivate external access record in Dataverse.",
                extensions: new Dictionary<string, object?> { ["traceId"] = httpContext.TraceIdentifier });
        }

        // ── Step 2: Remove the revoked grantee's SPE container permission(s) ──
        var (speOutcome, orgCleanup) = await RemoveSpeContainerPermissionsAsync(
            speContainerMembership, dataverseClient, request, grantKey, logger, ct);

        // ── Step 3: Invalidate Redis cache ────────────────────────────────────
        try
        {
            var tenantId = ExtractTenantId(httpContext);
            if (request.ContactId == Guid.Empty)
            {
                // Organization-grant revoke (task 073 #7): no single grantee contact to invalidate — every
                // active member's participation set changes. Members refresh within the 60s TTL (an
                // org-scoped fan-out invalidation is a possible future optimization).
                logger.LogDebug(
                    "[EXT-REVOKE] Organization-grant revoke — no per-contact cache to invalidate; members refresh within the participation TTL.");
            }
            else if (!string.IsNullOrEmpty(tenantId))
            {
                await cache.RemoveAsync(
                    tenantId, ExternalAccessResource, request.ContactId.ToString(), CacheVersion,
                    ct: ct);
                logger.LogDebug("[EXT-REVOKE] Invalidated cache for Contact {ContactId}", request.ContactId);
            }
            else
            {
                logger.LogWarning(
                    "[EXT-REVOKE] No tenant claim found — skipping cache invalidation for Contact {ContactId}",
                    request.ContactId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[EXT-REVOKE] Failed to invalidate Redis cache for Contact {ContactId}. Non-critical.",
                request.ContactId);
        }

        // DeactivatedCount makes the outcome explicit rather than inferable: 0 means the grant was
        // already fully inactive (a safe no-op), >1 means duplicates existed and were swept — the exact
        // condition that used to leave access standing after a "successful" revoke.
        return TypedResults.Ok(new RevokeAccessResponse(
            SpeContainerMembershipRevoked: speOutcome == SpeContainerRevokeOutcome.PermissionRemoved,
            SpeContainerOutcome: speOutcome,
            DeactivatedCount: deactivatedCount,
            SpeOrgMemberCleanup: orgCleanup));
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Extracts the Azure AD tenant ID ('tid' claim) from the authenticated HttpContext.
    /// Returns null when no claim is present (in which case cache invalidation is skipped).
    /// </summary>
    private static string? ExtractTenantId(HttpContext httpContext)
        => httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

    /// <summary>
    /// Removes the revoked Contact's SPE container permission, and reports honestly what happened.
    /// </summary>
    /// <remarks>
    /// <para><b>Finding A-13 (task 017).</b> This used to be a private re-implementation of
    /// <see cref="SpeContainerMembershipService.RevokeMembershipAsync"/> that matched a permission by
    /// looking for the contact's <b>GUID</b> inside <c>userPrincipalName</c>. But membership is written
    /// with <c>userPrincipalName</c> = the contact's <b>email</b>, and an email never contains a GUID — so
    /// the predicate matched nothing, ever. It then returned <c>true</c> on no-match ("may have already
    /// been removed"), so <c>/revoke</c> reported SPE success while the ACL entry stayed in place.</para>
    ///
    /// <para>The fix is deletion, not repair: the service already had a correct email matcher with zero
    /// callers (CLAUDE.md §11 — reuse, don't fork). What remains here is the endpoint's own job — turning
    /// a contact id into the email key, and mapping the result to an honest outcome.</para>
    ///
    /// <para><b>Broker-only context.</b> Nothing in this codebase ADDS a container permission
    /// (<c>GrantMembershipAsync</c> has no callers; <c>/grant</c> reports
    /// <c>SpeContainerMembershipGranted: false</c>). So this is a CLEANUP path for ACLs created by legacy
    /// versions or by admins outside Spaarke — not the counterpart of a grant-time write. That is why
    /// <see cref="SpeContainerRevokeOutcome.NoPermissionFound"/> is the ordinary, healthy answer rather
    /// than a problem.</para>
    /// </remarks>
    private static async Task<(SpeContainerRevokeOutcome Outcome, SpeOrgMemberCleanupSummary? OrgCleanup)>
        RemoveSpeContainerPermissionsAsync(
            SpeContainerMembershipService speContainerMembership,
            DataverseWebApiClient dataverseClient,
            RevokeAccessRequest request,
            ExternalGrantKey grantKey,
            ILogger logger,
            CancellationToken ct)
    {
        if (!request.ContainerId.HasValue)
        {
            logger.LogInformation(
                "[EXT-REVOKE] No ContainerId provided — no SPE container permission to remove.");
            return (SpeContainerRevokeOutcome.NotAttempted, null);
        }

        var containerId = request.ContainerId.Value.ToString();

        // ── Which grantee? The ROW decides, not the request ───────────────────
        //
        // Task 020 (FR-16b). Dispatching on the derived grant KEY rather than on request.ContactId is
        // deliberate: the row is what the Dataverse sweep in Step 1 acted on, so keying the SPE cleanup
        // off anything else would let the two halves of a revoke disagree about who was revoked — which
        // is finding A-11's shape, one layer down. It also means an org revoke is recognised as one even
        // if a caller supplies some incidental ContactId.
        if (grantKey.IsOrganizationGrant)
        {
            // Unreachable by construction — ExternalGrantKey.ForOrganization requires an organization id,
            // and IsOrganizationGrant is exactly "no contact". Handled rather than asserted with `!`
            // because the fail-closed answer to "an org grant with no organization" is "we cannot
            // identify the members", not a silent fall-through to the single-contact path, which would
            // clean up nobody while reporting an outcome.
            if (grantKey.OrganizationId is not { } organizationId)
            {
                logger.LogError(
                    "[EXT-REVOKE] Access record {AccessRecordId} derives an organization grant with no " +
                    "organization id. Members cannot be identified; no SPE container permission on " +
                    "{ContainerId} was removed.", request.AccessRecordId, containerId);
                return (SpeContainerRevokeOutcome.Failed, UnknownMembership);
            }

            return await RemoveOrganizationMembersSpePermissionsAsync(
                speContainerMembership, dataverseClient, containerId, organizationId, logger, ct);
        }

        // A CONTACT-grant row revoked without a ContactId on the request: the row names the grantee, but
        // resolving them here would change which identity the per-contact path acts on, which task 017's
        // constraint pins. Left as NotAttempted — the honest answer, and unchanged behaviour.
        if (request.ContactId == Guid.Empty)
        {
            logger.LogInformation(
                "[EXT-REVOKE] Contact-grant revoke with no ContactId on the request — no identity key to " +
                "match; SPE container permission removal not attempted for container {ContainerId}.",
                request.ContainerId);
            return (SpeContainerRevokeOutcome.NotAttempted, null);
        }

        return (await RemoveContactSpePermissionAsync(
            speContainerMembership, dataverseClient, request, containerId, logger, ct), null);
    }

    /// <summary>
    /// The per-CONTACT half of the SPE cleanup: one identity, one permission. Unchanged by task 020.
    /// </summary>
    private static async Task<SpeContainerRevokeOutcome> RemoveContactSpePermissionAsync(
        SpeContainerMembershipService speContainerMembership,
        DataverseWebApiClient dataverseClient,
        RevokeAccessRequest request,
        string containerId,
        ILogger logger,
        CancellationToken ct)
    {
        string? contactEmail;
        try
        {
            contactEmail = await ResolveContactEmailAsync(dataverseClient, request.ContactId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[EXT-REVOKE] Could not read the email for Contact {ContactId}; the SPE container " +
                "permission on {ContainerId} could NOT be matched and may remain.",
                request.ContactId, containerId);
            return SpeContainerRevokeOutcome.Failed;
        }

        if (string.IsNullOrWhiteSpace(contactEmail))
        {
            // No email means no way to identify their ACL entry. If one exists we cannot find it, so this
            // is an unknown state, not an absence — report Failed rather than NoPermissionFound.
            logger.LogWarning(
                "[EXT-REVOKE] Contact {ContactId} has no emailaddress1, which is the key SPE membership " +
                "is written with — any container permission on {ContainerId} cannot be matched.",
                request.ContactId, containerId);
            return SpeContainerRevokeOutcome.Failed;
        }

        var result = await speContainerMembership.RevokeMembershipAsync(containerId, contactEmail, ct);

        if (result.Success)
        {
            logger.LogInformation(
                "[EXT-REVOKE] Removed SPE container permission {PermissionId} for Contact {ContactId} on {ContainerId}",
                result.PermissionId, request.ContactId, containerId);
            return SpeContainerRevokeOutcome.PermissionRemoved;
        }

        // The service distinguishes "nobody matched" from "Graph refused". Only the former is benign:
        // under the broker-only model most contacts have no container ACL at all.
        if (result.Error?.StartsWith("No permission found", StringComparison.OrdinalIgnoreCase) == true)
        {
            logger.LogInformation(
                "[EXT-REVOKE] No SPE container permission exists for Contact {ContactId} on {ContainerId} " +
                "— nothing to remove (expected under the broker-only model).",
                request.ContactId, containerId);
            return SpeContainerRevokeOutcome.NoPermissionFound;
        }

        logger.LogError(
            "[EXT-REVOKE] Failed to remove the SPE container permission for Contact {ContactId} on " +
            "{ContainerId}: {Error}. They may RETAIN file access.",
            request.ContactId, containerId, result.Error);
        return SpeContainerRevokeOutcome.Failed;
    }

    /// <summary>
    /// The per-ORGANIZATION half of the SPE cleanup: expand the organization to its ACTIVE member
    /// contacts and remove each one's container permission, reporting per-member arithmetic.
    /// </summary>
    /// <remarks>
    /// <para><b>Finding A-13's org half (task 020, spec FR-16b), filed by task 017 §6.</b> An org revoke
    /// deactivates the grant for every member in Dataverse, but attempted NO container cleanup at all —
    /// there is no single grantee, so no single email, so nothing to match. It returned
    /// <c>NotAttempted</c>: honest, but it left every member's ACL entry in place for a grant the caller
    /// had been told was revoked.</para>
    ///
    /// <para><b>Why sweeping by <c>statecode</c> alone.</b> The junction also carries
    /// <c>sprk_enddate</c>, and this ignores it — deliberately, to match
    /// <c>ExternalParticipationService.QueryActiveOrgIdsAsync</c>, which grants inherited access on
    /// <c>statecode</c> alone. A membership that has ended by date but was never deactivated therefore
    /// still CONFERS access on the read side, so a revoke that skipped it would leave a live inheritance
    /// standing. Over-including on a revoke removes more access (fail-closed); under-including does not.
    /// The read-side asymmetry itself is recorded for the Phase 1 evaluator (FR-24/FR-25, task 043) —
    /// changing who has access on the read path is out of scope here.</para>
    ///
    /// <para><b>⚠️ What this CANNOT confirm.</b> Per-member <c>NoPermissionFound</c> is only as good as
    /// <see cref="SpeContainerMembershipService.RevokeMembershipAsync"/>'s match, and that method reads
    /// the container's permissions with a single <c>GetAsync</c> — it does not follow Graph's
    /// <c>@odata.nextLink</c>. On a container whose permission list spans more than one page, a member
    /// whose entry sits beyond page 1 is reported as "holds no permission" when they in fact retain file
    /// access. That is the same class of false assurance as <c>container_not_cleared</c>'s and is owned by
    /// task 024 (SPE Graph paging); this method inherits it and cannot detect it from the callee's
    /// result. Fixing it HERE would mean forking the matcher, which is exactly what task 017 deleted.</para>
    /// </remarks>
    private static async Task<(SpeContainerRevokeOutcome Outcome, SpeOrgMemberCleanupSummary? OrgCleanup)>
        RemoveOrganizationMembersSpePermissionsAsync(
            SpeContainerMembershipService speContainerMembership,
            DataverseWebApiClient dataverseClient,
            string containerId,
            Guid organizationId,
            ILogger logger,
            CancellationToken ct)
    {
        // ── Enumerate ────────────────────────────────────────────────────────
        OrganizationMemberSet memberSet;
        try
        {
            memberSet = await ExternalOrganizationMembership.QueryActiveMembersAsync(
                dataverseClient, organizationId, ct);
        }
        catch (Exception ex)
        {
            // We do not know WHO to clean up, so we cannot claim anything was cleaned up. Attempting a
            // partial sweep off an unknown member list would be worse than doing nothing: it would
            // produce counts that read like a complete answer.
            logger.LogError(ex,
                "[EXT-REVOKE] Could not enumerate the active members of Organization {OrganizationId}; " +
                "NO SPE container permission on {ContainerId} was removed and members may RETAIN file access.",
                organizationId, containerId);
            return (SpeContainerRevokeOutcome.Failed, UnknownMembership);
        }

        if (memberSet.ExceededBound)
        {
            // Task 020's escalation trigger, enforced in code rather than assumed away: a sweep that
            // silently truncates and reports success is the exact failure class this project exists to
            // remove. Refusing, loudly, hands the operator a decision instead of a false assurance.
            logger.LogError(
                "[EXT-REVOKE] Organization {OrganizationId} has more than {Bound} active members — more " +
                "than one revoke request may sweep. NO SPE container permission on {ContainerId} was " +
                "removed; escalate for a bulk cleanup rather than retrying.",
                organizationId, ExternalOrganizationMembership.MaxMembersPerSweep, containerId);
            return (SpeContainerRevokeOutcome.Failed, UnknownMembership);
        }

        // ── Sweep ────────────────────────────────────────────────────────────
        var removed = 0;
        var notFound = 0;
        var failed = 0;

        foreach (var memberContactId in memberSet.ContactIds)
        {
            // Per-member failure must NOT abort the loop (mirrors tasks 016/017): stopping early leaves
            // strictly MORE access in place. Every member gets an outcome, and the failures are counted.
            try
            {
                var memberEmail = await ResolveContactEmailAsync(dataverseClient, memberContactId, ct);

                if (string.IsNullOrWhiteSpace(memberEmail))
                {
                    failed++;
                    logger.LogWarning(
                        "[EXT-REVOKE] Member Contact {ContactId} of Organization {OrganizationId} has no " +
                        "emailaddress1 — the key SPE membership is written with — so any permission on " +
                        "{ContainerId} cannot be matched. They may RETAIN file access.",
                        memberContactId, organizationId, containerId);
                    continue;
                }

                var result = await speContainerMembership.RevokeMembershipAsync(containerId, memberEmail, ct);

                if (result.Success)
                {
                    removed++;
                    logger.LogInformation(
                        "[EXT-REVOKE] Removed SPE container permission {PermissionId} for member Contact " +
                        "{ContactId} of Organization {OrganizationId} on {ContainerId}",
                        result.PermissionId, memberContactId, organizationId, containerId);
                }
                else if (result.Error?.StartsWith("No permission found", StringComparison.OrdinalIgnoreCase) == true)
                {
                    notFound++;
                }
                else
                {
                    failed++;
                    logger.LogError(
                        "[EXT-REVOKE] Failed to remove the SPE container permission for member Contact " +
                        "{ContactId} of Organization {OrganizationId} on {ContainerId}: {Error}. They may " +
                        "RETAIN file access. Continuing with the rest.",
                        memberContactId, organizationId, containerId, result.Error);
                }
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogError(ex,
                    "[EXT-REVOKE] Unexpected error cleaning up member Contact {ContactId} of Organization " +
                    "{OrganizationId} on {ContainerId}. They may RETAIN file access. Continuing with the rest.",
                    memberContactId, organizationId, containerId);
            }
        }

        var summary = new SpeOrgMemberCleanupSummary(
            MembersEnumerated: memberSet.ContactIds.Count,
            PermissionsRemoved: removed,
            PermissionsNotFound: notFound,
            Failed: failed);

        var outcome = AggregateOrgOutcome(summary);

        logger.LogInformation(
            "[EXT-REVOKE] Organization {OrganizationId} SPE cleanup on {ContainerId}: {Members} active " +
            "member(s), {Removed} permission(s) removed, {NotFound} with none, {Failed} FAILED → {Outcome}",
            organizationId, containerId, summary.MembersEnumerated, removed, notFound, failed, outcome);

        return (outcome, summary);
    }

    /// <summary>
    /// The summary reported when the member list could not be established at all. Distinguished from
    /// "the organization has no members" by <c>MembersEnumerated == null</c>, which the counts cannot say.
    /// </summary>
    private static SpeOrgMemberCleanupSummary UnknownMembership =>
        new(MembersEnumerated: null, PermissionsRemoved: 0, PermissionsNotFound: 0, Failed: 0);

    /// <summary>
    /// Collapses the per-member arithmetic into the single outcome the caller reads. A total function
    /// over the summary — every shape maps somewhere, and only one shape maps to success.
    /// </summary>
    /// <remarks>
    /// The ordering is the ADR-003 fail-closed rule at member granularity, and each branch exists because
    /// the alternative is a lie: an unknown member list is not "clean" (we never looked), and one member
    /// retaining access is not "removed" merely because eleven others lost theirs.
    /// </remarks>
    internal static SpeContainerRevokeOutcome AggregateOrgOutcome(SpeOrgMemberCleanupSummary summary)
    {
        // "We could not tell who the members are" — never reportable as cleaned.
        if (summary.MembersEnumerated is null)
            return SpeContainerRevokeOutcome.Failed;

        // "Some members retain access" — never reportable as success, however many others were cleaned.
        if (summary.Failed > 0)
            return SpeContainerRevokeOutcome.Failed;

        if (summary.PermissionsRemoved > 0)
            return SpeContainerRevokeOutcome.PermissionRemoved;

        // The member list was established (possibly empty) and nobody held a permission. Under the
        // broker-only model this is the ordinary, healthy answer — not a problem.
        return SpeContainerRevokeOutcome.NoPermissionFound;
    }

    /// <summary>
    /// Reads a contact's <c>emailaddress1</c> — the key SPE membership is written with.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="DataverseWebApiClient.RetrieveAsync{T}"/> directly rather than introducing a
    /// contact-email service: one column on one row, and the client is already injected (CLAUDE.md §11).
    /// </remarks>
    private static async Task<string?> ResolveContactEmailAsync(
        DataverseWebApiClient dataverseClient, Guid contactId, CancellationToken ct)
    {
        var row = await dataverseClient.RetrieveAsync<ContactEmailRow>(
            "contacts", contactId, "emailaddress1", ct);

        return row?.emailaddress1;
    }

    /// <summary>Minimal projection of <c>contacts</c> for SPE identity matching.</summary>
    internal sealed class ContactEmailRow
    {
        public string? emailaddress1 { get; set; }
    }
}

/// <summary>
/// The ACTIVE member contacts of one <c>sprk_organization</c>, and whether that list is trustworthy.
/// </summary>
/// <param name="ContactIds">Distinct member contact ids. Empty when the organization has no active members.</param>
/// <param name="ExceededBound">
/// <c>true</c> when the organization has MORE members than one revoke request may sweep, so
/// <paramref name="ContactIds"/> is a truncation rather than the member list. A caller must treat this as
/// "unknown membership", never as the answer — a truncated sweep reported as success is precisely the
/// failure this type exists to make unsayable.
/// </param>
internal readonly record struct OrganizationMemberSet(
    IReadOnlyList<Guid> ContactIds,
    bool ExceededBound);

/// <summary>
/// Reads <c>sprk_contactorganization</c> — the contact↔organization membership junction — in the
/// organization → members direction.
/// </summary>
/// <remarks>
/// <para><b>CLAUDE.md §11 justification (task 020).</b> <i>Existing</i>:
/// <c>ExternalParticipationService.QueryActiveOrgIdsAsync</c> reads the same junction in the INVERSE
/// direction (contact → organizations). <i>Extension</i>: not callable — it is private and built on a raw
/// <c>HttpClient</c> with its own app-only token flow, whereas the revoke path holds a
/// <c>DataverseWebApiClient</c>; reaching it would drag the participation service's token flow into the
/// revoke path. So the QUERY SHAPE is mirrored, not the code. <i>Cost of doing nothing</i>: an
/// organization-grant revoke deactivates the grant for every member and reports success while every one
/// of those members keeps their SPE container permission, and therefore continued access to the
/// project's files — a population no other code path will ever clean up (see the broker-only note on
/// <see cref="SpeContainerMembershipService.GrantMembershipAsync"/>).</para>
///
/// <para><b>Deliberately the smallest surface that answers one question</b> — "who are this
/// organization's active members" — rather than a general-purpose membership service. Task 043
/// (FR-24/FR-25 org expansion) needs the same answer and should EXTEND this rather than write a third
/// reader. It lives in this file only because task 020's wave-scoped modify-set is three files; nothing
/// about it is endpoint-specific, and hoisting it to
/// <c>Infrastructure/ExternalAccess/ExternalOrganizationMembership.cs</c> is a pure file move.</para>
///
/// <para><b>Schema live-verified 2026-08-26</b> (Dataverse MCP <c>describe</c>), which is not optional
/// here: three Phase 0 tasks (070, 016, 017) turned on a stale column name, and a wrong one in a
/// revocation query reads as "nothing to revoke" — silently. Confirmed: collection
/// <c>sprk_contactorganizations</c>; lookups <c>sprk_contact</c> → <c>contact</c> and
/// <c>sprk_organization</c> → <c>sprk_organization</c>, projected as <c>_sprk_contact_value</c> /
/// <c>_sprk_organization_value</c>; <c>statecode</c> Active(0)/Inactive(1). This confirms the assumption
/// standing as a caveat comment in <c>QueryActiveOrgIdsAsync</c> is CORRECT.</para>
/// </remarks>
internal static class ExternalOrganizationMembership
{
    internal const string EntitySet = "sprk_contactorganizations";

    internal const string MemberSelect = "_sprk_contact_value";

    /// <summary>
    /// The largest membership one revoke request will sweep.
    /// </summary>
    /// <remarks>
    /// <para>Task 020's escalation trigger names &gt;200 members as an owner decision rather than an
    /// implementation detail. The bound is not decoration: <c>DataverseWebApiClient.QueryAsync</c> reads
    /// ONE page and discards <c>@odata.nextLink</c>, so an unbounded query on a large organization would
    /// return a silently truncated list that looks exactly like a complete one. Asking for
    /// <c>Bound + 1</c> converts that silent truncation into a detectable, reportable condition.</para>
    /// <para>Live check 2026-08-26: the largest organization in the environment has <b>1</b> active
    /// member, so this is a guard rail rather than a live limit.</para>
    /// </remarks>
    internal const int MaxMembersPerSweep = 200;

    /// <summary>
    /// The <c>$filter</c> selecting one organization's ACTIVE memberships.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>ExternalParticipationService.QueryActiveOrgIdsAsync</c> term for term, inverted:
    /// <c>statecode eq 0</c> only. It deliberately does NOT filter on <c>sprk_enddate</c> — see the
    /// remarks on the caller for why matching the read path matters more than being date-correct here.
    /// </remarks>
    internal static string ActiveMembersFilter(Guid organizationId)
        => $"_sprk_organization_value eq {organizationId} and statecode eq 0";

    /// <summary>
    /// The distinct ACTIVE member contacts of an organization.
    /// </summary>
    /// <remarks>Exceptions propagate: "the query failed" and "the organization has no members" must never
    /// be the same answer on a revocation path — that equivalence is the shape of finding A-13 and of the
    /// <c>ListExternalMembersAsync</c> defect task 016 filed.</remarks>
    internal static async Task<OrganizationMemberSet> QueryActiveMembersAsync(
        DataverseWebApiClient dataverseClient, Guid organizationId, CancellationToken ct)
    {
        var rows = await dataverseClient.QueryAsync<ContactOrganizationRow>(
            EntitySet,
            filter: ActiveMembersFilter(organizationId),
            select: MemberSelect,
            top: MaxMembersPerSweep + 1,
            cancellationToken: ct);

        // The bound is checked on RAW rows, before Distinct: duplicate junction rows must not be able to
        // collapse an over-bound organization back under the limit and hide the truncation.
        if (rows.Count > MaxMembersPerSweep)
            return new OrganizationMemberSet(Array.Empty<Guid>(), ExceededBound: true);

        var contactIds = rows
            .Where(r => r.ContactId.HasValue)
            .Select(r => r.ContactId!.Value)
            .Distinct()
            .ToList();

        return new OrganizationMemberSet(contactIds, ExceededBound: false);
    }

    /// <summary>Minimal projection of a <c>sprk_contactorganization</c> junction row.</summary>
    internal sealed class ContactOrganizationRow
    {
        [JsonPropertyName("_sprk_contact_value")]
        public Guid? ContactId { get; set; }
    }
}
