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
/// <para><b>Levels.</b> <see cref="RecordShareLevels"/> is the one level-to-rights table (View Only, Collaborate, Full
/// Access; no level carries Share or Assign).</para>
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

    /// <summary>A write was sent and its result could not be confirmed.</summary>
    internal const string WriteNotConfirmedReasonCode = "sdap.access.user_share.write_not_confirmed";

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
    internal const string SystemUserSelect = "systemuserid,fullname,isdisabled,accessmode,applicationid,sprk_isexternal";

    /// <summary>The columns the share list reads for names.</summary>
    internal const string SystemUserNameSelect = "systemuserid,fullname";

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

        if (current == rights.AccessRightsMask)
        {
            logger.LogInformation(
                "[USER-SHARE] {SystemUserId} already holds {Level} on {RootType} {RootId}; nothing was written (caller {CallerOid}).",
                systemUserId, level, root.Type, root.Id, callerOid);
            return TypedResults.Ok(new ShareRecordWithUserResponse(systemUserId, level, current, OutcomeUnchanged));
        }

        // ── Write, then confirm what Dataverse stored ─────────────────────────
        var entitySet = ExternalGrantRoot.BindFor(root.Type).EntitySet;
        var principal = DataversePrincipalRef.User(systemUserId);
        int? stored = null;
        Exception? failure = null;
        try
        {
            if (current == 0)
                await recordShare.GrantAccessAsync(entitySet, root.Id, principal, rights.AccessRightsCsv, ct);
            else
                await recordShare.ModifyAccessAsync(entitySet, root.Id, principal, rights.AccessRightsCsv, ct);

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

        if (failure is not null || stored != rights.AccessRightsMask)
        {
            logger.LogError(failure,
                "[USER-SHARE] Sharing {RootType} {RootId} with {SystemUserId} at {Level} was not confirmed: the user held " +
                "mask {Previous}, the write asked for {Requested}, and the read-back found {Stored} (caller {CallerOid}).",
                root.Type, root.Id, systemUserId, level, current, rights.AccessRightsMask,
                stored?.ToString() ?? "nothing readable", callerOid);
            return Refused(httpContext, StatusCodes.Status500InternalServerError, NotConfirmedTitle,
                WriteNotConfirmedReasonCode,
                "The share could not be confirmed at the requested level. Reload the list to see what this user holds, then try again.");
        }

        var outcome = current == 0 ? OutcomeCreated : OutcomeUpdated;
        logger.LogInformation(
            "[USER-SHARE] Caller {CallerOid} shared {RootType} {RootId} with {SystemUserId} at {Level} ({Outcome}; mask {Previous} -> {Stored}).",
            callerOid, root.Type, root.Id, systemUserId, level, outcome, current, stored);

        return TypedResults.Ok(new ShareRecordWithUserResponse(systemUserId, level, rights.AccessRightsMask, outcome));
    }

    // =========================================================================
    // POST /unshare-user
    // =========================================================================

    /// <summary>Handles <c>POST /api/v1/external-access/unshare-user</c>.</summary>
    /// <returns>
    /// 200 with <c>removed = true</c> when a share existed and is confirmed gone, or <c>removed = false</c> when the user
    /// held none (nothing was written). 400 for a missing record or user. 404 for an unknown user. 500 when the user or
    /// the shares could not be read (nothing was written), or when the removal could not be confirmed.
    /// </returns>
    internal static async Task<IResult> UnshareAsync(
        UnshareRecordWithUserRequest request,
        IDataverseRecordShareService recordShare,
        DataverseWebApiClient dataverseClient,
        ITenantCache cache,
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

        // The user must exist — and nothing more. A share held by a user who has since been disabled, or reclassified
        // as external, is exactly the kind that must stay removable.
        SystemUserRow? user;
        try
        {
            user = await ReadSystemUserAsync(dataverseClient, systemUserId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[USER-SHARE] Could not read system user {SystemUserId} (caller {CallerOid}). Nothing was removed.",
                systemUserId, callerOid);
            return Refused(httpContext, StatusCodes.Status500InternalServerError, NotUnsharedTitle,
                ReadFailedReasonCode, "The user could not be looked up, so no share was removed. Try again.");
        }

        if (user is null)
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
            return TypedResults.Ok(new UnshareRecordWithUserResponse(systemUserId, Removed: false));
        }

        var entitySet = ExternalGrantRoot.BindFor(root.Type).EntitySet;
        int? remaining = null;
        Exception? failure = null;
        try
        {
            await recordShare.RevokeAccessAsync(entitySet, root.Id, DataversePrincipalRef.User(systemUserId), ct);
            remaining = await ReadDirectShareMaskAsync(recordShare, root, systemUserId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            failure = ex;
        }
        finally
        {
            await ImpersonatedRootSetSource.InvalidateAsync(
                cache, httpContext.User, systemUserId, ExternalGrantRoot.LogicalNameFor(root.Type), logger);
        }

        if (failure is not null || remaining != 0)
        {
            logger.LogError(failure,
                "[USER-SHARE] Removing {SystemUserId}'s share on {RootType} {RootId} was not confirmed: it held mask " +
                "{Previous} and the read-back found {Remaining} (caller {CallerOid}).",
                systemUserId, root.Type, root.Id, current, remaining?.ToString() ?? "nothing readable", callerOid);
            return Refused(httpContext, StatusCodes.Status500InternalServerError, NotConfirmedTitle,
                WriteNotConfirmedReasonCode,
                "Removing this user's share could not be confirmed. Reload the list to see whether they still have access, then try again.");
        }

        logger.LogInformation(
            "[USER-SHARE] Caller {CallerOid} removed {SystemUserId}'s share on {RootType} {RootId} (it held mask {Previous}).",
            callerOid, systemUserId, root.Type, root.Id, current);

        return TypedResults.Ok(new UnshareRecordWithUserResponse(systemUserId, Removed: true));
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
            .ThenBy(s => s.FullName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(s => s.SystemUserId)
            .ToList();

        return TypedResults.Ok(new RecordUserSharesResponse(listed));
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
    {
        if (user.IsDisabled is not false)
            return Refused(httpContext, StatusCodes.Status422UnprocessableEntity, NotSharedTitle,
                UserDisabledReasonCode, "This user is disabled, so the record was not shared with them.");

        if (user.ApplicationId is not null || user.AccessMode is not (>= 0 and <= LastPersonAccessMode))
            return Refused(httpContext, StatusCodes.Status422UnprocessableEntity, NotSharedTitle,
                UserNotAPersonReasonCode,
                "This is an application, support or delegated-administration account, not a person, so the record was not shared with it.");

        if (user.IsExternal is not false)
            return Refused(httpContext, StatusCodes.Status422UnprocessableEntity, NotSharedTitle,
                UserNotInternalReasonCode,
                "This user is not confirmed as internal, so the record was not shared with them. Give an external person access with an external grant, which carries an expiry.");

        return null;
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
