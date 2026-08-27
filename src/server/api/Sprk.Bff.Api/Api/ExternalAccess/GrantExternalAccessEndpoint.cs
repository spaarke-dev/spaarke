using System.Security.Claims;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Authentication;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/grant
///
/// Grants an external Contact access to a Secure Project by:
///   1. Creating a sprk_externalrecordaccess record in Dataverse.
///   2. Invalidating the contact's participation cache in Redis.
///
/// Broker-only (ADR-028 Amendment A1): external users never authenticate to SPE
/// directly — all external SPE access is app-only via the BFF — so no synthetic
/// SPE container permission is written on grant.
///
/// ADR-001: Minimal API — no controllers.
/// ADR-008: Endpoint filter for internal caller check (RequireAuthorization).
/// ADR-009: Redis cache invalidation after grant (key: sdap:external:access:{contactId}).
/// ADR-010: Concrete DI injections.
/// </summary>
public static class GrantExternalAccessEndpoint
{
    private const string EntitySet = "sprk_externalrecordaccesses";
    // Cache key components for invalidation. BOUND to ExternalParticipationService (the read/store side,
    // the single source of truth) so a version bump there stays in sync here automatically. Task 073 #7
    // fix: the prior hard-coded `CacheVersion = 1` silently missed the v2/v3 stored key, so grant
    // invalidation never actually cleared the cache (it relied on the 60s TTL). Tenant scope is derived
    // from the caller's 'tid' claim; the cached value is per-Contact participation data, not an authz
    // decision (ADR-009).
    private const string ExternalAccessResource = ExternalParticipationService.ExternalAccessResource;
    private const int CacheVersion = ExternalParticipationService.CacheVersion;

    /// <summary>
    /// Registers the grant endpoint on the external-access group.
    /// </summary>
    public static RouteGroupBuilder MapGrantExternalAccessEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/grant", GrantAccessAsync)
            .WithName("GrantExternalAccess")
            .WithSummary("Grant external access to a Contact for a Secure Project")
            .WithDescription(
                "Creates a sprk_externalrecordaccess record and invalidates the contact's Redis " +
                "participation cache after granting. External SPE access is app-only (broker-only).")
            .Produces<GrantAccessResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    // =========================================================================
    // Handler
    // =========================================================================

    private static async Task<IResult> GrantAccessAsync(
        GrantAccessRequest request,
        DataverseWebApiClient dataverseClient,
        ITenantCache cache,
        HttpContext httpContext,
        ILogger<Program> logger,
        IConfiguration configuration,
        CancellationToken ct)
    {
        // ── Validation ───────────────────────────────────────────────────────
        // Grantee kind (task 073 #7): a normal grant names a Contact; an ORGANIZATION grant names an
        // Organization with NO ContactId — every ACTIVE member of that org then inherits access at
        // check time (AccessibleRecordSetService Term 3). Exactly one grantee kind is required; an
        // empty ContactId is only valid when an OrganizationId is supplied.
        var isOrgGrant = request.ContactId == Guid.Empty;
        if (isOrgGrant && !request.OrganizationId.HasValue)
            return ProblemDetailsHelper.ValidationError(
                "ContactId is required, unless granting to an Organization (supply OrganizationId with no ContactId).");

        // Resolve the polymorphic grant root (project|matter|workassignment) or the legacy ProjectId
        // shorthand. Fail-closed: a missing/unknown root is rejected 400 and NO row is written.
        var root = ResolveGrantRoot(request);
        if (!root.Ok)
            return ProblemDetailsHelper.ValidationError(root.Error!);

        if (!Enum.IsDefined(typeof(ExternalAccessLevel), request.AccessLevel))
            return ProblemDetailsHelper.ValidationError(
                $"AccessLevel must be one of: {string.Join(", ", Enum.GetNames<ExternalAccessLevel>())}.");

        // ── Resolve caller identity for granted-by reference ─────────────────
        var callerSystemUserId = ResolveCallerSystemUserId(httpContext);

        logger.LogInformation(
            "[EXT-GRANT] Granting {AccessLevel} access to Contact {ContactId} for {RootType} {RootId}",
            request.AccessLevel, request.ContactId, root.Type, root.Id);

        // ── Create the access record (Dataverse) + invalidate cache ──────────
        Guid accessRecordId;
        try
        {
            accessRecordId = await CreateGrantAsync(request, root.Type, root.Id, callerSystemUserId, dataverseClient, cache, httpContext, logger, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[EXT-GRANT] Failed to create Dataverse access record for Contact {ContactId} / {RootType} {RootId}",
                request.ContactId, root.Type, root.Id);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to create external access record in Dataverse.",
                extensions: new Dictionary<string, object?> { ["traceId"] = httpContext.TraceIdentifier });
        }

