using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using static Sprk.Bff.Api.Api.ExternalAccess.InternalShareEndpoints;

namespace Sprk.Bff.Api.Api.ExternalAccess;

// =====================================================================================================================
// unified-access-control-r2 task 193 (#2338) step 2 — the /share-user write core, extracted so Manage Access's /grant can
// share with the ONE internal user a contact represents through the SAME code.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: InternalShareEndpoints.ShareAsync held this logic inline as the /share-user handler.
//   Extension: moved, not rewritten — /share-user is now a thin call to it, with the same refusal codes, narrowing, No Access
//     guard, read-back, cache invalidation, MarkShareAdoptedAsync, notification and secure-child fan-out.
//   Cost of doing nothing: /grant (task 193 step 3) would need a second copy of the share write to give a contact's internal
//     user model-driven-app access, and two copies drift.
// =====================================================================================================================

/// <summary>
/// The ONE internal-user share write: validates the request, checks who the user is (eligibility, the Restricted bar), the
/// No Access list, the caller's own rights (a share never passes on more than the caller holds), never silently lowers an
/// existing share, writes the POA share, reads it back, clears the impersonated root sets, marks an Assigned-To share
/// adopted, tells the user and brings the record's secure children and filed records into line.
/// </summary>
/// <remarks>Authorization is the caller's route's: <c>/share-user</c> and <c>/grant</c> both run behind the delegation filter
/// (Write + Share as the caller). This core re-probes the caller's rights only to intersect the level (owner decision
/// 2026-09-16).</remarks>
internal static class InternalUserShareCore
{
    /// <summary>
    /// Shares <paramref name="request"/>'s record with its system user. <see cref="InternalUserShareResult.Result"/> is the
    /// <c>/share-user</c> answer; <see cref="InternalUserShareResult.Shared"/> is set only with the confirmed 200.
    /// </summary>
    internal static async Task<InternalUserShareResult> ShareAsync(
        ShareRecordWithUserRequest request,
        IDataverseRecordShareService recordShare,
        DataverseWebApiClient dataverseClient,
        ExternalParticipationService participations,
        ITenantCache cache,
        CallerRecordAccessProbe callerAccessProbe,
        SecureChildShareSynchronizer secureChildShares,
        SecureShareNoAccessGuard noAccessGuard,
        SecureRootInheritance relatedRoots,
        Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer assignedAccess,
        GrantAccessNotifier accessNotifier,
        HttpContext httpContext,
        ILogger logger,
        CancellationToken ct,
        IOptionsMonitor<WorkforceIdentityOptions>? workforceIdentity = null)
    {
        var confirmed = new Confirmation();
        var result = await ShareCoreAsync(
            request, recordShare, dataverseClient, participations, cache, callerAccessProbe, secureChildShares,
            noAccessGuard, relatedRoots, assignedAccess, accessNotifier, httpContext, logger, confirmed, ct,
            workforceIdentity);
        return new InternalUserShareResult(result, confirmed.Shared);
    }

    private static async Task<IResult> ShareCoreAsync(
        ShareRecordWithUserRequest request,
        IDataverseRecordShareService recordShare,
        DataverseWebApiClient dataverseClient,
        ExternalParticipationService participations,
        ITenantCache cache,
        CallerRecordAccessProbe callerAccessProbe,
        SecureChildShareSynchronizer secureChildShares,
        SecureShareNoAccessGuard noAccessGuard,
        SecureRootInheritance relatedRoots,
        Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer assignedAccess,
        GrantAccessNotifier accessNotifier,
        HttpContext httpContext,
        ILogger logger,
        Confirmation confirmed,
        CancellationToken ct,
        IOptionsMonitor<WorkforceIdentityOptions>? workforceIdentity)
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

        // ── Who: an existing, enabled person — and, on a Restricted record, not flagged external ──
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

