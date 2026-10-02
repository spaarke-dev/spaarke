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
/// Provisioning sequence:
///   1. Confirm the record exists and carries <c>sprk_issecure = true</c>
///   2. Resolve the ONE canonical Secure Record business unit, BY NAME, from configuration
///   3. Resolve that BU's NAMED, non-default owner team (<c>SecureRecord:OwnerTeamName</c>), and prove it has ZERO
///      members and that ZERO systemusers sit in the BU — before any mutation (<see cref="SecureRecordOwnerTeam"/>)
///   4. Refuse if the record is already provisioned (see <see cref="RootRow"/>)
///   5. Assign the record's owner to that team, and verify the assignment took effect
///   5.5 Share the record explicitly to its creator (and any named colleagues) — the ONLY way a
///       human can reach it, since the owner team has no members (task 061)
///   6. Create the record's own SPE container
///   7. Record the container on the record — and FAIL if that record cannot be written
///
/// There is deliberately no rollback path. Nothing destructive is created: the BU and the owner team
/// both pre-exist, and the only artifact this endpoint creates is the SPE container, which a failed
/// run reports in its error body so an operator can reconcile it (ADR-003).
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
/// wizard writes <c>sprk_containerid</c> at CREATE time from the creating user's BU, so every secure
/// project 409'd as "already provisioned" and none was ever provisioned.</para>
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

    // ── Share rights (task 061) ──────────────────────────────────────────────

    /// <summary>
    /// Rights the creating user receives on their own secure record: the Collaborate level of
    /// <see cref="RecordShareLevels"/> — Read, Write, Append, AppendTo and Share.
    /// </summary>
    /// <remarks>
    /// Read/Write/Append/AppendTo is "can actually work the matter"; <c>ShareAccess</c> lets them bring colleagues
    /// in, through the FR-29 "+ User" surface or the model-driven app's own Share command, without an
    /// administrator. Delete and Assign are deliberately absent — a secure record leaves the secure business unit
    /// only through the explicit unsecure path, not by being reassigned out of it. Before task 139 this constant was
    /// spelled <c>CollaborateRights + ",ShareAccess"</c>; since the owner's 2026-09-30 rule put Share INTO
    /// Collaborate it is the level itself, and the value is unchanged (mask 262167).
    /// </remarks>
    internal const string CreatorAccessRights = RecordShareLevels.CollaborateRights;

    /// <summary>
    /// Rights a named colleague receives at provisioning time: EXACTLY the creator's rights (owner, 2026-09-30, C4).
    /// </summary>
    /// <remarks>
    /// <para>The owner's rule: a person with Write may share (OOB) and use Manage Access; only a View holder may not
    /// pass access on. A colleague named at provisioning is a Collaborate holder, so they carry <c>ShareAccess</c>
    /// like the creator. This supersedes the 2026-09-15 rationale that kept re-sharing with the creator alone; every
    /// grant they make is still capped at their own level (Dataverse's native sharing rule in the model-driven app,
    /// the grantor ceiling on every Spaarke grant route).</para>
    /// <para>The Collaborate level of <see cref="RecordShareLevels"/> (task 063), the one level-to-rights table — so
    /// a colleague shared at provisioning and one shared later at Collaborate through the "+ User" picker hold the
    /// same rights, and the two cannot drift apart. Kept as its own name, beside <see cref="CreatorAccessRights"/>,
    /// because the two shares are written by different steps and a test pins each against the literal.</para>
    /// </remarks>
    internal const string CollaboratorAccessRights = RecordShareLevels.CollaborateRights;

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

        // ── Step 4: Refuse to provision an already-provisioned record ────────
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

        if (row.IsOwnedBy(ownerTeamId))
        {
            // Two distinguishable states, and the operator needs to know which one they are in:
            //   - container recorded  → provisioning completed; there is nothing to do
            //   - no container        → ownership was claimed but a later step failed
            var hasContainer = !string.IsNullOrWhiteSpace(row.sprk_containerid);

            logger.LogWarning(
                "[PROVISION] {RecordType} {RecordId} is already owned by the Secure Record owner team " +
                "{TeamId} (container recorded: {HasContainer}). Refusing to re-provision. " +
                "TraceId={TraceId}",
                root.WireToken, recordId, ownerTeamId, hasContainer, traceId);

            return Problem(StatusCodes.Status409Conflict, "Conflict",
                hasContainer
                    ? $"{root.DisplayLabel} {recordId} has already been provisioned. Re-provisioning " +
                      "would create a second SPE container and repoint the record at it, orphaning " +
                      "the documents already stored."
                    : $"{root.DisplayLabel} {recordId} is already owned by the Secure Record owner team " +
                      "but has no SPE container recorded, so an earlier run claimed it and then " +
                      "failed. Reassign the record's owner and retry, or record the container " +
                      "manually if one was created — the failed run's response named it.",
                traceId,
                (ReasonKey, ReasonAlreadyProvisioned),
                ("businessUnitId", secureBuId),
                ("ownerTeamId", ownerTeamId),
                ("speContainerId", row.sprk_containerid));
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

        // ── Step 5: Assign ownership to the Secure Record owner team ────────
        //
        // ORDER MATTERS, and this step is deliberately FIRST of the two mutations.
        //
        // Ownership is the SECURITY step; the container is the storage step. If the container step
        // fails after this, the record is at least correctly owned inside the Secure Record BU. If
        // the order were reversed, the same failure would leave a secure record owned by its creating
        // user in an Operations business unit — strictly the worse posture of the two.
        //
        // It is also what makes the idempotency marker sound: ownership by this team is state only
        // provisioning ever writes (see RootRow).
        var assignment = await AssignOwnerToSecureTeamAsync(
            dataverseClient, root, recordId, ownerTeamId, logger, ct);

        if (assignment != OwnerAssignmentOutcome.Assigned)
        {
            var (reason, detail) = assignment switch
            {
                OwnerAssignmentOutcome.NotApplied => (
                    ReasonOwnerAssignmentNotApplied,
                    "Dataverse accepted the ownership assignment but the record is still not owned " +
                    "by the Secure Record owner team. This is the silent-navigation-property failure " +
                    "mode: an unrecognised @odata.bind property is accepted and ignored rather than " +
                    "rejected. Nothing has been provisioned."),
                _ => (
                    ReasonOwnerAssignmentFailed,
                    "Failed to assign the record to the Secure Record owner team. If Dataverse " +
                    "refused the assignment, the owner team most likely lacks the entity privileges " +
                    "an assignment target must hold — see the Secure Record Owner role in " +
                    "docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md §5. Nothing has been provisioned.")
            };

            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error", detail,
                traceId, (ReasonKey, reason), ("ownerTeamId", ownerTeamId));
        }

        // ── Step 5.5: Share the record to its creator (and any named colleagues) ──
        //
        // Ordering is deliberate: this runs AFTER ownership is verified and BEFORE the SPE container
        // is created. Ownership must land first or the share would be issued on a record still in the
        // caller's own business unit; and running before container creation means a share failure
        // leaves NOTHING orphaned to reconcile.
        var shareOutcome = await ShareToCreatorAndPrincipalsAsync(
            recordShare, callerAccessProbe, httpContext, request, root, recordId, logger, traceId, ct);

        if (shareOutcome.Error != null)
            return shareOutcome.Error;

        // ── Step 6: Create the record's own SPE container ────────────────────
        var containerResult = await CreateSpeContainerAsync(
            speFileStore, configuration, root, recordName, recordId, logger, traceId, ct);

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
                "not be recorded on the record, so the record does not yet point at it. The " +
                "container id is included here: record it on sprk_containerid, or delete the " +
                "container, before retrying.",
                traceId,
                (ReasonKey, ReasonContainerNotRecorded),
                ("speContainerId", speContainerId),
                ("ownerTeamId", ownerTeamId));
        }

        logger.LogInformation(
            "[PROVISION] Provisioning complete for {RecordType} {RecordId}: BU={BuId} ({BuName}), " +
            "OwnerTeam={TeamId}, Container={ContainerId}",
            root.WireToken, recordId, secureBuId, secureBuName, ownerTeamId, speContainerId);

        return TypedResults.Ok(new ProvisionProjectResponse(
            BusinessUnitId: secureBuId,
            BusinessUnitName: secureBuName,
            OwnerTeamId: ownerTeamId,
            OwnerTeamName: ownerTeamName,
            SpeContainerId: speContainerId,
            SharedToCreatorSystemUserId: shareOutcome.CreatorSystemUserId!.Value,
            AdditionalPrincipalsShared: shareOutcome.AdditionalPrincipalsShared,
            RecordType: root.WireToken,
            RecordId: recordId));
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
    /// Assigns the record's owner to <paramref name="ownerTeamId"/>, then reads the owner back to
    /// confirm the assignment actually took effect.
    /// </summary>
    /// <remarks>
    /// <para><b>The read-back is the point, not belt-and-braces.</b> <c>ownerid</c> is written through
    /// <c>@odata.bind</c>, and Dataverse's behaviour on an unrecognised <c>@odata.bind</c> property is
    /// to accept the request and ignore the property — no error, no write. Reading the value back converts an
    /// unverifiable assumption into an observed fact.</para>
    /// </remarks>
    private static async Task<OwnerAssignmentOutcome> AssignOwnerToSecureTeamAsync(
        DataverseWebApiClient dataverseClient,
        SecureRecordRoot root,
        Guid recordId,
        Guid ownerTeamId,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            // Ownership is assigned on its own, not folded into another PATCH. Dataverse treats an
            // owner change as a distinct operation from a field update, and combining them is a
            // documented way to have one of the two quietly not happen.
            await dataverseClient.UpdateAsync(
                root.EntitySet,
                recordId,
                new Dictionary<string, object?>
                {
                    ["ownerid@odata.bind"] = $"/teams({ownerTeamId})"
                },
                ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PROVISION] Dataverse refused the ownership assignment of {RecordType} {RecordId} to " +
                "team {TeamId}. If this is a privilege error, the owner team lacks the entity rights " +
                "an assignment target must hold (Secure Record Owner role, setup guide §5).",
                root.WireToken, recordId, ownerTeamId);
            return OwnerAssignmentOutcome.Failed;
        }

        try
        {
            var rows = await dataverseClient.QueryAsync<RootRow>(
                root.EntitySet,
                filter: $"{root.IdColumn} eq {recordId}",
                select: $"{root.IdColumn},_owningteam_value",
                top: 1,
                cancellationToken: ct);

            var reread = rows.FirstOrDefault();
            if (reread is null || !reread.IsOwnedBy(ownerTeamId))
            {
                logger.LogError(
                    "[PROVISION] Ownership read-back FAILED for {RecordType} {RecordId}: expected owning " +
                    "team {TeamId}, found {ActualTeamId}. The PATCH was accepted, so the owner " +
                    "navigation property was almost certainly ignored rather than applied.",
                    root.WireToken, recordId, ownerTeamId, reread?._owningteam_value);
                return OwnerAssignmentOutcome.NotApplied;
            }
        }
        catch (Exception ex)
        {
            // An unverifiable assignment is treated as a failed one. Reporting success here would
            // reintroduce exactly the "assume the write happened" defect this method exists to close.
            logger.LogError(ex,
                "[PROVISION] Could not read back the owner of {RecordType} {RecordId} to verify the " +
                "assignment. Treating the assignment as unverified, therefore failed.", root.WireToken, recordId);
            return OwnerAssignmentOutcome.Failed;
        }

        logger.LogInformation(
            "[PROVISION] {RecordType} {RecordId} is now owned by the Secure Record owner team {TeamId} " +
            "(verified by read-back)", root.WireToken, recordId, ownerTeamId);

        return OwnerAssignmentOutcome.Assigned;
    }

    /// <summary>
    /// Issues the explicit POA shares that are the ONLY way a human reaches a secure record.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this exists (task 061).</b> A provisioned secure record is owned by the Secure Record business
    /// unit's NAMED owner team, which has no members — Step 3 proves it — so ownership grants nobody access, by
    /// design. design.md §5.1: <i>"All human access is by explicit Dataverse share, including the creating
    /// attorney's."</i> Before this step existed, provisioning completed and left a record **no human could open**.</para>
    ///
    /// <para><b>The creator is identified from their own token</b>, via <c>WhoAmI()</c> on the OBO
    /// exchange (<see cref="CallerRecordAccessProbe.GetCallerSystemUserIdAsync"/>) — never from the
    /// request body. A caller cannot nominate someone else as "the creator". This endpoint has no resume path (a
    /// record already owned by the team is a 409), so it never shares to someone calling to finish another
    /// person's provisioning (owner decision F8, interim; task 133 owns resume).</para>
    ///
    /// <para><b>Fail closed, and loudly (ADR-003).</b> If the creator's identity cannot be established
    /// or their share cannot be issued, provisioning FAILS. Shares to the optional named principals are best-effort
    /// by contrast: their absence is visible and fixable through the FR-29 "+ User" path.</para>
    /// </remarks>
    private static async Task<ShareOutcome> ShareToCreatorAndPrincipalsAsync(
        IDataverseRecordShareService recordShare,
        CallerRecordAccessProbe callerAccessProbe,
        HttpContext httpContext,
        ProvisionProjectRequest request,
        SecureRecordRoot root,
        Guid recordId,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        var callerToken = TokenHelper.ExtractBearerTokenOrNull(httpContext);

        var creatorId = await callerAccessProbe.GetCallerSystemUserIdAsync(callerToken, ct);
        if (creatorId is null || creatorId == Guid.Empty)
        {
            logger.LogError(
                "[PROVISION] Could not resolve the calling user's systemuserid, so the creator's share " +
                "cannot be issued for {RecordType} {RecordId}. Refusing to complete provisioning: the " +
                "record is owned by a memberless team, so finishing here would leave a record no " +
                "human can open. TraceId={TraceId}", root.WireToken, recordId, traceId);

            return new ShareOutcome(null, 0, Problem(
                StatusCodes.Status403Forbidden, "Forbidden",
                "The calling user's Dataverse identity could not be established, so the record could " +
                "not be shared back to its creator. Provisioning was stopped rather than leaving a " +
                "secure record that nobody can open.",
                traceId, (ReasonKey, ReasonCreatorUnresolved)));
        }

        try
        {
            await recordShare.GrantAccessAsync(
                root.EntitySet,
                recordId,
                DataversePrincipalRef.User(creatorId.Value),
                CreatorAccessRights,
                ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PROVISION] The creator's share could not be issued on {RecordType} {RecordId} for user " +
                "{CreatorId}. Provisioning stopped — the record would otherwise be unreachable. " +
                "TraceId={TraceId}", root.WireToken, recordId, creatorId, traceId);

            return new ShareOutcome(creatorId, 0, Problem(
                StatusCodes.Status500InternalServerError, "Internal Server Error",
                "The record could not be shared back to its creator, so provisioning was stopped. " +
                "No SPE container was created; retry once the share path is healthy.",
                traceId, (ReasonKey, ReasonCreatorShareFailed)));
        }

        var additionalShared = 0;
        foreach (var principalId in (request.SharePrincipalIds ?? Array.Empty<Guid>())
                     .Where(id => id != Guid.Empty && id != creatorId.Value)
                     .Distinct())
        {
            try
            {
                await recordShare.GrantAccessAsync(
                    root.EntitySet,
                    recordId,
                    DataversePrincipalRef.User(principalId),
                    CollaboratorAccessRights,
                    ct);

                additionalShared++;
            }
            catch (Exception ex)
            {
                // Best-effort, per the remarks: named colleagues can be added afterwards, the creator
                // cannot. Logged with the id so an operator can see exactly who was missed.
                logger.LogWarning(ex,
                    "[PROVISION] Could not share {RecordType} {RecordId} with named principal {PrincipalId}. " +
                    "Provisioning continues; add them via the Manage Access surface. TraceId={TraceId}",
                    root.WireToken, recordId, principalId, traceId);
            }
        }

        logger.LogInformation(
            "[PROVISION] {RecordType} {RecordId} shared to creator {CreatorId} and {Count} named principal(s).",
            root.WireToken, recordId, creatorId, additionalShared);

        return new ShareOutcome(creatorId, additionalShared, null);
    }

    /// <summary>
    /// Creates the SPE container via the SpeFileStore facade (ADR-007).
    /// </summary>
    /// <remarks>
    /// There is no rollback of the ownership assignment if this fails. That is deliberate: ownership
    /// inside the Secure Record business unit is the safer state to be left in, so undoing it on a
    /// container failure would move the record back OUT of the secure business unit — turning a
    /// storage failure into a disclosure.
    /// </remarks>
    private static async Task<SpeContainerCreationResult> CreateSpeContainerAsync(
        SpeFileStore speFileStore,
        IConfiguration configuration,
        SecureRecordRoot root,
        string recordName,
        Guid recordId,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        var containerTypeIdStr = configuration["SharePointEmbedded:ContainerTypeId"];
        if (!Guid.TryParse(containerTypeIdStr, out var containerTypeId))
        {
            logger.LogError(
                "[PROVISION] SharePointEmbedded:ContainerTypeId is not configured or invalid: '{Value}'",
                containerTypeIdStr);

            return new SpeContainerCreationResult(null, Problem(
                StatusCodes.Status500InternalServerError, "Internal Server Error",
                "SPE ContainerTypeId is not configured on the BFF API.", traceId));
        }

        logger.LogInformation("[PROVISION] Creating SPE container for {RecordType} {RecordId}", root.WireToken, recordId);

        try
        {
            var containerDisplayName = root.ContainerDisplayName(recordName);
            var containerDescription = root.ContainerDescription(recordName);

            var container = await speFileStore.CreateContainerAsync(
                containerTypeId, containerDisplayName, containerDescription, ct);

            if (container == null)
            {
                logger.LogError(
                    "[PROVISION] SpeFileStore.CreateContainerAsync returned null for {RecordType} {RecordId}",
                    root.WireToken, recordId);

                return new SpeContainerCreationResult(null, Problem(
                    StatusCodes.Status500InternalServerError, "Internal Server Error",
                    "Failed to provision SPE container — Graph API returned null.", traceId));
            }

            logger.LogInformation(
                "[PROVISION] Created SPE container {ContainerId} ('{DisplayName}') for {RecordType} {RecordId}",
                container.Id, containerDisplayName, root.WireToken, recordId);

            return new SpeContainerCreationResult(container.Id, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PROVISION] Failed to create SPE container for {RecordType} {RecordId}", root.WireToken, recordId);

            return new SpeContainerCreationResult(null, Problem(
                StatusCodes.Status500InternalServerError, "Internal Server Error",
                "Failed to provision SPE container.", traceId));
        }
    }

    /// <summary>
    /// Records the provisioned container on the record. Throws on failure — the caller turns that
    /// into a non-2xx carrying the container id (ADR-003).
    /// </summary>
    /// <remarks>
    /// <para><c>sprk_containerid</c> is <c>NVARCHAR(100)</c> on all three roots (live metadata) — a PLAIN STRING
    /// write. No <c>@odata.bind</c>, no navigation property, nothing case-sensitive.</para>
    ///
    /// <para>Overwriting a pre-existing value is intentional. Any value already here on an
    /// unprovisioned secure record came from the wizard's business-unit cascade
    /// (<c>EntityCreationService.applyUserBuDefaults</c>) and points at the CREATING USER'S business
    /// unit container — shared storage that other users can reach, i.e. the opposite of isolation.
    /// The old value is logged so a genuinely orphaned container remains traceable.</para>
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
                "[PROVISION] Overwriting sprk_containerid on {RecordType} {RecordId}: '{Previous}' → " +
                "'{New}'. The previous value was cascaded from the creating user's business unit and " +
                "is shared storage, not this record's container.",
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

    /// <summary>Outcome of assigning a record to the Secure Record owner team.</summary>
    private enum OwnerAssignmentOutcome
    {
        /// <summary>Dataverse refused the write, or the result could not be verified.</summary>
        Failed,

        /// <summary>The write was accepted but the owner did not change — a silently ignored bind.</summary>
        NotApplied,

        /// <summary>The record is owned by the team, confirmed by reading the owner back.</summary>
        Assigned
    }

    /// <summary>Internal result wrapper for SPE container creation with optional error result.</summary>
    private sealed record SpeContainerCreationResult(string? ContainerId, IResult? Error);

    /// <summary>
    /// Result of the task-061 share step: who the creator turned out to be, how many named principals
    /// were also shared to, and the error that stopped provisioning (null when it succeeded).
    /// </summary>
    private sealed record ShareOutcome(Guid? CreatorSystemUserId, int AdditionalPrincipalsShared, IResult? Error);

    // ── Dataverse row DTOs ────────────────────────────────────────────────

    /// <summary>
    /// The columns Step 1 reads, common to all three roots. The id and name columns differ per table, so the name
    /// is read from <see cref="Extra"/> by the column <see cref="SecureRecordRoot.NameColumn"/> names.
    /// </summary>
    private sealed class RootRow
    {
        [JsonPropertyName("sprk_issecure")]
        public bool? sprk_issecure { get; set; }

        /// <summary>
        /// The container recorded on the record — which is NOT a reliable sign of provisioning.
        /// </summary>
        /// <remarks>
        /// See <see cref="IsOwnedBy"/> for why this field must never be used as the marker.
        /// </remarks>
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

        /// <summary>The business unit the record's owner places it in.</summary>
        [JsonPropertyName("_owningbusinessunit_value")]
        public Guid? _owningbusinessunit_value { get; set; }

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

        /// <summary>True when this record still references a retired per-record security BU.</summary>
        public bool HasLegacyPerProjectBusinessUnit =>
            _sprk_securitybu_value is { } bu && bu != Guid.Empty;

        /// <summary>
        /// Whether this record is already owned by <paramref name="ownerTeamId"/> — the idempotency marker.
        /// </summary>
        /// <remarks>
        /// <para><b>Why ownership, and specifically NOT <c>sprk_containerid</c>.</b> The guard added on
        /// 2026-08-23 keyed on <c>sprk_containerid</c> being non-empty. But the Create Project wizard
        /// writes <c>sprk_containerid</c> at CREATE time, cascaded from the creating user's business
        /// unit, for every project including secure ones — so every secure project answered 409 "already
        /// provisioned" and none was ever provisioned. A guard against double-provisioning became a guard against
        /// provisioning.</para>
        ///
        /// <para>Ownership by the named Secure Record owner team is state that ONLY this endpoint (and the one-time
        /// migration script) ever writes: the wizard cannot set it (it does not know the team), and the cascade
        /// copies business-unit-derived FIELDS, not ownership.</para>
        ///
        /// <para>Residual edge, stated rather than hidden: if an administrator deliberately reassigns a provisioned
        /// secure record away from the owner team AND out of the Secure Record business unit, a later provisioning run
        /// would see it as unprovisioned and create a second container. <see cref="RecordContainerAsync"/> logs the
        /// displaced container id so the first one stays traceable.</para>
        /// </remarks>
        public bool IsOwnedBy(Guid ownerTeamId) =>
            _owningteam_value is { } team && team == ownerTeamId;

        /// <summary>
        /// Owned INSIDE the Secure Record BU, but by a team other than the named one (task 144) — the retired
        /// default team, before the migration. Refused like an already-provisioned record so a retry cannot create a
        /// second container.
        /// </summary>
        public bool IsOwnedInBusinessUnitByAnotherTeam(Guid secureBusinessUnitId, Guid ownerTeamId) =>
            _owningbusinessunit_value is { } bu && bu == secureBusinessUnitId && !IsOwnedBy(ownerTeamId);
    }
}
