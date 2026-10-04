using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;

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
///   1. Read the record; a record that is not secure changes nothing about the record itself (idempotent) — but its
///      related records are reconciled (task 148: an earlier unsecure may have left them isolated)
///   2. Resolve the new owner — request, else configuration, else the calling user
///   2.5 Snapshot the rows the record's Assign cascades to (task 133's primitive) — refuse before any write if they
///      cannot be read
///   3. Assign ownership to that user, and verify by read-back
///   3.5 Re-own every EXISTING related record OUT of the Secure Record owner team to the owner the ownership rule gives a
///      child of an ordinary record, then remove its mirrored shares (task 148) — a pass that does not complete stops
///      here: the record's own shares and its flag are left for the next call to finish
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

    public static RouteGroupBuilder MapUnsecureProjectEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/unsecure-project", UnsecureProjectAsync)
            .WithName("UnsecureProject")
            .WithSummary("Remove the secure designation from a project, matter or work assignment")
            .WithDescription(
                "Reassigns ownership off the Secure Record business unit's named owner team, re-owns the record's " +
                "existing related records out of isolation and removes their mirrored shares, revokes the record's " +
                "explicit shares and clears sprk_issecure. Accepts recordType + recordId " +
                "(project | matter | workassignment) or the legacy projectId. Idempotent: a record that is " +
                "already not secure returns 200 having changed nothing about the record itself (related records an " +
                "earlier unsecure left isolated are completed).")
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
        SecureChildReconciler secureChildren,
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
                select: $"{root.IdColumn},sprk_issecure",
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

        // Idempotency: nothing to undo. Returning 200 rather than 409 because the caller's intent
        // ("this record should not be secure") is already satisfied — a repeat is not a conflict.
        if (record.sprk_issecure != true)
        {
            // Task 148: the record is not secure, but its related records may still be — an unsecure from before task 148
            // left them owned by the memberless Secure team (owner round 11 item 3's ship gate). Completing that is this
            // call's job ("re-invoking unsecure ... completes the pass instead of returning 200-no-op"). Only children the
            // Secure team owns are moved; nothing about the record itself changes.
            var completing = await secureChildren.ReconcileAsync(
                root.LogicalName, recordId, SecureChildReconcileMode.Apply, unsecuring: false, ct);
            if (!completing.IsComplete)
                return ChildrenIncomplete(completing, root, recordId, flagStillSet: false, logger, traceId);

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

        // ── Step 2: Resolve the new owner ────────────────────────────────────
        var newOwnerId = await ResolveNewOwnerAsync(
            request, configuration, callerAccessProbe, httpContext, ct);

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

        // ── Step 3.5 (task 148): the record's EXISTING related records leave isolation ──
        //
        // Ownership first, for the children too: each related record the Secure Record owner team owns is re-owned to the
        // owner the ownership rule gives a child of an ordinary record (its business unit's team — task 146's rule, the
        // resolver told this ONE record is mid-unsecure, its flag still set), and only then are its mirrored shares removed
        // (task 149's synchronizer). So no related record is ever reachable by nobody. The rows the move above cascaded to
        // are placed by the same rule (owner round 13 item 1), not left with the new owner. Owner round 6: related records
        // that are ROOTS of their own (a secure work assignment under this project) stay secure — they are not children.
        //
        // A pass that does not complete STOPS here (ADR-003): the record's own shares stay (its sharees keep reaching the
        // related records that are still isolated) and sprk_issecure stays set — the flag keeps meaning "related records
        // may still be isolated". The ownership move above stands. Calling again completes the pass.
        var childPass = await secureChildren.ReconcileAsync(
            root.LogicalName, recordId, SecureChildReconcileMode.Apply, unsecuring: true, ct);
        if (!childPass.IsComplete)
            return ChildrenIncomplete(childPass, root, recordId, flagStillSet: true, logger, traceId,
                ("newOwnerSystemUserId", newOwnerId));

        // ── Step 4: Revoke the explicit shares ───────────────────────────────
        //
        // After Step 3.5 every related record is out of isolation and carries none of the record's sharees, so revoking
        // the record's own shares now leaves nothing reachable by nobody (owner round 11 item 3: 148 re-owns the children
        // before the record's shares go).
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
            RecordId: recordId,
            Children: SecureChildPassSummary.From(childPass)));
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
    /// The owner an un-secured record lands on: the request's nomination, else configuration, else
    /// the calling user.
    /// </summary>
    private static async Task<Guid?> ResolveNewOwnerAsync(
        UnsecureProjectRequest request,
        IConfiguration configuration,
        CallerRecordAccessProbe callerAccessProbe,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (request.ReassignToSystemUserId is { } requested && requested != Guid.Empty)
            return requested;

        if (Guid.TryParse(configuration[UnsecureOwnerUserIdConfigKey], out var configured)
            && configured != Guid.Empty)
        {
            return configured;
        }

        var callerToken = TokenHelper.ExtractBearerTokenOrNull(httpContext);

        return await callerAccessProbe.GetCallerSystemUserIdAsync(callerToken, ct);
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
    }
}
