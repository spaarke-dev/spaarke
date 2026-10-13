using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Microsoft.AspNetCore.Http.HttpResults;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/unsecure-project
///
/// Removes a record's secure designation — the reverse of <see cref="ProvisionProjectEndpoint"/>, for a
/// <c>sprk_project</c>, <c>sprk_matter</c> or <c>sprk_workassignment</c> (task 144 widened it from projects only;
/// the request takes <c>recordType</c> + <c>recordId</c>, or the legacy <c>projectId</c>). design.md §5.1 calls the
/// designation reversible, and spec FR-28 counts the reverse path as part of the mechanism rather than a
/// nice-to-have.
///
/// Sequence:
///   0. (task 175, owner round 84) A work assignment or project filed under a matter or project is refused 409
///      <c>sdap.access.access_follows_parent</c>, naming the parent: its access follows the parent and is locked. Its own
///      un-secure is the CASCADE of its parent's (or of its re-file), through <see cref="UnsecureInheritedAsync"/>
///   1. Read the record; a record that is not secure changes nothing about the record itself (idempotent) — but its
///      related records are reconciled (task 148: an earlier unsecure may have left them isolated); one whose flag
///      comes back EMPTY is refused — empty is "could not tell", never "not secure" (task 150)
///   1.5 Who may remove it (task 150, owner round 3b F3, through the ONE F3 rule <see cref="SecureDesignationRemoval"/>):
///      a Full Access holder or the record's creator — any other Write holder is refused 403 before any write
///   2. Resolve the new owner — request, else configuration, else the calling user
///   2.5 Snapshot the rows the record's Assign cascades to (task 133's primitive) — refuse before any write if they
///      cannot be read
///   3. Assign ownership to that user, and verify by read-back
///   3.5 Re-own every EXISTING related record OUT of the Secure Record owner team to the owner the ownership rule gives a
///      child of an ordinary record, then remove its mirrored shares (task 148) — a pass that does not complete stops
///      here: the record's own shares and its flag are left for the next call to finish
///   4. Revoke every POA share on the record
///   4.5 (task 158 r1c-v2, round 39 item 1) For each sharee revoked, end what the record passed on to the secure work
///      assignments / projects filed under it (round 30's reverse rule: only the unmodified inherited share; direct,
///      raised, another-parent and last-reader shares kept) — not complete → stop, flag kept, the same call completes it
///   5. Clear <c>sprk_issecure</c>
///   5.5 (round 39 item 1) Give back what the filed records' other secure parents still pass on (reported if not yet)
///   6. (task 175, owner round 84; replaces round 6 item 4) The work assignments and projects filed below it follow it:
///      each whose only secure source was this record is un-secured through these same steps (top-down, bounded; the
///      secure-root inheritance job completes the rest), and its Access Permission brought into step
///
/// ADR-001: Minimal API. ADR-003: fail closed — a step that cannot be verified is treated as failed.
/// ADR-008: authorization is the route group's delegation filter (FR-07 Write-on-record, as the caller).
/// ADR-010: concrete DI injections.
/// </summary>
/// <remarks>
/// <para><b>Ordering is the security-relevant part.</b> Ownership moves BEFORE the shares are revoked.
/// Reversed, there would be a window in which the record still sat on the memberless secure owner team (the
/// Secure Record business unit's NAMED owner team since task 144) with its shares already gone — reachable by
/// nobody, which is the exact failure this project exists to remove. Ownership first means someone can always see
/// the record.</para>
///
/// <para><b>The flag is cleared last, and on purpose.</b> <c>sprk_issecure</c> is a label that other
/// surfaces read (the container resolver, the evaluator veto). Clearing it while the record was still
/// team-owned and share-gated would advertise "this is a normal record" about a record that still
/// behaved like a secure one.</para>
///
/// <para><b>Every share is revoked, not only the ones provisioning issued.</b> A secure record's
/// access came entirely from explicit shares. Once ownership and business-unit access apply normally,
/// a leftover POA row is a second access path that no longer appears in any UI that reasons about
/// secure records — invisible access is worse than no access.</para>
/// </remarks>
public static class UnsecureProjectEndpoint
{
    private const string SystemUserEntitySet = "systemusers";
    private const string TeamEntitySet = "teams";

    /// <summary>
    /// Optional configuration naming the <c>systemuser</c> that un-secured records land on.
    /// </summary>
    /// <remarks>
    /// Optional by design. Without it the record goes to the caller, who has already proven Write on
    /// it — a deterministic, auditable owner that needs no environment setup. An environment that
    /// wants un-secured matters to land on a fixed steward sets this instead.
    /// </remarks>
    internal const string UnsecureOwnerUserIdConfigKey = "SecureRecord:UnsecureOwnerUserId";

    private const string ReasonKey = "reasonCode";

    /// <summary>
    /// The record could not be found or read. The code keeps its original "project" wording for every root type
    /// (task 144): it is a machine-readable contract, and the prose detail names the actual type.
    /// </summary>
    internal const string ReasonProjectNotFound = "sdap.unsecure.project_not_found";
    internal const string ReasonOwnerUnresolved = "sdap.unsecure.owner_unresolved";
    internal const string ReasonOwnerAssignmentFailed = "sdap.unsecure.owner_assignment_failed";
    internal const string ReasonOwnerAssignmentNotApplied = "sdap.unsecure.owner_assignment_not_applied";
    internal const string ReasonFlagNotCleared = "sdap.unsecure.flag_not_cleared";

    /// <summary>
    /// <summary>
    /// Task 148 (ADR-003): the record's existing related records were not all taken out of isolation (re-owned to their
    /// business unit's team, mirrored shares removed). 500 with <c>childrenReowned</c>, <c>childrenRemaining</c> and
    /// <c>childTables</c>. On a record still flagged secure, its own shares are NOT revoked and <c>sprk_issecure</c> is NOT
    /// cleared (the flag keeps meaning "related records may still be isolated"); the ownership move already made stands.
    /// Calling again completes it.
    /// </summary>
    internal const string ReasonChildrenIncomplete = "sdap.unsecure.children_incomplete";

    /// <summary>
    /// Task 148 (task 133's handoff): the rows the record's Assign cascades to (SharePoint document locations and documents)
    /// could not be read BEFORE the ownership move, so where each one ends up could not be recorded. Refused before any
    /// write, as provisioning refuses the same read (<c>cascadeChildState</c>: <c>unreadable</c> = the next call may pass;
    /// <c>refused</c> = deterministic, an administrator acts).
    /// </summary>
    internal const string ReasonCascadeChildrenUnreadable = "sdap.unsecure.cascade_children_unreadable";

    /// <summary>
    /// Task 150 (owner round 3b, F3): the caller holds Write and Share — the route group's delegation filter — but is neither a
    /// Full Access holder on the record (Write AND Delete on it, as Dataverse reports the caller's rights) nor the person
    /// who created it. Refused before any write (403); the record stays secure. The code is the ONE F3 rule's
    /// (<see cref="SecureDesignationRemoval.NotPermittedReasonCode"/>, task 146; owner round 13 item 6).
    /// </summary>
    internal const string ReasonNotPermitted = SecureDesignationRemoval.NotPermittedReasonCode;

    /// <summary>
    /// Task 150: who may remove the designation could not be established — the caller's identity, their rights, the
    /// record's recorded creator person, or (a column this environment lacks) whether a creator person is recorded at
    /// all. Refused before any write. The code is the ONE F3 rule's
    /// (<see cref="SecureDesignationRemoval.PermissionUnverifiableReasonCode"/>).
    /// </summary>
    internal const string ReasonPermissionUnverifiable = SecureDesignationRemoval.PermissionUnverifiableReasonCode;

    /// <summary>
    /// Task 150: <c>sprk_issecure</c> came back EMPTY. Every row reads <c>true</c> or <c>false</c> once the one-time
    /// backfill has run (<c>scripts/Repair-SecureFlagNulls.ps1</c>) and the column defaults to No, so an empty value
    /// means this service cannot read the field-secured column. Refused before any write: reporting "already not
    /// secure" would claim a state nobody observed.
    /// </summary>
    internal const string ReasonSecureFlagUnreadable = "sdap.unsecure.secure_flag_unreadable";

    /// <summary>
    /// Task 158 (owner round 6), kept by task 175: the record is a work assignment or project still secure through what it
    /// is filed under (at any level), so it cannot be unsecured — it would be secured again. 409, nothing written; the
    /// detail names the secure record. Since round 84 the route refuses every record WITH a parent first
    /// (<see cref="AccessFollowsParent.ReasonCode"/>); this one is the cascade's answer when a record's parent became secure
    /// again before its turn.
    /// </summary>
    internal const string ReasonParentStillSecure = "sdap.unsecure.parent_still_secure";

