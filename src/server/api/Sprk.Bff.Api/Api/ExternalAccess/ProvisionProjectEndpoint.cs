using System.Text.Json;
using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Access;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/provision-project
///
/// Provisions the infrastructure a secure record needs — a <c>sprk_project</c>, <c>sprk_matter</c> or
/// <c>sprk_workassignment</c> carrying <c>sprk_issecure = true</c> (task 144 widened it from projects only). Called by
/// the Create Project wizard immediately after creating the project with the Secure toggle on, and by a Write holder
/// for any secure root.
///
/// Provisioning sequence (task 133 reordered the share around the owner move — C11):
///   1. Confirm the record exists and carries <c>sprk_issecure = true</c>
///   2. Resolve the ONE canonical Secure Record business unit, BY NAME, from configuration
///   3. Resolve that BU's NAMED, non-default owner team (<c>SecureRecord:OwnerTeamName</c>), and prove it has ZERO
///      members and that ZERO systemusers sit in the BU — before any mutation (<see cref="SecureRecordOwnerTeam"/>)
///   4. Read the marker (see <see cref="RootRow"/>): owned by the team WITH a container → 409, nothing written; owned
///      by the team WITHOUT a container → RESUME (below); otherwise continue. Then, still before any mutation: the
///      SPE container type is configured, the caller's systemuserid (WhoAmI), the record's current owner, and the
///      creator's current share (complete read or nothing)
///   4.5 SHARE-FIRST: give the creator their share while the record is still where it was created
///   5. Assign the record's owner to that team, and verify the assignment by reading it back
///   5.5 Prove the creator holds exactly <see cref="CreatorAccessRights"/> now that the team owns it (re-issue if the
///       move dropped it). If that cannot be proven, COMPENSATE: move the record back to its pre-call owner, read it
///       back, and put the creator's share back to what it was. Then share any named colleagues.
///   6. Create the record's own SPE container
///   7. Record the container on the record — and FAIL if that record cannot be written
///
/// <para><b>The one rule this ordering serves</b> (owner, session 27 round 3, S5): a secure record must always keep
/// at least one person who can open it. A memberless team owns it, so after step 5 the creator's share is the only
/// way in. Every single failure therefore ends either with the record back in its pre-call ownership (the creator
/// keeps the access they had, and can retry through the normal Write gate) or with the creator's share in place.
/// Only a DOUBLE failure — the share and then the compensating move — can leave a record nobody opens; that state has
/// its own reason code, a CRITICAL log line, and a resume path an administrator (who holds Write) can run.</para>
///
/// <para><b>RESUME.</b> A record owned by the team with no container recorded did not finish: steps 5.5/6/7 did not
/// all complete. A call on it ensures the share for the record's <c>createdby</c> user — never for the caller, unless
/// the caller IS that user (owner decision F8) — then runs steps 6 and 7. Every resume step is an "ensure" keyed on
/// observed state, so it re-runs whatever the forward path would. Two rules keep a resume from widening the access
/// list (task 133 verifier round 1): named colleagues are accepted only from the record's creator (anyone else is
/// refused before any write, and adds people through Manage Access); and when <c>createdby</c> cannot be used, the
/// resume completes only if a person ALREADY holds a share — an administrator's, made through Manage Access — and then
/// shares to nobody.</para>
///
/// <para><b>Rollback is for ownership and shares only.</b> Steps 4.5–5.5 are undone on failure, because the undo
/// restores the access state every earlier refusal already leaves (the creator's own). Steps 6 and 7 are NOT undone:
/// moving a record OUT of the Secure Record business unit because storage failed would turn a storage failure into a
/// disclosure. A container failure leaves a secured, shared record that the next call resumes; the only artifact a
/// failed run can strand is an empty SPE container, which its error body names (ADR-003). <c>sprk_issecure</c> is
/// never written here, on any path: a secure-requested record that failed provisioning stays flagged, so uploads to
/// it fail closed (<c>RecordContainerResolver</c>).</para>
///
/// Authentication: Azure AD JWT (RequireAuthorization via the adminGroup).
/// ADR-001: Minimal API — no controllers.
/// ADR-003: fail closed; a run that cannot record what it created does not return success.
/// ADR-007: SPE container created through the SpeFileStore facade.
/// ADR-008: Authorization applied at route group level in ExternalAccessEndpoints.
/// ADR-010: Concrete DI injections.
/// </summary>
/// <remarks>
/// <para><b>TASK 144 (C10 part 1, #967) — a NAMED owner team, not the business unit's default team.</b> Until
/// 2026-10-01 this endpoint assigned every secure project to the Secure Record BU's DEFAULT owner team and checked only
/// that one such team existed. Dataverse maintains a default team's membership from each user's
/// <c>businessunitid</c> and it cannot be curated, so any user moved into the Secure Record BU silently became a
/// member of the team owning every secure record, and nothing checked. Ownership now goes to a named team that this
/// endpoint proves memberless, in a BU it proves user-free (a user in the BU would read every secure record by DEPTH
/// whatever team owned them). Matters and work assignments, which carry <c>sprk_issecure</c> too but were never
/// re-owned, are provisioned exactly as projects are.</para>
///
/// <para><b>RE-SCOPED 2026-08-25 (task 021).</b> This endpoint previously created a business unit
/// per secure project and an <c>account</c> per secure project, then stamped three references onto
/// the project using three column names that do not exist — and swallowed the resulting 400. All of
/// that is gone. What replaced it, and why, in order of how badly each mattered:</para>
///
/// <para><b>1. BU-per-project contradicted the design and escaped its own guardrail.</b> design.md
/// §5.1 says verbatim "no BU-per-project proliferation" and specifies ONE <c>Secure Record</c>
/// business unit (named <c>Secure Project</c> until task 121 renamed it, 2026-09-29). The old code
/// created a child BU per project and parented it to the ROOT BU — so
/// those BUs sat OUTSIDE the BU that NFR-05's standing assertion guards.</para>
///
/// <para><b>2. Repairing the stamp would have destroyed client data.</b> <c>sprk_externalaccount</c>
/// is the CLIENT lookup — <c>ProjectLiveFactResolver.cs:33</c> and <c>MatterLiveFactResolver.cs:35</c>
/// both map the predicate <c>client</c> to it. The old code created a synthetic
/// "External Access — {project}" account and, in the broken PATCH, aimed it at that column. That column is
/// now never written, and a test fails if it reappears in any payload.</para>
///
/// <para><b>3. The idempotency marker keyed on shared state — a live regression, introduced by this
/// project on 2026-08-23.</b> See <see cref="RootRow"/> for the full account; short version: the
/// wizard then stamped <c>sprk_containerid</c> on every new project from the creating user's BU (removed by task
/// 076), so a marker reading that field ALONE 409'd every secure project as "already provisioned".</para>
///
/// <para><b>4. The nav-property blocker dissolved.</b> The only stamped field is <c>sprk_containerid</c>, an
/// <c>NVARCHAR(100)</c> plain string. The one remaining bind — <c>ownerid</c> — is an out-of-the-box owner field,
/// and it is verified by reading the owner back rather than trusted (see
/// <see cref="AssignOwnerToSecureTeamAsync"/>).</para>
/// </remarks>
public static class ProvisionProjectEndpoint
{
    // ── Configuration (owned by SecureRecordOwnerTeam; aliased here for the pinning tests) ──

    /// <inheritdoc cref="SecureRecordOwnerTeam.BusinessUnitNameConfigKey"/>
    internal const string SecureBusinessUnitNameConfigKey = SecureRecordOwnerTeam.BusinessUnitNameConfigKey;

    /// <inheritdoc cref="SecureRecordOwnerTeam.DefaultBusinessUnitName"/>
    internal const string DefaultSecureBusinessUnitName = SecureRecordOwnerTeam.DefaultBusinessUnitName;

    /// <inheritdoc cref="SecureRecordOwnerTeam.OwnerTeamNameConfigKey"/>
    internal const string SecureOwnerTeamNameConfigKey = SecureRecordOwnerTeam.OwnerTeamNameConfigKey;

    /// <inheritdoc cref="SecureRecordOwnerTeam.DefaultOwnerTeamName"/>
    internal const string DefaultSecureOwnerTeamName = SecureRecordOwnerTeam.DefaultOwnerTeamName;

    // ── Reason codes (ProblemDetails extensions["reasonCode"]) ───────────────
    //
    // Same convention as DelegationRuleFilter (task 008) and ProjectClosureEndpoint: a stable machine
    // -readable code alongside the human-readable detail, so a caller can distinguish "your request
    // named nothing" from "the environment is not set up" without parsing prose. The codes keep the word
    // "provision" for every root type; they are a contract, so they were not renamed when matters and work
    // assignments joined (task 144).

    private const string ReasonKey = "reasonCode";

    internal const string ReasonSecureBuNotFound = "sdap.provision.secure_bu_not_found";
    internal const string ReasonSecureBuAmbiguous = "sdap.provision.secure_bu_ambiguous";
    internal const string ReasonOwnerTeamNotFound = "sdap.provision.secure_owner_team_not_found";
    internal const string ReasonOwnerTeamAmbiguous = "sdap.provision.secure_owner_team_ambiguous";
    internal const string ReasonOwnerAssignmentFailed = "sdap.provision.owner_assignment_failed";
    internal const string ReasonOwnerAssignmentNotApplied = "sdap.provision.owner_assignment_not_applied";
    internal const string ReasonContainerNotRecorded = "sdap.provision.container_not_recorded";
    internal const string ReasonAlreadyProvisioned = "sdap.provision.already_provisioned";
    internal const string ReasonLegacyPerProjectBu = "sdap.provision.legacy_per_project_bu";

    // Task 061 — the share plane. Both are provisioning FAILURES, not warnings: a secure record whose
    // creator was not shared to is a record no human can open.
    internal const string ReasonCreatorUnresolved = "sdap.provision.creator_unresolved";
    internal const string ReasonCreatorShareFailed = "sdap.provision.creator_share_failed";

    // Task 144 — the two invariants a named team needs, each with a distinct "could not tell" code. An unreadable
    // count is a refusal in its own right, never folded into "zero" (ADR-003).
    internal const string ReasonOwnerTeamHasMembers = "sdap.provision.secure_owner_team_has_members";
    internal const string ReasonOwnerTeamMembershipUnreadable = "sdap.provision.secure_owner_team_membership_unreadable";
    internal const string ReasonSecureBuHasUsers = "sdap.provision.secure_bu_has_users";
    internal const string ReasonSecureBuUsersUnreadable = "sdap.provision.secure_bu_users_unreadable";

    // Task 144 — a record already owned INSIDE the Secure Record BU by a team other than the named one: in practice a
    // record provisioned under the retired default team, before the migration script ran. Re-provisioning it would
    // create a second container, so it is refused like any other already-provisioned record.
    internal const string ReasonOwnedByOtherSecureTeam = "sdap.provision.owned_by_other_secure_team";

