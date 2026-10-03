using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
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
///   1. Read the record; a record that is not secure returns 200 having changed nothing (idempotent); one whose flag
///      comes back EMPTY is refused — empty is "could not tell", never "not secure" (task 150)
///   1.5 Who may remove it (task 150, owner round 3b F3): a Full Access holder or the record's creator — any other Write
///      holder is refused 403 before any write
///   2. Resolve the new owner — request, else configuration, else the calling user
///   3. Assign ownership to that user, and verify by read-back
///   4. Revoke every POA share on the record
///   5. Clear <c>sprk_issecure</c>
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
    /// Task 150 (owner round 3b, F3): the caller holds Write — the route group's delegation filter — but is neither a
    /// Full Access holder on the record (Delete on it, as Dataverse reports the caller's rights) nor the person who
    /// created it. Refused before any write (403); the record stays secure.
    /// </summary>
    internal const string ReasonNotPermitted = "sdap.unsecure.not_permitted";

    /// <summary>
    /// Task 150: who may remove the designation could not be established — the caller's identity, or the record's
    /// recorded creator person, could not be read. Refused before any write; the same caller may retry.
    /// </summary>
    internal const string ReasonPermissionUnverifiable = "sdap.unsecure.permission_unverifiable";

    /// <summary>
    /// Task 150: <c>sprk_issecure</c> came back EMPTY. Every row reads <c>true</c> or <c>false</c> once the one-time
    /// backfill has run (<c>scripts/Repair-SecureFlagNulls.ps1</c>) and the column defaults to No, so an empty value
    /// means this service cannot read the field-secured column. Refused before any write: reporting "already not
    /// secure" would claim a state nobody observed.
    /// </summary>
    internal const string ReasonSecureFlagUnreadable = "sdap.unsecure.secure_flag_unreadable";

    public static RouteGroupBuilder MapUnsecureProjectEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/unsecure-project", UnsecureProjectAsync)
            .WithName("UnsecureProject")
            .WithSummary("Remove the secure designation from a project, matter or work assignment")
            .WithDescription(
                "Reassigns ownership off the Secure Record business unit's named owner team, revokes the " +
                "record's explicit shares and clears sprk_issecure. Accepts recordType + recordId " +
                "(project | matter | workassignment) or the legacy projectId. Idempotent: a record that is " +
                "already not secure returns 200 having changed nothing.")
            .Produces<UnsecureProjectResponse>(StatusCodes.Status200OK)
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
        IConfiguration configuration,
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
            logger.LogInformation(
                "[UNSECURE] {RecordType} {RecordId} is already not secure; nothing to do. TraceId={TraceId}",
                root.WireToken, recordId, traceId);

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
                RecordId: recordId));
        }

        // ── Step 1.5: who may remove it (task 150, owner round 3b F3) ─────────
        var permission = await RefuseUnlessPermittedToRemoveAsync(
            record, dataverseClient, callerAccessProbe, httpContext, root, recordId, logger, traceId, ct);

        if (permission.Refusal != null)
            return permission.Refusal;

        // ── Step 2: Resolve the new owner ────────────────────────────────────
        var newOwnerId = ResolveNewOwner(request, configuration, permission.CallerId);

        if (newOwnerId is null || newOwnerId == Guid.Empty)
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

        // ── Step 3: Assign ownership, and verify it applied ──────────────────
        try
        {
            await dataverseClient.UpdateAsync(
                root.EntitySet,
                recordId,
                new Dictionary<string, object?>
                {
                    ["ownerid@odata.bind"] = $"/{SystemUserEntitySet}({newOwnerId})"
                },
                ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[UNSECURE] Dataverse refused the ownership assignment of {RecordType} {RecordId} to user " +
                "{OwnerId}. TraceId={TraceId}", root.WireToken, recordId, newOwnerId, traceId);

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
                select: $"{root.IdColumn},_owninguser_value",
                top: 1,
                cancellationToken: ct);

            var reread = rows.FirstOrDefault();
            if (reread?._owninguser_value != newOwnerId)
            {
                logger.LogError(
                    "[UNSECURE] Ownership read-back FAILED for {RecordType} {RecordId}: expected owning user " +
                    "{OwnerId}, found {ActualOwnerId}. Leaving the record secure. TraceId={TraceId}",
                    root.WireToken, recordId, newOwnerId, reread?._owninguser_value, traceId);

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

            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                "The ownership reassignment could not be verified, so the secure designation was left " +
                "in place.", traceId, (ReasonKey, ReasonOwnerAssignmentNotApplied));
        }

        // ── Step 4: Revoke the explicit shares ───────────────────────────────
        var sweep = await RevokeAllSharesAsync(
            recordShare, root, recordId, logger, traceId, ct);

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
                root.WireToken, recordId, newOwnerId, sweep.Revoked, sweep.Complete, traceId);

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
                ("newOwnerSystemUserId", newOwnerId),
                ("sharesRevoked", sweep.Revoked),
                ("sweepComplete", sweep.Complete));
        }

        logger.LogInformation(
            "[UNSECURE] {RecordType} {RecordId} un-secured: owner={OwnerId}, sharesRevoked={Count}, " +
            "sweepComplete={SweepComplete}. TraceId={TraceId}",
            root.WireToken, recordId, newOwnerId, sweep.Revoked, sweep.Complete, traceId);

        return TypedResults.Ok(new UnsecureProjectResponse(
            ProjectId: legacyProjectId,
            NewOwnerSystemUserId: newOwnerId.Value,
            SharesRevoked: sweep.Revoked,
            AlreadyUnsecure: false,
            SweepComplete: sweep.Complete,
            RecordType: root.WireToken,
            RecordId: recordId));
    }

    /// <summary>
    /// Owner round 3b, F3 (task 150): only a <b>Full Access holder</b> on the record, or <b>the person who created
    /// it</b>, may remove its secure designation. Securing stays open to any Write holder; removing it does not, because
    /// it ends the isolation of everything stored against the record. Returns the refusal to send, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>The creator</b> is <c>createdby</c>, or — for a record the BFF created app-only, whose <c>createdby</c>
    /// is the application user — the server-stamped <c>sprk_createdbyperson</c> (task 133). The caller's identity is
    /// <c>WhoAmI</c> on their own token, never the request. Checked first, because it needs no rights probe.</para>
    ///
    /// <para><b>Full Access</b> is read as Dataverse's own answer about the caller (<c>RetrievePrincipalAccess</c>,
    /// OBO): Write AND Delete on the record. The Full Access share level is Collaborate plus Delete
    /// (<see cref="RecordShareLevels.FullAccessRights"/>), and a secure record is owned by a memberless team in a
    /// user-free business unit, so Delete on it comes from a Full Access share — or from an administrator's
    /// role, which is the "an administrator can remove the secure designation" the wizard promises.</para>
    ///
    /// <para><b>Fail closed.</b> An identity that cannot be established, or a recorded creator person that cannot be
    /// read, refuses (500, retryable) rather than guessing either way. A column this environment lacks
    /// (<c>sprk_createdbyperson</c> before its schema script ran — Dataverse answers 400) records nobody, so it simply
    /// does not admit anyone. The refusal message is shown to the user as is (the ribbon command renders the endpoint's
    /// ProblemDetails), so it names who CAN do it.</para>
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
        var callerToken = TokenHelper.ExtractBearerTokenOrNull(httpContext);
        var callerId = await callerAccessProbe.GetCallerSystemUserIdAsync(callerToken, ct);
        if (callerId is not { } caller || caller == Guid.Empty)
        {
            logger.LogWarning(
                "[UNSECURE] The caller's systemuserid could not be established for {RecordType} {RecordId}; refusing " +
                "before any change. TraceId={TraceId}", root.WireToken, recordId, traceId);

            return (Problem(StatusCodes.Status403Forbidden, "Forbidden",
                "Your account could not be confirmed, so whether you may remove the secure designation could not be " +
                "checked. Nothing was changed.",
                traceId, (ReasonKey, ReasonPermissionUnverifiable)), Guid.Empty);
        }

        if (record._createdby_value == caller)
        {
            logger.LogInformation(
                "[UNSECURE] Caller {CallerId} created {RecordType} {RecordId} (createdby): permitted to remove the " +
                "secure designation (F3).", caller, root.WireToken, recordId);
            return (null, caller);
        }

        // The recorded creator PERSON, in its own query: an environment without the column still un-secures for a
        // Full Access holder (the provisioning-resume precedent, RecordCreatorPerson remarks).
        try
        {
            var people = await dataverseClient.QueryAsync<SecurityRow>(
                root.EntitySet,
                filter: $"{root.IdColumn} eq {recordId}",
                select: $"{root.IdColumn},{RecordCreatorPerson.ValueColumn}",
                top: 1,
                cancellationToken: ct);

            if (people.FirstOrDefault()?.CreatedByPerson == caller)
            {
                logger.LogInformation(
                    "[UNSECURE] Caller {CallerId} is the person recorded as creating {RecordType} {RecordId} " +
                    "({Column}): permitted to remove the secure designation (F3).",
                    caller, root.WireToken, recordId, RecordCreatorPerson.Column);
                return (null, caller);
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            logger.LogInformation(
                "[UNSECURE] {Column} is not in this environment (400); no creator person is recorded on {RecordType} " +
                "{RecordId}.", RecordCreatorPerson.Column, root.WireToken, recordId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[UNSECURE] The person recorded as creating {RecordType} {RecordId} could not be read; refusing before " +
                "any change. TraceId={TraceId}", root.WireToken, recordId, traceId);

            return (Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"Whether you may remove the secure designation from this {root.DisplayLabel.ToLowerInvariant()} could " +
                "not be checked, because the person who created it could not be looked up. Nothing was changed.",
                traceId, (ReasonKey, ReasonPermissionUnverifiable)), caller);
        }

        var rights = await callerAccessProbe.GetCallerRightsAsync(callerToken, root.EntitySet, recordId, ct);
        const AccessRights fullAccess = AccessRights.Write | AccessRights.Delete;
        if ((rights & fullAccess) == fullAccess)
        {
            logger.LogInformation(
                "[UNSECURE] Caller {CallerId} holds Full Access (Write + Delete) on {RecordType} {RecordId}: permitted to " +
                "remove the secure designation (F3).", caller, root.WireToken, recordId);
            return (null, caller);
        }

        logger.LogWarning(
            "[UNSECURE] Caller {CallerId} holds {Rights} on {RecordType} {RecordId} and did not create it: not permitted to " +
            "remove the secure designation (F3: Full Access holders and the creator only). TraceId={TraceId}",
            caller, rights, root.WireToken, recordId, traceId);

        return (Problem(StatusCodes.Status403Forbidden, "Forbidden",
            $"Only someone with Full Access to this {root.DisplayLabel.ToLowerInvariant()}, or the person who created it, " +
            "can remove its secure designation. It is still secure, and nothing was changed.",
            traceId, (ReasonKey, ReasonNotPermitted)), caller);
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
    /// </remarks>
    private static async Task<ShareSweep> RevokeAllSharesAsync(
        IDataverseRecordShareService recordShare,
        SecureRecordRoot root,
        Guid recordId,
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
                await recordShare.RevokeAccessAsync(root.EntitySet, recordId, share.Principal, ct);
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
    }
}
