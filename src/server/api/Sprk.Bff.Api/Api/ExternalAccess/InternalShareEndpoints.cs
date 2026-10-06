using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// Internal system-user shares on a record — the server half of the Manage Access "+ User" picker (spec FR-29,
/// task 063; the picker itself is task 065):
/// <list type="bullet">
/// <item><c>POST /api/v1/external-access/share-user</c> — share a record with a system user at one level;</item>
/// <item><c>POST /api/v1/external-access/unshare-user</c> — remove that user's share;</item>
/// <item><c>GET /api/v1/external-access/user-shares</c> — list the system users holding a share on the record.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para><b>Route placement.</b> These routes join the existing <c>/api/v1/external-access</c> management group
/// rather than a sibling group. That group already carries <see cref="DelegationRuleFilter"/> — Write on the record,
/// evaluated as the CALLER over OBO (owner decision B-14) — and the filter denies any request whose record it cannot
/// identify, so the routes are gated from their first request. A sibling group would repeat that wiring, and a
/// repeat is a second place to forget it. "External" in the path names the surface (Manage Access), not the
/// principal: nothing here reads or writes <c>sprk_externalrecordaccess</c>.</para>
///
/// <para><b>Every write is decided from a complete read and confirmed by reading back.</b> A share is a POA row,
/// written app-only through the one share seam (<see cref="IDataverseRecordShareService"/>, task 060).
/// <list type="bullet">
/// <item>No share yet → GrantAccess. An existing share → ModifyAccess, which Microsoft Learn documents as REPLACING the
/// rights. GrantAccess on an existing share is undocumented and reported as additive, so a downgrade through it could
/// silently keep Write and Delete.</item>
/// <item>Unsharing a user who holds no share → 200 with <c>removed = false</c> and no write. RevokeAccess for a
/// principal with no share is undocumented, so idempotency is decided here rather than assumed of Dataverse.</item>
/// <item>The current shares come from the STRICT read
/// (<see cref="IDataverseRecordShareService.GetPrincipalAccessOrThrowAsync"/>). A failed read is a refusal, never
/// "no share", which would pick GrantAccess for an existing share, or report "nothing to remove" for one that
/// exists.</item>
/// <item>After a write, the stored mask must equal the level's mask exactly. Anything else answers 500 "not
/// confirmed", which claims no outcome it cannot prove.</item>
/// </list></para>
///
/// <para><b>Who can be shared with.</b> A user who exists, is enabled, is a person (access mode Read-Write,
/// Administrative or Read, and no application id — the rule task 100 applies to reminder recipients) and whose
/// <c>sprk_isexternal</c> flag confirms them internal. An external person reaches a record through a contact grant,
/// which carries an expiry and reminders. An unreadable value counts as the refusing one (ADR-003). Unsharing checks
/// only that the user exists: a share must stay removable after its holder is disabled.</para>
///
/// <para><b>Levels.</b> <see cref="RecordShareLevels"/> is the one level-to-rights table: View Only = Read; Collaborate =
/// Read, Write, Append, AppendTo and Share; Full Access = Collaborate + Delete; no level carries Assign. Collaborate and
/// Full Access carry Share since the owner's 2026-09-30 rule (task 139): a person with Write may pass access on — through
/// the model-driven app's own Share command as well as Manage Access — and only a View holder may not.</para>
///
/// <para><b>You may grant only what you hold.</b> A share is the INTERSECTION of the requested level with the caller's
/// own rights, re-probed as the caller (owner 2026-09-16; Dataverse's own rule, which an app-only POA write cannot apply
/// for us). It is narrowed, never refused, unless nothing grantable remains (403 <c>caller_cannot_grant</c>). A narrowed
/// request never LOWERS an existing share that holds more (409 <c>sdap.access.grant.would_lower_existing</c>, task 139);
/// an explicit request for a lower level is a deliberate downgrade and is applied.</para>
///
/// <para><b>The No Access list binds internal users on secure records</b> (task 143, owner Q4). Before any share read
/// or write, <see cref="SecureShareNoAccessGuard"/> answers whether the user is walled off the record; a walled user is
/// refused (403 <c>subject_no_access</c>, a message that names no entry or reason), and an unanswerable check refuses
/// too (500 <c>no_access_unverifiable</c>). On a non-secure record the list does not bind internal users.</para>
///
/// <para><b>A secure record always keeps someone who can see it</b> (owner round 3, S5; task 139 amendment R3):
/// <c>/unshare-user</c> refuses to remove the last enabled user whose share can read a secure record (409
/// <c>last_reader_on_secure_record</c>).</para>
///
/// <para><b>The children of a SECURE record follow its shares</b> (task 149, C10 part 2). Once the root's share is written
/// and confirmed — or found already right, or found already absent — <see cref="SecureChildShareSynchronizer.SyncRootAsync"/>
/// brings every child the Secure Record Owners team owns into line with the root's shares: never wider, never Share or
/// Assign. It runs only AFTER the root write succeeded (a refused or unconfirmed root write fans out nothing) and it is a
/// no-op for a record that is not secure. When some children could not be updated the answer is 500
/// <c>children_incomplete</c> with the counts — never a silent 200 — and the root write STANDS (an unshare is never rolled
/// back to restore access); the scheduled reconcile completes it, and repeating the request is safe.</para>
///
/// <para>ADR-001 Minimal API · ADR-008 authorization by the group's endpoint filter · ADR-019 every refusal is
/// ProblemDetails with a stable reason code and the trace id · ADR-009 the affected user's impersonated root-set cache
/// is cleared after every write attempt.</para>
/// </remarks>
public static class InternalShareEndpoints
{
    // ── Reason codes (ADR-019) ───────────────────────────────────────────────

    /// <summary>The request named no record the handler can resolve (unreachable through the route — the filter denies first).</summary>
    internal const string RecordUnresolvedReasonCode = "sdap.access.user_share.record_unresolved";

    /// <summary>The request named no user.</summary>
    internal const string UserRequiredReasonCode = "sdap.access.user_share.user_required";

    /// <summary>The share request named no level, or a value outside the three levels.</summary>
    internal const string LevelInvalidReasonCode = "sdap.access.user_share.level_invalid";

    /// <summary>No system user has the requested id.</summary>
    internal const string UserNotFoundReasonCode = "sdap.access.user_share.user_not_found";

    /// <summary>The user is disabled, so nothing was shared.</summary>
    internal const string UserDisabledReasonCode = "sdap.access.user_share.user_disabled";

    /// <summary>The account is an application, support or delegated-administration account, so nothing was shared.</summary>
    internal const string UserNotAPersonReasonCode = "sdap.access.user_share.user_not_a_person";

    /// <summary>The user is not confirmed internal, so nothing was shared.</summary>
    internal const string UserNotInternalReasonCode = "sdap.access.user_share.user_not_internal";

    /// <summary>The user or the record's shares could not be read, so nothing was written.</summary>
    internal const string ReadFailedReasonCode = "sdap.access.user_share.read_failed";

    /// <summary>
    /// The caller's own rights on the record include nothing grantable from the requested level — or could not be
    /// established at all. You may grant only what you hold (owner decision 2026-09-16).
    /// </summary>
    internal const string CallerCannotGrantReasonCode = "sdap.access.user_share.caller_cannot_grant";

    /// <summary>A write was sent and its result could not be confirmed.</summary>
    internal const string WriteNotConfirmedReasonCode = "sdap.access.user_share.write_not_confirmed";

    /// <summary>
    /// Removing this share would leave a SECURE record with nobody who can open it (owner round 3, S5; task 139
    /// amendment R3), so nothing was removed. 409.
    /// </summary>
    internal const string LastReaderOnSecureRecordReasonCode = "sdap.access.user_share.last_reader_on_secure_record";