        // Owner round 67: whether the record is Restricted matters only for a user flagged external who would otherwise be
        // eligible, so the flags are read only then — an ordinary share costs no extra read, and a disabled or non-person
        // account keeps its own 422. A read that fails, or flags that come back unreadable, refuse (ADR-003) as "could not
        // be read", never as "the record is Restricted", which it could not back (task 138's rule for the grant policy).
        bool? rootIsRestricted = null;
        if (user.IsExternal == true
            && ClassifyEligibility(user.IsDisabled, user.AccessMode, user.ApplicationId, user.IsExternal, rootIsRestricted: false)
               == ShareEligibility.Eligible)
        {
            RootRecordFlags? readFlags = null;
            try
            {
                // Task 174 (owner round 84): Restricted through a parent bars the external-flagged user too.
                var flags = await participations.GetEffectiveRootRecordFlagsAsync(
                    ExternalGrantRoot.LogicalNameFor(root.Type), new[] { root.Id }, ct);
                if (flags.TryGetValue(root.Id, out var f) && !f.IsUnreadable)
                    readFlags = f;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex,
                    "[USER-SHARE] Could not read the access settings of {RootType} {RootId} (caller {CallerOid}). Nothing " +
                    "was shared.", root.Type, root.Id, callerOid);
            }

            if (readFlags is not { } known)
                return Refused(httpContext, StatusCodes.Status500InternalServerError, NotSharedTitle,
                    ReadFailedReasonCode,
                    "This user is flagged as external, and whether this record is Restricted could not be read, so it " +
                    "was not shared. Try again.");

            rootIsRestricted = known.IsRestricted;
        }

        if (EligibilityRefusal(user, rootIsRestricted, httpContext) is { } ineligible)
        {
            logger.LogWarning(
                "[USER-SHARE] Refused to share {RootType} {RootId} with {SystemUserId}: disabled={IsDisabled}, " +
                "accessmode={AccessMode}, application={IsApplication}, external={IsExternal}, restricted={IsRestricted} " +
                "(caller {CallerOid}).",
                root.Type, root.Id, systemUserId, user.IsDisabled, user.AccessMode, user.ApplicationId is not null,
                user.IsExternal, rootIsRestricted, callerOid);
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
            // #2334: the equalised gate question — the same answer, but a record the caller cannot see takes the same work as
            // one that does not exist, so the refusal below cannot tell them apart by time.
            callerRights = await callerAccessProbe.GetCallerRightsForAuthorizationGateAsync(
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
                   ?? confirmed.Ok(user, new ShareRecordWithUserResponse(
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
                cache, httpContext.User, systemUserId, ExternalGrantRoot.LogicalNameFor(root.Type),
                ImpersonatedRootSetSource.CustomerTenantIdsOrNone(workforceIdentity, logger), logger);
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

        // ── Task 181 (owner round 89 item 3): the user is told, in-app with a link, when the share GAVE them rights they
        // did not hold. Best effort, after the confirmed write; a failure is reported, never undoes the share ──
        var notificationFailed = (granted.AccessRightsMask & ~current) != 0
            && await accessNotifier.NotifyUserShareAsync(
                   root.Type, root.Id, systemUserId, RecordShareLevels.LevelForMask(granted.AccessRightsMask), httpContext.User)
               == GrantNotificationOutcome.Failed;

        // ── Task 149: the root write is confirmed; now its secure children (a no-op for an ordinary record) ──
        return await ChildrenIncompleteAfterShareAsync(
                   secureChildShares, relatedRoots, root, systemUserId, outcome, granted.AccessRightsMask, httpContext, logger,
                   callerOid, ct, notificationFailed)
               ?? confirmed.Ok(user, new ShareRecordWithUserResponse(
                   systemUserId, RecordShareLevels.LevelForMask(granted.AccessRightsMask), granted.AccessRightsMask,
                   outcome, narrowed, notificationFailed));
    }

    /// <summary>Records the user a confirmed 200 was for.</summary>
    private sealed class Confirmation
    {
        public SharedInternalUser? Shared { get; private set; }

        public IResult Ok(SystemUserRow user, ShareRecordWithUserResponse response)
        {
            Shared = new SharedInternalUser(user.Id, response);
            return TypedResults.Ok(response);
        }
    }
}

/// <summary>What <see cref="InternalUserShareCore.ShareAsync"/> did.</summary>
/// <param name="Result">The <c>/share-user</c> answer: the confirmed 200, or the refusal (nothing written, or — for
/// <c>children_incomplete</c> — the record's own share in place).</param>
/// <param name="Shared">The user the record was confirmed shared with; <c>null</c> unless <paramref name="Result"/> is the 200.</param>
internal sealed record InternalUserShareResult(IResult Result, SharedInternalUser? Shared);

/// <summary>An internal user a record was confirmed shared with. The display name is not read here: the eligibility read
/// selects no name, and /share-user's query stays as it was (a caller that shows the name reads it, display only).</summary>
internal sealed record SharedInternalUser(Guid SystemUserId, ShareRecordWithUserResponse Response);