    // Task 133 (C11) — the creator lock-out. Each names a state the client must tell apart from the others, because
    // what the person in front of the wizard can do next differs.

    /// <summary>
    /// The creator's share failed AND the record could not be shown to be back with its pre-call owner — or the owner
    /// move could not be verified and no creator share could be issued. The record may be owned by the memberless
    /// team with nobody shared: the creator cannot call again (they no longer pass the Write gate); an administrator, who
    /// holds Write, calls provisioning again to resume, which shares it to the record's <c>createdby</c> user.
    /// </summary>
    internal const string ReasonCreatorShareFailedResumable = "sdap.provision.creator_share_failed_resumable";

    /// <summary>
    /// A resume found the record's <c>createdby</c> unusable as the person to share to — disabled, an application
    /// user, absent, or unreadable — AND no other person already holds a share on the record. Resume never shares to
    /// the caller instead (owner decision F8). Recovery: an administrator shares the record to the person who should
    /// hold it (Manage Access), then calls again; the resume then completes without sharing to anyone.
    /// </summary>
    internal const string ReasonResumeCreatorUnavailable = "sdap.provision.resume_creator_unavailable";

    /// <summary>
    /// A resume request named colleagues (<c>sharePrincipalIds</c>) but its caller is not the record's creator
    /// (task 133 verifier round 1). Only the creator may add people while finishing an earlier run; anyone else adds
    /// them through Manage Access, which applies its own eligibility and grantor checks. Refused before any write.
    /// </summary>
    internal const string ReasonResumeColleaguesNotPermitted = "sdap.provision.resume_colleagues_not_permitted";

    /// <summary>
    /// The owner PATCH was sent but its outcome could not be read back. A share to the creator was issued — read back
    /// when the read works (<c>creatorShareConfirmed: true</c>), otherwise issued without confirmation.
    /// </summary>
    internal const string ReasonOwnerAssignmentUnverified = "sdap.provision.owner_assignment_unverified";

    /// <summary>Step 6 failed: the record is secured and shared, with no container yet. The same caller may resume.</summary>
    internal const string ReasonContainerCreationFailed = "sdap.provision.container_creation_failed";

    /// <summary><c>SharePointEmbedded:ContainerTypeId</c> is missing or invalid — refused before any mutation.</summary>
    internal const string ReasonContainerTypeNotConfigured = "sdap.provision.container_type_not_configured";

    /// <summary>
    /// The record was read without an owning user or team, so the move could not be undone — refused before any
    /// mutation. Deterministic for that row: calling again repeats the refusal, so it is not a self-service retry.
    /// </summary>
    internal const string ReasonRecordOwnerUnreadable = "sdap.provision.record_owner_unreadable";

    // ── Share rights (task 061) ──────────────────────────────────────────────

    /// <summary>
    /// Rights the creating user receives on their own secure record.
    /// </summary>
    /// <remarks>
    /// Read/Write/Append/AppendTo is "can actually work the matter"; <c>ShareAccess</c> is what lets
    /// them bring colleagues in through the FR-29 "+ User" surface without an administrator. Delete
    /// and Assign are deliberately absent — a secure record leaves the secure business unit only
    /// through the explicit unsecure path, not by being reassigned out of it.
    /// </remarks>
    internal const string CreatorAccessRights = RecordShareLevels.CollaborateRights + ",ShareAccess";

    /// <summary>
    /// Rights a named colleague receives at provisioning time: the same working access as the creator,
    /// WITHOUT <c>ShareAccess</c> — re-sharing stays with the creator so the access list cannot widen
    /// through a chain nobody reviewed.
    /// </summary>
    /// <remarks>
    /// The Collaborate level of <see cref="RecordShareLevels"/> (task 063), the one level-to-rights table — so a
    /// colleague shared at provisioning and one shared later at Collaborate through the "+ User" picker hold the
    /// same rights, and the two cannot drift apart.
    /// </remarks>
    internal const string CollaboratorAccessRights = RecordShareLevels.CollaborateRights;

    /// <summary>
    /// The mask Dataverse stores for <see cref="CreatorAccessRights"/> — what the creator's share is confirmed against
    /// (task 133). Derived from the literal, never written as a number, so it follows task 139 if that changes the
    /// rights.
    /// </summary>
    internal static readonly int CreatorAccessMask = RecordShareLevels.MaskForRightsCsv(CreatorAccessRights);

    /// <summary>The Read bit of a stored share mask — what makes a share one a person can open the record with.</summary>
    private static readonly int ReadMask = RecordShareLevels.MaskForRightsCsv(RecordShareLevels.ViewOnlyRights);

    /// <summary>
    /// The columns Step 1 reads from <c>sprk_project</c> — the project row of <see cref="SecureRecordRoot"/>.
    /// </summary>
    /// <remarks>
    /// <para>Every name here is verified against live <c>sprk_project</c> metadata (2026-08-25, re-checked for all
    /// three roots 2026-10-01). <c>sprk_securitybuid</c>, <c>sprk_specontainerid</c> and
    /// <c>sprk_externalaccountid</c> — the three names the old stamping PATCH used — do not exist on this table, which
    /// is why that PATCH 400'd for five months.</para>
    ///
    /// <para>Internal (not private) so a test can pin the exact names against the live column set.</para>
    /// </remarks>
    internal static readonly string ProjectProvisioningSelect = SecureRecordRoot.Project.ProvisioningSelect;

    /// <summary>
    /// Registers the provision-project endpoint on the external-access management group.
    /// </summary>
    public static RouteGroupBuilder MapProvisionProjectEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/provision-project", ProvisionProjectAsync)
            .WithName("ProvisionSecureProject")
            .WithSummary("Provision infrastructure for a new secure project, matter or work assignment")
            .WithDescription(
                "Assigns the record to the canonical Secure Record business unit's NAMED owner team — after " +
                "proving that team has no members and that no user sits in the business unit — and provisions " +
                "the record's own SPE container, recording it on sprk_containerid. Accepts recordType + recordId " +
                "(project | matter | workassignment) or the legacy projectId. Creates no business unit and no " +
                "account.")
            .Produces<ProvisionProjectResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            // 409: already provisioned — re-running would orphan the existing container.
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    /// <summary>
    /// The record this request targets. The delegation filter calls this too, so the record whose Write was
    /// checked is the record re-owned (task 144; the <c>FromGrantRoot</c> pattern).
    /// </summary>
    internal static GrantExternalAccessEndpoint.GrantRootResolution ResolveRoot(ProvisionProjectRequest request)
        => SecureRecordRoot.ResolveTarget(request.ProjectId, request.RecordType, request.RecordId);

    // =========================================================================
    // Handler
    // =========================================================================

