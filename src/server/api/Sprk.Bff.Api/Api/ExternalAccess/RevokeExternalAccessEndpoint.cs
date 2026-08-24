// Graph, Azure.Identity and HTTP-header usings were dropped by task 017: this endpoint no longer talks to
// Graph at all. Its forked SPE matcher (finding A-13) was deleted in favour of
// SpeContainerMembershipService, which owns that conversation.
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

        // ── Step 2: Remove the Contact's SPE container permission ─────────────
        var speOutcome = await RemoveSpeContainerPermissionAsync(
            speContainerMembership, dataverseClient, request, logger, ct);

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
            DeactivatedCount: deactivatedCount));
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
    private static async Task<SpeContainerRevokeOutcome> RemoveSpeContainerPermissionAsync(
        SpeContainerMembershipService speContainerMembership,
        DataverseWebApiClient dataverseClient,
        RevokeAccessRequest request,
        ILogger logger,
        CancellationToken ct)
    {
        if (!request.ContainerId.HasValue)
        {
            logger.LogInformation(
                "[EXT-REVOKE] No ContainerId provided — no SPE container permission to remove.");
            return SpeContainerRevokeOutcome.NotAttempted;
        }

        // An ORGANIZATION-grant revoke passes an empty ContactId (task 073 #7) — there is no single
        // grantee, so no single email, so no permission to match. Saying NotAttempted is the honest
        // answer; claiming success would repeat A-13's mistake in a new place. See the org-expansion
        // follow-up in notes/task-017-spe-revoke-matcher.md.
        if (request.ContactId == Guid.Empty)
        {
            logger.LogInformation(
                "[EXT-REVOKE] Organization-grant revoke (no single grantee contact) — SPE container " +
                "permission removal not attempted for container {ContainerId}.", request.ContainerId);
            return SpeContainerRevokeOutcome.NotAttempted;
        }

        var containerId = request.ContainerId.Value.ToString();

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
