using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/provision-project
///
/// Provisions the infrastructure a Secure Project needs. Called by the Create Project wizard
/// immediately after creating the <c>sprk_project</c> record with the Secure Project toggle enabled.
///
/// Provisioning sequence:
///   1. Confirm the project exists and carries <c>sprk_issecure = true</c>
///   2. Resolve the ONE canonical Secure Project business unit, BY NAME, from configuration
///   3. Resolve that BU's default owner team
///   4. Refuse if the project is already provisioned (see <see cref="ProjectRow"/>)
///   5. Assign the project's owner to that team, and verify the assignment took effect
///   6. Create the project's own SPE container
///   7. Record the container on the project — and FAIL if that record cannot be written
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
/// <para><b>RE-SCOPED 2026-08-25 (task 021).</b> This endpoint previously created a business unit
/// per secure project and an <c>account</c> per secure project, then stamped three references onto
/// the project using three column names that do not exist — and swallowed the resulting 400. All of
/// that is gone. What replaced it, and why, in order of how badly each mattered:</para>
///
/// <para><b>1. BU-per-project contradicted the design and escaped its own guardrail.</b> design.md
/// §5.1 says verbatim "no BU-per-project proliferation" and specifies ONE <c>Secure Project</c>
/// business unit. The old code created a child BU per project and parented it to the ROOT BU — so
/// those BUs sat OUTSIDE the BU that NFR-05's standing assertion guards. The guardrail this project
/// exists to build would silently not have covered the records provisioning created.</para>
///
/// <para><b>2. Repairing the stamp would have destroyed client data.</b> <c>sprk_externalaccount</c>
/// is the CLIENT lookup — <c>ProjectLiveFactResolver.cs:33</c> and <c>MatterLiveFactResolver.cs:35</c>
/// both map the predicate <c>client</c> to it. The old code created a synthetic
/// "External Access — {project}" account and, in the broken PATCH, aimed it at that column. Writing
/// the correct name there would have overwritten every secure project's client. The five-month
/// silent failure is the only reason no client data was corrupted. That column is now never written,
/// and a test fails if it reappears in any payload.</para>
///
/// <para><b>3. The idempotency marker keyed on shared state — a live regression, introduced by this
/// project on 2026-08-23.</b> See <see cref="ProjectRow"/> for the full account; short version: the
/// wizard writes <c>sprk_containerid</c> at CREATE time from the creating user's BU, so every secure
/// project 409'd as "already provisioned" and none was ever provisioned.</para>
///
/// <para><b>4. The nav-property blocker dissolved.</b> The first attempt at this task stopped because
/// <c>@odata.bind</c> needs case-sensitive PascalCase navigation properties that are not derivable
/// from the logical name, and a wrong one is silently accepted as an unknown property. Under this
/// scope the only stamped field is <c>sprk_containerid</c>, an <c>NVARCHAR(100)</c> plain string. The
/// one remaining bind — <c>ownerid</c> — is an out-of-the-box owner field, and it is verified by
/// reading the owner back rather than trusted (see
/// <see cref="AssignOwnerToSecureTeamAsync"/>).</para>
///
/// <para>Not done here, deliberately: nothing yet READS the container this endpoint records. Document
/// isolation needs the three container-resolution strategies special-cased, which is
/// <c>projects/spaarke-secure-project-r1</c>. design.md §5.1c states that sequencing gap.</para>
/// </remarks>
public static class ProvisionProjectEndpoint
{
    private const string ProjectEntitySet = "sprk_projects";
    private const string BusinessUnitEntitySet = "businessunits";
    private const string TeamEntitySet = "teams";

    /// <summary>
    /// Dataverse <c>teamtype</c> option value for an OWNER team.
    /// </summary>
    /// <remarks>
    /// Live option set (verified 2026-08-25): Owner = 0, Access = 1, Security Group = 2,
    /// Office Group = 3. Only an owner team can own a record, so the filter must pin this — an access
    /// team on the same business unit would resolve and then fail the assignment.
    /// </remarks>
    private const int OwnerTeamType = 0;

    // ── Configuration ────────────────────────────────────────────────────────