    /// <summary>
    /// The share on the record WAS written (or removed), but not every child of the secure record could be brought into
    /// line with it yet (task 149). 500 with the counts; the scheduled reconcile completes it, and repeating the request is
    /// safe. An unshare is never rolled back to restore access.
    /// </summary>
    internal const string ChildrenIncompleteReasonCode = "sdap.access.user_share.children_incomplete";

    /// <summary>
    /// The user is on the record's No Access list (task 143, owner Q4: the list applies to internal users on secure
    /// records), so nothing was shared. 403. The message never names the entry or its reason.
    /// </summary>
    internal const string SubjectNoAccessReasonCode = "sdap.access.user_share.subject_no_access";

    /// <summary>
    /// Whether the user is on the secure record's No Access list could not be checked (a deny-list, flag, link or
    /// membership read failed), so nothing was shared (ADR-003). 500 — the same caller may try again.
    /// </summary>
    internal const string NoAccessUnverifiableReasonCode = "sdap.access.user_share.no_access_unverifiable";

    // ── Share outcomes ──────────────────────────────────────────────────────

    /// <summary>The user held no share; one now exists at the level.</summary>
    internal const string OutcomeCreated = "created";

    /// <summary>The user held a share at other rights; it now holds exactly the level.</summary>
    internal const string OutcomeUpdated = "updated";

    /// <summary>The user already held exactly the level; nothing was written.</summary>
    internal const string OutcomeUnchanged = "unchanged";

    // ── Dataverse ───────────────────────────────────────────────────────────

    internal const string SystemUserEntitySet = "systemusers";

    /// <summary>
    /// The columns the eligibility check reads. All are standard <c>systemuser</c> columns except
    /// <c>sprk_isexternal</c>, which <c>SystemUserIdentityResolver</c> already reads on the same table.
    /// </summary>
    /// <remarks>
    /// <c>fullname</c> is deliberately absent (Step 9.5 review): the share path never displays a name, and every
    /// column named here is one more whose absence — or column-level security — would 400 the whole read.
    /// </remarks>
    internal const string SystemUserSelect = "systemuserid,isdisabled,accessmode,applicationid,sprk_isexternal";

    /// <summary>
    /// What the UNSHARE path reads: existence, and nothing else.
    /// </summary>
    /// <remarks>
    /// Removing a share must not depend on a value it does not consult — least of all <c>sprk_isexternal</c>, a
    /// custom column whose absence, or column-level security, would 400 the read and block the one operation that
    /// has to keep working (Step 9.5 review finding 4).
    /// </remarks>
    internal const string SystemUserExistsSelect = "systemuserid";

    /// <summary>The columns the share list reads for names.</summary>
    internal const string SystemUserNameSelect = "systemuserid,fullname";

    /// <summary>What the secure-record last-reader check reads: whether each other sharer is enabled (task 139, S5).</summary>
    internal const string SystemUserEnabledSelect = "systemuserid,isdisabled";

    /// <summary>
    /// The last access mode that is a person: 0 Read-Write, 1 Administrative, 2 Read. Not 3 Support User,
    /// 4 Non-interactive or 5 Delegated Admin.
    /// </summary>
    private const int LastPersonAccessMode = 2;

    /// <summary>Users whose names one list query reads, which keeps the OData filter far below Dataverse's URL limit.</summary>
    internal const int NameBatchSize = 50;

    private const string NotSharedTitle = "Record not shared";
    private const string NotUnsharedTitle = "Share not removed";
    private const string NotConfirmedTitle = "Change not confirmed";

    /// <summary>Registers the three routes on the external-access management group.</summary>
    public static RouteGroupBuilder MapInternalShareEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/share-user", ShareAsync)
            .WithName("ShareRecordWithUser")
            .WithSummary("Share a record with an internal user at one access level")
            .WithDescription(
                "Creates or changes a system user's POA share on the record so it carries exactly the level's rights " +
                "(View Only, Collaborate or Full Access), then reads the stored rights back. Requires Write on the record.")
            .Produces<ShareRecordWithUserResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            // 409 (task 139): the request was narrowed to the caller's own rights and the user already holds more.
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost("/unshare-user", UnshareAsync)
            .WithName("UnshareRecordWithUser")
            .WithSummary("Remove an internal user's share on a record")
            .WithDescription(
                "Revokes a system user's POA share on the record and confirms it is gone. A user with no share is " +
                "answered removed = false without a write, so repeating the call is safe. Requires Write on the record.")
            .Produces<UnshareRecordWithUserResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            // 409 (task 139, S5): removing the last person who can open a secure record.
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapGet("/user-shares", ListAsync)
            .WithName("ListRecordUserShares")
            .WithSummary("List the internal users holding a share on a record")
            .WithDescription(
                "Returns every system user with a direct POA share on the record, with the stored rights mask and the " +
                "matching level (null when the rights match no level). Requires Write on the record.")
            .Produces<RecordUserSharesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    // =========================================================================
    // POST /share-user
    // =========================================================================

