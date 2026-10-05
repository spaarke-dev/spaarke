// Graph, Azure.Identity and HTTP-header usings were dropped by task 017: this endpoint no longer talks to
// Graph at all. Its forked SPE matcher (finding A-13) was deleted in favour of
// SpeContainerMembershipService, which owns that conversation.
using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
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
/// ADR-009: Redis cache invalidation after revoke — ExternalParticipationService.InvalidateGrantSetsAsync (task 137).
/// ADR-010: Concrete DI injections.
/// </summary>
public static class RevokeExternalAccessEndpoint
{
    private const string AccessEntitySet = "sprk_externalrecordaccesses";
    // Cache invalidation (task 137): ExternalParticipationService.InvalidateGrantSetsAsync owns the key, every
    // tenant it can be cached under, and the organization → members expansion. No private RemoveAsync copy here.

    /// <summary>
    /// M2 (task 024 → task 065). The reason code for the one incomplete-revoke shape this endpoint can
    /// report: the Dataverse grant WAS deactivated, but the SPE container permission could not be
    /// confirmed removed (<see cref="SpeContainerRevokeOutcome.Failed"/>). Reuses the
    /// <c>sdap.*.incomplete.*</c> FAMILY <see cref="ProjectClosureEndpoint"/> established for the
    /// identical failure shape, with its own leaf — a client switching on the code must be able to tell a
    /// single-grant revoke from a project closure, not just "something is incomplete".
    /// </summary>
    internal const string RevokeSpeCleanupIncompleteReason = "sdap.revoke.incomplete.container_not_cleared";

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
        ExternalParticipationService participations,
        Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer assignedAccess,
        // Task 166 (amendment b): the container is derived from the grant's ROOT — never from the request.
        RecordContainerResolver containerResolver,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct,
        // Task 166 r1: "today" for the remaining-grant overlap check (which grants still confer access). Optional so
        // the many direct callers in tests keep compiling; resolved from DI on the route (registered unconditionally
        // by ExternalAccessModule).
        TimeProvider? timeProvider = null)
    {
        // ── Validation ───────────────────────────────────────────────────────
        if (request.AccessRecordId == Guid.Empty)
            return ProblemDetailsHelper.ValidationError("AccessRecordId is required and must be a valid GUID.");

        // ContactId is OPTIONAL (task 073 #7): revoke is authoritative by AccessRecordId (root- AND
        // grantee-agnostic). Since task 137 the cache invalidation (Step 3) is keyed on the revoked ROW's grantee,
        // so it no longer needs ContactId either: a contact grant clears that contact, an ORGANIZATION grant clears
        // every active member. ContactId still keys the per-contact SPE cleanup (Step 2), unchanged.
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
            speContainerMembership, dataverseClient, containerResolver, request, grantKey,
            ExternalGrantLifecycle.TodayUtc(timeProvider ?? TimeProvider.System), logger, ct);

        // ── Step 3: Invalidate the grant cache — the ONE routine (task 137) ───
        // Keyed on the REVOKED ROW's grantee (the same key Step 1 swept), not on request.ContactId: a contact grant
        // revoked without a ContactId on the request is still invalidated, and an organization grant expands to
        // every ACTIVE member. Every tenant a grant set can be cached under is cleared — the CIAM one too, which a
        // removal under the admin's own tid alone used to miss for the 60-second TTL. Non-fatal by construction:
        // the routine never throws, so a failed invalidation cannot change this response.
        await participations.InvalidateGrantSetsAsync(
            grantKey.ContactId is { } revokedContact ? new[] { revokedContact } : Array.Empty<Guid>(),
            grantKey.IsOrganizationGrant && grantKey.OrganizationId is { } revokedOrganization
                ? new[] { revokedOrganization }
                : Array.Empty<Guid>(),
            CancellationToken.None);

        // ── Task 142: an operator removal of an Assigned-To auto grant STICKS (owner round 2 item 5) ──
        // The subject's ledger rows on this record become Declined, so no trigger re-creates the grant while the
        // assignment persists (a manual /grant afterwards still succeeds). Ledger-only and never thrown: the revoke above
        // stands whatever happens here, and a lost marker is repaired by the next pass's out-of-band rule. For a CONTACT
        // auto grant the response also names the read-time terms that still bring it to the record (owner A2: standing
        // and organization access stay) — criterion 17: never "removed" while access silently remains.
        var residualAccessTerms = await assignedAccess.MarkGrantRevokedAsync(
            grantKey.RootType, grantKey.RootId, grantKey.ContactId,
            grantKey.IsOrganizationGrant ? grantKey.OrganizationId : null, CancellationToken.None);

        // ── M2 (task 024 → task 065): align with /close-project for the identical failure shape ──
        //
        // ONLY Failed warrants a 500 — NotAttempted, PermissionRemoved and NoPermissionFound are all
        // healthy, expected outcomes under the broker-only model (see SpeContainerRevokeOutcome's own doc
        // comments) and stay a 200. Before this, /revoke reported Failed — "the grantee may RETAIN file
        // access" — as a 200 with the failure buried in the body, while ProjectClosureEndpoint reported
        // the identical shape as a 500. Per this task's ADR-003 constraint and the owner's 2026-09-10
        // directive ("if revoked, a user message should be provided not just a 500 error"), the status
        // code now matches AND deactivatedCount/speContainerOutcome/speOrgMemberCleanup ride in the
        // ProblemDetails extensions so the caller can still render the specific, three-outcome message —
        // never a bare 500.
        if (speOutcome == SpeContainerRevokeOutcome.Failed)
        {
            logger.LogError(
                "[EXT-REVOKE] Revoke of {AccessRecordId} INCOMPLETE: {Deactivated} Dataverse row(s) " +
                "deactivated, but the SPE container permission could not be confirmed removed. The " +
                "grantee may RETAIN file access. Reporting 500 (M2) rather than 200.",
                request.AccessRecordId, deactivatedCount);

            return RevokeIncomplete(httpContext, deactivatedCount, speOutcome, orgCleanup, residualAccessTerms);
        }

        // DeactivatedCount makes the outcome explicit rather than inferable: 0 means the grant was
        // already fully inactive (a safe no-op), >1 means duplicates existed and were swept — the exact
        // condition that used to leave access standing after a "successful" revoke.
        return TypedResults.Ok(new RevokeAccessResponse(
            SpeContainerMembershipRevoked: speOutcome == SpeContainerRevokeOutcome.PermissionRemoved,
            SpeContainerOutcome: speOutcome,
            DeactivatedCount: deactivatedCount,
            SpeOrgMemberCleanup: orgCleanup,
            ResidualAccessTerms: residualAccessTerms.Count > 0 ? residualAccessTerms : null));
    }

    /// <summary>
    /// The "SPE container permission could not be confirmed removed" shape: 500 + ProblemDetails carrying
    /// a machine-readable reason code (ADR-003) and the correlation id (ADR-019) — the M2 alignment with
    /// <see cref="ProjectClosureEndpoint"/>'s <c>ClosureIncomplete</c> for the identical failure shape.
    /// </summary>
    /// <remarks>
    /// <para><b>M2 (task 024 → task 065).</b> Only <see cref="SpeContainerRevokeOutcome.Failed"/> reaches
    /// here. Reuses the <c>sdap.*.incomplete.*</c> shape closure established, with its OWN leaf
    /// (<see cref="RevokeSpeCleanupIncompleteReason"/>) rather than closure's literal code — a client
    /// switching on the code must be told a single-grant revoke from a project closure.</para>
    ///
    /// <para><b>Owner directive, 2026-09-10.</b> "if revoked, a user message should be provided not just a
    /// 500 error" — <paramref name="deactivatedCount"/> rides in extensions for exactly the reason
    /// <c>ClosureIncomplete</c>'s own remarks give: N grants WERE deactivated even when the SPE half
    /// failed, and a bare 500 cannot distinguish that from nothing having happened.
    /// <paramref name="speOutcome"/> and <paramref name="orgCleanup"/> are carried too — for an
    /// organization revoke, <c>orgCleanup.MembersEnumerated == null</c> is the only signal distinguishing
    /// "we swept everyone" from "we do not know who to sweep", the same load-bearing <c>null</c> the 200
    /// path already preserves through serialization.</para>
    /// </remarks>
    private static IResult RevokeIncomplete(
        HttpContext httpContext,
        int deactivatedCount,
        SpeContainerRevokeOutcome speOutcome,
        SpeOrgMemberCleanupSummary? orgCleanup,
        IReadOnlyList<string>? residualAccessTerms = null)
    {
        var detail = deactivatedCount > 0
            ? $"Deactivated {deactivatedCount} Dataverse access grant(s), but the SPE container " +
              "permission could not be confirmed removed. The grantee may RETAIN file access. Retry the " +
              "revoke."
            : "The SPE container permission could not be confirmed removed, and no Dataverse access " +
              "grants were deactivated. Retry the revoke.";

        return Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "External access revoke incomplete",
            detail: detail,
            type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            extensions: new Dictionary<string, object?>
            {
                ["reasonCode"] = RevokeSpeCleanupIncompleteReason,
                ["deactivatedCount"] = deactivatedCount,
                ["speContainerOutcome"] = speOutcome,
                ["speOrgMemberCleanup"] = orgCleanup,
                ["residualAccessTerms"] = residualAccessTerms is { Count: > 0 } ? residualAccessTerms : null,
                ["traceId"] = httpContext.TraceIdentifier
            });
    }

    // =========================================================================
    // Helpers
    // =========================================================================

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
    ///
    /// <para><b>WHICH container — task 166 (route-authorization sweep amendment (b)).</b> It used to be
    /// <c>request.ContainerId</c>, a client-chosen value, while <c>DelegationRuleFilter</c> authorized the grant
    /// row's ROOT — the same "authorized against one id, mutates another" shape as close-project (S-39). The field
    /// is deleted and the container is derived from the grant's root through
    /// <see cref="ProjectClosureEndpoint.DeriveRecordOwnContainerAsync"/>: a SECURE root's own container is cleaned;
    /// a non-secure root's derived container is the SHARED business-unit container, which a single revoke must not
    /// sweep (the grantee may hold other grants it serves), so that case is <c>NotAttempted</c>; an undecidable
    /// container is <c>Failed</c> (→ the M2 500). Note the shipped Manage Access modal never sent a container, so
    /// until this change secure-root revokes never cleaned up SPE at all.</para>
    ///
    /// <para><b>Overlap — task 166 r1.</b> Permissions are keyed by EMAIL, not by grant, so one person can hold the
    /// same container permission on the strength of two grants on this root (a direct contact grant AND membership of
    /// an organization granted the same root). Revoking one of them must not strip a permission the other still
    /// justifies. So before removing, the root's REMAINING active, unexpired grants are read and every contact they
    /// still entitle (direct grantees, and the active members of granted organizations) is kept. If that answer
    /// cannot be established (a read fault, or more rows than one revoke may read) the removal proceeds for everyone
    /// the revoked grant named — fail closed (ADR-003): an entitlement that cannot be verified is not honoured.</para>
    /// </remarks>
    private static async Task<(SpeContainerRevokeOutcome Outcome, SpeOrgMemberCleanupSummary? OrgCleanup)>
        RemoveSpeContainerPermissionsAsync(
            SpeContainerMembershipService speContainerMembership,
            DataverseWebApiClient dataverseClient,
            RecordContainerResolver containerResolver,
            RevokeAccessRequest request,
            ExternalGrantKey grantKey,
            DateOnly today,
            ILogger logger,
            CancellationToken ct)
    {
        var (derivedContainerId, decided) = await ProjectClosureEndpoint.DeriveRecordOwnContainerAsync(
            containerResolver, ExternalGrantRoot.LogicalNameFor(grantKey.RootType), grantKey.RootId, logger, ct);

        if (!decided)
        {
            logger.LogError(
                "[EXT-REVOKE] The container of grant root {RootType} {RootId} could not be determined; the SPE "
                + "permission cleanup cannot run and the grantee may RETAIN file access.",
                grantKey.RootType, grantKey.RootId);
            return (SpeContainerRevokeOutcome.Failed, grantKey.IsOrganizationGrant ? UnknownMembership : null);
        }

        if (derivedContainerId is null)
        {
            logger.LogInformation(
                "[EXT-REVOKE] Grant root {RootType} {RootId} is not a secure record with its own container — no "
                + "record-owned container permission to remove.", grantKey.RootType, grantKey.RootId);
            return (SpeContainerRevokeOutcome.NotAttempted, null);
        }

        var containerId = derivedContainerId;

        // ── Task 166 r1: who does ANOTHER live grant on this root still entitle? ──
        // null = could not be established; the removal then proceeds for everyone (fail closed).
        var stillEntitled = await ResolveStillEntitledContactsAsync(
            dataverseClient, grantKey.RootType, grantKey.RootId, today, logger, ct);

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
                speContainerMembership, dataverseClient, containerId, organizationId, stillEntitled, logger, ct);
        }

        // A CONTACT-grant row revoked without a ContactId on the request: the row names the grantee, but
        // resolving them here would change which identity the per-contact path acts on, which task 017's
        // constraint pins. Left as NotAttempted — the honest answer, and unchanged behaviour.
        if (request.ContactId == Guid.Empty)
        {
            logger.LogInformation(
                "[EXT-REVOKE] Contact-grant revoke with no ContactId on the request — no identity key to " +
                "match; SPE container permission removal not attempted for container {ContainerId}.",
                containerId);
            return (SpeContainerRevokeOutcome.NotAttempted, null);
        }

        if (stillEntitled is not null && stillEntitled.Contains(request.ContactId))
        {
            logger.LogInformation(
                "[EXT-REVOKE] Contact {ContactId} is still entitled to {RootType} {RootId} through another active grant "
                + "on it; their container permission on {ContainerId} is KEPT (task 166).",
                request.ContactId, grantKey.RootType, grantKey.RootId, containerId);
            return (SpeContainerRevokeOutcome.NotAttempted, null);
        }

        return (await RemoveContactSpePermissionAsync(
            speContainerMembership, dataverseClient, request, containerId, logger, ct), null);
    }

    /// <summary>The most remaining grant rows on one root a single revoke reads for the overlap check.</summary>
    internal const int MaxRemainingGrantsPerOverlapCheck = 200;

    /// <summary>
    /// Task 166 r1: the contacts that the root's REMAINING active, unexpired grants still entitle — each direct
    /// contact grantee, and every active member of each granted organization — or <see langword="null"/> when that
    /// set cannot be established (a read fault, more than <see cref="MaxRemainingGrantsPerOverlapCheck"/> rows, or an
    /// organization whose membership cannot be read or exceeds its sweep bound).
    /// </summary>
    /// <remarks>
    /// Runs AFTER Step 1, so the revoked grant's own rows are already inactive and are not counted. Expiry is applied
    /// in memory with <see cref="ExternalParticipationService.ConfersAccessOn"/>, the one mirror of the read side's
    /// predicate, so a lapsed grant keeps no one's permission. Organization membership is the statecode-active set the
    /// revoke sweep itself uses (<see cref="ExternalOrganizationMembership"/>). <c>internal</c> for the tests.
    /// </remarks>
    internal static async Task<IReadOnlySet<Guid>?> ResolveStillEntitledContactsAsync(
        DataverseWebApiClient dataverseClient,
        ExternalGrantRootType rootType,
        Guid rootId,
        DateOnly today,
        ILogger logger,
        CancellationToken ct)
    {
        List<ExternalGrantRow> remaining;
        try
        {
            remaining = await ExternalGrantLifecycle.QueryActiveRowsForRootAsync(
                dataverseClient, rootType, rootId, MaxRemainingGrantsPerOverlapCheck + 1, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[EXT-REVOKE] Could not read the remaining grants on {RootType} {RootId}; no permission is kept on the "
                + "strength of another grant (fail closed).", rootType, rootId);
            return null;
        }

        if (remaining.Count > MaxRemainingGrantsPerOverlapCheck)
        {
            logger.LogWarning(
                "[EXT-REVOKE] {RootType} {RootId} has more than {Bound} remaining active grants; the overlap check "
                + "cannot be completed, so no permission is kept on the strength of another grant (fail closed).",
                rootType, rootId, MaxRemainingGrantsPerOverlapCheck);
            return null;
        }

        var entitled = new HashSet<Guid>();
        foreach (var row in remaining)
        {
            if (!ExternalParticipationService.ConfersAccessOn(row.ExpiresDate, today))
            {
                continue;
            }

            if (row.ContactId is { } contactId)
            {
                entitled.Add(contactId);
                continue;
            }

            if (row.OrganizationId is not { } organizationId)
            {
                continue;
            }

            OrganizationMemberSet members;
            try
            {
                members = await ExternalOrganizationMembership.QueryActiveMembersAsync(dataverseClient, organizationId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "[EXT-REVOKE] Could not read the members of organization {OrganizationId}, which still holds a grant "
                    + "on {RootType} {RootId}; the overlap check fails closed.", organizationId, rootType, rootId);
                return null;
            }

            if (members.ExceededBound)
            {
                logger.LogWarning(
                    "[EXT-REVOKE] Organization {OrganizationId} (still granted {RootType} {RootId}) exceeds the member "
                    + "sweep bound; the overlap check fails closed.", organizationId, rootType, rootId);
                return null;
            }

            entitled.UnionWith(members.ContactIds);
        }

        return entitled;
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
        if (result.Error?.StartsWith(SpeContainerMembershipService.NoPermissionFoundError, StringComparison.OrdinalIgnoreCase) == true)
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
    /// <c>sprk_enddate</c> and <c>sprk_startdate</c>, and this ignores both — deliberately. Since task 109
    /// the read side (<c>ExternalParticipationService.ReadOrganizationMembershipsAsync</c>) confers
    /// inherited access only through memberships that are date-current under an active organization, a
    /// SUBSET of the <c>statecode</c>-active rows swept here. So this sweep is a superset of everyone the
    /// read side can still let in: over-including on a revoke removes more access (fail-closed);
    /// under-including does not. (Before task 109 the read side conferred on <c>statecode</c> alone, and
    /// this sweep matched it exactly; the superset relation is what has to hold, and still does.)</para>
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
            IReadOnlySet<Guid>? stillEntitled,
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

        // ── Resolve identities ───────────────────────────────────────────────
        // Membership is written with userPrincipalName = the contact's email, so an email is the ONLY
        // key that can match an ACL entry. Resolve them all first, then do ONE container read.
        //
        // ISS-004 (#968), closed by task 024: this loop used to call RevokeMembershipAsync per member,
        // and each of those calls read the container's ENTIRE permission collection — N members = N
        // full reads. Task 024's paging made that N × pages. The container is now read ONCE.
        var removed = 0;
        var notFound = 0;
        var failed = 0;
        var retained = 0;

        var resolved = new List<(Guid ContactId, string Email)>();

        foreach (var memberContactId in memberSet.ContactIds)
        {
            // Task 166 r1: a member another live grant on this root still entitles keeps their permission.
            if (stillEntitled is not null && stillEntitled.Contains(memberContactId))
            {
                retained++;
                logger.LogInformation(
                    "[EXT-REVOKE] Member Contact {ContactId} of Organization {OrganizationId} is still entitled through "
                    + "another active grant on this root; their permission on {ContainerId} is KEPT.",
                    memberContactId, organizationId, containerId);
                continue;
            }

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

                resolved.Add((memberContactId, memberEmail));
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogError(ex,
                    "[EXT-REVOKE] Could not resolve the email for member Contact {ContactId} of " +
                    "Organization {OrganizationId}; their permission on {ContainerId} cannot be matched " +
                    "and may remain. Continuing with the rest.",
                    memberContactId, organizationId, containerId);
            }
        }

        // ── Sweep: ONE paged read, then match every member against it ────────
        // ⚠️ Behaviour note: results are keyed by EMAIL, so two contact rows sharing one address now
        // both report the outcome of the single permission that address owns. Previously the second
        // call found the permission already deleted and reported NoPermissionFound, which read as "this
        // person never had access". Keying by identity is the more truthful of the two.
        IReadOnlyDictionary<string, SpeContainerMembershipResult> speResults =
            resolved.Count == 0
                ? new Dictionary<string, SpeContainerMembershipResult>(StringComparer.OrdinalIgnoreCase)
                : await speContainerMembership.RemoveMembershipsAsync(
                    containerId, resolved.Select(r => r.Email).ToList(), ct);

        foreach (var (memberContactId, memberEmail) in resolved)
        {
            if (!speResults.TryGetValue(memberEmail, out var result))
            {
                // A member we asked about but got no answer for is an unknown, not an absence.
                failed++;
                logger.LogError(
                    "[EXT-REVOKE] No SPE outcome was returned for member Contact {ContactId} of " +
                    "Organization {OrganizationId} on {ContainerId}. Treating as FAILED — they may " +
                    "RETAIN file access.",
                    memberContactId, organizationId, containerId);
                continue;
            }

            if (result.Success)
            {
                removed++;
                logger.LogInformation(
                    "[EXT-REVOKE] Removed SPE container permission {PermissionId} for member Contact " +
                    "{ContactId} of Organization {OrganizationId} on {ContainerId}",
                    result.PermissionId, memberContactId, organizationId, containerId);
            }
            else if (result.Error?.StartsWith(SpeContainerMembershipService.NoPermissionFoundError, StringComparison.OrdinalIgnoreCase) == true)
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

        var summary = new SpeOrgMemberCleanupSummary(
            MembersEnumerated: memberSet.ContactIds.Count,
            PermissionsRemoved: removed,
            PermissionsNotFound: notFound,
            Failed: failed,
            RetainedByOtherGrant: retained);

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

        // Task 166 r1: every enumerated member is still entitled through another live grant on this root — nobody's
        // permission was even looked up, so "nobody held one" would be a claim this revoke never checked.
        if (summary.MembersEnumerated > 0 && summary.RetainedByOtherGrant == summary.MembersEnumerated)
            return SpeContainerRevokeOutcome.NotAttempted;

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
    /// <c>internal</c> since task 166: project closure removes the revoked grantees' container permissions by the
    /// SAME identity key, so it reuses this rather than a second copy.
    /// </remarks>
    internal static async Task<string?> ResolveContactEmailAsync(
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