    /// <summary>
    /// Task 158: whether a matter or project the record is filed under is still secure could not be read (or read empty).
    /// 500, nothing written (ADR-003 — an unreadable parent is never "not secure"). The same caller may call again.
    /// </summary>
    internal const string ReasonParentUnverifiable = "sdap.unsecure.parent_unverifiable";

    /// <summary>
    /// Task 158: an <c>alsoUnsecure</c> record is not a secure work assignment or project filed under this record — it is
    /// left as it is and reported.
    /// </summary>
    internal const string ReasonNotRelated = "sdap.unsecure.not_related";

    public static RouteGroupBuilder MapUnsecureProjectEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/unsecure-project", UnsecureProjectAsync)
            .WithName("UnsecureProject")
            .WithSummary("Remove the secure designation from a project, matter or work assignment")
            .WithDescription(
                "Reassigns ownership off the Secure Record business unit's named owner team, re-owns the record's " +
                "existing related records out of isolation and removes their mirrored shares, revokes the record's " +
                "explicit shares and clears sprk_issecure; then the work assignments and projects filed under it follow it " +
                "(owner round 84): each whose only secure source it was is un-secured the same way. A work assignment or " +
                "project filed under a matter or project is refused 409 sdap.access.access_follows_parent (its access " +
                "follows its parent). Accepts recordType + recordId (project | matter | workassignment) or the legacy " +
                "projectId. Idempotent: a record that is already not secure returns 200 having changed nothing about the " +
                "record itself (related records an earlier unsecure left isolated are completed).")
            .Produces<UnsecureProjectResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    /// <summary>
    /// The record this request targets. The delegation filter calls this too, so the record whose Write was
    /// checked is the record re-owned (task 144; the <c>FromGrantRoot</c> pattern).
    /// </summary>
    internal static GrantExternalAccessEndpoint.GrantRootResolution ResolveRoot(UnsecureProjectRequest request)
        => SecureRecordRoot.ResolveTarget(request.ProjectId, request.RecordType, request.RecordId);

    private static async Task<IResult> UnsecureProjectAsync(
        UnsecureProjectRequest request,
        DataverseWebApiClient dataverseClient,
        IDataverseRecordShareService recordShare,
        CallerRecordAccessProbe callerAccessProbe,
        SecureChildReconciler secureChildren,
        SecureRootInheritance relatedRoots,
        IConfiguration configuration,
        IMembershipCacheInvalidator accessCacheInvalidator,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var traceId = httpContext.TraceIdentifier;

        var target = ResolveRoot(request);
        if (!target.Ok)
            return Problem(StatusCodes.Status400BadRequest, "Bad Request",
                target.Error ?? "A record to un-secure is required.", traceId);

        var root = SecureRecordRoot.For(target.Type);
        var recordId = target.Id;

        // ── Task 175 (owner round 87): never below the floor the parents set ──
        //
        // A work assignment or project filed under a SECURE matter or project (at any level) has its secure designation from
        // there: removing it would make it looser than its parents, so it is refused before anything is read or written,
        // naming the secure parent (409 access_follows_parent). One whose parents are NOT secure — its secure designation was
        // set on it by hand — is un-secured by its F3 holder exactly as a parentless record is. What it is filed under that
        // cannot be read refuses too (ADR-003): never "not secure" on a guess.
        if (SecureRootInheritance.Inherits(root.LogicalName))
        {
            var filing = await relatedRoots.FindFilingParentsAsync(root.LogicalName, recordId, ct);
            if (!filing.IsKnown)
            {
                logger.LogWarning(
                    "[UNSECURE] {RecordType} {RecordId}: what it is filed under could not be read ({Why}). Nothing was changed. " +
                    "TraceId={TraceId}", root.WireToken, recordId, filing.Unverifiable, traceId);
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    $"Whether the matter or project this {root.DisplayLabel.ToLowerInvariant()} is filed under is secure could " +
                    "not be determined, so its secure designation was left in place and nothing was changed. Try again.",
                    traceId, (ReasonKey, ReasonParentUnverifiable));
            }

            if (filing.HasSecureParent)
            {
                var secureParents = filing.DirectParents.Where(p => p.EffectiveSecure).Select(p => p.Parent).ToList();
                logger.LogInformation(
                    "[UNSECURE] {RecordType} {RecordId} is filed under secure {Parent}; its secure designation comes from there " +
                    "(owner round 87). Refused. TraceId={TraceId}", root.WireToken, recordId,
                    string.Join(", ", secureParents.Select(p => $"{p.Table}:{p.Id:D}")), traceId);
                return AccessFollowsParent.SecureFloorProblem(root.DisplayLabel,
                    secureParents.Count > 0 ? secureParents : filing.SecureParents.ToList(), traceId);
            }
        }

        // Task 158: the related records the caller asks to unsecure too — validated before anything is written.
        var also = new List<(SecureRecordRoot Root, Guid Id)>();
        foreach (var related in request.AlsoUnsecure ?? Array.Empty<RelatedRecordRef>())
        {
            if (related is null
                || !ExternalGrantRoot.TryParse(related.RecordType, out var relatedType)
                || related.RecordId == Guid.Empty
                || !SecureRootInheritance.Inherits(ExternalGrantRoot.LogicalNameFor(relatedType))
                || (relatedType == root.Type && related.RecordId == recordId))
            {
                return Problem(StatusCodes.Status400BadRequest, "Bad Request",
                    "Each alsoUnsecure entry names a work assignment or project filed under this record (recordType " +
                    "'workassignment' or 'project', and its recordId). Nothing was changed.", traceId);
            }

            also.Add((SecureRecordRoot.For(relatedType), related.RecordId));
        }

        var result = await UnsecureRecordAsync(
            root, recordId, request, UnsecureActor.Caller(callerAccessProbe, httpContext), dataverseClient, recordShare,
            secureChildren, relatedRoots, configuration, accessCacheInvalidator, logger, traceId, ct);

        // The record's own unsecure did not succeed: its answer stands, and no related record is touched.
        if (result is not Ok<UnsecureProjectResponse> { Value: { } response } || !SecureRootInheritance.IsParent(root.LogicalName))
        {
            if (also.Count > 0 && result is Ok<UnsecureProjectResponse> { Value: { } plain })
            {
                // A work assignment has nothing filed under it for this rule: every alsoUnsecure entry is not related.
                return TypedResults.Ok(plain with
                {
                    RelatedRecordsUnsecured = also.Select(a => new RelatedUnsecureOutcome(
                        a.Root.WireToken, a.Id, "refused", ReasonNotRelated,
                        $"Nothing is filed under a {root.DisplayLabel.ToLowerInvariant()} for this rule.")).ToList(),
                });
            }

            return result;
        }