    private static async Task<IResult> ProvisionProjectAsync(
        ProvisionProjectRequest request,
        DataverseWebApiClient dataverseClient,
        SpeFileStore speFileStore,
        IDataverseRecordShareService recordShare,
        CallerRecordAccessProbe callerAccessProbe,
        IConfiguration configuration,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // ── Validation ───────────────────────────────────────────────────────
        var target = ResolveRoot(request);
        if (!target.Ok)
            return ProblemDetailsHelper.ValidationError(target.Error ?? "A record to provision is required.");

        var root = SecureRecordRoot.For(target.Type);
        var recordId = target.Id;
        var traceId = httpContext.TraceIdentifier;

        logger.LogInformation(
            "[PROVISION] Starting secure provisioning: RecordType={RecordType}, RecordId={RecordId}, " +
            "Ref={Ref}, TraceId={TraceId}",
            root.WireToken, recordId, request.ProjectRef, traceId);

        // ── Step 1: Confirm the record exists and is secure ──────────────────
        RootRow? row;
        try
        {
            var rows = await dataverseClient.QueryAsync<RootRow>(
                root.EntitySet,
                filter: $"{root.IdColumn} eq {recordId}",
                select: root.ProvisioningSelect,
                top: 1,
                cancellationToken: ct);

            row = rows.FirstOrDefault();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PROVISION] Failed to query {RecordType} {RecordId} from Dataverse", root.WireToken, recordId);
            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"Failed to retrieve the {root.DisplayLabel.ToLowerInvariant()} record from Dataverse.", traceId);
        }

        if (row == null)
        {
            return Problem(StatusCodes.Status404NotFound, "Not Found",
                $"{root.DisplayLabel} {recordId} not found.", traceId);
        }

        if (row.sprk_issecure != true)
        {
            return ProblemDetailsHelper.ValidationError(
                $"{root.DisplayLabel} {recordId} is not secure (sprk_issecure is false or null). " +
                "Mark it secure before provisioning.");
        }

        var recordName = row.NameFrom(root.NameColumn) ?? request.ProjectRef ?? recordId.ToString();

        // ── Steps 2 + 3: the Secure Record BU, its NAMED owner team, and the two invariants ──
        //
        // FAIL CLOSED, and never substitute. An absent or ambiguous BU or team, a team with any member, a BU with any
        // user, or an unreadable answer to any of those stops provisioning BEFORE anything is written. Never the
        // root BU, never the caller's BU, and never the BU's default team: any of those would put a secure record
        // where people can reach it, and a silent substitution is not detectable from the outside at all.
        var topology = await SecureRecordOwnerTeam.ResolveAsync(dataverseClient, configuration, ct);
        if (!topology.IsResolved)
            return TopologyRefusal(topology, logger, traceId);

        var secureBuId = topology.BusinessUnitId!.Value;
        var secureBuName = topology.BusinessUnitName;
        var ownerTeamId = topology.OwnerTeamId!.Value;
        var ownerTeamName = topology.OwnerTeamName;

        logger.LogInformation(
            "[PROVISION] Resolved Secure Record BU '{BuName}' ({BuId}) and its named owner team " +
            "'{TeamName}' ({TeamId}); the team has no members and the BU holds no users",
            secureBuName, secureBuId, ownerTeamName, ownerTeamId);

        // ── Step 4: The marker ───────────────────────────────────────────────
        if (row.HasLegacyPerProjectBusinessUnit)
        {
            logger.LogWarning(
                "[PROVISION] {RecordType} {RecordId} carries a legacy per-record security BU " +
                "({LegacyBuId}). Refusing: migrating it onto the canonical BU is a deliberate act, " +
                "not something a provisioning retry should do. TraceId={TraceId}",
                root.WireToken, recordId, row._sprk_securitybu_value, traceId);

            return Problem(StatusCodes.Status409Conflict, "Conflict",
                $"{root.DisplayLabel} {recordId} was provisioned by the retired BU-per-project mechanism " +
                "and still references its own security business unit. Migrating it to the canonical " +
                "Secure Record business unit is a manual operation — provisioning will not do it as " +
                "a side effect, because it would leave the old business unit and its container behind " +
                "with nothing pointing at them.",
                traceId,
                (ReasonKey, ReasonLegacyPerProjectBu),
                ("legacyBusinessUnitId", row._sprk_securitybu_value),
                ("speContainerId", row.sprk_containerid));
        }

        var resume = false;
        if (row.IsOwnedBy(ownerTeamId))
        {
            if (row.IsProvisioned(ownerTeamId))
            {
                // Genuinely provisioned: Step 7 is the only writer of sprk_containerid (see RootRow), so a recorded
                // container on a team-owned record means every step completed. Nothing is written.
                logger.LogWarning(
                    "[PROVISION] {RecordType} {RecordId} is already provisioned: owned by the Secure Record owner " +
                    "team {TeamId} with container {ContainerId} recorded. Refusing. TraceId={TraceId}",
                    root.WireToken, recordId, ownerTeamId, row.sprk_containerid, traceId);

                return Problem(StatusCodes.Status409Conflict, "Conflict",
                    $"{root.DisplayLabel} {recordId} has already been provisioned: it is owned by the Secure Record " +
                    "owner team and its own SPE container is recorded on it. Nothing was changed. Re-provisioning " +
                    "would create a second container and repoint the record at it, orphaning the documents " +
                    "already stored.",
                    traceId,
                    (ReasonKey, ReasonAlreadyProvisioned),
                    ("businessUnitId", secureBuId),
                    ("ownerTeamId", ownerTeamId),
                    ("speContainerId", row.sprk_containerid));
            }

            // Owned by the team, no container recorded: an earlier run stopped after the owner move.
            resume = true;
        }

        if (row.IsOwnedInBusinessUnitByAnotherTeam(secureBuId, ownerTeamId))
        {
            logger.LogWarning(
                "[PROVISION] {RecordType} {RecordId} is already owned inside the Secure Record BU by team " +
                "{OwningTeamId}, not by the named owner team {TeamId}. Refusing: it was provisioned under the " +
                "retired default team and must be migrated, not re-provisioned. TraceId={TraceId}",
                root.WireToken, recordId, row._owningteam_value, ownerTeamId, traceId);

            return Problem(StatusCodes.Status409Conflict, "Conflict",
                $"{root.DisplayLabel} {recordId} is already owned inside the Secure Record business unit, by a " +
                "team other than the named secure owner team — most likely the business unit's default team, " +
                "which owned secure records before task 144. Re-provisioning would create a second SPE " +
                "container; move it with scripts/Migrate-SecureRecordsToNamedOwnerTeam.ps1 instead.",
                traceId,
                (ReasonKey, ReasonOwnedByOtherSecureTeam),
                ("businessUnitId", secureBuId),
                ("ownerTeamId", ownerTeamId),
                ("owningTeamId", row._owningteam_value),
                ("speContainerId", row.sprk_containerid));
        }

        // ── Still before any mutation: the container type (task 133) ─────────
        //
        // Checked here rather than at Step 6 because a fault that is certain to stop Step 6 must stop the run while
        // nothing has moved, not after the record is owned by a memberless team.
        var containerTypeIdStr = configuration["SharePointEmbedded:ContainerTypeId"];
        if (!Guid.TryParse(containerTypeIdStr, out var containerTypeId))
        {
            logger.LogError(
                "[PROVISION] SharePointEmbedded:ContainerTypeId is not configured or invalid: '{Value}'. Refusing " +
                "before any change. TraceId={TraceId}", containerTypeIdStr, traceId);

            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                "The SPE container type is not configured on the BFF API (SharePointEmbedded:ContainerTypeId), so " +
                "the record could not be given its own container. Nothing was changed.",
                traceId, (ReasonKey, ReasonContainerTypeNotConfigured));
        }

        Guid creatorId;
        var creatorUnavailable = false;
        if (resume)
        {
            // ── RESUME: only the record's creator may name colleagues (task 133 verifier round 1) ──
            var colleagueRefusal = await RefuseResumeColleaguesUnlessCreatorAsync(
                request, callerAccessProbe, httpContext, root, recordId, row, ownerTeamId, logger, traceId, ct);

            if (colleagueRefusal != null)
                return colleagueRefusal;

            // ── RESUME: ensure the share for the record's createdby user ──
            var resumed = await EnsureResumeCreatorShareAsync(
                dataverseClient, recordShare, root, recordId, row, ownerTeamId, logger, traceId, ct);

            if (resumed.Error != null)
                return resumed.Error;

            creatorId = resumed.CreatorId;
            creatorUnavailable = resumed.CreatorUnavailable;
        }
        else
        {
            // ── FORWARD: share-first, move, prove, compensate ──
            var forward = await MoveWithCreatorShareAsync(
                dataverseClient, recordShare, callerAccessProbe, httpContext, root, recordId, row, ownerTeamId,
                logger, traceId, ct);

            if (forward.Error != null)
                return forward.Error;

            creatorId = forward.CreatorId;
        }

        // ── Named colleagues: only once the creator's share is proven ─────────
        var additionalShared = await ShareToColleaguesAsync(
            recordShare, request, root, recordId, creatorId, logger, traceId, ct);

        // ── Step 6: Create the record's own SPE container ────────────────────
        var containerResult = await CreateSpeContainerAsync(
            speFileStore, containerTypeId, root, recordName, recordId, ownerTeamId, logger, traceId, ct);

        if (containerResult.Error != null)
            return containerResult.Error;

        var speContainerId = containerResult.ContainerId!;

        // ── Step 7: Record the container on the record — FAIL if it cannot be written ──
        try
        {
            await RecordContainerAsync(
                dataverseClient, root, recordId, speContainerId, row.sprk_containerid, logger, ct);
        }
        catch (Exception ex)
        {
            // ADR-003. This used to be a catch + LogWarning + return 200, and that swallow is the
            // single reason the broken column names survived five months: provisioning created real
            // infrastructure, failed to record any of it, and reported success. A container nobody
            // recorded is invisible — so the error carries its id, which is the only thing that makes
            // the orphan reconcilable.
            logger.LogError(ex,
                "[PROVISION] Container {ContainerId} was created for {RecordType} {RecordId} but could " +
                "NOT be recorded on the record. The container is orphaned until an operator " +
                "reconciles it. TraceId={TraceId}",
                speContainerId, root.WireToken, recordId, traceId);

            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"An SPE container was created for {root.DisplayLabel.ToLowerInvariant()} {recordId} but could " +
                "not be recorded on the record. The record is owned by the Secure Record owner team and shared " +
                "to its creator, with no container recorded, so calling provisioning again (the same caller may) " +
                "resumes and creates and records a new container. The container named here holds nothing — " +
                "uploads to a secure record with no container of its own are refused — and can be deleted.",
                traceId,
                (ReasonKey, ReasonContainerNotRecorded),
                ("speContainerId", speContainerId),
                ("ownerTeamId", ownerTeamId));
        }

        logger.LogInformation(
            "[PROVISION] Provisioning complete for {RecordType} {RecordId}: BU={BuId} ({BuName}), " +
            "OwnerTeam={TeamId}, Container={ContainerId}, Resumed={Resumed}",
            root.WireToken, recordId, secureBuId, secureBuName, ownerTeamId, speContainerId, resume);

        return TypedResults.Ok(new ProvisionProjectResponse(
            BusinessUnitId: secureBuId,
            BusinessUnitName: secureBuName,
            OwnerTeamId: ownerTeamId,
            OwnerTeamName: ownerTeamName,
            SpeContainerId: speContainerId,
            SharedToCreatorSystemUserId: creatorId,
            AdditionalPrincipalsShared: additionalShared,
            RecordType: root.WireToken,
            RecordId: recordId,
            Resumed: resume,
            CreatorUnavailable: creatorUnavailable));
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// The refusal for every state in which the Secure Record topology is not provably safe to assign into. Each
    /// is returned BEFORE any mutation (task 144), so "nothing was written" is true of every one.
    /// </summary>
    /// <remarks>
    /// The unreadable BU and team reads keep the codes they always had (<c>secure_bu_not_found</c>,
    /// <c>secure_owner_team_not_found</c>), because the wizard already classifies those as "environment not set
    /// up". The two invariants task 144 adds each get a distinct "could not tell" code — an unreadable member count
    /// must never be reported as a clean one. A team with members, or a BU with users, is logged at CRITICAL: it is
    /// not only a refusal here but a live exposure of every secure record that already exists.
    /// </remarks>
    private static IResult TopologyRefusal(SecureOwnerTeamResolution topology, ILogger logger, string traceId)
    {
        var buName = topology.BusinessUnitName;
        var teamName = topology.OwnerTeamName;
        var buId = topology.BusinessUnitId;

        switch (topology.Status)
        {
            case SecureOwnerTeamStatus.BusinessUnitUnreadable:
                logger.LogError(topology.Fault,
                    "[PROVISION] Failed to resolve the Secure Record business unit by name '{BuName}'", buName);
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    "Failed to resolve the Secure Record business unit from Dataverse.", traceId,
                    (ReasonKey, ReasonSecureBuNotFound));

            case SecureOwnerTeamStatus.BusinessUnitNotFound:
                logger.LogError(
                    "[PROVISION] No business unit named '{BuName}' exists. Provisioning refused — a secure " +
                    "record must not be placed in any other business unit. TraceId={TraceId}", buName, traceId);
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    $"No business unit named '{buName}' exists in this environment. The canonical " +
                    "Secure Record business unit is created during environment setup; provisioning will " +
                    "not create one, and will not fall back to another business unit.",
                    traceId, (ReasonKey, ReasonSecureBuNotFound), ("businessUnitName", buName));

            case SecureOwnerTeamStatus.BusinessUnitAmbiguous:
                logger.LogError(
                    "[PROVISION] More than one business unit is named '{BuName}'. Provisioning refused — " +
                    "picking one would be arbitrary. TraceId={TraceId}", buName, traceId);
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    $"More than one business unit is named '{buName}'. Provisioning cannot choose " +
                    "between them. Resolve the duplicate, or set " +
                    $"'{SecureBusinessUnitNameConfigKey}' to an unambiguous name.",
                    traceId, (ReasonKey, ReasonSecureBuAmbiguous), ("businessUnitName", buName));

            case SecureOwnerTeamStatus.OwnerTeamUnreadable:
                logger.LogError(topology.Fault,
                    "[PROVISION] Failed to resolve the named owner team '{TeamName}' in business unit {BuId}",
                    teamName, buId);
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    "Failed to resolve the Secure Record owner team from Dataverse.", traceId,
                    (ReasonKey, ReasonOwnerTeamNotFound));

            case SecureOwnerTeamStatus.OwnerTeamNotFound:
                logger.LogError(
                    "[PROVISION] Business unit '{BuName}' has no non-default owner team named '{TeamName}'. " +
                    "Provisioning refused — it will NOT fall back to the business unit's default team. " +
                    "TraceId={TraceId}", buName, teamName, traceId);
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    $"Business unit '{buName}' has no owner team named '{teamName}' (an Owner team that is " +
                    "not the business unit's default team). Create it per docs/guides/" +
                    "SECURE-PROJECT-ENVIRONMENT-SETUP.md §4, or set " +
                    $"'{SecureOwnerTeamNameConfigKey}'. Provisioning will not use the default team: its " +
                    "membership follows every user placed in the business unit and cannot be curated.",
                    traceId, (ReasonKey, ReasonOwnerTeamNotFound),
                    ("businessUnitId", buId), ("ownerTeamName", teamName));

            case SecureOwnerTeamStatus.OwnerTeamAmbiguous:
                logger.LogError(
                    "[PROVISION] More than one non-default owner team in '{BuName}' is named '{TeamName}'. " +
                    "Provisioning refused — picking one would be arbitrary. TraceId={TraceId}",
                    buName, teamName, traceId);
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    $"Business unit '{buName}' has more than one owner team named '{teamName}'. " +
                    "Provisioning cannot choose between them.",
                    traceId, (ReasonKey, ReasonOwnerTeamAmbiguous),
                    ("businessUnitId", buId), ("ownerTeamName", teamName));

            case SecureOwnerTeamStatus.OwnerTeamHasMembers:
                logger.LogCritical(
                    "[PROVISION] The secure owner team '{TeamName}' ({TeamId}) has member(s) {MemberIds}. Every " +
                    "secure record it owns is readable by each of them. Provisioning refused. TraceId={TraceId}",
                    teamName, topology.OwnerTeamId, topology.MemberIds, traceId);
                // WHO the members are goes to the operator log above, not to the caller: any Write holder on any
                // secure record can reach this response, and the remedy is an administrator's, not theirs.
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    $"The secure owner team '{teamName}' has members. It owns every secure record, so each " +
                    "member reads all of them. An administrator must remove every member (human or application " +
                    "user) before provisioning; nothing was changed.",
                    traceId, (ReasonKey, ReasonOwnerTeamHasMembers),
                    ("ownerTeamId", topology.OwnerTeamId), ("memberCount", topology.MemberIds.Count));

            case SecureOwnerTeamStatus.OwnerTeamMembershipUnreadable:
                logger.LogError(topology.Fault,
                    "[PROVISION] The membership of the secure owner team {TeamId} could not be read. Refusing — " +
                    "an unreadable count is not zero. TraceId={TraceId}", topology.OwnerTeamId, traceId);
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    "The secure owner team's membership could not be read, so it cannot be shown to be empty. " +
                    "Nothing was changed; retry when Dataverse is reachable.",
                    traceId, (ReasonKey, ReasonOwnerTeamMembershipUnreadable),
                    ("ownerTeamId", topology.OwnerTeamId));

            case SecureOwnerTeamStatus.BusinessUnitHasUsers:
                logger.LogCritical(
                    "[PROVISION] User(s) {UserIds} sit in the Secure Record business unit '{BuName}' ({BuId}). " +
                    "With Business Unit or Deep depth on a secure table they read every secure record. " +
                    "Provisioning refused. TraceId={TraceId}", topology.BusinessUnitUserIds, buName, buId, traceId);
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    $"The Secure Record business unit '{buName}' contains users. It must hold none — a user " +
                    "there reads every secure record by business-unit depth, whoever owns them. Nothing was " +
                    "changed; an administrator must move them out (this is an owner decision, not something " +
                    "provisioning does).",
                    traceId, (ReasonKey, ReasonSecureBuHasUsers),
                    ("businessUnitId", buId), ("userCount", topology.BusinessUnitUserIds.Count));

            case SecureOwnerTeamStatus.BusinessUnitUsersUnreadable:
                logger.LogError(topology.Fault,
                    "[PROVISION] The users of the Secure Record business unit {BuId} could not be read. " +
                    "Refusing — an unreadable count is not zero. TraceId={TraceId}", buId, traceId);
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    "The Secure Record business unit's users could not be read, so it cannot be shown to hold " +
                    "none. Nothing was changed; retry when Dataverse is reachable.",
                    traceId, (ReasonKey, ReasonSecureBuUsersUnreadable), ("businessUnitId", buId));

            default:
                // Exhaustive above. An unknown status is refused, not treated as resolved.
                return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                    "The Secure Record owner team could not be resolved.", traceId,
                    (ReasonKey, ReasonOwnerTeamNotFound));
        }
    }

    /// <summary>
    /// The forward path (task 133, C11): resolve the creator, share to them BEFORE the owner move where the platform
    /// allows it, move the record to the team, prove the creator's share on the moved record — and if that cannot be
    /// proven, move the record back to its pre-call owner and put the creator's share back the way it was.
    /// </summary>
    /// <remarks>
    /// <para><b>Why share-first.</b> Before task 133 the share was issued only AFTER the move. When it failed, the
    /// record was already owned by a memberless team, so its creator could not open it, could not retry (the
    /// delegation filter wants Write, which they no longer held), and anyone else who retried hit the 409. Sharing
    /// while the record still sits where it was created means the creator's access never depends on a write made after
    /// they lost it.</para>
    ///
    /// <para><b>Two platform behaviours are proven live, not assumed</b> (task 133 manual gate): (a) a share to the
    /// record's CURRENT owner is accepted; (b) that share survives the reassignment. This method does not depend on
    /// either: if a share to a creator who owns the record is refused, it falls back to the post-move grant; if the
    /// move drops the share, Step 5.5 re-issues it. Either way the post-move read is what decides.</para>
    ///
    /// <para><b>Compensation restores ownership and shares, never <c>sprk_issecure</c></b> — the access state every
    /// earlier refusal already leaves. No branch here gives anyone access they lacked before the call: a share this
    /// call issued is removed when the move is undone (ADR-003 path C, recorded in the task 133 note).</para>
    /// </remarks>
    private static async Task<CreatorShareStep> MoveWithCreatorShareAsync(
        DataverseWebApiClient dataverseClient,
        IDataverseRecordShareService recordShare,
        CallerRecordAccessProbe callerAccessProbe,
        HttpContext httpContext,
        SecureRecordRoot root,
        Guid recordId,
        RootRow row,
        Guid ownerTeamId,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        // The creator is identified from their own token via WhoAmI (task 061) — never from the request body, so a
        // caller cannot nominate someone else. Resolved before anything is written.
        var callerToken = TokenHelper.ExtractBearerTokenOrNull(httpContext);
        var resolved = await callerAccessProbe.GetCallerSystemUserIdAsync(callerToken, ct);
        if (resolved is not { } creatorId || creatorId == Guid.Empty)
        {
            logger.LogError(
                "[PROVISION] Could not resolve the calling user's systemuserid for {RecordType} {RecordId}. Refusing " +
                "before any change: the record could not be shared back to its creator. TraceId={TraceId}",
                root.WireToken, recordId, traceId);

            return CreatorShareStep.Failed(Problem(
                StatusCodes.Status403Forbidden, "Forbidden",
                "The calling user's Dataverse identity could not be established, so the record could not be shared " +
                "back to them. Provisioning stopped before changing anything: the record's ownership and shares are " +
                "as they were, and the same caller may retry.",
                traceId, (ReasonKey, ReasonCreatorUnresolved)));
        }

        // The owner compensation would restore. Every Dataverse row has one; a row read without it is not one this
        // endpoint can safely move, because the move could not be undone.
        if (row.Owner is not { } preOwner)
        {
            logger.LogError(
                "[PROVISION] {RecordType} {RecordId} was read without an owning user or team. Refusing before any " +
                "change: a move that cannot be undone is not attempted. TraceId={TraceId}",
                root.WireToken, recordId, traceId);

            return CreatorShareStep.Failed(Problem(
                StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"The {root.DisplayLabel.ToLowerInvariant()}'s current owner could not be read, so provisioning could " +
                "not guarantee it could put the record back if a later step failed. Nothing was changed. The record was " +
                "read without an owning user or team, so calling again repeats this refusal: an administrator checks " +
                "the record's owner in Dataverse first.",
                traceId, (ReasonKey, ReasonRecordOwnerUnreadable)));
        }

        // The creator's share BEFORE the call — complete, or not at all. An unreadable list is never "no share"
        // (ADR-003): without it, share-first is not used, and compensation's only safe target is "no share".
        int? preCreatorMask;
        try
        {
            var preShares = await recordShare.GetPrincipalAccessOrThrowAsync(root.LogicalName, recordId, ct);
            preCreatorMask = MaskOf(preShares, creatorId);
        }
        catch (Exception ex)
        {
            preCreatorMask = null;
            logger.LogWarning(ex,
                "[PROVISION] The shares on {RecordType} {RecordId} could not be read completely before any change. " +
                "Share-first is not used for this call: the creator's share is issued after the owner move, and if " +
                "the move is undone the creator's explicit share is removed entirely — including any share they held " +
                "before the call, which cannot be told apart. TraceId={TraceId}",
                root.WireToken, recordId, traceId);
        }

        var wroteCreatorShare = false;   // this call wrote, or tried to write, the creator's share
        var creatorShareProven = false;  // a read has shown the creator holding exactly CreatorAccessMask

        // ── Step 4.5: SHARE-FIRST ────────────────────────────────────────────
        if (preCreatorMask is { } knownPreMask)
        {
            var first = await EnsureCreatorShareAsync(recordShare, root, recordId, creatorId, logger, ct);
            wroteCreatorShare = first.WriteAttempted;
            creatorShareProven = first.Proven;

            if (!first.Proven)
            {
                var restored = !wroteCreatorShare
                               || await RestoreCreatorShareAsync(recordShare, root, recordId, creatorId, knownPreMask, logger, ct);

                if (restored && preOwner == DataversePrincipalRef.User(creatorId))
                {
                    // The creator still owns the record, so ownership gives them access until the move. Whether
                    // Dataverse accepts a share to a record's CURRENT owner is live gate (a); if it does not, share-first
                    // can never succeed for a creator-owned record. Carry on with the post-move grant, which
                    // compensation covers — nothing is left that this call added.
                    logger.LogWarning(
                        "[PROVISION] The pre-move share to creator {CreatorId}, who owns {RecordType} {RecordId}, was not " +
                        "confirmed. Falling back to the share after the owner move. TraceId={TraceId}",
                        creatorId, root.WireToken, recordId, traceId);
                    wroteCreatorShare = false;
                }
                else
                {
                    logger.LogError(
                        "[PROVISION] The creator's share on {RecordType} {RecordId} for user {CreatorId} could not be " +
                        "confirmed before the owner move. Stopped with ownership unchanged (share restored: {Restored}). " +
                        "TraceId={TraceId}", root.WireToken, recordId, creatorId, restored, traceId);

                    return CreatorShareStep.Failed(Problem(
                        StatusCodes.Status500InternalServerError, "Internal Server Error",
                        "The record could not be shared to its creator, so provisioning stopped BEFORE moving it. Its " +
                        "ownership was not changed. " + (restored
                            ? "The creator's share is as it was before the call (read back). "
                            : "A share this call may have issued to the creator could not be confirmed removed; it " +
                              "shows under Manage Access. ") +
                        "The same caller may retry.",
                        traceId, (ReasonKey, ReasonCreatorShareFailed),
                        ("ownershipRestored", true), ("sharesRestored", restored)));
                }
            }
        }

        // ── Step 5: the owner move, read back ────────────────────────────────
        //
        // Ownership is the SECURITY step and the container the storage step, so the move comes before the container:
        // a storage failure then leaves the record inside the Secure Record business unit, never outside it.
        var move = await MoveOwnerAsync(
            dataverseClient, root, recordId, DataversePrincipalRef.Team(ownerTeamId), logger, ct);

        if (move.Outcome == OwnerMoveOutcome.NotMoved)
        {
            // Read back and NOT moved: nothing moved, so undo the only other write — the share-first grant.
            var restored = !wroteCreatorShare
                           || await RestoreCreatorShareAsync(recordShare, root, recordId, creatorId, preCreatorMask ?? 0, logger, ct);

            var cause = move.PatchRefused
                ? "Dataverse refused the assignment to the Secure Record owner team. If this is a privilege error, " +
                  "the owner team lacks the entity privileges an assignment target must hold — see the Secure Record " +
                  "Owner role in docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md §5."
                : "Dataverse accepted the ownership assignment but the record is still not owned by the Secure Record " +
                  "owner team. This is the silent-navigation-property failure mode: an unrecognised @odata.bind " +
                  "property is accepted and ignored rather than rejected.";

            return CreatorShareStep.Failed(Problem(
                StatusCodes.Status500InternalServerError, "Internal Server Error",
                cause + " The owner was read back and is unchanged, so the record was not moved. " + (restored
                    ? "The creator's share is as it was before the call."
                    : "A share this call issued to the creator could not be confirmed removed; it shows under Manage Access."),
                traceId,
                (ReasonKey, move.PatchRefused ? ReasonOwnerAssignmentFailed : ReasonOwnerAssignmentNotApplied),
                ("ownerTeamId", ownerTeamId), ("sharesRestored", restored)));
        }

        if (move.Outcome == OwnerMoveOutcome.Unverified)
        {
            // The PATCH may have landed. Whatever happened, the creator's share must be in place (S5). It is PROVEN by
            // the same complete read every other share write on this path uses (task 133 verifier round 1) — a share
            // proven before the move is not assumed to have survived it (live gate (b)).
            var ensured = await EnsureCreatorShareAsync(recordShare, root, recordId, creatorId, logger, ct);
            var shareConfirmed = ensured.Proven;
            var shareIssued = ensured.Proven;

            if (!shareConfirmed)
            {
                // The read or the write failed. Issue the share without a read to confirm it: leaving no share risks a
                // record nobody can open if the move DID land. The response then says the share is NOT confirmed.
                try
                {
                    await recordShare.GrantAccessAsync(
                        root.EntitySet, recordId, DataversePrincipalRef.User(creatorId), CreatorAccessRights, ct);
                    shareIssued = true;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "[PROVISION] The creator's share on {RecordType} {RecordId} could not be issued after an " +
                        "unverifiable owner move. TraceId={TraceId}", root.WireToken, recordId, traceId);
                }

                // A share proven before the move was issued too, even if it cannot be confirmed now.
                shareIssued |= creatorShareProven;
            }

            if (shareIssued)
            {
                return CreatorShareStep.Failed(Problem(
                    StatusCodes.Status500InternalServerError, "Internal Server Error",
                    "The record's owner could not be read back after the assignment, so whether it is now owned by the " +
                    "Secure Record owner team is not known. " + (shareConfirmed
                        ? "The creator's share is in place (read back), so the creator can open the record either way, " +
                          "and the same caller may retry: a record the team now owns is resumed, and one it does not " +
                          "own is provisioned from the start."
                        : "A share to the creator was issued but could not be read back, so it is NOT confirmed. If the " +
                          "creator can open the record they may call again: a record the team now owns is resumed, and " +
                          "one it does not own is provisioned from the start. If they cannot, an administrator (who holds " +
                          "Write on it) calls provisioning again: it resumes and ensures the share for the record's " +
                          "creator (createdby)."),
                    traceId, (ReasonKey, ReasonOwnerAssignmentUnverified), ("ownerTeamId", ownerTeamId),
                    ("creatorShareConfirmed", shareConfirmed)));
            }

            logger.LogCritical(
                "[PROVISION] {RecordType} {RecordId}: the owner move could not be verified AND no share to creator " +
                "{CreatorId} could be issued. If the Secure Record owner team now owns it, NOBODY can open it. An " +
                "administrator must call provisioning again to resume. TraceId={TraceId}",
                root.WireToken, recordId, creatorId, traceId);

            return CreatorShareStep.Failed(Problem(
                StatusCodes.Status500InternalServerError, "Internal Server Error",
                "The record's owner could not be read back after the assignment, and no share to its creator could be " +
                "issued. If the record is now owned by the Secure Record owner team, nobody can open it, and its creator " +
                "no longer passes the Write check this endpoint requires. An administrator (who holds Write on it) " +
                "calls provisioning again: it resumes and shares the record to its creator (createdby).",
                traceId, (ReasonKey, ReasonCreatorShareFailedResumable), ("ownerTeamId", ownerTeamId)));
        }

        // ── Step 5.5: prove the creator's share on the moved record ───────────
        var proof = await EnsureCreatorShareAsync(recordShare, root, recordId, creatorId, logger, ct);
        if (proof.Proven)
            return CreatorShareStep.Ok(creatorId);

        wroteCreatorShare |= proof.WriteAttempted;

        // ── COMPENSATE: back to the pre-call owner, read back ────────────────
        logger.LogError(
            "[PROVISION] The creator's share on {RecordType} {RecordId} for user {CreatorId} could not be proven after " +
            "the owner move. Moving the record back to its pre-call owner {OwnerKind} {OwnerId}. TraceId={TraceId}",
            root.WireToken, recordId, creatorId, preOwner.Kind, preOwner.Id, traceId);

        var back = await MoveOwnerAsync(dataverseClient, root, recordId, preOwner, logger, ct);
        if (back.Outcome == OwnerMoveOutcome.Moved)
        {
            var restored = !wroteCreatorShare
                           || await RestoreCreatorShareAsync(recordShare, root, recordId, creatorId, preCreatorMask ?? 0, logger, ct);

            // What the response may claim about the creator's shares (task 133 verifier round 1). When the pre-call share
            // set could not be read, the only safe restore target is "no share" — which also removes any explicit share
            // the creator held BEFORE the call. That is a narrowing, never a widening, but it is not "as it was", so it
            // is reported as such: sharesRestored false, creatorShareRemoved true.
            string sharesText;
            var sharesRestored = restored;
            var creatorShareRemoved = false;
            if (!wroteCreatorShare)
            {
                sharesText = "This call wrote no share for the creator, so the creator's shares are as they were before " +
                             "the call. ";
            }
            else if (!restored)
            {
                sharesText = "A share this call issued to the creator could not be confirmed removed; it shows under " +
                             "Manage Access. ";
            }
            else if (preCreatorMask is null)
            {
                sharesRestored = false;
                creatorShareRemoved = true;
                sharesText = "The record's shares could not be read before the call, so the creator's explicit share was " +
                             "removed entirely (read back) — including any share the creator held before this call. If " +
                             "the creator had one and still needs it, an administrator re-adds it through Manage Access. ";
            }
            else
            {
                sharesText = "The creator's share is as it was before the call (read back). ";
            }

            return CreatorShareStep.Failed(Problem(
                StatusCodes.Status500InternalServerError, "Internal Server Error",
                "The record could not be shared to its creator once it was owned by the Secure Record owner team, so " +
                "the move was undone: its owner is back to the owner it had before the call (read back). " + sharesText +
                "The same caller may retry.",
                traceId, (ReasonKey, ReasonCreatorShareFailed),
                ("ownershipRestored", true), ("sharesRestored", sharesRestored),
                ("creatorShareRemoved", creatorShareRemoved)));
        }

        logger.LogCritical(
            "[PROVISION] {RecordType} {RecordId}: the creator's share could not be proven AND the record could not be " +
            "moved back to {OwnerKind} {OwnerId} (outcome {Outcome}). It is owned by the memberless Secure Record owner " +
            "team without a confirmed creator share — possibly NOBODY can open it. An administrator must call " +
            "provisioning again to resume. TraceId={TraceId}",
            root.WireToken, recordId, preOwner.Kind, preOwner.Id, back.Outcome, traceId);

        return CreatorShareStep.Failed(Problem(
            StatusCodes.Status500InternalServerError, "Internal Server Error",
            "The record could not be shared to its creator once it was owned by the Secure Record owner team, and the " +
            "move could not be undone. The record is owned by that memberless team without a confirmed creator share, " +
            "so its creator may not be able to open it and no longer passes the Write check this endpoint requires. An " +
            "administrator (who holds Write on it) calls provisioning again: it resumes and shares the record to its " +
            "creator (createdby).",
            traceId, (ReasonKey, ReasonCreatorShareFailedResumable),
            ("ownerTeamId", ownerTeamId), ("ownershipRestored", false)));
    }

    /// <summary>
    /// On RESUME, refuses a request that names colleagues unless its caller IS the record's creator — before any write
    /// (task 133 verifier round 1).
    /// </summary>
    /// <remarks>
    /// <para><b>Why.</b> On the forward path the caller becomes the creator and holds the creator's <c>ShareAccess</c>,
    /// so the colleagues they name are theirs to add. A resume is different: its caller is often an administrator, or
    /// any Write holder — including a Collaborate colleague after a container failure. Honouring their
    /// <c>sharePrincipalIds</c> would widen the record's explicit access list through the app identity as a side effect
    /// of recovery (owner decision F8 / C4), skipping the eligibility and grantor checks Manage Access applies — and a
    /// caller could name themselves. So only the creator may name colleagues while finishing a run; anyone else adds
    /// people through Manage Access.</para>
    ///
    /// <para><b>Refused, not ignored.</b> Silently dropping the list would answer 200 with
    /// <c>additionalPrincipalsShared: 0</c> to a caller who asked for shares — a success that hides what did not
    /// happen. The refusal names the reason and changes nothing; the same call without the list completes the
    /// resume. The record's creator named in the list is not a colleague (they receive the creator share anyway).</para>
    /// </remarks>
    private static async Task<IResult?> RefuseResumeColleaguesUnlessCreatorAsync(
        ProvisionProjectRequest request,
        CallerRecordAccessProbe callerAccessProbe,
        HttpContext httpContext,
        SecureRecordRoot root,
        Guid recordId,
        RootRow row,
        Guid ownerTeamId,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        var createdBy = row._createdby_value ?? Guid.Empty;
        var named = (request.SharePrincipalIds ?? Array.Empty<Guid>())
            .Where(id => id != Guid.Empty && id != createdBy)
            .Distinct()
            .ToList();

        if (named.Count == 0)
            return null;

        var caller = await callerAccessProbe.GetCallerSystemUserIdAsync(
            TokenHelper.ExtractBearerTokenOrNull(httpContext), ct);

        if (caller is { } callerId && callerId != Guid.Empty && callerId == createdBy)
            return null;

        logger.LogWarning(
            "[PROVISION] Resume of {RecordType} {RecordId} refused: the request names {Count} principal(s) to share " +
            "with, and its caller ({CallerId}) is not the record's creator (createdby {CreatorId}). Nothing was " +
            "changed. TraceId={TraceId}",
            root.WireToken, recordId, named.Count, caller, createdBy, traceId);

        return Problem(
            StatusCodes.Status403Forbidden, "Forbidden",
            $"{root.DisplayLabel} {recordId} is owned by the Secure Record owner team with no container recorded, so " +
            "this call would finish securing it. The request names people to share it with, but only the person who " +
            "created the record may do that while finishing it; anyone else adds people through Manage Access, which " +
            "applies its own checks. Nothing was changed. Calling again without sharePrincipalIds finishes securing " +
            "the record.",
            traceId, (ReasonKey, ReasonResumeColleaguesNotPermitted), ("ownerTeamId", ownerTeamId));
    }

    /// <summary>
    /// RESUME (task 133): the record is owned by the team with no container recorded, so an earlier run stopped after
    /// the move. Ensures the share for the record's <c>createdby</c> user — the persisted creator — and nobody else.
    /// </summary>
    /// <remarks>
    /// <para><b>Never the caller as a substitute</b> (owner decision F8). A resume is often run by an administrator,
    /// who reaches the record through their role; adding them to its explicit access list would be a C4 decision made
    /// as a recovery side effect. When the caller IS the creator, it is the same share.</para>
    ///
    /// <para><b><c>createdby</c> must be a person</b>: present, enabled, and not an application user. A row created
    /// app-only (Office quick-create) has the app as <c>createdby</c> (the interim default of F8 — a persisted human
    /// creator for app-created rows is an open owner decision).</para>
    ///
    /// <para><b>When <c>createdby</c> cannot be used</b> (task 133 verifier round 1). The resume shares to nobody. It
    /// completes ONLY if a person — an enabled, non-application systemuser — already holds a share carrying Read on the
    /// record: an administrator made that share deliberately (Manage Access), so the record keeps someone who can open
    /// it (S5) without provisioning choosing who. Otherwise it is refused with its own reason code. Before this, the
    /// refusal told the administrator to share and call again, and the second call was refused the same way forever —
    /// the stated recovery could not work.</para>
    ///
    /// <para>No Access (task 143) is not yet in this branch: when it lands, a <c>createdby</c> on the record's No Access
    /// list is refused here, before the share.</para>
    /// </remarks>
    private static async Task<CreatorShareStep> EnsureResumeCreatorShareAsync(
        DataverseWebApiClient dataverseClient,
        IDataverseRecordShareService recordShare,
        SecureRecordRoot root,
        Guid recordId,
        RootRow row,
        Guid ownerTeamId,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        logger.LogWarning(
            "[PROVISION] {RecordType} {RecordId} is owned by the Secure Record owner team {TeamId} with no container " +
            "recorded: an earlier run stopped after the owner move. RESUMING. TraceId={TraceId}",
            root.WireToken, recordId, ownerTeamId, traceId);

        string? unusable;
        var unreadable = false;
        var createdBy = row._createdby_value;

        if (createdBy is not { } creatorId || creatorId == Guid.Empty)
        {
            unusable = "absent";
            creatorId = Guid.Empty;
        }
        else
        {
            try
            {
                unusable = await UnusablePersonStateAsync(dataverseClient, creatorId, ct);
            }
            catch (Exception ex)
            {
                unusable = "unreadable";
                unreadable = true;
                logger.LogError(ex,
                    "[PROVISION] The creator (createdby {CreatorId}) of {RecordType} {RecordId} could not be read. " +
                    "TraceId={TraceId}", creatorId, root.WireToken, recordId, traceId);
            }
        }

        if (unusable is not null)
        {
            var holder = await FindPersonHoldingAShareAsync(
                dataverseClient, recordShare, root, recordId, logger, traceId, ct);

            if (holder.HolderId is { } holderId)
            {
                logger.LogWarning(
                    "[PROVISION] Resume of {RecordType} {RecordId}: its creator (createdby {CreatorId}) is " +
                    "{CreatorState}, and user {HolderId} already holds a share on it. Completing WITHOUT sharing to " +
                    "anyone. TraceId={TraceId}",
                    root.WireToken, recordId, createdBy, unusable, holderId, traceId);

                return CreatorShareStep.Ok(holderId, creatorUnavailable: true);
            }

            unreadable |= holder.Unreadable;

            logger.LogWarning(
                "[PROVISION] Resume of {RecordType} {RecordId} refused: its creator (createdby {CreatorId}) is " +
                "{CreatorState} and no person holds a share on it (shares unreadable: {SharesUnreadable}). It is not " +
                "shared to anyone instead. TraceId={TraceId}",
                root.WireToken, recordId, createdBy, unusable, holder.Unreadable, traceId);

            return CreatorShareStep.Failed(Problem(
                unreadable ? StatusCodes.Status500InternalServerError : StatusCodes.Status409Conflict,
                unreadable ? "Internal Server Error" : "Conflict",
                $"{root.DisplayLabel} {recordId} is owned by the Secure Record owner team with no container recorded, " +
                "so provisioning would resume and share it to the person who created it. That person cannot be used " +
                $"(their user record is {unusable}), and it is never shared to the caller instead. " + (holder.Unreadable
                    ? "Whether another person already holds a share on it could not be read. "
                    : "No other person holds a share on it. ") +
                "Nothing was changed. An administrator shares the record to the person who should hold it through " +
                "Manage Access — it stays owned by the Secure Record owner team — then calls provisioning again: once a " +
                "person holds a share, provisioning records the container without sharing the record to anyone." +
                (unreadable ? " A read failed, so calling again once Dataverse is reachable may also be enough." : ""),
                traceId, (ReasonKey, ReasonResumeCreatorUnavailable), ("creatorState", unusable),
                ("ownerTeamId", ownerTeamId)));
        }

        var ensured = await EnsureCreatorShareAsync(recordShare, root, recordId, creatorId, logger, ct);
        if (!ensured.Proven)
        {
            return CreatorShareStep.Failed(Problem(
                StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"This call was resuming {root.DisplayLabel.ToLowerInvariant()} {recordId}, which is owned by the " +
                "Secure Record owner team with no container recorded, and could not confirm the share to its creator " +
                "(createdby). This call did not change the record's ownership. The same caller may retry.",
                traceId, (ReasonKey, ReasonCreatorShareFailed), ("resumed", true), ("ownerTeamId", ownerTeamId)));
        }

        return CreatorShareStep.Ok(creatorId);
    }

    /// <summary>
    /// Why <paramref name="systemUserId"/> cannot be a person a secure record is kept open for — <c>absent</c>,
    /// <c>disabled</c> or <c>application-user</c> — or <c>null</c> when it can. Throws when the user cannot be read:
    /// an unreadable user is never "usable".
    /// </summary>
    private static async Task<string?> UnusablePersonStateAsync(
        DataverseWebApiClient dataverseClient, Guid systemUserId, CancellationToken ct)
    {
        var users = await dataverseClient.QueryAsync<CreatorRow>(
            "systemusers",
            filter: $"systemuserid eq {systemUserId}",
            select: "systemuserid,isdisabled,applicationid",
            top: 1,
            cancellationToken: ct);

        var user = users.FirstOrDefault();
        if (user is null)
            return "absent";
        if (user.isdisabled != false)
            return "disabled";
        if (user.applicationid is { } app && app != Guid.Empty)
            return "application-user";
        return null;
    }

    /// <summary>
    /// A person who already holds a share carrying Read on the record — an enabled, non-application systemuser — read
    /// with the complete-or-throw share read. <c>Unreadable</c> is true when the shares, or a sharee that might have
    /// qualified, could not be read: "could not tell" is never "nobody".
    /// </summary>
    private static async Task<(Guid? HolderId, bool Unreadable)> FindPersonHoldingAShareAsync(
        DataverseWebApiClient dataverseClient,
        IDataverseRecordShareService recordShare,
        SecureRecordRoot root,
        Guid recordId,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        IReadOnlyList<DataversePrincipalAccess> shares;
        try
        {
            shares = await recordShare.GetPrincipalAccessOrThrowAsync(root.LogicalName, recordId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PROVISION] The shares on {RecordType} {RecordId} could not be read completely while looking for a " +
                "person who holds one. TraceId={TraceId}", root.WireToken, recordId, traceId);
            return (null, true);
        }

        var unreadable = false;
        var candidates = shares
            .Where(s => s.Principal.Kind == DataversePrincipalKind.SystemUser && (s.AccessRightsMask & ReadMask) == ReadMask)
            .Select(s => s.Principal.Id)
            .Distinct()
            .OrderBy(id => id);

        foreach (var candidate in candidates)
        {
            try
            {
                if (await UnusablePersonStateAsync(dataverseClient, candidate, ct) is null)
                    return (candidate, false);
            }
            catch (Exception ex)
            {
                unreadable = true;
                logger.LogError(ex,
                    "[PROVISION] Sharee {UserId} of {RecordType} {RecordId} could not be read. TraceId={TraceId}",
                    candidate, root.WireToken, recordId, traceId);
            }
        }

        return (null, unreadable);
    }

    /// <summary>
    /// Makes the creator hold EXACTLY <see cref="CreatorAccessRights"/> on the record, and proves it by reading the
    /// shares back. Idempotent: a share already holding exactly those rights is not written again.
    /// </summary>
    /// <remarks>
    /// A creator with no share gets GrantAccess; one whose share holds other rights gets ModifyAccess, because
    /// GrantAccess is not documented to set the rights of an existing share (task 063). Every read is the
    /// complete-or-throw read: "the read failed" is never "the share is there".
    /// </remarks>
    private static async Task<ShareEnsureResult> EnsureCreatorShareAsync(
        IDataverseRecordShareService recordShare,
        SecureRecordRoot root,
        Guid recordId,
        Guid creatorId,
        ILogger logger,
        CancellationToken ct)
    {
        var principal = DataversePrincipalRef.User(creatorId);

        int current;
        try
        {
            current = MaskOf(await recordShare.GetPrincipalAccessOrThrowAsync(root.LogicalName, recordId, ct), creatorId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PROVISION] The shares on {RecordType} {RecordId} could not be read completely, so the creator's " +
                "share cannot be confirmed.", root.WireToken, recordId);
            return new ShareEnsureResult(Proven: false, WriteAttempted: false);
        }

        if (current == CreatorAccessMask)
            return new ShareEnsureResult(Proven: true, WriteAttempted: false);

        try
        {
            if (current == 0)
                await recordShare.GrantAccessAsync(root.EntitySet, recordId, principal, CreatorAccessRights, ct);
            else
                await recordShare.ModifyAccessAsync(root.EntitySet, recordId, principal, CreatorAccessRights, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PROVISION] The creator's share on {RecordType} {RecordId} for user {CreatorId} could not be written.",
                root.WireToken, recordId, creatorId);
            return new ShareEnsureResult(Proven: false, WriteAttempted: true);
        }

        try
        {
            var after = MaskOf(await recordShare.GetPrincipalAccessOrThrowAsync(root.LogicalName, recordId, ct), creatorId);
            if (after == CreatorAccessMask)
                return new ShareEnsureResult(Proven: true, WriteAttempted: true);

            logger.LogError(
                "[PROVISION] The creator's share on {RecordType} {RecordId} reads back as mask {Mask}, not {Expected}.",
                root.WireToken, recordId, after, CreatorAccessMask);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PROVISION] The creator's share on {RecordType} {RecordId} could not be read back.",
                root.WireToken, recordId);
        }

        return new ShareEnsureResult(Proven: false, WriteAttempted: true);
    }

    /// <summary>
    /// Puts the creator's share back to <paramref name="targetMask"/> (0 = no share) and proves it by reading back.
    /// Returns false when that cannot be proven — the caller then says so rather than claiming a clean undo.
    /// </summary>
    private static async Task<bool> RestoreCreatorShareAsync(
        IDataverseRecordShareService recordShare,
        SecureRecordRoot root,
        Guid recordId,
        Guid creatorId,
        int targetMask,
        ILogger logger,
        CancellationToken ct)
    {
        var principal = DataversePrincipalRef.User(creatorId);
        try
        {
            var current = MaskOf(await recordShare.GetPrincipalAccessOrThrowAsync(root.LogicalName, recordId, ct), creatorId);
            if (current == targetMask)
                return true;

            if (targetMask == 0)
                await recordShare.RevokeAccessAsync(root.EntitySet, recordId, principal, ct);
            else if (current == 0)
                await recordShare.GrantAccessAsync(root.EntitySet, recordId, principal, RecordShareLevels.RightsCsvForMask(targetMask), ct);
            else
                await recordShare.ModifyAccessAsync(root.EntitySet, recordId, principal, RecordShareLevels.RightsCsvForMask(targetMask), ct);

            var after = MaskOf(await recordShare.GetPrincipalAccessOrThrowAsync(root.LogicalName, recordId, ct), creatorId);
            if (after == targetMask)
                return true;

            logger.LogError(
                "[PROVISION] Restoring the creator's share on {RecordType} {RecordId}: reads back as mask {Mask}, not " +
                "{Expected}.", root.WireToken, recordId, after, targetMask);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PROVISION] The creator's share on {RecordType} {RecordId} could not be restored to mask {Expected}.",
                root.WireToken, recordId, targetMask);
            return false;
        }
    }

    /// <summary>The rights mask <paramref name="systemUserId"/>'s own share carries (0 = no share).</summary>
    private static int MaskOf(IReadOnlyList<DataversePrincipalAccess> shares, Guid systemUserId) =>
        shares
            .Where(s => s.Principal == DataversePrincipalRef.User(systemUserId))
            .Aggregate(0, (mask, s) => mask | s.AccessRightsMask);

    /// <summary>
    /// Assigns the record's owner to <paramref name="target"/>, then reads the owner back. Used for the move to the
    /// team AND for the compensating move back.
    /// </summary>
    /// <remarks>
    /// <para><b>The read-back is the point, not belt-and-braces.</b> <c>ownerid</c> is written through
    /// <c>@odata.bind</c>, and Dataverse's behaviour on an unrecognised <c>@odata.bind</c> property is to accept the
    /// request and ignore the property — no error, no write. So the outcome is what the read shows: moved, not moved,
    /// or (the read failed) unverified. The read runs even when the PATCH threw, because a PATCH that reports failure
    /// can still have committed, and only the read tells the caller which state to describe.</para>
    ///
    /// <para>Ownership is assigned on its own, not folded into another PATCH: Dataverse treats an owner change as a
    /// distinct operation from a field update, and combining them is a documented way to have one of the two quietly
    /// not happen.</para>
    /// </remarks>
    private static async Task<OwnerMove> MoveOwnerAsync(
        DataverseWebApiClient dataverseClient,
        SecureRecordRoot root,
        Guid recordId,
        DataversePrincipalRef target,
        ILogger logger,
        CancellationToken ct)
    {
        var patchRefused = false;
        try
        {
            await dataverseClient.UpdateAsync(
                root.EntitySet,
                recordId,
                new Dictionary<string, object?>
                {
                    ["ownerid@odata.bind"] = $"/{target.Kind.ToEntitySet()}({target.Id})"
                },
                ct);
        }
        catch (Exception ex)
        {
            patchRefused = true;
            logger.LogError(ex,
                "[PROVISION] Dataverse refused the ownership assignment of {RecordType} {RecordId} to {OwnerKind} " +
                "{OwnerId}. If this is a privilege error, the owner team lacks the entity rights an assignment target " +
                "must hold (Secure Record Owner role, setup guide §5).", root.WireToken, recordId, target.Kind, target.Id);
        }

        try
        {
            var rows = await dataverseClient.QueryAsync<RootRow>(
                root.EntitySet,
                filter: $"{root.IdColumn} eq {recordId}",
                select: $"{root.IdColumn},_owningteam_value,_owninguser_value",
                top: 1,
                cancellationToken: ct);

            var reread = rows.FirstOrDefault();
            if (reread?.Owner == target)
            {
                logger.LogInformation(
                    "[PROVISION] {RecordType} {RecordId} is now owned by {OwnerKind} {OwnerId} (verified by read-back)",
                    root.WireToken, recordId, target.Kind, target.Id);
                return new OwnerMove(OwnerMoveOutcome.Moved, patchRefused);
            }

            logger.LogError(
                "[PROVISION] Ownership read-back for {RecordType} {RecordId}: expected {OwnerKind} {OwnerId}, found " +
                "team {ActualTeamId} / user {ActualUserId}. The record was NOT moved.",
                root.WireToken, recordId, target.Kind, target.Id, reread?._owningteam_value, reread?._owninguser_value);
            return new OwnerMove(OwnerMoveOutcome.NotMoved, patchRefused);
        }
        catch (Exception ex)
        {
            // An unverifiable assignment is never reported as done, and never as "nothing moved" either.
            logger.LogError(ex,
                "[PROVISION] Could not read back the owner of {RecordType} {RecordId} after assigning it to " +
                "{OwnerKind} {OwnerId}. The outcome is UNVERIFIED.", root.WireToken, recordId, target.Kind, target.Id);
            return new OwnerMove(OwnerMoveOutcome.Unverified, patchRefused);
        }
    }

    /// <summary>
    /// Shares the record with the request's named colleagues — best-effort (task 061), and only once the creator's
    /// share is proven, so no colleague outcome changes the result of any branch.
    /// </summary>
    /// <remarks>
    /// A colleague who already holds a share (a resumed run that got this far before) is not shared to again. If the
    /// shares cannot be read, every colleague is shared to, as before task 133 — their absence is visible and fixable
    /// through the FR-29 "+ User" path, the creator's is not.
    /// </remarks>
    private static async Task<int> ShareToColleaguesAsync(
        IDataverseRecordShareService recordShare,
        ProvisionProjectRequest request,
        SecureRecordRoot root,
        Guid recordId,
        Guid creatorId,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        var colleagues = (request.SharePrincipalIds ?? Array.Empty<Guid>())
            .Where(id => id != Guid.Empty && id != creatorId)
            .Distinct()
            .ToList();

        if (colleagues.Count == 0)
            return 0;

        IReadOnlyList<DataversePrincipalAccess> existing;
        try
        {
            existing = await recordShare.GetPrincipalAccessOrThrowAsync(root.LogicalName, recordId, ct);
        }
        catch (Exception)
        {
            existing = Array.Empty<DataversePrincipalAccess>();
        }

        var shared = 0;
        foreach (var principalId in colleagues)
        {
            if (MaskOf(existing, principalId) != 0)
            {
                shared++;
                continue;
            }

            try
            {
                await recordShare.GrantAccessAsync(
                    root.EntitySet, recordId, DataversePrincipalRef.User(principalId), CollaboratorAccessRights, ct);
                shared++;
            }
            catch (Exception ex)
            {
                // Logged with the id so an operator can see exactly who was missed.
                logger.LogWarning(ex,
                    "[PROVISION] Could not share {RecordType} {RecordId} with named principal {PrincipalId}. " +
                    "Provisioning continues; add them via the Manage Access surface. TraceId={TraceId}",
                    root.WireToken, recordId, principalId, traceId);
            }
        }

        logger.LogInformation(
            "[PROVISION] {RecordType} {RecordId} shared to creator {CreatorId} and {Count} named principal(s).",
            root.WireToken, recordId, creatorId, shared);

        return shared;
    }

    /// <summary>
    /// Creates the SPE container via the SpeFileStore facade (ADR-007).
    /// </summary>
    /// <remarks>
    /// There is no rollback of the ownership assignment if this fails. That is deliberate: ownership
    /// inside the Secure Record business unit is the safer state to be left in, so undoing it on a
    /// container failure would move the record back OUT of the secure business unit — turning a
    /// storage failure into a disclosure. The record is left secured and shared to its creator with no container
    /// recorded, which the next call resumes (task 133).
    /// </remarks>
    private static async Task<SpeContainerCreationResult> CreateSpeContainerAsync(
        SpeFileStore speFileStore,
        Guid containerTypeId,
        SecureRecordRoot root,
        string recordName,
        Guid recordId,
        Guid ownerTeamId,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        logger.LogInformation("[PROVISION] Creating SPE container for {RecordType} {RecordId}", root.WireToken, recordId);

        string? failure;
        try
        {
            var containerDisplayName = root.ContainerDisplayName(recordName);
            var containerDescription = root.ContainerDescription(recordName);

            var container = await speFileStore.CreateContainerAsync(
                containerTypeId, containerDisplayName, containerDescription, ct);

            if (container != null)
            {
                logger.LogInformation(
                    "[PROVISION] Created SPE container {ContainerId} ('{DisplayName}') for {RecordType} {RecordId}",
                    container.Id, containerDisplayName, root.WireToken, recordId);

                return new SpeContainerCreationResult(container.Id, null);
            }

            failure = "Graph API returned no container";
            logger.LogError(
                "[PROVISION] SpeFileStore.CreateContainerAsync returned null for {RecordType} {RecordId}",
                root.WireToken, recordId);
        }
        catch (Exception ex)
        {
            failure = "the container request failed";
            logger.LogError(ex,
                "[PROVISION] Failed to create SPE container for {RecordType} {RecordId}", root.WireToken, recordId);
        }

        return new SpeContainerCreationResult(null, Problem(
            StatusCodes.Status500InternalServerError, "Internal Server Error",
            $"The {root.DisplayLabel.ToLowerInvariant()} is owned by the Secure Record owner team and shared to its " +
            $"creator, but its own SPE container could not be created ({failure}). Nothing needs undoing: calling " +
            "provisioning again (the same caller may) resumes from here.",
            traceId, (ReasonKey, ReasonContainerCreationFailed), ("ownerTeamId", ownerTeamId)));
    }

    /// <summary>
    /// Records the provisioned container on the record. Throws on failure — the caller turns that
    /// into a non-2xx carrying the container id (ADR-003).
    /// </summary>
    /// <remarks>
    /// <para><c>sprk_containerid</c> is <c>NVARCHAR(100)</c> on all three roots (live metadata) — a PLAIN STRING
    /// write. No <c>@odata.bind</c>, no navigation property, nothing case-sensitive.</para>
    ///
    /// <para>This is the ONLY writer of <c>sprk_containerid</c> on a provisionable root (task 076; see
    /// <see cref="RootRow.IsProvisioned"/>). Overwriting a pre-existing value is intentional: a record reaching this
    /// step is not yet provisioned, so a value already here was not written by a completed provisioning of the
    /// current topology — a business-unit container stamped by a client before task 076 removed that write, or the
    /// container of an earlier provisioning whose record an administrator later reassigned out of the Secure Record
    /// business unit. The old value is logged so a genuinely orphaned container remains traceable.</para>
    /// </remarks>
    private static async Task RecordContainerAsync(
        DataverseWebApiClient dataverseClient,
        SecureRecordRoot root,
        Guid recordId,
        string speContainerId,
        string? previousContainerId,
        ILogger logger,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(previousContainerId))
        {
            logger.LogWarning(
                "[PROVISION] Overwriting sprk_containerid on {RecordType} {RecordId}: '{Previous}' → '{New}'. The " +
                "previous value was not written by a completed provisioning of this record under the named owner " +
                "team; it is logged so that container stays traceable.",
                root.WireToken, recordId, previousContainerId, speContainerId);
        }

        await dataverseClient.UpdateAsync(
            root.EntitySet,
            recordId,
            new Dictionary<string, object?>
            {
                ["sprk_containerid"] = speContainerId
            },
            ct);

        logger.LogInformation(
            "[PROVISION] Recorded container {ContainerId} on {RecordType} {RecordId}",
            speContainerId, root.WireToken, recordId);
    }

    /// <summary>Builds a ProblemDetails result with a traceId and any extra extension members.</summary>
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

    // =========================================================================
    // Private types
    // =========================================================================

    /// <summary>What a read-back of the owner showed after an assignment.</summary>
    private enum OwnerMoveOutcome
    {
        /// <summary>The owner reads back as the target.</summary>
        Moved,

        /// <summary>The owner reads back as something else: the assignment did not take effect.</summary>
        NotMoved,

        /// <summary>The owner could not be read back: the assignment may or may not have taken effect.</summary>
        Unverified
    }

    /// <summary>An owner assignment's observed outcome, and whether the PATCH itself reported failure.</summary>
    private readonly record struct OwnerMove(OwnerMoveOutcome Outcome, bool PatchRefused);

    /// <summary>
    /// Whether a read proved the creator's share exact, and whether this call wrote (or tried to write) it — the
    /// second decides whether an undo has anything to undo.
    /// </summary>
    private readonly record struct ShareEnsureResult(bool Proven, bool WriteAttempted);

    /// <summary>
    /// The creator whose share is proven, or the response that stopped provisioning. <c>CreatorUnavailable</c>: a resume
    /// whose <c>createdby</c> could not be used completed because <c>CreatorId</c> — another person — already held a
    /// share; this call issued none.
    /// </summary>
    private sealed record CreatorShareStep(Guid CreatorId, IResult? Error, bool CreatorUnavailable = false)
    {
        public static CreatorShareStep Ok(Guid creatorId, bool creatorUnavailable = false) =>
            new(creatorId, null, creatorUnavailable);

        public static CreatorShareStep Failed(IResult error) => new(Guid.Empty, error);
    }

    /// <summary>Internal result wrapper for SPE container creation with optional error result.</summary>
    private sealed record SpeContainerCreationResult(string? ContainerId, IResult? Error);

    // ── Dataverse row DTOs ────────────────────────────────────────────────

    /// <summary>The <c>systemuser</c> columns a resume needs to decide whether <c>createdby</c> is a usable person.</summary>
    private sealed class CreatorRow
    {
        [JsonPropertyName("systemuserid")]
        public Guid systemuserid { get; set; }

        [JsonPropertyName("isdisabled")]
        public bool? isdisabled { get; set; }

        [JsonPropertyName("applicationid")]
        public Guid? applicationid { get; set; }
    }

    /// <summary>
    /// The columns Step 1 reads, common to all three roots. The id and name columns differ per table, so the name
    /// is read from <see cref="Extra"/> by the column <see cref="SecureRecordRoot.NameColumn"/> names.
    /// </summary>
    private sealed class RootRow
    {
        [JsonPropertyName("sprk_issecure")]
        public bool? sprk_issecure { get; set; }

        /// <summary>
        /// The container recorded on the record. Half of the marker — see <see cref="IsProvisioned"/>; never a marker
        /// on its own.
        /// </summary>
        [JsonPropertyName("sprk_containerid")]
        public string? sprk_containerid { get; set; }

        /// <summary>
        /// A per-record security business unit stamped by the RETIRED BU-per-project mechanism.
        /// </summary>
        [JsonPropertyName("_sprk_securitybu_value")]
        public Guid? _sprk_securitybu_value { get; set; }

        /// <summary>The team that owns this record, if it is team-owned.</summary>
        [JsonPropertyName("_owningteam_value")]
        public Guid? _owningteam_value { get; set; }

        /// <summary>The user that owns this record, if it is user-owned (task 133: what compensation restores).</summary>
        [JsonPropertyName("_owninguser_value")]
        public Guid? _owninguser_value { get; set; }

        /// <summary>The business unit the record's owner places it in.</summary>
        [JsonPropertyName("_owningbusinessunit_value")]
        public Guid? _owningbusinessunit_value { get; set; }

        /// <summary>
        /// Who created the record — stamped by Dataverse, not writable by a client. The person a RESUME shares to
        /// (task 133; owner decision F8).
        /// </summary>
        [JsonPropertyName("_createdby_value")]
        public Guid? _createdby_value { get; set; }

        /// <summary>Every other column the read returned — the table-specific id and name.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }

        /// <summary>The record's display name, read from the table's own name column.</summary>
        public string? NameFrom(string nameColumn) =>
            Extra is not null
            && Extra.TryGetValue(nameColumn, out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()
                : null;

        /// <summary>The record's owner as a principal: its owning user, else its owning team, else null.</summary>
        public DataversePrincipalRef? Owner =>
            _owninguser_value is { } user && user != Guid.Empty ? DataversePrincipalRef.User(user)
            : _owningteam_value is { } team && team != Guid.Empty ? DataversePrincipalRef.Team(team)
            : null;

        /// <summary>True when this record still references a retired per-record security BU.</summary>
        public bool HasLegacyPerProjectBusinessUnit =>
            _sprk_securitybu_value is { } bu && bu != Guid.Empty;

        /// <summary>Whether this record is owned by <paramref name="ownerTeamId"/> — provisioning reached Step 5.</summary>
        public bool IsOwnedBy(Guid ownerTeamId) =>
            _owningteam_value is { } team && team == ownerTeamId;

        /// <summary>
        /// The idempotency marker: owned by the named owner team AND a container recorded — provisioning reached
        /// Step 7. Owned by the team with NO container means an earlier run stopped after the move, and is resumed.
        /// </summary>
        /// <remarks>
        /// <para><b>Why both halves, and why the container half is now sound.</b> On 2026-08-23 a guard keyed on
        /// <c>sprk_containerid</c> ALONE. But the Create Project wizard then stamped that field on every new project from
        /// the creating user's business unit, so every secure project answered 409 "already provisioned" and none was
        /// ever provisioned — a guard against double-provisioning had become a guard against provisioning. Ownership
        /// replaced it: owned by the named team is state only this endpoint (and the one-time migration script) writes.
        /// Ownership alone, though, cannot tell a finished run from one that stopped after the move, and refusing the
        /// second locked its creator out (C11). Task 076 then made Step 7 the ONLY writer of <c>sprk_containerid</c> on
        /// these tables (<c>EntityCreationService</c> deleted the client stamp; <c>RecordCreationService</c> never writes
        /// it; live 2026-10-01: no field-mapping rule targets it). So on a team-owned record a recorded container means
        /// Step 7 ran — and a container Step 6 created without Step 7 is unrecorded and could never receive content
        /// (uploads to a secure record with no container of its own are refused), so a resume that creates a fresh one
        /// orphans at most an empty container its failed run already named.</para>
        ///
        /// <para>Residual edge, stated rather than hidden: if an administrator deliberately reassigns a provisioned
        /// secure record away from the owner team AND out of the Secure Record business unit, a later provisioning run
        /// would see it as unprovisioned and create a second container. <see cref="RecordContainerAsync"/> logs the
        /// displaced container id so the first one stays traceable.</para>
        /// </remarks>
        public bool IsProvisioned(Guid ownerTeamId) =>
            IsOwnedBy(ownerTeamId) && !string.IsNullOrWhiteSpace(sprk_containerid);

        /// <summary>
        /// Owned INSIDE the Secure Record BU, but by a team other than the named one (task 144) — the retired
        /// default team, before the migration. Refused like an already-provisioned record so a retry cannot create a
        /// second container.
        /// </summary>
        public bool IsOwnedInBusinessUnitByAnotherTeam(Guid secureBusinessUnitId, Guid ownerTeamId) =>
            _owningbusinessunit_value is { } bu && bu == secureBusinessUnitId && !IsOwnedBy(ownerTeamId);
    }
}