    /// <summary>Configuration key naming the canonical Secure Project business unit.</summary>
    /// <remarks>
    /// A NAME, not a GUID, and configurable rather than compiled in. The GUID differs per environment
    /// — the BU is created as part of new-environment setup, so no id is stable across tenants — and a
    /// customer who renames the BU must not need a redeploy. Business unit names are unique per
    /// organisation, so a name lookup is deterministic.
    /// </remarks>
    internal const string SecureBusinessUnitNameConfigKey = "SecureProject:BusinessUnitName";

    /// <summary>
    /// Default name of the canonical Secure Project business unit.
    /// </summary>
    /// <remarks>
    /// <b>SINGULAR.</b> Verified against live Dataverse metadata 2026-08-25: the deployed BU is named
    /// <c>Secure Project</c>. design.md §5.1 and this task's POML both wrote "Secure Projects"
    /// (plural) — an assumed name, never checked. Shipping the plural would have made every
    /// provisioning call fail closed with "business unit not found": the correct DIRECTION, but for a
    /// fabricated reason, and it would have looked like a missing environment rather than a typo.
    /// Eighth instance of "schema/name docs lose to live metadata" in this project.
    /// </remarks>
    internal const string DefaultSecureBusinessUnitName = "Secure Project";

    // ── Reason codes (ProblemDetails extensions["reasonCode"]) ───────────────
    //
    // Same convention as DelegationRuleFilter (task 008) and ProjectClosureEndpoint: a stable machine
    // -readable code alongside the human-readable detail, so a caller can distinguish "your request
    // named nothing" from "the environment is not set up" without parsing prose.

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

    /// <summary>
    /// The columns Step 1 reads from <c>sprk_project</c>.
    /// </summary>
    /// <remarks>
    /// <para>Every name here is verified against live <c>sprk_project</c> metadata (2026-08-25).
    /// <c>sprk_securitybuid</c>, <c>sprk_specontainerid</c> and <c>sprk_externalaccountid</c> — the
    /// three names the old stamping PATCH used — do not exist on this table, which is why that PATCH
    /// 400'd for five months. The root cause was not a typo but
    /// <c>src/solutions/SpaarkeCore/entities/sprk_project/secure-project-fields-schema.md</c>, which
    /// documents all three authoritatively and is wrong; the code was implemented faithfully from it.
    /// Repairing that doc is task 026.</para>
    ///
    /// <para>Internal (not private) so a test can pin the exact names against the live column set —
    /// the guard task 016 built for the closure cascade and this endpoint originally did not get.</para>
    /// </remarks>
    internal const string ProjectProvisioningSelect =
        "sprk_projectid,sprk_projectname,sprk_issecure,sprk_containerid," +
        "_sprk_securitybu_value,_owningteam_value";

    /// <summary>
    /// Registers the provision-project endpoint on the external-access management group.
    /// </summary>
    public static RouteGroupBuilder MapProvisionProjectEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/provision-project", ProvisionProjectAsync)
            .WithName("ProvisionSecureProject")
            .WithSummary("Provision infrastructure for a new Secure Project")
            .WithDescription(
                "Assigns the project to the canonical Secure Project business unit's default owner " +
                "team and provisions the project's own SPE container, recording it on the " +
                "sprk_project record. Creates no business unit and no account.")
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

    // =========================================================================
    // Handler
    // =========================================================================