    /// <summary>Handles <c>POST /api/v1/external-access/share-user</c>.</summary>
    /// <returns>
    /// 200 with the confirmed level and mask and whether the share was created, updated or already right. 400 for a
    /// missing record, user or level. 404 for an unknown user. 422 for a user who is disabled, not a person, or not
    /// confirmed internal. 500 when the user or the shares could not be read (nothing was written), or when a write
    /// could not be confirmed.
    /// </returns>
    internal static async Task<IResult> ShareAsync(
        ShareRecordWithUserRequest request,
        IDataverseRecordShareService recordShare,
        DataverseWebApiClient dataverseClient,
        ITenantCache cache,
        CallerRecordAccessProbe callerAccessProbe,
        SecureChildShareSynchronizer secureChildShares,
        SecureShareNoAccessGuard noAccessGuard,
        SecureRootInheritance relatedRoots,
        Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer assignedAccess,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // ── Validation — nothing reaches Dataverse until the request is well-formed ──
        // The delegation filter resolved this root already and denies an unresolvable one. Checked again anyway: a
        // handler must not trust a pipeline it cannot see.
        var root = GrantExternalAccessEndpoint.ResolveExplicitRoot(request.RecordType, request.RecordId);
        if (!root.Ok)
            return Refused(httpContext, StatusCodes.Status400BadRequest, "Validation Error",
                RecordUnresolvedReasonCode, root.Error!);

        if (request.SystemUserId is not { } systemUserId || systemUserId == Guid.Empty)
            return Refused(httpContext, StatusCodes.Status400BadRequest, "Validation Error",
                UserRequiredReasonCode, "SystemUserId is required and must be a valid GUID.");

        if (!RecordShareLevels.TryGetRights(request.AccessLevel, out var rights))
            return Refused(httpContext, StatusCodes.Status400BadRequest, "Validation Error",
                LevelInvalidReasonCode,
                "AccessLevel is required and must be 100000000 (View Only), 100000001 (Collaborate) or 100000002 (Full Access).");

        var level = request.AccessLevel!.Value;
        var callerOid = CallerResolution.ResolveObjectId(httpContext.User);
        var rootEntitySet = ExternalGrantRoot.BindFor(root.Type).EntitySet;

        // ── Who: an existing, enabled, internal person ────────────────────────
        SystemUserRow? user;
        try
        {
            user = await ReadSystemUserAsync(dataverseClient, systemUserId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[USER-SHARE] Could not read system user {SystemUserId} (caller {CallerOid}). Nothing was shared.",
                systemUserId, callerOid);
            return Refused(httpContext, StatusCodes.Status500InternalServerError, NotSharedTitle,
                ReadFailedReasonCode, "The user could not be looked up, so the record was not shared. Try again.");
        }

        if (user is null)
            return Refused(httpContext, StatusCodes.Status404NotFound, NotSharedTitle,
                UserNotFoundReasonCode, "No user with this id exists, so the record was not shared.");

        if (EligibilityRefusal(user, httpContext) is { } ineligible)
        {
            logger.LogWarning(
                "[USER-SHARE] Refused to share {RootType} {RootId} with {SystemUserId}: disabled={IsDisabled}, " +
                "accessmode={AccessMode}, application={IsApplication}, external={IsExternal} (caller {CallerOid}).",
                root.Type, root.Id, systemUserId, user.IsDisabled, user.AccessMode, user.ApplicationId is not null,
                user.IsExternal, callerOid);
            return ineligible;
        }

        // ── The No Access list for internal users on secure records (task 143, owner Q4) ──
        //
        // Before any share read or write. On a secure record a walled person is refused — through any of the three
        // subject forms (the user, a contact that represents them, an organization that contact belongs to) and any
        // object form (this record, or an organization it references). An unanswerable check refuses too (ADR-003).
        // A non-secure record is not the wall's (Q4): the check answers NotSecure and the share proceeds as before.
        //
        // Task 158 r1c-v2 (main-session round 39 item 2): a work assignment or project filed under a SECURE matter or project
        // is secure itself (owner round 6), so a DIRECT share on it honours the No Access list of every secure record it is
        // filed under as well as its own — the guard's ONE entry point (never a second copy of the parent walk). The same
        // codes; the message says whose list, never which entry. A filing or parent that cannot be read refuses (ADR-003).
        var wall = await noAccessGuard.CheckRecordAndSecureParentsAsync(
            ExternalGrantRoot.LogicalNameFor(root.Type), root.Id, systemUserId, SecureWallRecordScope.AsFlagged, ct);
        var wallList = wall.ParentTable is { } wallParent
            ? $"the No Access list of the secure {SecureRootInheritance.WireTokenFor(wallParent)} this record is filed under"
            : wall.FilingUnreadable
                ? "the No Access list of a secure record this record is filed under"
                : "the No Access list for this record";
        if (wall.Outcome == SecureShareWallOutcome.Walled)
        {
            logger.LogWarning(
                "[USER-SHARE] Refused to share secure {RootType} {RootId} with {SystemUserId}: on the No Access list of " +
                "{Where} (entries {EntryIds}) (caller {CallerOid}).",
                root.Type, root.Id, systemUserId,
                wall.ParentTable is { } p ? $"{p}:{wall.ParentId:D}" : "the record itself", string.Join(",", wall.EntryIds), callerOid);
            return Refused(httpContext, StatusCodes.Status403Forbidden, NotSharedTitle, SubjectNoAccessReasonCode,
                $"This person is on {wallList}, so it was not shared with them.");
        }

        if (wall.Outcome == SecureShareWallOutcome.Unverifiable)
        {
            logger.LogError(
                "[USER-SHARE] Refused to share secure {RootType} {RootId} with {SystemUserId}: the No Access check could " +
                "not read its {Fault} (caller {CallerOid}). Nothing was shared.",
                root.Type, root.Id, systemUserId, wall.Fault, callerOid);
            return Refused(httpContext, StatusCodes.Status500InternalServerError, NotSharedTitle,
                NoAccessUnverifiableReasonCode,
                $"Whether this person is on {wallList} could not be checked, so it was not shared with them. Try again.");
        }

        // ── You may grant only what you hold (owner decision 2026-09-16) ──────
        //
        // The POA write is app-only, so Dataverse sees the APPLICATION's rights and cannot apply its own rule that a
        // sharer may only pass on rights they hold. Intersecting the requested level with the caller's own rights
        // restores it: a caller who cannot delete this record cannot hand Delete to anyone — themselves included,
        // which is the escalation path this closes.
        //
        // The rights are re-probed rather than carried over from the delegation filter, which computed them a moment
        // ago. That mirrors the filter's own reasoning for re-reading the grant row (DelegationRuleFilter's remarks on
        // FromAccessRecordAsync): one more caller-scoped read on a low-volume admin mutation does not materially
        // change request cost, and a handler that trusted authorization state cached by a filter would be trusting
        // something it cannot verify.
        AccessRights callerRights;
        try
        {
            callerRights = await callerAccessProbe.GetCallerRightsAsync(
                Infrastructure.Auth.TokenHelper.ExtractBearerTokenOrNull(httpContext),
                rootEntitySet, root.Id, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[USER-SHARE] Could not establish the caller's own rights on {RootType} {RootId} (caller {CallerOid}). " +
                "Nothing was shared.", root.Type, root.Id, callerOid);
            return Refused(httpContext, StatusCodes.Status500InternalServerError, NotSharedTitle,
                ReadFailedReasonCode,
                "Your own access to this record could not be established, so nothing was shared. Try again.");
        }

        var granted = RecordShareLevels.Intersect(rights, callerRights);
        var narrowed = granted.AccessRightsMask != rights.AccessRightsMask;

        // The probe answers None both for "no rights" and for "could not answer" — deliberately indistinguishable —
        // so an unanswerable probe refuses here too, which is the fail-closed direction.
        if (!RecordShareLevels.IsGrantable(granted))
        {
            logger.LogWarning(
                "[USER-SHARE] Caller {CallerOid} holds {CallerRights} on {RootType} {RootId}, which grants nothing of " +
                "{Level}; nothing was shared.", callerOid, callerRights, root.Type, root.Id, level);
            return Refused(httpContext, StatusCodes.Status403Forbidden, NotSharedTitle, CallerCannotGrantReasonCode,
                "You can only give someone the access you have on this record, and yours does not cover the level you " +
                "asked for. Nothing was shared.");
        }

        if (narrowed)
        {
            logger.LogInformation(
                "[USER-SHARE] Caller {CallerOid} asked for {Level} on {RootType} {RootId} but holds {CallerRights}, so " +
                "the share is narrowed to mask {Granted}.",
                callerOid, level, root.Type, root.Id, callerRights, granted.AccessRightsMask);
        }

        // ── The user's current share, from the STRICT read ─────────────────────
        int current;
        try
        {
            current = await ReadDirectShareMaskAsync(recordShare, root, systemUserId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[USER-SHARE] Could not read the shares on {RootType} {RootId} (caller {CallerOid}). Nothing was shared.",
                root.Type, root.Id, callerOid);
            return Refused(httpContext, StatusCodes.Status500InternalServerError, NotSharedTitle,
                ReadFailedReasonCode, "The shares on this record could not be read, so nothing was changed. Try again.");
        }

        // ── Never silently lower (task 139) ─────────────────────────────────────
        // ModifyAccess REPLACES the rights. When the caller's own rights narrowed the request, writing the narrowed
        // mask over a share that holds MORE would take rights away while the caller asked for more — lowering access
        // somebody else gave. Refused instead, with nothing written. An explicit request for a lower level (not
        // narrowed) is a deliberate downgrade by a Write-holder and is still applied below.
        if (narrowed && RecordShareLevels.WouldRemoveRights(current, granted.AccessRightsMask))
        {
            logger.LogWarning(
                "[USER-SHARE] Refused: {SystemUserId} holds mask {Current} on {RootType} {RootId}; the request for {Level} " +
                "was narrowed to {Granted} by the caller's own rights {CallerRights}, and writing it would remove rights " +
                "(caller {CallerOid}).",
                systemUserId, current, root.Type, root.Id, level, granted.AccessRightsMask, callerRights, callerOid);
            return Refused(httpContext, StatusCodes.Status409Conflict, NotSharedTitle,
                ExternalGrantLifecycle.WouldLowerExistingReasonCode,
                "They already have more access than you can grant on this record. Nothing was changed, so their " +
                "existing access stays as it is.");
        }

        if (current == granted.AccessRightsMask)
        {
            logger.LogInformation(
                "[USER-SHARE] {SystemUserId} already holds mask {Mask} on {RootType} {RootId}; nothing was written " +
                "(caller {CallerOid}).", systemUserId, current, root.Type, root.Id, callerOid);

            // Task 142: a deliberate share onto a user the Assigned-To rule shared to (or suggested) is now the operator's.
            await assignedAccess.MarkShareAdoptedAsync(root.Type, root.Id, systemUserId, CancellationToken.None);

            // Still fanned out: a repeat of a share whose fan-out was incomplete is how a caller completes it.
            return await ChildrenIncompleteAfterShareAsync(
                       secureChildShares, relatedRoots, root, systemUserId, OutcomeUnchanged, current, httpContext, logger,
                       callerOid, ct)
                   ?? TypedResults.Ok(new ShareRecordWithUserResponse(
                       systemUserId, RecordShareLevels.LevelForMask(current), current, OutcomeUnchanged, narrowed));
        }

        // ── Write, then confirm what Dataverse stored ─────────────────────────
        var principal = DataversePrincipalRef.User(systemUserId);
        int? stored = null;
        Exception? failure = null;
        try
        {
            if (current == 0)
                await recordShare.GrantAccessAsync(rootEntitySet, root.Id, principal, granted.AccessRightsCsv, ct);
            else
                await recordShare.ModifyAccessAsync(rootEntitySet, root.Id, principal, granted.AccessRightsCsv, ct);

            stored = await ReadDirectShareMaskAsync(recordShare, root, systemUserId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            failure = ex;
        }
        finally
        {
            // Cleared whether or not the write is confirmed: a write that threw, or was cancelled, may still have
            // applied, and a stale cached set is exactly what this clean-up exists to prevent.
            await ImpersonatedRootSetSource.InvalidateAsync(
                cache, httpContext.User, systemUserId, ExternalGrantRoot.LogicalNameFor(root.Type), logger);
        }

        if (failure is not null || stored != granted.AccessRightsMask)
        {
            logger.LogError(failure,
                "[USER-SHARE] Sharing {RootType} {RootId} with {SystemUserId} at {Level} was not confirmed: the user held " +
                "mask {Previous}, the write asked for {Requested}, and the read-back found {Stored} (caller {CallerOid}).",
                root.Type, root.Id, systemUserId, level, current, granted.AccessRightsMask,
                stored?.ToString() ?? "nothing readable", callerOid);
            return NotConfirmed(httpContext,
                "The share could not be confirmed. Reload the list to see what this user holds, then try again.",
                stored);
        }

        var outcome = current == 0 ? OutcomeCreated : OutcomeUpdated;

        // Task 142: a manual share (incl. "Grant" on a secure-record suggestion) on a user the Assigned-To ledger holds
        // becomes ADOPTED — never revoked by the rule afterwards. Ledger-only; never thrown.
        await assignedAccess.MarkShareAdoptedAsync(root.Type, root.Id, systemUserId, CancellationToken.None);

        logger.LogInformation(
            "[USER-SHARE] Caller {CallerOid} shared {RootType} {RootId} with {SystemUserId}: asked {Level}, granted mask " +
            "{Granted} ({Outcome}; was {Previous}; narrowed={Narrowed}).",
            callerOid, root.Type, root.Id, systemUserId, level, granted.AccessRightsMask, outcome, current, narrowed);

        // ── Task 149: the root write is confirmed; now its secure children (a no-op for an ordinary record) ──
        return await ChildrenIncompleteAfterShareAsync(
                   secureChildShares, relatedRoots, root, systemUserId, outcome, granted.AccessRightsMask, httpContext, logger,
                   callerOid, ct)
               ?? TypedResults.Ok(new ShareRecordWithUserResponse(
                   systemUserId, RecordShareLevels.LevelForMask(granted.AccessRightsMask), granted.AccessRightsMask,
                   outcome, narrowed));
    }

    // =========================================================================
    // POST /unshare-user
    // =========================================================================

    /// <summary>Handles <c>POST /api/v1/external-access/unshare-user</c>.</summary>
    /// <returns>
    /// 200 with <c>removed = true</c> when a share existed and is confirmed gone, or <c>removed = false</c> when the user
    /// held none (nothing was written). 400 for a missing record or user. 404 for an unknown user. 409 when the record is
    /// secure and this user is the last one who can open it (task 139, S5). 500 when the user or the shares could not be
    /// read (nothing was written), or when the removal could not be confirmed.
    /// </returns>
    internal static async Task<IResult> UnshareAsync(
        UnshareRecordWithUserRequest request,
        IDataverseRecordShareService recordShare,
        DataverseWebApiClient dataverseClient,
        ExternalParticipationService participations,
        ITenantCache cache,
        Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer assignedAccess,
        SecureChildShareSynchronizer secureChildShares,
        SecureRootInheritance relatedRoots,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var root = GrantExternalAccessEndpoint.ResolveExplicitRoot(request.RecordType, request.RecordId);
        if (!root.Ok)
            return Refused(httpContext, StatusCodes.Status400BadRequest, "Validation Error",
                RecordUnresolvedReasonCode, root.Error!);

        if (request.SystemUserId is not { } systemUserId || systemUserId == Guid.Empty)
            return Refused(httpContext, StatusCodes.Status400BadRequest, "Validation Error",
                UserRequiredReasonCode, "SystemUserId is required and must be a valid GUID.");

        var callerOid = CallerResolution.ResolveObjectId(httpContext.User);

        // The user must exist — and nothing more. A share held by a user who has since been disabled, or
        // reclassified as external, is exactly the kind that must stay removable, so the eligibility rules are
        // deliberately NOT applied here. The read selects ONE column for the same reason: removal must not be
        // blocked by a value it does not consult (Step 9.5 review finding 4).
        bool userExists;
        try
        {
            userExists = await SystemUserExistsAsync(dataverseClient, systemUserId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[USER-SHARE] Could not read system user {SystemUserId} (caller {CallerOid}). Nothing was removed.",
                systemUserId, callerOid);
            return Refused(httpContext, StatusCodes.Status500InternalServerError, NotUnsharedTitle,
                ReadFailedReasonCode, "The user could not be looked up, so no share was removed. Try again.");
        }

        if (!userExists)
            return Refused(httpContext, StatusCodes.Status404NotFound, NotUnsharedTitle,
                UserNotFoundReasonCode, "No user with this id exists.");

        int current;
        try
        {
            current = await ReadDirectShareMaskAsync(recordShare, root, systemUserId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[USER-SHARE] Could not read the shares on {RootType} {RootId} (caller {CallerOid}). Nothing was removed.",
                root.Type, root.Id, callerOid);
            return Refused(httpContext, StatusCodes.Status500InternalServerError, NotUnsharedTitle,
                ReadFailedReasonCode, "The shares on this record could not be read, so nothing was removed. Try again.");
        }

        if (current == 0)
        {
            logger.LogInformation(
                "[USER-SHARE] {SystemUserId} holds no share on {RootType} {RootId}; nothing to remove (caller {CallerOid}).",
                systemUserId, root.Type, root.Id, callerOid);

            // Still fanned out: the user may still hold a share on a child (an incomplete earlier unshare, or one made in
            // the model-driven app), and repeating the unshare is how a caller removes it.
            return await ChildrenIncompleteAfterUnshareAsync(
                       secureChildShares, relatedRoots, root, systemUserId, removed: false, httpContext, logger, callerOid, ct)
                   ?? TypedResults.Ok(new UnshareRecordWithUserResponse(systemUserId, Removed: false));
        }

        // ── S5 (owner round 3, task 139 amendment R3): a secure record always keeps someone who can see it ──
        if (RecordShareLevels.CanRead(current)
            && await LastReaderRefusalAsync(
                root, systemUserId, recordShare, dataverseClient, participations, httpContext, logger, callerOid, ct)
                is { } lastReader)
        {
            return lastReader;
        }

        // Task 142: an operator removal of an Assigned-To auto share STICKS (owner round 2 item 5) — the ledger rows naming
        // this user on this record become Declined, so no trigger shares again while the assignment persists; the same marker
        // records the removal of a share a secure parent passed on (task 158, owner round 30). Ledger-only and never thrown.
        // Task 158 r1c-v2 (main-session round 47 item 2, E-158-v1-2): written BEFORE the revoke — write-ahead, as the
        // inherited-share provenance is — so a pass that reads the ledger after this point never mistakes the removal it is
        // about to see for a share to give again. The rest of that race — a pass that read the row BEFORE this marker writing
        // over it — is closed at integration by If-Match on every pass's ledger update (round 47 item 2, task 140's support).
        // Task 158 final round (main-session round 58 item 2): a revoke that then fails, or is not confirmed, PUTS THE MARKER
        // BACK in this same request (finally — a cancellation too). A share the operator did not actually remove is never on
        // record as declined: a Declined row is ended by its parent's unshare WITHOUT removing the share, so the share would
        // outlive its source. Put back while the share is in fact gone (the read-back alone failed), the next pass sees the
        // removal itself and records it (the out-of-band rule) — the safe direction.
        var marked = await assignedAccess.MarkShareRemovedAsync(root.Type, root.Id, systemUserId, CancellationToken.None);

        var entitySet = ExternalGrantRoot.BindFor(root.Type).EntitySet;
        int? remaining = null;
        Exception? failure = null;
        var confirmed = false;
        try
        {
            await recordShare.RevokeAccessAsync(entitySet, root.Id, DataversePrincipalRef.User(systemUserId), ct);
            remaining = await ReadDirectShareMaskAsync(recordShare, root, systemUserId, ct);
            confirmed = remaining == 0;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            failure = ex;
        }
        finally
        {
            await ImpersonatedRootSetSource.InvalidateAsync(
                cache, httpContext.User, systemUserId, ExternalGrantRoot.LogicalNameFor(root.Type), logger);
            if (!confirmed)
                await assignedAccess.RevertShareRemovedAsync(root.Type, root.Id, marked, CancellationToken.None);
        }

        if (failure is not null || remaining != 0)
        {
            logger.LogError(failure,
                "[USER-SHARE] Removing {SystemUserId}'s share on {RootType} {RootId} was not confirmed: it held mask " +
                "{Previous} and the read-back found {Remaining} (caller {CallerOid}).",
                systemUserId, root.Type, root.Id, current, remaining?.ToString() ?? "nothing readable", callerOid);
            return NotConfirmed(httpContext,
                "Removing this user's share could not be confirmed. Reload the list to see whether they still have access, then try again.",
                remaining);
        }

        logger.LogInformation(
            "[USER-SHARE] Caller {CallerOid} removed {SystemUserId}'s share on {RootType} {RootId} (it held mask {Previous}).",
            callerOid, systemUserId, root.Type, root.Id, current);

        // ── Task 149: the root share is confirmed gone; now remove the user from its secure children ──
        return await ChildrenIncompleteAfterUnshareAsync(
                   secureChildShares, relatedRoots, root, systemUserId, removed: true, httpContext, logger, callerOid, ct)
               ?? TypedResults.Ok(new UnshareRecordWithUserResponse(systemUserId, Removed: true));
    }

    // =========================================================================
    // Task 149 — the secure children follow the root
    // =========================================================================

    /// <summary>
    /// After a confirmed share on the root: brings the root's secure children into line, and answers the 500
    /// <see cref="ChildrenIncompleteReasonCode"/> refusal when some could not be — or <c>null</c> when every child is in line
    /// (or the record is not secure).
    /// </summary>
    private static async Task<IResult?> ChildrenIncompleteAfterShareAsync(
        SecureChildShareSynchronizer secureChildShares,
        SecureRootInheritance relatedRoots,
        GrantExternalAccessEndpoint.GrantRootResolution root,
        Guid systemUserId,
        string rootOutcome,
        int rootMask,
        HttpContext httpContext,
        ILogger logger,
        string? callerOid,
        CancellationToken ct)
    {
        var children = await secureChildShares.SyncRootAsync(ExternalGrantRoot.LogicalNameFor(root.Type), root.Id, ct);

        // Task 158 (owner round 6: "the parent's sharees can see it"): the SECURE work assignments and projects filed under
        // this matter or project are given the new sharee now (add-only), each with its provenance recorded (task 158 r1,
        // owner round 30). A record filed under it that is not secure yet is the job's to secure, not this share's, so it
        // is not counted; every other record left out is reported through children_incomplete, as 149's children are
        // (round 30: "failures report through children_incomplete"). The record's own share stands either way.
        var filed = await relatedRoots.PassSharesOnAsync(
            ExternalGrantRoot.LogicalNameFor(root.Type), root.Id, httpContext.TraceIdentifier, ct);
        var filedLeft = FiledRecordsLeft(filed);

        if (children.IsComplete && filedLeft == 0)
            return null;

        if (children.IsComplete)
        {
            logger.LogWarning(
                "[USER-SHARE] {SystemUserId}'s share on {RootType} {RootId} is in place ({Outcome}), but {Left} secure record(s) " +
                "filed under it were not given it yet: {Detail} (caller {CallerOid}). The secure-root inheritance job completes it.",
                systemUserId, root.Type, root.Id, rootOutcome, filedLeft, filed.Detail, callerOid);
            return ChildrenIncomplete(httpContext, ChildrenIncompleteTitle,
                $"The record was shared, but {filedLeft} of the secure work assignments and projects filed under it could not " +
                "be given this person yet. They are updated automatically within a few minutes, or you can try again.",
                systemUserId, children, ("outcome", rootOutcome), ("accessRightsMask", rootMask),
                ("filedRecordsNotUpdated", filedLeft));
        }

        logger.LogWarning(
            "[USER-SHARE] {SystemUserId}'s share on {RootType} {RootId} is in place ({Outcome}), but its secure children are " +
            "not all in line: status={Status} inScope={InScope} notUpdated={NotUpdated} held={Held} (caller {CallerOid}). " +
            "The scheduled reconcile completes it.",
            systemUserId, root.Type, root.Id, rootOutcome, children.Status, children.ChildrenInScope,
            children.ChildrenNotUpdated, children.ChildrenHeld, callerOid);

        var detail = children.Status == SecureChildShareSyncStatus.Failed
            ? "The record was shared, but its related records (documents, events, to-dos and communications) could not be " +
              "read, so they do not show it yet. They are updated automatically within a few minutes, or you can try again."
            : $"The record was shared, but {children.ChildrenLeftOutOfLine} of its {children.ChildrenInScope} related records " +
              "(documents, events, to-dos and communications) could not be updated yet. They are updated automatically " +
              "within a few minutes, or you can try again." + HeldSentence(children);

        return ChildrenIncomplete(httpContext, ChildrenIncompleteTitle, detail, systemUserId, children,
            ("outcome", rootOutcome), ("accessRightsMask", rootMask), ("filedRecordsNotUpdated", filedLeft));
    }

    /// <summary>
    /// Task 158 r1: the secure filed records a sharee-only pass left out — every incomplete record except one that is not
    /// secure yet (the job secures it; a share is not that act), or 1 when the pass itself failed.
    /// </summary>
    private static int FiledRecordsLeft(SecureFiledRootsPass pass) =>
        pass.Status == SecureFiledRootsStatus.Failed
            ? Math.Max(1, pass.Results.Count)
            : pass.Results.Count(r => !r.IsComplete && r.ReasonCode != SecureRootInheritance.ReasonNotYetSecure)
              + (pass.Status == SecureFiledRootsStatus.Incomplete && pass.Results.Count == 0 ? 1 : 0);

    /// <summary>
    /// After a confirmed unshare on the root (or none to remove): removes the user from the root's secure children. The
    /// root's unshare STANDS whatever happens here — access is never restored to undo a partial removal.
    /// </summary>
    private static async Task<IResult?> ChildrenIncompleteAfterUnshareAsync(
        SecureChildShareSynchronizer secureChildShares,
        SecureRootInheritance relatedRoots,
        GrantExternalAccessEndpoint.GrantRootResolution root,
        Guid systemUserId,
        bool removed,
        HttpContext httpContext,
        ILogger logger,
        string? callerOid,
        CancellationToken ct)
    {
        var children = await secureChildShares.SyncRootAsync(ExternalGrantRoot.LogicalNameFor(root.Type), root.Id, ct);

        // Task 158 r1 (owner round 30): the reverse fan-out — the secure work assignments and projects this record's sharing
        // gave the user lose it too, but ONLY where the provenance ledger says it came from here and the share is still the
        // unmodified one passed on (a direct share, a raised one, or one another parent still justifies is kept).
        var filed = await relatedRoots.PassUnshareOnAsync(
            ExternalGrantRoot.LogicalNameFor(root.Type), root.Id, DataversePrincipalRef.User(systemUserId),
            httpContext.TraceIdentifier, ct);

        // A HELD child does not keep the removed user: held children are only narrowed, and a principal its known roots do
        // not share is revoked from it. So for an unshare only children that could not be updated at all are incomplete.
        var childrenDone = children.IsComplete
                           || (children.Status != SecureChildShareSyncStatus.Failed && children.ChildrenNotUpdated == 0);
        if (childrenDone && filed.IsComplete)
            return null;

        // Task 158 r1c-v1 (verifier item 3): records whose removed share their OTHER secure parents still pass on in part,
        // where that part could not be given back yet — the opposite direction from a share not removed, so its own sentence.
        var filedNotRegiven = filed.NotRegiven;
        var filedNotEnded = filed.IsComplete ? 0 : filedNotRegiven > 0 && filed.NotDone == 0 ? 0 : Math.Max(filed.NotDone, 1);
        var regivenSentence = filedNotRegiven == 0
            ? string.Empty
            : $" On {filedNotRegiven} secure work assignment(s) or project(s) filed under it, the access the other secure records " +
              "they are filed under still give this person could not be given back yet; it is given back automatically within a " +
              "few minutes.";

        if (childrenDone)
        {
            logger.LogWarning(
                "[USER-SHARE] {SystemUserId}'s share on {RootType} {RootId} is gone (removed={Removed}), but the inherited shares it " +
                "had passed on were not all ended or given back: {Detail} (caller {CallerOid}). The secure-root inheritance job " +
                "completes it.", systemUserId, root.Type, root.Id, removed, filed.Detail, callerOid);
            return ChildrenIncomplete(httpContext, ChildrenIncompleteTitle,
                (filedNotEnded == 0
                    ? "This user's access to the record was removed."
                    : "This user's access to the record was removed, but the access this record gave them on " +
                      $"{filedNotEnded} secure work assignment(s) or project(s) filed under it could not be removed yet, so " +
                      "they may still open those. They are removed automatically within a few minutes, or you can try again.")
                + regivenSentence,
                systemUserId, children, ("removed", removed), ("childrenNotUpdated", children.ChildrenNotUpdated),
                ("filedRecordsNotUpdated", filedNotEnded + filedNotRegiven));
        }

        logger.LogWarning(
            "[USER-SHARE] {SystemUserId}'s share on {RootType} {RootId} is gone (removed={Removed}), but its secure children " +
            "are not all in line: status={Status} inScope={InScope} notUpdated={NotUpdated} held={Held} (caller {CallerOid}). " +
            "The scheduled reconcile completes it.",
            systemUserId, root.Type, root.Id, removed, children.Status, children.ChildrenInScope,
            children.ChildrenNotUpdated, children.ChildrenHeld, callerOid);

        var detail = children.Status == SecureChildShareSyncStatus.Failed
            ? "This user's access to the record was removed, but its related records (documents, events, to-dos and " +
              "communications) could not be read, so they may still open them. They are removed automatically within a few " +
              "minutes, or you can try again."
            : $"This user's access to the record was removed, but {children.ChildrenNotUpdated} of its " +
              $"{children.ChildrenInScope} related records (documents, events, to-dos and communications) could not be " +
              "updated yet, so they may still open those. They are removed automatically within a few minutes, or you can " +
              "try again.";

        return ChildrenIncomplete(httpContext, ChildrenIncompleteTitle, detail + regivenSentence, systemUserId, children,
            ("removed", removed), ("childrenNotUpdated", children.ChildrenNotUpdated),
            ("filedRecordsNotUpdated", filedNotEnded + filedNotRegiven));
    }

    private const string ChildrenIncompleteTitle = "Related records not all updated";

    /// <summary>
    /// The extra sentence when some children are HELD: their secure records cannot be determined from the data, so the
    /// schedule will not add anyone to them until an administrator repairs how they are filed — "a few minutes" would
    /// not be true for those.
    /// </summary>
    private static string HeldSentence(SecureChildShareSyncResult children) =>
        children.ChildrenHeld == 0
            ? string.Empty
            : $" {children.ChildrenHeld} of them cannot be matched to their secure record until an administrator repairs " +
              "how they are filed; nobody is added to those meanwhile.";

    /// <summary>The 500 for a root write that stands while some of its secure children are not yet in line.</summary>
    private static IResult ChildrenIncomplete(
        HttpContext httpContext,
        string title,
        string detail,
        Guid systemUserId,
        SecureChildShareSyncResult children,
        params (string Key, object? Value)[] rootFacts)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["traceId"] = httpContext.TraceIdentifier,
            ["reasonCode"] = ChildrenIncompleteReasonCode,
            ["systemUserId"] = systemUserId,
            ["childrenStatus"] = children.Status.ToString(),
            ["childrenInScope"] = children.ChildrenInScope,
            ["childrenUpdated"] = children.ChildrenUpdated + children.ChildrenUnchanged,
            ["childrenNotUpdated"] = children.ChildrenLeftOutOfLine,
            ["childrenHeld"] = children.ChildrenHeld,
        };
        foreach (var (key, value) in rootFacts)
            extensions[key] = value;