        return TypedResults.Ok(await WithRelatedRecordsAsync(response, root, recordId, also, relatedRoots, logger, traceId, ct));
    }

    /// <summary>
    /// Task 175 (owner round 84: "if parent changes, then child changes"; replaces round 6 item 4's "never auto-unsecure")
    /// — after the record itself is no longer secure, every work assignment and project filed below it follows it: the
    /// cascade (<see cref="SecureRootInheritance.CascadeBelowAsync"/>) un-secures each one whose only secure source was this
    /// record through this endpoint's own steps (<see cref="UnsecureInheritedAsync"/>: ownership to its business unit's
    /// team, related records out of isolation, shares revoked, flag cleared last), top-down, and brings its Access
    /// Permission into step. One also filed under another secure record stays secure. No F3 per related record: F3 was
    /// asked on this record, and a record with a parent cannot be un-secured on its own (round 84). What this call does
    /// not finish (bounded, or a step that did not complete) stays at the more restrictive state and is listed; the
    /// secure-root inheritance job completes it (≤ 5 minutes).
    /// </summary>
    /// <remarks>
    /// <c>alsoUnsecure</c> is still accepted (older clients send it) but asks for nothing more: every record filed under this
    /// one follows it. An entry that is not filed below it is reported <c>not_related</c>, as before.
    /// </remarks>
    private static async Task<UnsecureProjectResponse> WithRelatedRecordsAsync(
        UnsecureProjectResponse response,
        SecureRecordRoot root,
        Guid recordId,
        IReadOnlyList<(SecureRecordRoot Root, Guid Id)> also,
        SecureRootInheritance relatedRoots,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        // A record below with no access record yet is never loosened (the backfill rule keeps what it holds beyond the floor):
        // its record is written when it is secured by inheritance, and the job backfills the rest (task 175 fix round).
        var pass = await relatedRoots.CascadeBelowAsync(root.LogicalName, recordId, traceId, ct);
        if (pass.Unreadable)
        {
            logger.LogError(
                "[UNSECURE] The records filed under {RecordType} {RecordId} could not be read; none followed it in this call " +
                "(the secure-root inheritance job does). TraceId={TraceId}", root.WireToken, recordId, traceId);
            return response with
            {
                RelatedSecureRecordsUnreadable = true,
                RelatedRecordsUnsecured = also.Count == 0
                    ? null
                    : also.Select(a => new RelatedUnsecureOutcome(a.Root.WireToken, a.Id, "failed", ReasonNotRelated,
                        "The records filed under this one could not be read, so it was not unsecured. Try again.")).ToList(),
            };
        }

        var outcomes = new List<RelatedUnsecureOutcome>();
        foreach (var result in pass.Results.Where(r => r.UnsecureOutcome is not null))
        {
            outcomes.Add(new RelatedUnsecureOutcome(SecureRootInheritance.WireTokenFor(result.Table), result.Id,
                result.UnsecureOutcome!, result.UnsecureOutcome == FollowParentsResult.Unsecured ? null : result.ReasonCode,
                result.UnsecureOutcome == FollowParentsResult.Unsecured ? null : result.Detail));
        }

        foreach (var (relatedRoot, relatedId) in also)
        {
            if (outcomes.Any(o => o.RecordId == relatedId))
                continue;
            if (!pass.Filed.Any(f => f.Confirmed && f.Id == relatedId
                                     && string.Equals(f.Table, relatedRoot.LogicalName, StringComparison.OrdinalIgnoreCase)))
            {
                outcomes.Add(new RelatedUnsecureOutcome(relatedRoot.WireToken, relatedId, "refused", ReasonNotRelated,
                    $"It is not a {relatedRoot.DisplayLabel.ToLowerInvariant()} filed under this " +
                    $"{root.DisplayLabel.ToLowerInvariant()}, so it was left as it is."));
            }
        }

        var stillSecure = pass.StillSecure
            .Select(f => new RelatedSecureRecord(SecureRootInheritance.WireTokenFor(f.Table), f.Id, f.Name))
            .ToList();
        if (stillSecure.Count > 0 || pass.Deferred > 0)
        {
            logger.LogInformation(
                "[UNSECURE] After {RecordType} {RecordId}: {StillSecure} record(s) filed below it are still secure (another secure " +
                "parent, a step that did not complete, or {Deferred} left to the job). TraceId={TraceId}",
                root.WireToken, recordId, stillSecure.Count, pass.Deferred, traceId);
        }

        return response with
        {
            RelatedSecureRecords = stillSecure,
            RelatedRecordsUnsecured = outcomes.Count == 0 ? null : outcomes,
        };
    }

    /// <summary>
    /// unified-access-control-r2 task 175 (owner round 84: "if parent changes, then child changes" — un-securing cascades)
    /// — removes the secure designation from a work assignment or project whose secure parents are no longer secure, through
    /// THIS endpoint's own steps, never a second implementation (the <see cref="ProvisionProjectEndpoint.ProvisionInheritedAsync"/>
    /// precedent). There is no caller: no F3 (the parent's unsecure, or the re-file, is the act — round 84: F3 applies only to
    /// a parentless record), and the record lands on <paramref name="owner"/> — the team the ownership rule gives a record
    /// filed under its now-ordinary parents (their business unit's team, D-11). Every other step, its ORDER and its stops are
    /// the endpoint's (task 158): ownership moved and read back BEFORE the related records leave isolation, BEFORE the shares
    /// are revoked, the flag cleared LAST — so a step that does not complete leaves the record flagged secure (the more
    /// restrictive state) and the same call completes it. Step 1.5's check asks the WHOLE filing chain here: a record that is
    /// still secure through any ancestor is refused (409 <see cref="ReasonParentStillSecure"/>) and stays secure.
    /// </summary>
    /// <returns>The endpoint's own result: 200 (un-secured, or already not secure), or the refusal / failure it would answer.</returns>
    internal static Task<IResult> UnsecureInheritedAsync(
        SecureRecordRoot root,
        Guid recordId,
        DataversePrincipalRef owner,
        string traceId,
        DataverseWebApiClient dataverseClient,
        IDataverseRecordShareService recordShare,
        SecureChildReconciler secureChildren,
        SecureRootInheritance relatedRoots,
        IConfiguration configuration,
        IMembershipCacheInvalidator accessCacheInvalidator,
        ILogger logger,
        CancellationToken ct)
        => UnsecureRecordAsync(
            root, recordId, new UnsecureProjectRequest(Guid.Empty, null, root.WireToken, recordId), UnsecureActor.Cascade(owner),
            dataverseClient, recordShare, secureChildren, relatedRoots, configuration, accessCacheInvalidator, logger, traceId, ct);

    /// <summary>
    /// Removes the secure designation from ONE record — the endpoint's steps (below), for the record the request names (a
    /// caller, F3) and, task 175, for a work assignment or project whose parents are no longer secure (the cascade).
    /// </summary>
    private static async Task<IResult> UnsecureRecordAsync(
        SecureRecordRoot root,
        Guid recordId,
        UnsecureProjectRequest request,
        UnsecureActor actor,
        DataverseWebApiClient dataverseClient,
        IDataverseRecordShareService recordShare,
        SecureChildReconciler secureChildren,
        SecureRootInheritance relatedRoots,
        IConfiguration configuration,
        IMembershipCacheInvalidator accessCacheInvalidator,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {

        // The legacy field names a PROJECT. For a matter or work assignment it is empty, never the matter's id.
        var legacyProjectId = root.Type == ExternalGrantRootType.Project ? recordId : Guid.Empty;

        // ── Step 1: Read the record ──────────────────────────────────────────
        SecurityRow? record;
        try
        {
            var rows = await dataverseClient.QueryAsync<SecurityRow>(
                root.EntitySet,
                filter: $"{root.IdColumn} eq {recordId}",
                select: $"{root.IdColumn},sprk_issecure,_createdby_value",
                top: 1,
                cancellationToken: ct);

            record = rows.FirstOrDefault();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[UNSECURE] Could not read {RecordType} {RecordId}. TraceId={TraceId}", root.WireToken, recordId, traceId);

            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"The {root.DisplayLabel.ToLowerInvariant()} could not be read from Dataverse.", traceId,
                (ReasonKey, ReasonProjectNotFound));
        }

        if (record is null)
            return Problem(StatusCodes.Status404NotFound, "Not Found",
                $"{root.DisplayLabel} {recordId} was not found.", traceId, (ReasonKey, ReasonProjectNotFound));

        // Task 150: EMPTY is not FALSE. A field-secured column this identity cannot read comes back empty, not refused,
        // and after the one-time backfill no row legitimately holds NULL — so this is "could not tell", never
        // "already not secure".
        if (record.sprk_issecure is null)
        {
            logger.LogError(
                "[UNSECURE] sprk_issecure came back EMPTY on {RecordType} {RecordId}. Refusing: this service has likely " +
                "lost its field-level-security Read on the column (scripts/Set-SecureFlagFieldSecurity.ps1 -Verify), or " +
                "the row predates the backfill (scripts/Repair-SecureFlagNulls.ps1). TraceId={TraceId}",
                root.WireToken, recordId, traceId);

            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"Whether this {root.DisplayLabel.ToLowerInvariant()} is secure could not be read, so its secure " +
                "designation was not changed. An administrator needs to check the secure-record setup.",
                traceId, (ReasonKey, ReasonSecureFlagUnreadable));
        }

        // Idempotency: nothing to undo. Returning 200 rather than 409 because the caller's intent
        // ("this record should not be secure") is already satisfied — a repeat is not a conflict.
        if (record.sprk_issecure != true)
        {
            // Task 148: the record is not secure, but its related records may still be — an unsecure from before task 148
            // left them owned by the memberless Secure team (owner round 11 item 3's ship gate). Completing that is this
            // call's job ("re-invoking unsecure ... completes the pass instead of returning 200-no-op"). Only children the
            // Secure team owns are moved; nothing about the record itself changes.
            var completing = await secureChildren.ReconcileAsync(
                root.LogicalName, recordId, SecureChildReconcileMode.Apply, SecureChildPassTrigger.UnsecureCompletion, ct);
            if (!completing.IsComplete)
                return ChildrenIncomplete(completing, root, recordId, flagStillSet: false, logger, traceId);

            // Task 158 r1c-v2 (round 39 item 1): an ordinary matter or project passes nothing on, so anything it is still on
            // record as having passed on to a secure record filed under it — a flag cleared outside the BFF, or a pass that wrote
            // while an earlier unsecure ran — is ended here too, and what the filed records' other secure parents still pass on
            // is given back. Never anything for a record that never passed anything on.
            var leftover = await relatedRoots.EndWhatAParentPassedOnAsync(root.LogicalName, recordId, traceId, ct);
            var (leftoverNotRegiven, _) = leftover.IsComplete
                ? await relatedRoots.GiveBackAsync(leftover.GiveBack, traceId, ct)
                : (0, Array.Empty<string>());
            if (!leftover.IsComplete || leftoverNotRegiven > 0)
            {
                logger.LogError(
                    "[UNSECURE] {RecordType} {RecordId} is not secure, but what it was on record as passing on to the secure records " +
                    "filed under it was not all ended or given back (notDone={NotDone}, notRegiven={NotRegiven}: {Detail}). " +
                    "TraceId={TraceId}", root.WireToken, recordId, leftover.NotDone, leftoverNotRegiven, leftover.Detail, traceId);
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    AlreadyOrdinaryLeftoverDetail(root.DisplayLabel.ToLowerInvariant(), leftover.NotDone, leftoverNotRegiven),
                    traceId,
                    (ReasonKey, ReasonChildrenIncomplete),
                    ("filedRecordsNotUpdated", leftover.NotDone + leftoverNotRegiven));
            }

            logger.LogInformation(
                "[UNSECURE] {RecordType} {RecordId} is already not secure; nothing to do to the record itself " +
                "(related records re-owned: {Reowned}). TraceId={TraceId}",
                root.WireToken, recordId, completing.ChildrenReowned, traceId);

            return TypedResults.Ok(new UnsecureProjectResponse(
                ProjectId: legacyProjectId,
                NewOwnerSystemUserId: Guid.Empty,
                SharesRevoked: 0,
                AlreadyUnsecure: true,
                // NOT true. No sweep runs on this path, so this response cannot vouch that the record
                // carries no shares — and claiming it could would reinstate ISS-018 precisely here: an
                // operator who reads sweepComplete=false retries, the flag is now clear, and they would
                // be told "complete sweep of zero" while the surviving rows are still in place.
                SweepComplete: null,
                RecordType: root.WireToken,
                RecordId: recordId,
                Children: SecureChildPassSummary.From(completing)));
        }

        // ── Step 1.5: who may remove it (task 150, owner round 3b F3) — a CALLER only ─────────
        //
        // Task 175 (round 84): the cascade has no caller. It runs only for a record whose secure parents are no longer secure
        // (the parent's unsecure, a re-file, the job), and F3 applies only to a parentless record now.
        var callerId = Guid.Empty;
        if (!actor.IsCascade)
        {
            var permission = await RefuseUnlessPermittedToRemoveAsync(
                record, dataverseClient, actor.Probe!, actor.Http!, root, recordId, logger, traceId, ct);

            if (permission.Refusal != null)
                return permission.Refusal;
            callerId = permission.CallerId;
        }

        // ── Step 1.5 (task 158; task 175): a record still secure through what it is filed under stays secure ──
        //
        // A work assignment or project filed under a secure record is secure itself (round 6; round 84 both ways): unsecuring
        // it while one is still secure would be undone at once, so it is refused, naming that record. Before any write. Task 175:
        // the WHOLE filing chain is asked (a project filed under a secure matter is secure even before its own flag is set, and
        // what is filed under it with it — task 174's effective rule). An unreadable chain refuses too (ADR-003): it is never
        // "not secure". The route refuses every record with a parent before this (round 84); the cascade reaches it.
        var parents = await relatedRoots.FindSecureParentsAsync(root.LogicalName, recordId, ct, SecureRootInheritance.MaxFilingDepth);
        if (!parents.HasSecureParent && !parents.IsKnown)
        {
            logger.LogWarning(
                "[UNSECURE] {RecordType} {RecordId}: whether a record it is filed under is still secure could not be determined " +
                "({Why}). Nothing was changed. TraceId={TraceId}", root.WireToken, recordId, parents.Unverifiable, traceId);
            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"Whether the matter or project this {root.DisplayLabel.ToLowerInvariant()} is filed under is still secure could " +
                "not be determined, so its secure designation was left in place and nothing was changed. Try again.",
                traceId, (ReasonKey, ReasonParentUnverifiable));
        }

        if (parents.SecureParents.FirstOrDefault() is { } secureParent)
        {
            var parentLabel = SecureRootInheritance.WireTokenFor(secureParent.Table) == "matter" ? "matter" : "project";
            logger.LogInformation(
                "[UNSECURE] {RecordType} {RecordId} is filed under secure {ParentType} {ParentId}; refused (owner rounds 6, 84). " +
                "TraceId={TraceId}", root.WireToken, recordId, parentLabel, secureParent.Id, traceId);
            return Problem(StatusCodes.Status409Conflict, "Conflict",
                $"This {root.DisplayLabel.ToLowerInvariant()} is filed under the secure {parentLabel} " +
                $"'{secureParent.Name ?? secureParent.Id.ToString()}', which is still secure. A record filed under a secure record " +
                $"is secure itself, so it stays secure while that {parentLabel} is secure. Nothing was changed.",
                traceId, (ReasonKey, ReasonParentStillSecure),
                ("parentRecordType", parentLabel), ("parentRecordId", secureParent.Id), ("parentName", secureParent.Name));
        }

        // ── Step 1.6 (task 175 fix round 2, K1): what is filed below is recorded BEFORE this record stops being secure ──
        //
        // A work assignment or project below it with no access record yet (secured before task 175's deploy) gets one now,
        // while this record is still secure: a Secure it holds through this record is recorded as INHERITED, so it follows
        // this record out in the cascade after Step 5. Best effort; a record not reached stays secure (never loosened).
        RecordBelowResult? recordedBelow = null;
        if (SecureRootInheritance.IsParent(root.LogicalName))
            recordedBelow = await relatedRoots.RecordBelowBeforeUnsecureAsync(root.LogicalName, recordId, traceId, ct);

        // ── Step 2: Resolve the new owner ────────────────────────────────────
        //
        // A caller: the request's nominee, else configuration, else the caller (a user). The cascade: the team it was given.
        var newOwner = actor.CascadeOwner
                       ?? (ResolveNewOwner(request, configuration, callerId) is { } userId && userId != Guid.Empty
                           ? DataversePrincipalRef.User(userId)
                           : (DataversePrincipalRef?)null);

        if (newOwner is not { } owner || owner.Id == Guid.Empty)
        {
            logger.LogError(
                "[UNSECURE] No owner could be resolved for {RecordType} {RecordId} — the request named " +
                "none, '{ConfigKey}' is unset, and the caller's systemuserid could not be established. " +
                "Refusing: handing the record to nobody would strand it exactly as it is. TraceId={TraceId}",
                root.WireToken, recordId, UnsecureOwnerUserIdConfigKey, traceId);

            return Problem(StatusCodes.Status403Forbidden, "Forbidden",
                "No owner could be determined for the un-secured record. Name one in the request, or " +
                $"configure '{UnsecureOwnerUserIdConfigKey}'.",
                traceId, (ReasonKey, ReasonOwnerUnresolved));
        }

        var ownerBind = owner.Kind == DataversePrincipalKind.SystemUser
            ? $"/{SystemUserEntitySet}({owner.Id})"
            : $"/{TeamEntitySet}({owner.Id})";

        // ── Step 2.5 (task 148, task 133's handoff): the rows the ownership move cascades to, read first ──
        //
        // The move below cascades Assign to the record's SharePoint document locations / documents (live metadata). Step 3.5
        // places them by the ownership rule (owner round 13 item 1), which needs them readable; a move whose cascaded rows
        // cannot even be read is not made — the same fail-closed rule provisioning applies before ITS move. The snapshot
        // is also the record of each row's owner before this call (reversal evidence), logged here.
        var cascade = await AssignCascadeChildOwners.SnapshotAsync(dataverseClient, root.LogicalName, recordId, ct);
        if (cascade.Snapshot is not { } cascadeBefore)
        {
            var refused = cascade.Failure == CascadeReadFailure.Refused;
            logger.LogError(cascade.Fault,
                "[UNSECURE] The {Table} rows {RecordType} {RecordId}'s ownership move cascades to could not be read ({State}). " +
                "Refusing before any change. TraceId={TraceId}",
                cascade.FailedTable?.LogicalName, root.WireToken, recordId, cascade.Failure, traceId);

            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"The records Dataverse moves together with the {root.DisplayLabel.ToLowerInvariant()} " +
                $"({cascade.FailedTable?.LogicalName}) could not be read, so the secure designation was left in place and " +
                "nothing was changed. " + (refused
                    ? "Calling again repeats this refusal: an administrator looks at those records, and the service's " +
                      "permission to read them, first."
                    : "Calling again once Dataverse is reachable repeats the check."),
                traceId, (ReasonKey, ReasonCascadeChildrenUnreadable),
                ("childTable", cascade.FailedTable?.LogicalName),
                ("cascadeChildState", refused ? "refused" : "unreadable"));
        }

        foreach (var child in cascadeBefore.Children)
        {
            logger.LogInformation(
                "[UNSECURE] Before the move: {Table} {ChildId} of {RecordType} {RecordId} is owned by {OwnerKind} {OwnerId}. " +
                "TraceId={TraceId}", child.LogicalName, child.Id, root.WireToken, recordId, child.Owner.Kind, child.Owner.Id,
                traceId);
        }

        // ── Step 3: Assign ownership, and verify it applied ──────────────────
        try
        {
            await dataverseClient.UpdateAsync(
                root.EntitySet,
                recordId,
                new Dictionary<string, object?>
                {
                    ["ownerid@odata.bind"] = ownerBind
                },
                ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[UNSECURE] Dataverse refused the ownership assignment of {RecordType} {RecordId} to {OwnerKind} " +
                "{OwnerId}. TraceId={TraceId}", root.WireToken, recordId, owner.Kind, owner.Id, traceId);

            // Task 132 (C12): a PATCH that timed out after Dataverse committed it lands here too — the record may have
            // been re-owned. Evict (always safe), exactly as on the "could not verify" outcome below.
            await EvictAfterOwnerChangeAsync(accessCacheInvalidator, root, recordId, traceId);

            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                "Ownership could not be reassigned, so the secure designation was left in place.",
                traceId, (ReasonKey, ReasonOwnerAssignmentFailed));
        }

        // Read-back, for the same reason provisioning does it: an accepted PATCH is not proof that an
        // owner navigation property was applied rather than ignored.
        try
        {
            var rows = await dataverseClient.QueryAsync<SecurityRow>(
                root.EntitySet,
                filter: $"{root.IdColumn} eq {recordId}",
                select: owner.Kind == DataversePrincipalKind.SystemUser
                    ? $"{root.IdColumn},_owninguser_value"
                    : $"{root.IdColumn},_owningteam_value",
                top: 1,
                cancellationToken: ct);

            var reread = rows.FirstOrDefault();
            var actualOwner = owner.Kind == DataversePrincipalKind.SystemUser ? reread?._owninguser_value : reread?._owningteam_value;
            if (actualOwner != owner.Id)
            {
                logger.LogError(
                    "[UNSECURE] Ownership read-back FAILED for {RecordType} {RecordId}: expected owning {OwnerKind} " +
                    "{OwnerId}, found {ActualOwnerId}. Leaving the record secure. TraceId={TraceId}",
                    root.WireToken, recordId, owner.Kind, owner.Id, actualOwner, traceId);

                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    "The ownership reassignment was accepted but did not take effect, so the secure " +
                    "designation was left in place.",
                    traceId, (ReasonKey, ReasonOwnerAssignmentNotApplied));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[UNSECURE] Could not verify the ownership reassignment of {RecordType} {RecordId}. " +
                "Treating it as failed. TraceId={TraceId}", root.WireToken, recordId, traceId);

            // Task 132 (C12): the PATCH was accepted and may have applied — evict (always safe) before reporting.
            await EvictAfterOwnerChangeAsync(accessCacheInvalidator, root, recordId, traceId);

            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                "The ownership reassignment could not be verified, so the secure designation was left " +
                "in place.", traceId, (ReasonKey, ReasonOwnerAssignmentNotApplied));
        }

        // ── Step 3.5 (task 148): the record's EXISTING related records leave isolation ──
        //
        // Ownership first, for the children too: each related record the Secure Record owner team owns is re-owned to the
        // owner the ownership rule gives a child of an ordinary record (its business unit's team — task 146's rule, the
        // resolver told this ONE record is mid-unsecure, its flag still set), and only then are its mirrored shares removed
        // (task 149's synchronizer). So no related record is ever reachable by nobody. The rows the move above cascaded to
        // are placed by the same rule (owner round 13 item 1), not left with the new owner. Owner round 6: related records
        // that are ROOTS of their own (a secure work assignment under this project) are not children: they follow this record
        // through their OWN un-secure, after it (Step 6, task 175).
        //
        // A pass that does not complete STOPS here (ADR-003): the record's own shares stay (its sharees keep reaching the
        // related records that are still isolated) and sprk_issecure stays set — the flag keeps meaning "related records
        // may still be isolated". The ownership move above stands. Calling again completes the pass.
        //
        // Steps 3.5 and 4 share one eviction (task 132 · C12, Step 4.5 below): the record's owner changed in Step 3, so an
        // incomplete child pass that returns here must evict exactly as a completed one does.
        ShareSweep sweep;
        SecureChildReconcileReport childPass;
        try
        {
            childPass = await secureChildren.ReconcileAsync(
                root.LogicalName, recordId, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, ct);
            if (!childPass.IsComplete)
                return ChildrenIncomplete(childPass, root, recordId, flagStillSet: true, logger, traceId,
                    OwnerExtension(owner));

            // ── Step 3.6 (task 158 r1, owner round 30): what its secure parents passed on ends with its shares ──
            //
            // A work assignment or project filed under a secure record carries shares that record passed on, each with a
            // provenance row on task 142's ledger. Step 4 revokes every share; the rows end first, so a later re-secure never
            // reads a revoked inherited share as an operator's removal (Declined) and withholds the parents' sharees for good.
            // Not done → stop here, as an incomplete child pass does: the flag and the shares stay, the same call completes it.
            if (await relatedRoots.EndProvenanceForUnsecureAsync(root.LogicalName, recordId, ct) is { } provenanceNotEnded)
            {
                logger.LogError(
                    "[UNSECURE] {RecordType} {RecordId}: {Why}; its shares were NOT revoked and it still reads as secure. " +
                    "TraceId={TraceId}", root.WireToken, recordId, provenanceNotEnded, traceId);
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    $"The {root.DisplayLabel.ToLowerInvariant()}'s related records were not all updated: {provenanceNotEnded}. Its " +
                    "ownership was reassigned, but its shares were NOT revoked and it still reads as secure. Calling again " +
                    "completes it.",
                    traceId,
                    (ReasonKey, ReasonChildrenIncomplete),
                    ("inheritedSharesNotEnded", true),
                    OwnerExtension(owner));
            }

            // ── Step 4: Revoke the explicit shares ───────────────────────────────
            //
            // After Step 3.5 every related record is out of isolation and carries none of the record's sharees, so
            // revoking the record's own shares now leaves nothing reachable by nobody (owner round 11 item 3: 148 re-owns
            // the children before the record's shares go).
            //
            // The new owner's OWN share (a creator-driven unsecure hands the record back to its creator, who still holds the
            // share provisioning gave them) is revoked AS that owner: Dataverse refuses it app-only (0x80040223). Step 3's
            // read-back proved the new owner owns it, so the sweep is told so (a team owner: every revoke is app-only).
            sweep = await RevokeAllSharesAsync(
                recordShare, root, recordId, owner, logger, traceId, ct);
        }
        finally
        {
            // ── Step 4.5 (task 132 · C12): the owner changed (and, past Step 4, the shares went) — evict, before any
            // return ── The record now sits with a user in a normal business unit, so that unit's colleagues gain it by
            // ownership, and every former sharee loses it. Their cached membership, impersonated root sets and access
            // snapshots would otherwise keep the old answer for the TTLs.
            await EvictAfterOwnerChangeAsync(accessCacheInvalidator, root, recordId, traceId);
        }

        // ── Step 4.5 (task 158 r1c-v2, main-session round 39 item 1): unsecuring a parent ends what it passed on ──
        //
        // Step 4 revoked every explicit share on this matter or project. For each sharee it revoked, round 30's reverse rule
        // runs on the secure work assignments and projects filed under it: ENDED is only the unmodified inherited share; KEPT
        // are a direct share, a raised mask (put back to what it raised), a share another secure parent still justifies and
        // the record's last reader (S5). The filed records are still SECURE here (they follow this record in Step 6, task 175,
        // through their own un-secure) — but their
        // sharees' access came only from this record's share, which is gone. The rows come from this record's own provenance,
        // so a repeat call ends exactly what is left. Not complete → stop BEFORE the flag is cleared: 500 children_incomplete,
        // the flag kept (it still says "what it passed on may not have ended"; the job reports the records filed under it),
        // and the same call completes it.
        var passedOn = await relatedRoots.EndWhatAParentPassedOnAsync(root.LogicalName, recordId, traceId, ct);
        if (!passedOn.IsComplete)
        {
            logger.LogError(
                "[UNSECURE] {RecordType} {RecordId}: what it passed on to the secure records filed under it was not all ended " +
                "({NotDone} not done: {Detail}); its flag was NOT cleared. TraceId={TraceId}",
                root.WireToken, recordId, passedOn.NotDone, passedOn.Detail, traceId);
            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"The {root.DisplayLabel.ToLowerInvariant()}'s ownership was reassigned and its shares were revoked, but the access " +
                $"it had passed on to the secure work assignments and projects filed under it could not all be removed yet " +
                $"({passedOn.NotDone} not removed), so it still reads as secure. Calling again completes it.",
                traceId,
                (ReasonKey, ReasonChildrenIncomplete),
                ("filedRecordsNotUpdated", passedOn.NotDone),
                OwnerExtension(owner),
                ("sharesRevoked", sweep.Revoked),
                ("sweepComplete", sweep.Complete));
        }

        // ── Step 5: Clear the flag ───────────────────────────────────────────
        try
        {
            await dataverseClient.UpdateAsync(
                root.EntitySet,
                recordId,
                new Dictionary<string, object?> { ["sprk_issecure"] = false },
                ct);
        }
        catch (Exception ex)
        {
            // Ownership already moved and the sweep has run, so the record IS reachable and no longer
            // isolated — but it still advertises itself as secure, and the sweep may not have removed
            // everything (see sweepComplete). Report loudly: this is a half-applied state an operator
            // must finish, and reporting 200 would hide it.
            logger.LogError(ex,
                "[UNSECURE] {RecordType} {RecordId} was reassigned to {OwnerId} and had {Count} share(s) " +
                "revoked (sweepComplete={SweepComplete}), but sprk_issecure could NOT be cleared. The " +
                "record is no longer isolated yet still reads as secure. TraceId={TraceId}",
                root.WireToken, recordId, owner.Id, sweep.Revoked, sweep.Complete, traceId);

            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                sweep.Complete
                    ? "Ownership was reassigned and every share revoked, but the secure flag could " +
                      "not be cleared. The record is no longer isolated but still reads as secure — " +
                      "clear sprk_issecure manually, or retry."
                    // Prose and machine-readable extension must not disagree: claiming "shares revoked"
                    // here while sweepComplete=false would hand the human reader the pre-fix claim.
                    : "Ownership was reassigned, but the share sweep could NOT account for every share " +
                      "AND the secure flag could not be cleared. The record is no longer isolated, " +
                      "still reads as secure, and may retain shares — clear sprk_issecure manually and " +
                      "check the record's remaining shares.",
                traceId,
                (ReasonKey, ReasonFlagNotCleared),
                OwnerExtension(owner),
                ("sharesRevoked", sweep.Revoked),
                ("sweepComplete", sweep.Complete));
        }

        logger.LogInformation(
            "[UNSECURE] {RecordType} {RecordId} un-secured: owner={OwnerId}, sharesRevoked={Count}, " +
            "sweepComplete={SweepComplete}. TraceId={TraceId}",
            root.WireToken, recordId, owner.Id, sweep.Revoked, sweep.Complete, traceId);

        // ── Step 5.5 (round 39 item 1): what the filed records' OTHER secure parents still pass on is given back ──
        //
        // A share Step 4.5 removed may be one another secure parent of the filed record passes on at a lower level: that part
        // is given back now, by the mirror's own pass — after the flag is cleared, because a parent flagged secure but no longer
        // isolated holds every mirror. One that cannot be given back yet is reported; the secure-root inheritance job gives it
        // (the record is filed under that other secure parent, so the job reaches it).
        var (notRegiven, regiveDetails) = await relatedRoots.GiveBackAsync(passedOn.GiveBack, traceId, ct);
        if (notRegiven > 0)
        {
            logger.LogWarning(
                "[UNSECURE] {RecordType} {RecordId} is un-secured, but on {Count} secure record(s) filed under it what their other " +
                "secure parents still pass on could not be given back yet: {Detail}. TraceId={TraceId}",
                root.WireToken, recordId, notRegiven, string.Join("; ", regiveDetails), traceId);
            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"The {root.DisplayLabel.ToLowerInvariant()}'s secure designation was removed. On {notRegiven} secure work " +
                "assignment(s) or project(s) filed under it, the access the other secure records they are filed under still give " +
                "could not be given back yet; it is given back automatically within a few minutes.",
                traceId,
                (ReasonKey, ReasonChildrenIncomplete),
                ("filedRecordsNotUpdated", notRegiven),
                OwnerExtension(owner),
                ("sharesRevoked", sweep.Revoked),
                ("sweepComplete", sweep.Complete));
        }

        return TypedResults.Ok(new UnsecureProjectResponse(
            ProjectId: legacyProjectId,
            NewOwnerSystemUserId: owner.Kind == DataversePrincipalKind.SystemUser ? owner.Id : Guid.Empty,
            SharesRevoked: sweep.Revoked,
            AlreadyUnsecure: false,
            SweepComplete: sweep.Complete,
            RecordType: root.WireToken,
            RecordId: recordId,
            Children: SecureChildPassSummary.From(childPass))
        {
            // Task 175 fix round 3 (K1): records below with no access record that Step 1.6 did not record (they stay secure;
            // the job records them). Null for a work assignment, or when the records below could not all be read.
            AccessRecordsNotRecorded = recordedBelow?.NotRecorded,
        });
    }

    /// <summary>
    /// Task 158 final round (main-session round 58 item 2): the already-ordinary path's refusal says what the operator must
    /// do, and only what a call can do. What could not be REMOVED is still on record as passed on, so calling Unsecure again
    /// removes it. What could not be GIVEN BACK is not: those rows are ended, so a repeat call finds nothing left to give back
    /// — the secure-root inheritance job gives it back (the record is filed under that other secure record), and the operator
    /// need do nothing.
    /// </summary>
    internal static string AlreadyOrdinaryLeftoverDetail(string label, int notRemoved, int notRegiven)
    {
        var detail = notRemoved > 0
            ? $"The {label} is not secure, but the access it had passed on to the secure work assignments and projects filed " +
              $"under it could not all be removed yet ({notRemoved} not removed). Run Unsecure on this {label} again to remove " +
              "the rest."
            : $"The {label} is not secure, and the access it had passed on to the secure work assignments and projects filed " +
              "under it was removed.";
        if (notRegiven > 0)
        {
            detail += $" On {notRegiven} of them, the access the other secure records they are filed under still give could not " +
                      "be given back yet. No action is needed for that: it is given back automatically within a few minutes " +
                      "(running Unsecure again does not give it back); open those records' Manage Access to check.";
        }

        return detail;
    }

    /// <summary>
    /// Task 148 — a child pass that did not complete (ADR-003: never a success, never a bare 500): per-table counts in the
    /// detail and the extensions. <paramref name="flagStillSet"/> says whether the record is still flagged secure — then its
    /// own shares were not revoked and its flag not cleared, which the detail states.
    /// </summary>
    private static IResult ChildrenIncomplete(
        SecureChildReconcileReport pass,
        SecureRecordRoot root,
        Guid recordId,
        bool flagStillSet,
        ILogger logger,
        string traceId,
        params (string Key, object? Value)[] more)
    {
        var summary = SecureChildPassSummary.From(pass);
        var perTable = string.Join(", ", summary.Tables
            .Where(t => t.Reowned + t.Refused + t.Failed > 0)
            .Select(t => $"{t.Table}: {t.Reowned} re-owned, {t.Refused + t.Failed} not"));

        logger.LogError(
            "[UNSECURE] {RecordType} {RecordId}: its related records were not all taken out of isolation: status={Status} " +
            "reowned={Reowned} remaining={Remaining} ({PerTable}) detail={Detail} flagStillSet={FlagStillSet}. TraceId={TraceId}",
            root.WireToken, recordId, pass.Status, summary.Reowned, summary.Remaining, perTable, pass.Detail, flagStillSet,
            traceId);

        var extensions = new List<(string Key, object? Value)>
        {
            (ReasonKey, ReasonChildrenIncomplete),
            ("childrenReowned", summary.Reowned),
            ("childrenRemaining", summary.Remaining),
            ("childTables", summary.Tables),
        };
        extensions.AddRange(more);

        return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
            $"The {root.DisplayLabel.ToLowerInvariant()}'s existing related records (documents, events, to-dos, " +
            $"communications, memos) were not all taken out of isolation: {summary.Reowned} re-owned, {summary.Remaining} " +
            "remaining" + (perTable.Length > 0 ? $" ({perTable})" : "") + (pass.Detail is null ? "" : $" — {pass.Detail}") +
            (flagStillSet
                ? ". Its ownership was reassigned, but its shares were NOT revoked and it still reads as secure, so the people " +
                  "shared on it keep reaching the related records that are still isolated. Calling again completes it."
                : ". Calling again completes it."),
            traceId,
            extensions.ToArray());
    }

    /// <summary>
    /// Evicts the caches an owner change makes stale, through the ONE hook every owner-changing writer must call
    /// (<see cref="IMembershipCacheInvalidator.InvalidateRecordOwnerChangeAsync"/>, task 132). Never fails the request:
    /// the hook does not throw, and it is not bound to the request's token — a client that disconnects must not leave
    /// the clean-up half done.
    /// </summary>
    private static Task EvictAfterOwnerChangeAsync(
        IMembershipCacheInvalidator accessCacheInvalidator, SecureRecordRoot root, Guid recordId, string traceId)
        => accessCacheInvalidator.InvalidateRecordOwnerChangeAsync(
            root.LogicalName, root.EntitySet, recordId, traceId, CancellationToken.None);

    /// <summary>
    /// Owner round 3b, F3 (task 150): only a <b>Full Access holder</b> on the record, or <b>the person who created
    /// it</b>, may remove its secure designation. Securing stays open to any Write holder; removing it does not, because
    /// it ends the isolation of everything stored against the record. Returns the refusal to send, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>ONE F3 check (owner round 13 item 6).</b> The decision is <see cref="SecureDesignationRemoval.DecideAsync"/>,
    /// task 146's shared rule, which also gates moving a CHILD out of a secure root (owner round 10 item 7). This method
    /// only supplies the question for a ROOT — the root is both the secure record Full Access is asked on and the record
    /// whose creator counts — and words the refusal for the unsecure endpoint. It holds no second copy of the rule.</para>
    ///
    /// <para><b>The creator</b> is <c>createdby</c>, or — for a record the BFF created app-only, whose <c>createdby</c>
    /// is the application user — the server-stamped <c>sprk_createdbyperson</c> (task 133), read in its own query only
    /// when neither <c>createdby</c> nor Full Access has admitted. The caller's identity is <c>WhoAmI</c> on their own
    /// token, never the request.</para>
    ///
    /// <para><b>Full Access</b> is Dataverse's own answer about the caller (<c>RetrievePrincipalAccess</c>, OBO): Write AND
    /// Delete on the record (<see cref="SecureDesignationRemoval.FullAccess"/>). A secure record is owned by a memberless
    /// team in a user-free business unit, so Delete on it comes from a Full Access share — or from an administrator's
    /// role, which is the "an administrator can remove the secure designation" the wizard promises.</para>
    ///
    /// <para><b>Fail closed (the helper's behaviour, owner round 13 item 6).</b> An unknown caller → 403
    /// <c>sdap.unsecure.permission_unverifiable</c>; a rights probe that threw or a creator person that could not be read
    /// → 500, same code (retryable); a <c>sprk_createdbyperson</c> column this environment lacks (Dataverse answers 400)
    /// → 403 <c>permission_unverifiable</c> ("could not tell", never "not permitted" and never "allowed"); a definite
    /// "no" → 403 <c>sdap.unsecure.not_permitted</c>. A Full Access holder is admitted even where the creator person
    /// cannot be read (Full Access is asked first). The refusal message is shown to the user as is (the ribbon command
    /// renders the endpoint's ProblemDetails), so it names who CAN do it.</para>
    /// </remarks>
    private static async Task<(IResult? Refusal, Guid CallerId)> RefuseUnlessPermittedToRemoveAsync(
        SecurityRow record,
        DataverseWebApiClient dataverseClient,
        CallerRecordAccessProbe callerAccessProbe,
        HttpContext httpContext,
        SecureRecordRoot root,
        Guid recordId,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        var decision = await SecureDesignationRemoval.DecideAsync(
            new SecureRemovalQuestion
            {
                Caller = SecureRemovalCaller.ForRequest(callerAccessProbe, httpContext),
                SecuredRecords = new[] { new SecuredRecordRef(root.LogicalName, recordId) },
                CreatedBy = record._createdby_value,
                ReadCreatedByPersonAsync = async token =>
                {
                    try
                    {
                        var people = await dataverseClient.QueryAsync<SecurityRow>(
                            root.EntitySet,
                            filter: $"{root.IdColumn} eq {recordId}",
                            select: $"{root.IdColumn},{RecordCreatorPerson.ValueColumn}",
                            top: 1,
                            cancellationToken: token);
                        return CreatorPersonAnswer.Recorded(people.FirstOrDefault()?.CreatedByPerson);
                    }
                    catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        return CreatorPersonAnswer.ColumnAbsent; // the column is not in this environment (400)
                    }
                },
            },
            ct);

        if (decision.IsPermitted)
        {
            logger.LogInformation(
                "[UNSECURE] Caller {CallerId} may remove the secure designation of {RecordType} {RecordId} ({Basis}, F3).",
                decision.CallerSystemUserId, root.WireToken, recordId, decision.Basis);
            return (null, decision.CallerSystemUserId);
        }

        logger.LogWarning(
            "[UNSECURE] Removing the secure designation of {RecordType} {RecordId} refused: {Outcome} ({Basis}), caller " +
            "{CallerId}. TraceId={TraceId}",
            root.WireToken, recordId, decision.Outcome, decision.Basis, decision.CallerSystemUserId, traceId);

        var label = root.DisplayLabel.ToLowerInvariant();
        var detail = decision.Basis switch
        {
            SecureRemovalBasis.CallerUnknown =>
                "Your account could not be confirmed, so whether you may remove the secure designation could not be " +
                "checked. Nothing was changed.",
            SecureRemovalBasis.RightsUnreadable =>
                $"Whether you may remove the secure designation from this {label} could not be checked, because your " +
                "access to it could not be read. Nothing was changed.",
            SecureRemovalBasis.CreatorUnreadable or SecureRemovalBasis.CreatorColumnAbsent =>
                $"Whether you may remove the secure designation from this {label} could not be checked, because the " +
                "person who created it could not be looked up. Nothing was changed.",
            _ =>
                $"Only someone with Full Access to this {label}, or the person who created it, can remove its secure " +
                "designation. It is still secure, and nothing was changed.",
        };

        return (decision.ToProblem(detail, traceId), decision.CallerSystemUserId);
    }

    /// <summary>
    /// The owner an un-secured record lands on: the request's nomination, else configuration, else
    /// the calling user — whose identity the F3 check already established by <c>WhoAmI</c> (task 150), so it is not
    /// asked for a second time.
    /// </summary>
    private static Guid? ResolveNewOwner(
        UnsecureProjectRequest request,
        IConfiguration configuration,
        Guid callerSystemUserId)
    {
        if (request.ReassignToSystemUserId is { } requested && requested != Guid.Empty)
            return requested;

        if (Guid.TryParse(configuration[UnsecureOwnerUserIdConfigKey], out var configured)
            && configured != Guid.Empty)
        {
            return configured;
        }

        return callerSystemUserId == Guid.Empty ? null : callerSystemUserId;
    }

    /// <summary>
    /// The outcome of the share sweep: how many rows were revoked, and whether the enumeration that
    /// drove it accounted for every share on the record.
    /// </summary>
    /// <remarks>
    /// A bare count cannot express "I removed none because there were none" separately from "I removed
    /// none because I could not see them" — which is the whole of ISS-018. The two facts travel
    /// together or the count is not evidence of anything.
    /// </remarks>
    private readonly record struct ShareSweep(int Revoked, bool Complete);

    /// <summary>
    /// Removes every POA share on the record, reporting how many were removed AND whether that is
    /// the complete set.
    /// </summary>
    /// <remarks>
    /// <para>Best-effort per share and never fatal. Ownership has already moved by the time this runs,
    /// so the record is reachable regardless; a share that survives is a stale access path to report,
    /// not a reason to abandon a reassignment that already succeeded. Each failure is logged with its
    /// principal so an operator can finish the job.</para>
    ///
    /// <para><b>The enumeration is STRICT, with a soft fallback</b> (ISS-018 / #995, task 108). It was
    /// <see cref="IDataverseRecordShareService.GetPrincipalAccessAsync"/>, which answers an EMPTY LIST
    /// when the read fails — so a failed read was indistinguishable from "no shares", nothing was
    /// revoked, and the endpoint answered success with <c>sharesRevoked = 0</c>. The <c>catch</c> below
    /// could not fire for that case either: the soft read swallows a non-success status and an
    /// unreadable object type code internally, and only a transport-level throw ever reached it. An
    /// unsecure could therefore leave every share in place, silently.</para>
    ///
    /// <para><b>Why partial progress, rather than refusing.</b> The strict read refuses an incomplete
    /// answer — more than one page of shares, or a row whose principal or mask will not parse. For a
    /// "remove every share" sweep that is not a reason to stop: ownership has already moved and been
    /// read back, so refusing outright would leave ALL shares in place, while sweeping what can be
    /// enumerated removes some stale access. So the strict read decides COMPLETENESS and the soft read
    /// supplies whatever rows it can. What is never allowed is reporting the shortfall as success.</para>
    ///
    /// <para><b>The secure flag is still cleared on an incomplete sweep, deliberately.</b> Per ADR-003
    /// <c>sprk_issecure</c> suppresses the derived-member and org-expansion terms; it does NOT suppress
    /// explicit grants or Dataverse's own answer. A surviving POA row IS Dataverse's answer, so leaving
    /// the flag set buys no protection <i>against the surviving share</i>, while manufacturing the
    /// half-applied "no longer isolated yet still reads as secure" state this endpoint already treats as
    /// a defect. The honest report is a cleared flag plus <c>sweepComplete = false</c>.</para>
    ///
    /// <para><b>Clearing the flag is not free, though</b> — it is simply not a mitigation for THIS risk.
    /// <c>sprk_issecure</c> also drives container placement (<c>SecureContainerDecision</c>): a secure
    /// record gets its own container, a non-secure one may fall back to the owning business unit's
    /// SHARED container, and SPE permissions are additive-only, so that is not retractable later. That
    /// consequence is intended for a completed unsecure and is accepted here for an incomplete one,
    /// because the alternative — a record that is owner-reassigned but still flagged secure — is the
    /// half-applied state above.</para>
    ///
    /// <para><b>The new owner's own share goes too, revoked as the owner.</b> Every revoke names
    /// <paramref name="owner"/> (the owning user Step 3 read back): Dataverse refuses an app-only revoke of the
    /// owning user's own share ("Only owner can revoke access to the owner", 0x80040223), so the seam sends THAT one
    /// revoke as the owner and every other share's app-only. Before this, a creator-driven unsecure — the record handed
    /// back to the creator, who still held provisioning's share — always ended <c>sweepComplete = false</c>, and the
    /// stale explicit share (Share included) survived a later reassignment.</para>
    /// </remarks>
    private static async Task<ShareSweep> RevokeAllSharesAsync(
        IDataverseRecordShareService recordShare,
        SecureRecordRoot root,
        Guid recordId,
        DataversePrincipalRef owner,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        var (shares, complete) = await EnumerateSharesAsync(recordShare, root, recordId, logger, traceId, ct);

        var revoked = 0;
        foreach (var share in shares)
        {
            try
            {
                await recordShare.RevokeAccessAsync(root.EntitySet, recordId, share.Principal, owner, ct);
                revoked++;
            }
            catch (Exception ex)
            {
                // Unchanged: best-effort per share, logged with its principal, never fatal. What is new
                // is that a share we failed to remove stops the sweep claiming completeness — the
                // record is demonstrably not fully revoked.
                complete = false;

                logger.LogWarning(ex,
                    "[UNSECURE] Could not revoke the {Kind} share for {PrincipalId} on {RecordType} " +
                    "{RecordId}; the sweep is reported as INCOMPLETE. TraceId={TraceId}",
                    share.Principal.Kind, share.Principal.Id, root.WireToken, recordId, traceId);
            }
        }

        return new ShareSweep(revoked, complete);
    }

    /// <summary>
    /// The record's shares, and whether that list is known to be complete: the strict read's answer,
    /// else whatever the soft read can still parse, else nothing.
    /// </summary>
    /// <remarks>
    /// Split out of <see cref="RevokeAllSharesAsync"/> so the sweep reads as enumerate → revoke → report.
    /// <para><b>No cancellation filter on these catches, deliberately.</b> A draft guarded them with
    /// <c>when (!ct.IsCancellationRequested)</c>; review showed that tests the TOKEN's state rather than
    /// the exception's identity, so a genuine read failure that merely coincided with a client
    /// disconnect would be swallowed with no fallback and no warning. It also removed a diagnostic: a
    /// cancelled read previously fell through to the flag-clear, which threw on the dead token and
    /// produced the "half-applied state" error the operator needs. Both are the opposite of this task's
    /// goal, so the catches stay unfiltered.</para>
    /// </remarks>
    private static async Task<(IReadOnlyList<DataversePrincipalAccess> Shares, bool Complete)> EnumerateSharesAsync(
        IDataverseRecordShareService recordShare,
        SecureRecordRoot root,
        Guid recordId,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        try
        {
            var strict = await recordShare.GetPrincipalAccessOrThrowAsync(
                root.LogicalName, recordId, ct);

            return (strict, true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[UNSECURE] The shares on {RecordType} {RecordId} could not be read COMPLETELY. Sweeping " +
                "only the rows that can be enumerated and reporting the sweep as INCOMPLETE — a " +
                "surviving share is a stale access path an operator must remove. TraceId={TraceId}",
                root.WireToken, recordId, traceId);
        }

        try
        {
            var soft = await recordShare.GetPrincipalAccessAsync(root.LogicalName, recordId, ct);

            return (soft, false);
        }
        catch (Exception fallbackEx)
        {
            logger.LogWarning(fallbackEx,
                "[UNSECURE] No shares on {RecordType} {RecordId} could be enumerated at all; NONE were " +
                "revoked. This is not a clean sweep. TraceId={TraceId}", root.WireToken, recordId, traceId);

            return (Array.Empty<DataversePrincipalAccess>(), false);
        }
    }

    private static IResult Problem(
        int statusCode,
        string title,
        string detail,
        string traceId,
        params (string Key, object? Value)[] extensions)
    {
        var members = new Dictionary<string, object?> { ["traceId"] = traceId };
        foreach (var (key, value) in extensions)
            members[key] = value;

        return Results.Problem(statusCode: statusCode, title: title, detail: detail, extensions: members);
    }

    /// <summary>
    /// The columns this endpoint reads, common to all three roots. The table-specific id column is selected too but
    /// not bound — each read is keyed by its filter.
    /// </summary>
    private sealed class SecurityRow
    {
        [JsonPropertyName("sprk_issecure")]
        public bool? sprk_issecure { get; set; }

        [JsonPropertyName("_owninguser_value")]
        public Guid? _owninguser_value { get; set; }

        /// <summary>Who sent the create — the F3 creator check (task 150).</summary>
        [JsonPropertyName("_createdby_value")]
        public Guid? _createdby_value { get; set; }

        /// <summary>The server-stamped creator person (task 133) — the F3 creator for an app-created record.</summary>
        [JsonPropertyName(RecordCreatorPerson.ValueColumn)]
        public Guid? CreatedByPerson { get; set; }

        /// <summary>Task 175: the owning team — the read-back of a cascade's move to its business unit's team.</summary>
        [JsonPropertyName("_owningteam_value")]
        public Guid? _owningteam_value { get; set; }
    }

    /// <summary>
    /// Task 175: who removes the designation. A CALLER (the route): F3 decides, and the record lands on the request's
    /// nominee, else configuration, else the caller. The round-84 CASCADE (a parent's unsecure, a re-file, the job): no F3
    /// (the parent's own unsecure — or the re-file — is the act; a record with a parent cannot be un-secured on its own),
    /// and the record lands on <see cref="CascadeOwner"/>, its parents' business unit's team (D-11).
    /// </summary>
    private sealed record UnsecureActor(CallerRecordAccessProbe? Probe, HttpContext? Http, DataversePrincipalRef? CascadeOwner)
    {
        public static UnsecureActor Caller(CallerRecordAccessProbe probe, HttpContext http) => new(probe, http, null);

        public static UnsecureActor Cascade(DataversePrincipalRef owner) => new(null, null, owner);

        public bool IsCascade => CascadeOwner is not null;
    }

    /// <summary>The ProblemDetails extension naming the new owner: the user (as before) or, for a cascade, the team.</summary>
    private static (string Key, object? Value) OwnerExtension(DataversePrincipalRef owner) =>
        owner.Kind == DataversePrincipalKind.SystemUser
            ? ("newOwnerSystemUserId", owner.Id)
            : ("newOwnerTeamId", owner.Id);
}