    private static async Task<IResult> ProvisionProjectAsync(
        ProvisionProjectRequest request,
        DataverseWebApiClient dataverseClient,
        SpeFileStore speFileStore,
        IConfiguration configuration,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // ── Validation ───────────────────────────────────────────────────────
        if (request.ProjectId == Guid.Empty)
            return ProblemDetailsHelper.ValidationError("ProjectId is required and must be a valid GUID.");

        var traceId = httpContext.TraceIdentifier;

        logger.LogInformation(
            "[PROVISION] Starting secure project provisioning: ProjectId={ProjectId}, " +
            "ProjectRef={ProjectRef}, TraceId={TraceId}",
            request.ProjectId, request.ProjectRef, traceId);

        // ── Step 1: Confirm the project exists and is secure ─────────────────
        ProjectRow? projectRow;
        try
        {
            var rows = await dataverseClient.QueryAsync<ProjectRow>(
                ProjectEntitySet,
                filter: $"sprk_projectid eq {request.ProjectId}",
                select: ProjectProvisioningSelect,
                top: 1,
                cancellationToken: ct);

            projectRow = rows.FirstOrDefault();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PROVISION] Failed to query project {ProjectId} from Dataverse", request.ProjectId);
            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                "Failed to retrieve project record from Dataverse.", traceId);
        }

        if (projectRow == null)
        {
            return Problem(StatusCodes.Status404NotFound, "Not Found",
                $"Project {request.ProjectId} not found.", traceId);
        }

        if (projectRow.sprk_issecure != true)
        {
            return ProblemDetailsHelper.ValidationError(
                $"Project {request.ProjectId} is not a Secure Project (sprk_issecure is false or null). " +
                "Enable the Secure Project toggle before provisioning.");
        }

        var projectName = projectRow.sprk_projectname ?? request.ProjectRef ?? request.ProjectId.ToString();

        // ── Step 2: Resolve the canonical Secure Project BU, by NAME ─────────
        //
        // FAIL CLOSED, and never substitute. An absent or ambiguous BU must stop provisioning, not
        // fall back to the root BU or the caller's BU: either fallback would put a secure record in a
        // business unit ordinary users can reach at Deep depth (design.md §5.2), which is precisely
        // the disclosure this whole design exists to prevent. A loud stop is recoverable; a silent
        // substitution is not detectable from the outside at all.
        var secureBuName = configuration[SecureBusinessUnitNameConfigKey] is { } configured
                           && !string.IsNullOrWhiteSpace(configured)
            ? configured.Trim()
            : DefaultSecureBusinessUnitName;

        BuRow[] buMatches;
        try
        {
            // $top=2, not 1. Selecting one row makes ambiguity invisible — it silently takes whichever
            // row Dataverse happens to return first. Two rows make "more than one match" a state we
            // can detect and refuse.
            var rows = await dataverseClient.QueryAsync<BuRow>(
                BusinessUnitEntitySet,
                filter: $"name eq '{EscapeODataStringLiteral(secureBuName)}'",
                select: "businessunitid,name",
                top: 2,
                cancellationToken: ct);

            buMatches = rows.ToArray();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PROVISION] Failed to resolve the Secure Project business unit by name '{BuName}'",
                secureBuName);
            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                "Failed to resolve the Secure Project business unit from Dataverse.", traceId,
                (ReasonKey, ReasonSecureBuNotFound));
        }

        if (buMatches.Length == 0)
        {
            logger.LogError(
                "[PROVISION] No business unit named '{BuName}' exists. Provisioning refused — a secure " +
                "project must not be placed in any other business unit. TraceId={TraceId}",
                secureBuName, traceId);

            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"No business unit named '{secureBuName}' exists in this environment. The canonical " +
                "Secure Project business unit is created during environment setup; provisioning will " +
                "not create one, and will not fall back to another business unit.",
                traceId, (ReasonKey, ReasonSecureBuNotFound), ("businessUnitName", secureBuName));
        }

        if (buMatches.Length > 1)
        {
            logger.LogError(
                "[PROVISION] More than one business unit is named '{BuName}'. Provisioning refused — " +
                "picking one would be arbitrary. TraceId={TraceId}", secureBuName, traceId);

            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"More than one business unit is named '{secureBuName}'. Provisioning cannot choose " +
                "between them. Resolve the duplicate, or set " +
                $"'{SecureBusinessUnitNameConfigKey}' to an unambiguous name.",
                traceId, (ReasonKey, ReasonSecureBuAmbiguous), ("businessUnitName", secureBuName));
        }

        var secureBu = buMatches[0];
        if (secureBu.businessunitid is not { } secureBuId || secureBuId == Guid.Empty)
        {
            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"The business unit named '{secureBuName}' was returned without an id.",
                traceId, (ReasonKey, ReasonSecureBuNotFound));
        }

        // ── Step 3: Resolve that BU's DEFAULT OWNER TEAM ─────────────────────
        //
        // Not a service account (design.md §5.1a). Every Dataverse business unit is created with a
        // default owner team named after it, so this team already exists and needs no provisioning —
        // no licence, no credential to rotate, no extra identity to audit.
        //
        // Resolved by (business unit + isdefault + owner type) rather than by NAME. The default team
        // happens to share the BU's name, but that is a Dataverse convention, not a contract, and
        // matching on it would break the moment someone renames the team. These three fields are what
        // actually define "this BU's default owner team".
        TeamRow[] teamMatches;
        try
        {
            var rows = await dataverseClient.QueryAsync<TeamRow>(
                TeamEntitySet,
                filter: $"_businessunitid_value eq {secureBuId} and isdefault eq true " +
                        $"and teamtype eq {OwnerTeamType}",
                select: "teamid,name",
                top: 2,
                cancellationToken: ct);

            teamMatches = rows.ToArray();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PROVISION] Failed to resolve the default owner team for business unit {BuId}", secureBuId);
            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                "Failed to resolve the Secure Project owner team from Dataverse.", traceId,
                (ReasonKey, ReasonOwnerTeamNotFound));
        }

        if (teamMatches.Length == 0)
        {
            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"Business unit '{secureBuName}' has no default owner team. Every business unit is " +
                "created with one, so this indicates the business unit was altered after creation.",
                traceId, (ReasonKey, ReasonOwnerTeamNotFound), ("businessUnitId", secureBuId));
        }

        if (teamMatches.Length > 1)
        {
            // Dataverse creates exactly one default owner team per BU, so this should be unreachable.
            // It is checked anyway because the alternative to checking is taking an arbitrary row —
            // and an arbitrary owner is exactly the class of silent wrongness this task removes.
            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"Business unit '{secureBuName}' reports more than one default owner team.",
                traceId, (ReasonKey, ReasonOwnerTeamAmbiguous), ("businessUnitId", secureBuId));
        }

        var ownerTeam = teamMatches[0];
        if (ownerTeam.teamid is not { } ownerTeamId || ownerTeamId == Guid.Empty)
        {
            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"The default owner team for '{secureBuName}' was returned without an id.",
                traceId, (ReasonKey, ReasonOwnerTeamNotFound));
        }

        var ownerTeamName = ownerTeam.name ?? secureBuName;

        logger.LogInformation(
            "[PROVISION] Resolved Secure Project BU '{BuName}' ({BuId}) and its default owner team " +
            "'{TeamName}' ({TeamId})", secureBuName, secureBuId, ownerTeamName, ownerTeamId);

        // ── Step 4: Refuse to provision an already-provisioned project ───────
        if (projectRow.HasLegacyPerProjectBusinessUnit)
        {
            logger.LogWarning(
                "[PROVISION] Project {ProjectId} carries a legacy per-project security BU " +
                "({LegacyBuId}). Refusing: migrating it onto the canonical BU is a deliberate act, " +
                "not something a provisioning retry should do. TraceId={TraceId}",
                request.ProjectId, projectRow._sprk_securitybu_value, traceId);

            return Problem(StatusCodes.Status409Conflict, "Conflict",
                $"Project {request.ProjectId} was provisioned by the retired BU-per-project mechanism " +
                "and still references its own security business unit. Migrating it to the canonical " +
                "Secure Project business unit is a manual operation — provisioning will not do it as " +
                "a side effect, because it would leave the old business unit and its container behind " +
                "with nothing pointing at them.",
                traceId,
                (ReasonKey, ReasonLegacyPerProjectBu),
                ("legacyBusinessUnitId", projectRow._sprk_securitybu_value),
                ("speContainerId", projectRow.sprk_containerid));
        }

        if (projectRow.IsOwnedBy(ownerTeamId))
        {
            // Two distinguishable states, and the operator needs to know which one they are in:
            //   - container recorded  → provisioning completed; there is nothing to do
            //   - no container        → ownership was claimed but a later step failed
            var hasContainer = !string.IsNullOrWhiteSpace(projectRow.sprk_containerid);

            logger.LogWarning(
                "[PROVISION] Project {ProjectId} is already owned by the Secure Project owner team " +
                "{TeamId} (container recorded: {HasContainer}). Refusing to re-provision. " +
                "TraceId={TraceId}",
                request.ProjectId, ownerTeamId, hasContainer, traceId);

            return Problem(StatusCodes.Status409Conflict, "Conflict",
                hasContainer
                    ? $"Project {request.ProjectId} has already been provisioned. Re-provisioning " +
                      "would create a second SPE container and repoint the project at it, orphaning " +
                      "the documents already stored."
                    : $"Project {request.ProjectId} is already owned by the Secure Project owner team " +
                      "but has no SPE container recorded, so an earlier run claimed it and then " +
                      "failed. Reassign the project's owner and retry, or record the container " +
                      "manually if one was created — the failed run's response named it.",
                traceId,
                (ReasonKey, ReasonAlreadyProvisioned),
                ("businessUnitId", secureBuId),
                ("ownerTeamId", ownerTeamId),
                ("speContainerId", projectRow.sprk_containerid));
        }

        // ── Step 5: Assign ownership to the Secure Project owner team ────────
        //
        // ORDER MATTERS, and this step is deliberately FIRST of the two mutations.
        //
        // Ownership is the SECURITY step; the container is the storage step. If the container step
        // fails after this, the project is at least correctly owned inside the Secure Project BU. If
        // the order were reversed, the same failure would leave a secure project owned by its creating
        // user in an Operations business unit — strictly the worse posture of the two.
        //
        // It is also what makes the idempotency marker sound: ownership by this team is state only
        // provisioning ever writes (see ProjectRow).
        var assignment = await AssignOwnerToSecureTeamAsync(
            dataverseClient, request.ProjectId, ownerTeamId, logger, ct);

        if (assignment != OwnerAssignmentOutcome.Assigned)
        {
            var (reason, detail) = assignment switch
            {
                OwnerAssignmentOutcome.NotApplied => (
                    ReasonOwnerAssignmentNotApplied,
                    "Dataverse accepted the ownership assignment but the project is still not owned " +
                    "by the Secure Project owner team. This is the silent-navigation-property failure " +
                    "mode: an unrecognised @odata.bind property is accepted and ignored rather than " +
                    "rejected. Nothing has been provisioned."),
                _ => (
                    ReasonOwnerAssignmentFailed,
                    "Failed to assign the project to the Secure Project owner team. If Dataverse " +
                    "refused the assignment, the owner team most likely lacks the entity privileges " +
                    "an assignment target must hold — see the Secure Project Owner role in " +
                    "design.md §5.1a. Nothing has been provisioned.")
            };

            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error", detail,
                traceId, (ReasonKey, reason), ("ownerTeamId", ownerTeamId));
        }

        // ── Step 6: Create the project's own SPE container ───────────────────
        var containerResult = await CreateSpeContainerAsync(
            speFileStore, configuration, projectName, request.ProjectId, logger, traceId, ct);

        if (containerResult.Error != null)
            return containerResult.Error;

        var speContainerId = containerResult.ContainerId!;

        // ── Step 7: Record the container on the project — FAIL if it cannot be written ──
        try
        {
            await RecordContainerOnProjectAsync(
                dataverseClient, request.ProjectId, speContainerId, projectRow.sprk_containerid, logger, ct);
        }
        catch (Exception ex)
        {
            // ADR-003. This used to be a catch + LogWarning + return 200, and that swallow is the
            // single reason the broken column names survived five months: provisioning created real
            // infrastructure, failed to record any of it, and reported success. A container nobody
            // recorded is invisible — so the error carries its id, which is the only thing that makes
            // the orphan reconcilable.
            logger.LogError(ex,
                "[PROVISION] Container {ContainerId} was created for project {ProjectId} but could " +
                "NOT be recorded on the project record. The container is orphaned until an operator " +
                "reconciles it. TraceId={TraceId}",
                speContainerId, request.ProjectId, traceId);

            return Problem(StatusCodes.Status500InternalServerError, "Internal Server Error",
                $"An SPE container was created for project {request.ProjectId} but could not be " +
                "recorded on the project record, so the project does not yet point at it. The " +
                "container id is included here: record it on sprk_containerid, or delete the " +
                "container, before retrying.",
                traceId,
                (ReasonKey, ReasonContainerNotRecorded),
                ("speContainerId", speContainerId),
                ("ownerTeamId", ownerTeamId));
        }

        logger.LogInformation(
            "[PROVISION] Provisioning complete for project {ProjectId}: BU={BuId} ({BuName}), " +
            "OwnerTeam={TeamId}, Container={ContainerId}",
            request.ProjectId, secureBuId, secureBuName, ownerTeamId, speContainerId);

        return TypedResults.Ok(new ProvisionProjectResponse(
            BusinessUnitId: secureBuId,
            BusinessUnitName: secureBuName,
            OwnerTeamId: ownerTeamId,
            OwnerTeamName: ownerTeamName,
            SpeContainerId: speContainerId));
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Assigns the project's owner to <paramref name="ownerTeamId"/>, then reads the owner back to
    /// confirm the assignment actually took effect.
    /// </summary>
    /// <remarks>
    /// <para><b>The read-back is the point, not belt-and-braces.</b> <c>ownerid</c> is written through
    /// <c>@odata.bind</c>, and Dataverse's behaviour on an unrecognised <c>@odata.bind</c> property is
    /// to accept the request and ignore the property — no error, no write. That is the exact failure
    /// mode that hid the old stamping bug for five months, and it is not defended against by getting
    /// the name right, because "did I get the name right?" is unanswerable offline. Reading the value
    /// back converts an unverifiable assumption into an observed fact.</para>
    ///
    /// <para><c>ownerid</c> is an out-of-the-box owner field, so unlike the custom
    /// <c>sprk_securitybu</c> / <c>sprk_externalaccount</c> lookups its navigation property name is
    /// not in doubt. The check costs one read and holds regardless.</para>
    /// </remarks>
    private static async Task<OwnerAssignmentOutcome> AssignOwnerToSecureTeamAsync(
        DataverseWebApiClient dataverseClient,
        Guid projectId,
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
                ProjectEntitySet,
                projectId,
                new Dictionary<string, object?>
                {
                    ["ownerid@odata.bind"] = $"/{TeamEntitySet}({ownerTeamId})"
                },
                ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PROVISION] Dataverse refused the ownership assignment of project {ProjectId} to " +
                "team {TeamId}. If this is a privilege error, the owner team lacks the entity rights " +
                "an assignment target must hold (design.md §5.1a, Secure Project Owner role).",
                projectId, ownerTeamId);
            return OwnerAssignmentOutcome.Failed;
        }

        try
        {
            var rows = await dataverseClient.QueryAsync<ProjectRow>(
                ProjectEntitySet,
                filter: $"sprk_projectid eq {projectId}",
                select: "sprk_projectid,_owningteam_value",
                top: 1,
                cancellationToken: ct);

            var reread = rows.FirstOrDefault();
            if (reread is null || !reread.IsOwnedBy(ownerTeamId))
            {
                logger.LogError(
                    "[PROVISION] Ownership read-back FAILED for project {ProjectId}: expected owning " +
                    "team {TeamId}, found {ActualTeamId}. The PATCH was accepted, so the owner " +
                    "navigation property was almost certainly ignored rather than applied.",
                    projectId, ownerTeamId, reread?._owningteam_value);
                return OwnerAssignmentOutcome.NotApplied;
            }
        }
        catch (Exception ex)
        {
            // An unverifiable assignment is treated as a failed one. Reporting success here would
            // reintroduce exactly the "assume the write happened" defect this method exists to close.
            logger.LogError(ex,
                "[PROVISION] Could not read back the owner of project {ProjectId} to verify the " +
                "assignment. Treating the assignment as unverified, therefore failed.", projectId);
            return OwnerAssignmentOutcome.Failed;
        }

        logger.LogInformation(
            "[PROVISION] Project {ProjectId} is now owned by the Secure Project owner team {TeamId} " +
            "(verified by read-back)", projectId, ownerTeamId);

        return OwnerAssignmentOutcome.Assigned;
    }

    /// <summary>
    /// Creates the SPE container via the SpeFileStore facade (ADR-007).
    /// </summary>
    /// <remarks>
    /// There is no rollback of the ownership assignment if this fails. That is deliberate: ownership
    /// inside the Secure Project business unit is the safer state to be left in, so undoing it on a
    /// container failure would move the record back OUT of the secure business unit — turning a
    /// storage failure into a disclosure.
    /// </remarks>
    private static async Task<SpeContainerCreationResult> CreateSpeContainerAsync(
        SpeFileStore speFileStore,
        IConfiguration configuration,
        string projectName,
        Guid projectId,
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

        logger.LogInformation("[PROVISION] Creating SPE container for project {ProjectId}", projectId);

        try
        {
            var containerDisplayName = $"Secure Project — {projectName}";
            var containerDescription = $"Isolated document container for Secure Project: {projectName}";

            var container = await speFileStore.CreateContainerAsync(
                containerTypeId, containerDisplayName, containerDescription, ct);

            if (container == null)
            {
                logger.LogError(
                    "[PROVISION] SpeFileStore.CreateContainerAsync returned null for project {ProjectId}",
                    projectId);

                return new SpeContainerCreationResult(null, Problem(
                    StatusCodes.Status500InternalServerError, "Internal Server Error",
                    "Failed to provision SPE container — Graph API returned null.", traceId));
            }

            logger.LogInformation(
                "[PROVISION] Created SPE container {ContainerId} ('{DisplayName}') for project {ProjectId}",
                container.Id, containerDisplayName, projectId);

            return new SpeContainerCreationResult(container.Id, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PROVISION] Failed to create SPE container for project {ProjectId}", projectId);

            return new SpeContainerCreationResult(null, Problem(
                StatusCodes.Status500InternalServerError, "Internal Server Error",
                "Failed to provision SPE container.", traceId));
        }
    }

    /// <summary>
    /// Records the provisioned container on the project. Throws on failure — the caller turns that
    /// into a non-2xx carrying the container id (ADR-003).
    /// </summary>
    /// <remarks>
    /// <para><c>sprk_containerid</c> is <c>NVARCHAR(100)</c> (live metadata) — a PLAIN STRING write.
    /// No <c>@odata.bind</c>, no navigation property, nothing case-sensitive. That is the whole
    /// reason this task became possible: the two lookups the old code tried to stamp
    /// (<c>sprk_securitybu</c>, <c>sprk_externalaccount</c>) needed navigation-property names that
    /// could not be recovered offline, and neither should be written at all.</para>
    ///
    /// <para>Overwriting a pre-existing value is intentional. Any value already here on an
    /// unprovisioned secure project came from the wizard's business-unit cascade
    /// (<c>EntityCreationService.applyUserBuDefaults</c>) and points at the CREATING USER'S business
    /// unit container — shared storage that other users can reach, i.e. the opposite of isolation.
    /// The old value is logged so a genuinely orphaned container remains traceable.</para>
    /// </remarks>
    private static async Task RecordContainerOnProjectAsync(
        DataverseWebApiClient dataverseClient,
        Guid projectId,
        string speContainerId,
        string? previousContainerId,
        ILogger logger,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(previousContainerId))
        {
            logger.LogWarning(
                "[PROVISION] Overwriting sprk_containerid on project {ProjectId}: '{Previous}' → " +
                "'{New}'. The previous value was cascaded from the creating user's business unit and " +
                "is shared storage, not this project's container.",
                projectId, previousContainerId, speContainerId);
        }

        await dataverseClient.UpdateAsync(
            ProjectEntitySet,
            projectId,
            new Dictionary<string, object?>
            {
                ["sprk_containerid"] = speContainerId
            },
            ct);

        logger.LogInformation(
            "[PROVISION] Recorded container {ContainerId} on project {ProjectId}",
            speContainerId, projectId);
    }

    /// <summary>
    /// Escapes a string for use inside an OData single-quoted literal.
    /// </summary>
    /// <remarks>
    /// The same one-liner exists privately in <c>RagQueryBuilder</c>, <c>IndexRetrieveNode</c>,
    /// <c>RecordSearchService</c> and <c>RegistrationDataverseService</c>. Kept local rather than
    /// promoted to shared surface: CLAUDE.md §11 is about not multiplying services and abstractions,
    /// and a new shared utility for one <c>Replace</c> would be new surface for no behaviour.
    /// </remarks>
    private static string EscapeODataStringLiteral(string value) => value.Replace("'", "''");

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

    /// <summary>Outcome of assigning a project to the Secure Project owner team.</summary>
    private enum OwnerAssignmentOutcome
    {
        /// <summary>Dataverse refused the write, or the result could not be verified.</summary>
        Failed,

        /// <summary>The write was accepted but the owner did not change — a silently ignored bind.</summary>
        NotApplied,

        /// <summary>The project is owned by the team, confirmed by reading the owner back.</summary>
        Assigned
    }

    /// <summary>Internal result wrapper for SPE container creation with optional error result.</summary>
    private sealed record SpeContainerCreationResult(string? ContainerId, IResult? Error);

    // ── Dataverse row DTOs ────────────────────────────────────────────────

    private sealed class ProjectRow
    {
        [JsonPropertyName("sprk_projectid")]
        public Guid sprk_projectid { get; set; }

        [JsonPropertyName("sprk_projectname")]
        public string? sprk_projectname { get; set; }

        [JsonPropertyName("sprk_issecure")]
        public bool? sprk_issecure { get; set; }

        /// <summary>
        /// The container recorded on the project — which is NOT a reliable sign of provisioning.
        /// </summary>
        /// <remarks>
        /// See <see cref="IsOwnedBy"/> for why this field must never be used as the marker.
        /// </remarks>
        [JsonPropertyName("sprk_containerid")]
        public string? sprk_containerid { get; set; }

        /// <summary>
        /// A per-project security business unit stamped by the RETIRED BU-per-project mechanism.
        /// </summary>
        /// <remarks>
        /// Nothing writes this any more. It is still read because a project that carries one was
        /// provisioned by the old mechanism, and such a project must not be silently re-provisioned
        /// onto the canonical business unit — that would strand the old business unit and its
        /// container with nothing referencing them. Refusing makes the migration a deliberate act.
        /// </remarks>
        [JsonPropertyName("_sprk_securitybu_value")]
        public Guid? _sprk_securitybu_value { get; set; }

        /// <summary>The team that owns this project, if it is team-owned.</summary>
        [JsonPropertyName("_owningteam_value")]
        public Guid? _owningteam_value { get; set; }

        /// <summary>True when this project still references a retired per-project security BU.</summary>
        public bool HasLegacyPerProjectBusinessUnit =>
            _sprk_securitybu_value is { } bu && bu != Guid.Empty;

        /// <summary>
        /// Whether this project is already owned by <paramref name="ownerTeamId"/> — the idempotency
        /// marker.
        /// </summary>
        /// <remarks>
        /// <para><b>Why ownership, and specifically NOT <c>sprk_containerid</c>.</b> The guard added on
        /// 2026-08-23 keyed on <c>sprk_containerid</c> being non-empty. But the Create Project wizard
        /// writes <c>sprk_containerid</c> at CREATE time, cascaded from the creating user's business
        /// unit (<c>EntityCreationService.applyUserBuDefaults</c>), for every project including secure
        /// ones. So for any user whose business unit carries a container id — and real
        /// <c>sprk_project</c> rows do — the wizard created the project with the field already
        /// populated, provisioning saw it, answered 409 "already provisioned", and the project was
        /// never provisioned at all. A guard against double-provisioning became a guard against
        /// provisioning.</para>
        ///
        /// <para>The root error was not the comparison; it was choosing a marker without checking who
        /// else writes the field. Ownership by the Secure Project owner team is state that ONLY this
        /// endpoint ever writes: the wizard cannot set it (it does not know the team), and the cascade
        /// copies business-unit-derived FIELDS, not ownership.</para>
        ///
        /// <para>Residual edge, stated rather than hidden: if an administrator deliberately reassigns
        /// a provisioned secure project away from the owner team, a later provisioning run would see
        /// it as unprovisioned and create a second container. That requires a deliberate ownership
        /// change on a secure project, and <see cref="RecordContainerOnProjectAsync"/> logs the
        /// displaced container id so the first one stays traceable.</para>
        /// </remarks>
        public bool IsOwnedBy(Guid ownerTeamId) =>
            _owningteam_value is { } team && team == ownerTeamId;
    }

    private sealed class BuRow
    {
        [JsonPropertyName("businessunitid")]
        public Guid? businessunitid { get; set; }

        [JsonPropertyName("name")]
        public string? name { get; set; }
    }

    private sealed class TeamRow
    {
        [JsonPropertyName("teamid")]
        public Guid? teamid { get; set; }

        [JsonPropertyName("name")]
        public string? name { get; set; }
    }
}