        // Broker-only: no synthetic SPE container membership is granted on the external path.
        return TypedResults.Ok(new GrantAccessResponse(accessRecordId, SpeContainerMembershipGranted: false));
    }

    // =========================================================================
    // Reusable core (shared with the invite-and-grant orchestration, task 029)
    // =========================================================================

    /// <summary>
    /// UPSERTS a <c>sprk_externalrecordaccess</c> grant for one logical grant key (root × grantee), and
    /// invalidates the Contact's Redis participation cache. Throws on Dataverse failure; cache
    /// invalidation failure is non-fatal. Shared by <c>/grant</c> and <c>/invite-and-grant</c> (task 029)
    /// so both write an identical, audited grant.
    /// </summary>
    /// <returns>The id of the single surviving active row for the logical grant.</returns>
    /// <remarks>
    /// <para><b>Idempotent since task 010</b> (spec FR-09, finding A-11). This method previously CREATEd
    /// unconditionally, with no pre-existence check anywhere on the path — so granting the same contact
    /// the same access on the same root twice produced two active rows. Revoke then deactivated exactly
    /// one of them by id, leaving the other standing: access survived revocation, and the participation
    /// surface could not even show it (<c>QueryGrantSetAsync</c> collapses duplicates via
    /// <c>GroupBy(root).Max(level)</c> and never returns access-record ids).</para>
    ///
    /// <para>Now: query the logical key first — no match creates; an exact match is a no-op returning the
    /// existing id; a match at a different level updates that row IN PLACE. Any surplus active rows on
    /// the same key (pre-existing duplicates, or a lost create race) are collapsed onto the survivor.</para>
    /// </remarks>
    internal static async Task<Guid> CreateGrantAsync(
        GrantAccessRequest request,
        ExternalGrantRootType rootType,
        Guid rootId,
        string? callerOid,
        DataverseWebApiClient dataverseClient,
        ITenantCache cache,
        HttpContext httpContext,
        ILogger logger,
        CancellationToken ct)
    {
        var key = ResolveGrantKey(request, rootType, rootId);
        var requestedLevel = (int)request.AccessLevel;

        // ── UPSERT: does this logical grant already exist? ───────────────────
        // Failures propagate deliberately. Falling back to a blind create on a failed pre-existence
        // query would reintroduce exactly the duplicate this task removes.
        var existing = await ExternalGrantLifecycle.QueryActiveRowsAsync(dataverseClient, key, ct);

        if (existing.Count > 0)
        {
            // Deterministic survivor (lowest id) so concurrent grants elect the same row — see
            // QueryActiveRowsAsync's remarks.
            var survivor = existing[0];

            if (survivor.AccessLevel == requestedLevel)
            {
                logger.LogInformation(
                    "[EXT-GRANT] Grant {Key} already active at level {Level} — no-op (idempotent).",
                    key, requestedLevel);
            }
            else
            {
                await dataverseClient.UpdateAsync(
                    EntitySet, survivor.Id, new Dictionary<string, object?> { ["sprk_accesslevel"] = requestedLevel }, ct);

                logger.LogInformation(
                    "[EXT-GRANT] Grant {Key} level changed {Old} → {New} in place on record {AccessRecordId}.",
                    key, survivor.AccessLevel, requestedLevel, survivor.Id);
            }

            await CollapseDuplicatesAsync(dataverseClient, existing, survivor.Id, key, logger, ct);
            await InvalidateGranteeCacheAsync(request, cache, httpContext, logger, ct);

            return survivor.Id;
        }

        // sprk_grantedby is a systemuser lookup — its target is a Dataverse systemuserid, which is
        // DISTINCT from the caller's Azure AD object id (oid). Resolve the systemuserid from the oid;
        // if the caller has no matching systemuser, omit grantedby (an audit field must never 400 the grant).
        var grantedBySystemUserId = await ResolveGrantedBySystemUserIdAsync(dataverseClient, callerOid, logger, ct);

        var payload = BuildGrantPayload(request, rootType, rootId, grantedBySystemUserId);
        var accessRecordId = await dataverseClient.CreateAsync(EntitySet, payload, ct);

        logger.LogInformation(
            "[EXT-GRANT] Created access record {AccessRecordId} for Contact {ContactId} / {RootType} {RootId}",
            accessRecordId, request.ContactId, rootType, rootId);

        // Race window: two concurrent grants on the same key both see zero rows and both create. Re-query
        // and collapse so the pair converges on one row. Both racers elect the same survivor (lowest id),
        // so this is safe to run from either — they cannot deactivate each other.
        try
        {
            var afterCreate = await ExternalGrantLifecycle.QueryActiveRowsAsync(dataverseClient, key, ct);
            if (afterCreate.Count > 1)
            {
                var survivor = afterCreate[0];
                await CollapseDuplicatesAsync(dataverseClient, afterCreate, survivor.Id, key, logger, ct);
                accessRecordId = survivor.Id;
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: the grant itself succeeded. A surviving duplicate is swept at the next grant or
            // revoke on this key, both of which sweep by key rather than by id.
            logger.LogWarning(ex,
                "[EXT-GRANT] Post-create duplicate check failed for {Key}. The grant succeeded; any " +
                "duplicate will be collapsed by the next grant or revoke on this key.", key);
        }

        await InvalidateGranteeCacheAsync(request, cache, httpContext, logger, ct);

        return accessRecordId;
    }

    /// <summary>
    /// Derives the logical grant key this request targets: root × grantee, where the grantee is the
    /// Contact when one is named and the Organization otherwise.
    /// </summary>
    /// <remarks>
    /// An <c>OrganizationId</c> on a per-contact grant is the contact's firm — association metadata, not
    /// grantee identity. Treating it as identity would make a person grant and an org grant on the same
    /// root collide, so one could revoke the other.
    /// </remarks>
    internal static ExternalGrantKey ResolveGrantKey(
        GrantAccessRequest request, ExternalGrantRootType rootType, Guid rootId)
        => request.ContactId != Guid.Empty
            ? ExternalGrantKey.ForContact(rootType, rootId, request.ContactId)
            : ExternalGrantKey.ForOrganization(rootType, rootId, request.OrganizationId!.Value);

    /// <summary>
    /// Deactivates every active row for a logical grant except the elected survivor.
    /// </summary>
    /// <remarks>
    /// Non-fatal by design: the caller's grant has already been applied to the survivor, and a surviving
    /// duplicate is swept by the next grant or revoke on this key (both sweep by key, not by id). Failing
    /// the grant here would be worse — the caller's intent was satisfied.
    /// </remarks>
    private static async Task CollapseDuplicatesAsync(
        DataverseWebApiClient dataverseClient,
        IReadOnlyList<ExternalGrantRow> rows,
        Guid survivorId,
        ExternalGrantKey key,
        ILogger logger,
        CancellationToken ct)
    {
        var duplicates = rows.Where(r => r.Id != survivorId).Select(r => r.Id).ToList();
        if (duplicates.Count == 0)
            return;

        logger.LogWarning(
            "[EXT-GRANT] {Count} duplicate active row(s) found for {Key}; collapsing onto {SurvivorId}. " +
            "Duplicates predate task 010's upsert or lost a create race.",
            duplicates.Count, key, survivorId);

        try
        {
            await ExternalGrantLifecycle.DeactivateAsync(dataverseClient, duplicates, logger, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[EXT-GRANT] Failed to collapse duplicates for {Key}. The grant itself succeeded; the " +
                "next grant or revoke on this key will sweep them.", key);
        }
    }

    /// <summary>
    /// Invalidates the grantee Contact's Redis participation cache. Non-fatal.
    /// </summary>
    private static async Task InvalidateGranteeCacheAsync(
        GrantAccessRequest request,
        ITenantCache cache,
        HttpContext httpContext,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var tenantId = ExtractTenantId(httpContext);
            if (request.ContactId == Guid.Empty)
            {
                // Organization grant (task 073 #7): there is no single grantee contact to invalidate —
                // every active member's participation set is affected. We deliberately DO NOT fan out an
                // invalidation per member here (that would need a members-of-org read on the write path);
                // members pick up the new org grant within the 60s participation-cache TTL. (An org-scoped
                // cache key is a possible future optimization — see the org-grant design note.)
                logger.LogDebug(
                    "[EXT-GRANT] Organization grant — no per-contact cache to invalidate; members refresh within the participation TTL.");
            }
            else if (!string.IsNullOrEmpty(tenantId))
            {
                await cache.RemoveAsync(
                    tenantId, ExternalAccessResource, request.ContactId.ToString(), CacheVersion, ct: ct);
                logger.LogDebug("[EXT-GRANT] Invalidated cache for Contact {ContactId}", request.ContactId);
            }
            else
            {
                logger.LogWarning(
                    "[EXT-GRANT] No tenant claim found — skipping cache invalidation for Contact {ContactId}",
                    request.ContactId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[EXT-GRANT] Failed to invalidate Redis cache for Contact {ContactId}. Non-critical.",
                request.ContactId);
        }
    }

    /// <summary>
    /// Resolves the caller's Azure AD object id (<c>oid</c>) — the input to
    /// <see cref="ResolveGrantedBySystemUserIdAsync"/>, which maps it to the Dataverse systemuserid the
    /// audited <c>sprk_grantedby</c> lookup requires. NOTE: the oid is NOT itself a systemuserid.
    /// </summary>
    internal static string? ResolveCallerSystemUserId(HttpContext httpContext)
        => CallerResolution.ResolveObjectId(httpContext.User);

    /// <summary>
    /// Maps the caller's Azure AD object id (<paramref name="callerOid"/>) to their Dataverse
    /// <c>systemuserid</c> for the audited <c>sprk_grantedby</c> lookup. The systemuserid is DISTINCT
    /// from the AAD oid — binding the raw oid as a systemuserid fails Dataverse validation (400).
    /// Returns <c>null</c> when the oid is absent/unparseable or has no matching active systemuser, in
    /// which case <c>sprk_grantedby</c> is omitted (an audit field must never block the grant).
    /// </summary>
    internal static async Task<string?> ResolveGrantedBySystemUserIdAsync(
        DataverseWebApiClient dataverseClient, string? callerOid, ILogger logger, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(callerOid) || !Guid.TryParse(callerOid, out _))
            return null;

        try
        {
            var rows = await dataverseClient.QueryAsync<SystemUserRow>(
                "systemusers",
                filter: $"azureactivedirectoryobjectid eq {callerOid}",
                select: "systemuserid",
                top: 1,
                cancellationToken: ct);

            return rows.Count > 0 ? rows[0].systemuserid?.ToString() : null;
        }
        catch (Exception ex)
        {
            // Non-fatal: grantedby is audit metadata. Log and omit rather than fail the grant.
            logger.LogWarning(ex,
                "[EXT-GRANT] Failed to resolve grantedby systemuser for oid {Oid} — omitting the audit field.",
                callerOid);
            return null;
        }
    }

    private sealed class SystemUserRow
    {
        public Guid? systemuserid { get; set; }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Result of resolving the polymorphic grant root from a <see cref="GrantAccessRequest"/>.
    /// <c>Ok == false</c> carries a caller-safe validation <see cref="Error"/> (→ 400, no write).
    /// </summary>
    internal readonly record struct GrantRootResolution(bool Ok, ExternalGrantRootType Type, Guid Id, string? Error);

    /// <summary>
    /// Resolves the ONE grant root a request targets. Precedence: an explicit
    /// <c>RecordType</c> + <c>RecordId</c> wins; otherwise the legacy <c>ProjectId</c> shorthand maps to a
    /// project root. Fail-closed (NFR-08): an unknown <c>RecordType</c>, an explicit <c>RecordType</c> with
    /// an empty <c>RecordId</c>, or no root at all (incl. a bare <c>RecordId</c> without <c>RecordType</c>)
    /// returns <c>Ok == false</c> — the caller rejects 400 and writes NO row.
    /// </summary>
    internal static GrantRootResolution ResolveGrantRoot(GrantAccessRequest request)
    {
        // Explicit polymorphic root takes precedence over the legacy shorthand.
        if (!string.IsNullOrWhiteSpace(request.RecordType))
        {
            if (!ExternalGrantRoot.TryParse(request.RecordType, out var type))
                return new GrantRootResolution(false, default, Guid.Empty,
                    "RecordType must be one of: project, matter, workassignment.");

            var explicitId = request.RecordId ?? Guid.Empty;
            if (explicitId == Guid.Empty)
                return new GrantRootResolution(false, default, Guid.Empty,
                    "RecordId is required and must be a valid GUID when RecordType is specified.");

            return new GrantRootResolution(true, type, explicitId, null);
        }

        // Legacy shorthand: a bare ProjectId maps to the project root (back-compat until task 071).
        if (request.ProjectId != Guid.Empty)
            return new GrantRootResolution(true, ExternalGrantRootType.Project, request.ProjectId, null);

        // Fail-closed: no usable root (also covers RecordId supplied without RecordType).
        return new GrantRootResolution(false, default, Guid.Empty,
            "A grant root is required: provide recordType + recordId, or the legacy projectId.");
    }

    /// <summary>
    /// Builds the <c>sprk_externalrecordaccess</c> create payload. Internal (not private) so the test
    /// assembly (<c>InternalsVisibleTo("Sprk.Bff.Api.Tests")</c>) can assert the typed-lookup bind
    /// contract directly — a wrong <c>@odata.bind</c> key silently breaks the grant.
    /// </summary>
    internal static object BuildGrantPayload(
        GrantAccessRequest request, ExternalGrantRootType rootType, Guid rootId, string? grantedBySystemUserId)
    {
        // Bind exactly ONE typed root lookup per record type (never two). Nav property is PascalCase
        // (sprk_Project / sprk_Matter / sprk_WorkAssignment), verified live — see ExternalGrantRoot.
        var (navigationProperty, entitySet) = ExternalGrantRoot.BindFor(rootType);

        // @odata.bind nav-property names are PascalCase (sprk_Contact / sprk_GrantedBy / sprk_Project…),
        // verified live against sprk_externalrecordaccess $metadata (task 070). The lowercase *id forms
        // teams-app-r1 used were wrong and 400'd every grant.
        var payload = new Dictionary<string, object?>
        {
            [$"{navigationProperty}@odata.bind"] = $"/{entitySet}({rootId})",
            ["sprk_accesslevel"] = (int)request.AccessLevel,
            ["sprk_granteddate"] = DateTime.UtcNow.ToString("o")
        };

        // Grantee: bind the Contact for a per-contact grant; OMIT it for an ORGANIZATION grant (task 073
        // #7 — an empty ContactId + a bound sprk_Organization identifies the grantee, and every active
        // org member inherits at check time). A row with NO sprk_Contact is exactly how the read path
        // (AccessibleRecordSetService Term 3) distinguishes an org grant from a per-contact grant, so this
        // omission is load-bearing, not cosmetic.
        if (request.ContactId != Guid.Empty)
        {
            payload["sprk_Contact@odata.bind"] = $"/contacts({request.ContactId})";
        }

        // grantedBySystemUserId is already a resolved Dataverse systemuserid (see
        // ResolveGrantedBySystemUserIdAsync) — NOT the caller's raw AAD oid. Omitted when unresolved.
        if (!string.IsNullOrEmpty(grantedBySystemUserId) &&
            Guid.TryParse(grantedBySystemUserId, out var systemUserId))
        {
            payload["sprk_GrantedBy@odata.bind"] = $"/systemusers({systemUserId})";
        }

        if (request.ExpiryDate.HasValue)
        {
            // Bug fix (task 070): the grant table's expiry field is sprk_expiresdate (verified live via
            // describe), NOT sprk_expirydate — the prior name would 400 any grant that carries an expiry.
            payload["sprk_expiresdate"] = request.ExpiryDate.Value.ToString("o");
        }

        // Firm/org association (task 070, owner steer 2026-08-11): bind the grantee's sprk_organization —
        // NOT the OOB `account`. Nav property is PascalCase sprk_Organization (lookup added to
        // sprk_externalrecordaccess in this project); value uses the plural set /sprk_organizations({id}).
        if (request.OrganizationId.HasValue)
        {
            payload["sprk_Organization@odata.bind"] = $"/sprk_organizations({request.OrganizationId.Value})";
        }

        return payload;
    }

    /// <summary>
    /// Extracts the Azure AD tenant ID ('tid' claim) from the authenticated HttpContext.
    /// Returns null when no claim is present (in which case cache invalidation is skipped).
    /// </summary>
    private static string? ExtractTenantId(HttpContext httpContext)
        => httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
}