        return Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: title,
            detail: detail,
            extensions: extensions);
    }

    // =========================================================================
    // GET /user-shares
    // =========================================================================

    /// <summary>Handles <c>GET /api/v1/external-access/user-shares</c>.</summary>
    /// <returns>200 with the record's direct system-user shares. 400 for a missing record. 500 when the shares could not be read.</returns>
    internal static async Task<IResult> ListAsync(
        [AsParameters] RecordUserSharesQuery query,
        IDataverseRecordShareService recordShare,
        DataverseWebApiClient dataverseClient,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var root = GrantExternalAccessEndpoint.ResolveExplicitRoot(query.RecordType, query.RecordId);
        if (!root.Ok)
            return Refused(httpContext, StatusCodes.Status400BadRequest, "Validation Error",
                RecordUnresolvedReasonCode, root.Error!);

        IReadOnlyList<DataversePrincipalAccess> shares;
        try
        {
            shares = await recordShare.GetPrincipalAccessOrThrowAsync(
                ExternalGrantRoot.LogicalNameFor(root.Type), root.Id, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "[USER-SHARE] Could not read the shares on {RootType} {RootId}.", root.Type, root.Id);
            return Refused(httpContext, StatusCodes.Status500InternalServerError, "Shares not read",
                ReadFailedReasonCode, "The shares on this record could not be read. Try again.");
        }

        // Direct system-user shares only. A team's share is not a person's, and a zero mask carries no direct rights —
        // Dataverse keeps such a row for access inherited from a related record, which this surface neither made nor
        // can remove. Where access comes from is task 064's read.
        var userShares = shares
            .Where(s => s.Principal.Kind == DataversePrincipalKind.SystemUser && s.AccessRightsMask != 0)
            .GroupBy(s => s.Principal.Id)
            .Select(g => (
                SystemUserId: g.Key,
                Mask: g.Aggregate(0, (mask, s) => mask | s.AccessRightsMask),
                ModifiedOn: g.Max(s => s.ModifiedOn)))
            .ToList();

        var names = await ReadNamesAsync(dataverseClient, userShares.Select(s => s.SystemUserId).ToList(), logger, ct);

        var listed = userShares
            .Select(s => new RecordUserShare(
                s.SystemUserId,
                names.GetValueOrDefault(s.SystemUserId),
                s.Mask,
                RecordShareLevels.LevelForMask(s.Mask),
                s.ModifiedOn))
            .OrderBy(s => s.FullName is null)
            // Ordinal, not CurrentCulture (Step 9.5 review finding 11): a server-side order must not depend on the
            // host's culture configuration, or one record lists its users in different orders across hosts — or
            // after a config change — with no test able to see it.
            .ThenBy(s => s.FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.SystemUserId)
            .ToList();

        return TypedResults.Ok(new RecordUserSharesResponse(listed));
    }

    /// <summary>
    /// The refusal when removing <paramref name="systemUserId"/>'s share would leave a SECURE record with nobody who can
    /// see it — or <c>null</c> when the removal may proceed (owner round 3, S5: "a secure record MUST always have at
    /// least one user who can see it"; task 139 amendment R3).
    /// </summary>
    /// <remarks>
    /// <para><b>Secure, or could not tell.</b> A secure record is owned by a memberless team, so its people are exactly
    /// its direct user shares. An unreadable flag read counts as secure — the fail-closed direction is to keep the
    /// share. A non-secure record is visible through its owner's business unit, so the rule does not apply.</para>
    /// <para><b>Who counts as "someone else who can see it".</b> Another SYSTEM USER with a direct share carrying Read,
    /// who is enabled. Team shares are not counted: a team's membership is not read here, and counting one could let
    /// the last person go from a record only an empty team can reach. Over-refusing is the safe direction; an
    /// administrator can always share the record with someone else first.</para>
    /// <para>Any read that fails refuses (500 <c>read_failed</c>) rather than guessing that someone else remains.</para>
    /// </remarks>
    private static async Task<IResult?> LastReaderRefusalAsync(
        GrantExternalAccessEndpoint.GrantRootResolution root,
        Guid systemUserId,
        IDataverseRecordShareService recordShare,
        DataverseWebApiClient dataverseClient,
        ExternalParticipationService participations,
        HttpContext httpContext,
        ILogger logger,
        string? callerOid,
        CancellationToken ct)
    {
        var logicalName = ExternalGrantRoot.LogicalNameFor(root.Type);

        bool isSecure;
        try
        {
            var flags = await participations.GetRootRecordFlagsAsync(logicalName, new[] { root.Id }, ct);
            isSecure = !flags.TryGetValue(root.Id, out var f) || f.IsUnreadable || f.IsSecure;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex,
                "[USER-SHARE] Could not read whether {RootType} {RootId} is secure; treating it as secure (fail closed).",
                root.Type, root.Id);
            isSecure = true;
        }

        if (!isSecure)
            return null;

        List<Guid> otherReaders;
        try
        {
            var shares = await recordShare.GetPrincipalAccessOrThrowAsync(logicalName, root.Id, ct);
            otherReaders = shares
                .Where(s => s.Principal.Kind == DataversePrincipalKind.SystemUser
                            && s.Principal.Id != systemUserId
                            && RecordShareLevels.CanRead(s.AccessRightsMask))
                .Select(s => s.Principal.Id)
                .Distinct()
                .ToList();

            foreach (var batch in otherReaders.Chunk(NameBatchSize))
            {
                var rows = await dataverseClient.QueryAsync<SystemUserRow>(
                    SystemUserEntitySet,
                    filter: string.Join(" or ", batch.Select(id => $"systemuserid eq {id}")),
                    select: SystemUserEnabledSelect,
                    top: batch.Length,
                    cancellationToken: ct);

                if (rows.Any(r => batch.Contains(r.Id) && r.IsDisabled is false))
                    return null;
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[USER-SHARE] Could not confirm that someone else keeps access to secure {RootType} {RootId}; nothing " +
                "was removed (caller {CallerOid}).", root.Type, root.Id, callerOid);
            return Refused(httpContext, StatusCodes.Status500InternalServerError, NotUnsharedTitle,
                ReadFailedReasonCode,
                "Could not confirm that someone else keeps access to this secure record, so no share was removed. Try again.");
        }

        logger.LogWarning(
            "[USER-SHARE] Refused to remove {SystemUserId}'s share on secure {RootType} {RootId}: nobody else enabled holds " +
            "a share that can read it ({OtherReaders} other reader share(s) found) (caller {CallerOid}).",
            systemUserId, root.Type, root.Id, otherReaders.Count, callerOid);
        return Refused(httpContext, StatusCodes.Status409Conflict, NotUnsharedTitle, LastReaderOnSecureRecordReasonCode,
            "This is the last person who can open this secure record, so their access cannot be removed. Share the " +
            "record with someone else first, then remove this share.");
    }

    // =========================================================================
    // Reads
    // =========================================================================

    /// <summary>
    /// The rights mask of <paramref name="systemUserId"/>'s DIRECT share on the record — 0 when there is none — from
    /// the strict read, so a failure throws instead of reading as "no share".
    /// </summary>
    /// <remarks>
    /// A share row with a zero mask carries no direct rights (Dataverse keeps one for access inherited from a related
    /// record): not a share this surface made or can remove, so it reads as "no share", and a new share there is a
    /// GrantAccess. Rows are OR-ed in case more than one comes back for the principal, so none is missed.
    /// </remarks>
    private static async Task<int> ReadDirectShareMaskAsync(
        IDataverseRecordShareService recordShare,
        GrantExternalAccessEndpoint.GrantRootResolution root,
        Guid systemUserId,
        CancellationToken ct)
    {
        var shares = await recordShare.GetPrincipalAccessOrThrowAsync(
            ExternalGrantRoot.LogicalNameFor(root.Type), root.Id, ct);

        var principal = DataversePrincipalRef.User(systemUserId);
        return shares
            .Where(s => s.Principal == principal)
            .Aggregate(0, (mask, s) => mask | s.AccessRightsMask);
    }

    /// <summary>The user's row, or <c>null</c> when no system user has the id. Exceptions propagate.</summary>
    private static async Task<SystemUserRow?> ReadSystemUserAsync(
        DataverseWebApiClient dataverseClient, Guid systemUserId, CancellationToken ct)
    {
        var rows = await dataverseClient.QueryAsync<SystemUserRow>(
            SystemUserEntitySet,
            filter: $"systemuserid eq {systemUserId}",
            select: SystemUserSelect,
            top: 1,
            cancellationToken: ct);

        // Compared as well as filtered on: a row for any other user is not an answer about this one.
        return rows.FirstOrDefault(r => r.Id == systemUserId);
    }

    /// <summary>
    /// Whether a system user with this id exists — the ONLY thing the unshare path needs to know about them.
    /// Exceptions propagate.
    /// </summary>
    /// <remarks>
    /// A separate read from <see cref="ReadSystemUserAsync"/>, selecting one column, so that removing a share cannot
    /// be blocked by a value it does not consult (Step 9.5 review finding 4). <c>sprk_isexternal</c> is a CUSTOM
    /// column: its absence in an environment, or column-level security on it, would 400 the eligibility read and turn
    /// "remove this person's access" into a 500 for a share that plainly exists.
    /// </remarks>
    private static async Task<bool> SystemUserExistsAsync(
        DataverseWebApiClient dataverseClient, Guid systemUserId, CancellationToken ct)
    {
        var rows = await dataverseClient.QueryAsync<SystemUserRow>(
            SystemUserEntitySet,
            filter: $"systemuserid eq {systemUserId}",
            select: SystemUserExistsSelect,
            top: 1,
            cancellationToken: ct);

        return rows.Any(r => r.Id == systemUserId);
    }

    /// <summary>
    /// Names for the listed users, read in batches. Display-only, so a failed batch leaves its names <c>null</c> rather
    /// than failing the list: who holds which rights has already been read.
    /// </summary>
    private static async Task<IReadOnlyDictionary<Guid, string?>> ReadNamesAsync(
        DataverseWebApiClient dataverseClient, IReadOnlyList<Guid> systemUserIds, ILogger logger, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string?>();

        foreach (var batch in systemUserIds.Chunk(NameBatchSize))
        {
            try
            {
                var rows = await dataverseClient.QueryAsync<SystemUserRow>(
                    SystemUserEntitySet,
                    filter: string.Join(" or ", batch.Select(id => $"systemuserid eq {id}")),
                    select: SystemUserNameSelect,
                    top: batch.Length,
                    cancellationToken: ct);

                foreach (var row in rows.Where(r => batch.Contains(r.Id)))
                    names[row.Id] = row.FullName;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex,
                    "[USER-SHARE] Could not read the names of {Count} users; they are listed without names.", batch.Length);
            }
        }

        return names;
    }

    // =========================================================================
    // Eligibility and refusals
    // =========================================================================

    /// <summary>
    /// The refusal for a user who cannot receive a share, or <c>null</c> for one who can. An unreadable value counts as
    /// the refusing one (ADR-003).
    /// </summary>
    private static IResult? EligibilityRefusal(SystemUserRow user, HttpContext httpContext)
        => ClassifyEligibility(user.IsDisabled, user.AccessMode, user.ApplicationId, user.IsExternal) switch
        {
            ShareEligibility.Disabled => Refused(httpContext, StatusCodes.Status422UnprocessableEntity, NotSharedTitle,
                UserDisabledReasonCode, "This user is disabled, so the record was not shared with them."),
            ShareEligibility.NotAPerson => Refused(httpContext, StatusCodes.Status422UnprocessableEntity, NotSharedTitle,
                UserNotAPersonReasonCode,
                "This is an application, support or delegated-administration account, not a person, so the record was not shared with it."),
            ShareEligibility.NotInternal => Refused(httpContext, StatusCodes.Status422UnprocessableEntity, NotSharedTitle,
                UserNotInternalReasonCode,
                "This user is not confirmed as internal, so the record was not shared with them. Give an external person access with an external grant, which carries an expiry."),
            _ => null,
        };

    /// <summary>
    /// The ONE share-eligibility rule (task 063), extracted for task 142's Assigned-To materializer so both ask the same
    /// question: an existing, ENABLED PERSON (access mode Read-Write, Administrative or Read, and no application id) whose
    /// <c>sprk_isexternal</c> confirms them internal. Checked in that order; an unreadable (null) value counts as the
    /// refusing one (ADR-003).
    /// </summary>
    internal static ShareEligibility ClassifyEligibility(bool? isDisabled, int? accessMode, Guid? applicationId, bool? isExternal)
    {
        if (isDisabled is not false)
            return ShareEligibility.Disabled;

        if (applicationId is not null || accessMode is not (>= 0 and <= LastPersonAccessMode))
            return ShareEligibility.NotAPerson;

        if (isExternal is not false)
            return ShareEligibility.NotInternal;

        return ShareEligibility.Eligible;
    }

    /// <summary>The single refusal shape: ProblemDetails with a reason code (ADR-003) and the trace id (ADR-019).</summary>
    private static IResult Refused(HttpContext httpContext, int statusCode, string title, string reasonCode, string detail)
        => Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["traceId"] = httpContext.TraceIdentifier,
                ["reasonCode"] = reasonCode,
            });

    /// <summary>
    /// The refusal for a write whose result could not be confirmed.
    /// </summary>
    /// <remarks>
    /// Carries the mask the read-back actually OBSERVED, when it was readable, so a client can tell "nothing
    /// happened" from "something else is stored" without a second round trip (Step 9.5 review finding 18). The
    /// extension is absent when the read-back itself failed — which is its own answer, and not one to fake a number
    /// for.
    /// </remarks>
    private static IResult NotConfirmed(HttpContext httpContext, string detail, int? observedAccessRightsMask)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["traceId"] = httpContext.TraceIdentifier,
            ["reasonCode"] = WriteNotConfirmedReasonCode,
        };

        if (observedAccessRightsMask is { } observed)
            extensions["observedAccessRightsMask"] = observed;

        return Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: NotConfirmedTitle,
            detail: detail,
            extensions: extensions);
    }

    /// <summary>The outcome of <see cref="ClassifyEligibility"/>.</summary>
    internal enum ShareEligibility
    {
        /// <summary>An enabled, internal person: may receive a POA share.</summary>
        Eligible,

        /// <summary>Disabled (or unreadable).</summary>
        Disabled,

        /// <summary>An application, support, non-interactive or delegated-administration account (or unreadable).</summary>
        NotAPerson,

        /// <summary>Not confirmed internal: <c>sprk_isexternal</c> is true or unreadable.</summary>
        NotInternal,
    }

    /// <summary>The <c>systemuser</c> columns these routes read.</summary>
    internal sealed class SystemUserRow
    {
        [JsonPropertyName("systemuserid")]
        public Guid Id { get; set; }

        [JsonPropertyName("fullname")]
        public string? FullName { get; set; }

        [JsonPropertyName("isdisabled")]
        public bool? IsDisabled { get; set; }

        /// <summary>0 Read-Write, 1 Administrative, 2 Read, 3 Support User, 4 Non-interactive, 5 Delegated Admin.</summary>
        [JsonPropertyName("accessmode")]
        public int? AccessMode { get; set; }

        /// <summary>Set only on an application user.</summary>
        [JsonPropertyName("applicationid")]
        public Guid? ApplicationId { get; set; }

        /// <summary>Spaarke's internal-vs-external flag.</summary>
        [JsonPropertyName("sprk_isexternal")]
        public bool? IsExternal { get; set; }
    }
}
