// -----------------------------------------------------------------------------
// SecureRecordSetupProcedure.cs
//
// T256 (H7b) — the per-environment Secure Record setup, step by step: unified-access-control-r2 INCOMING-145 §2.2
// (S0–S9), its §4 item 2 (S10–S14, task 150's field security), its §6 topology checks (T2, T4) and the
// sprk_noaccessentry prerequisite (#1364) — plus S15–S18, the memberships of the contact identity-binding profiles
// (unified-access-control-r2 INCOMING-141 / task 141; T255), built exactly as S10/S11/S12/S14 — plus S19–S21 (ISS-020 / #1565):
// the standing-grant environment (S19 the Standing Grant Administrators profile and its lock on contact.sprk_standinggrant,
// S20 the BFF's application users as its members, S21 the sprk_noaccessentry role split unified-access-control-r2 task 154
// ships) — plus S22 (ISS-025 / #1803): the locks on contact.sprk_externalinvitedon and, once it ships,
// contact.sprk_externalinvitecodehash, VERIFIED ONLY — and (#2317) S19 also verifies the lock on the access level
// contact.sprk_accesspermissiongrant, and S22 the locks on the BFF-only columns sprk_externalinvitedemailhash,
// sprk_externalinvitedtenantid and sprk_externalaccessremovedon once they ship. Every step READS first
// and writes only what is missing, so a second run against a configured environment performs no write at all.
//
// THREE PHASES
//   1. Read and refuse. Everything that already exists is read and checked before anything is written: a refusal
//      (users in the unit, members on the team, a replica role, a privilege at the wrong depth, a stray field-security
//      writer on either lock, a root default team that reaches the unit by depth …) leaves the environment exactly as
//      it was found.
//   2. Write what is missing — or, on a dry run, only record what would be written. Order: S1 unit → S3 team → S4 role
//      → S5 grant → S6 strip (ALWAYS, after S5, even when S5 added nothing: creating a role injects ~9 platform
//      privileges and AddPrivilegesRole re-injects the SharePoint four — setup guide §5.4) → S7 assign → S8 contain →
//      S11 reader profile ↔ every default team → S12 writer profile ↔ the BFF's application users → S16 identity-link
//      reader profile ↔ every default team → S17 identity-link writer profile ↔ the BFF's application users → S13 NULL
//      flags.
//   3. Verify (S9): re-read the end state; anything but the codified state is a refusal.
//
// IDENTITY-LINK WRITERS. The BFF writes contact.sprk_externalobjectid and systemuser.sprk_primarycontact app-only with
// its stamp managed identity (DataverseContactIdentityStore over the BFF's TokenCredential); the writer profile holds
// exactly H10's two BFF application users — the same set as the BFF-managed writer profile (S12) and as the shipped
// profile's description and UAC-r2's dev configuration — and nobody else (S17 refuses any other member).
//
// WHAT IT NEVER DOES: move a user, remove a team member or a profile member, re-parent a business unit, widen or
// narrow an existing privilege, or create a field-security profile or secure a column. The profiles, both column locks
// and the deny-list table ship in SpaarkeMaster (H6); this procedure verifies them and adds only the memberships,
// which are data.
//
// CUSTOMER UNIT (T259 — ISS-010 / #1486, owner decision 2026-10-09; INCOMING-145 §6 T1/T3). Every user sits in the
// customer's own business unit (H10 created it and both BFF application users in it); the Secure Record unit is its
// SIBLING under the root and holds no user of any kind (S2). Phase 1 therefore also refuses, QuarantineRequired, a customer
// unit that is missing or not a direct child of the root (T1) and a BFF application user outside it (T3). Nothing here
// moves a user or re-parents a unit.
//
// NAMES. The business unit and the role come from config/secure-record-owner-role.json (the ONE file); the owner
// team's name is the BFF's compiled default (SecureRecordOwnerTeam.DefaultOwnerTeamName). Customer stamps run the BFF
// on its compiled defaults (scripts/canonical-secret-catalog/manifest.yaml sets neither SecureRecord__ key), and
// Spaarke.ArchTests SecureRecordOwnerRoleSetParityTests pins both facts.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;

/// <summary>What one Secure Record setup works on.</summary>
/// <param name="Target">The environment and the identity that signs in to it.</param>
/// <param name="RoleSet">The codified set (embedded <c>config/secure-record-owner-role.json</c>).</param>
/// <param name="BffApplicationUserIds">The BFF's Dataverse application users H10 registered — the only writer-profile members.</param>
/// <param name="CustomerBusinessUnitId">T259: the customer's business unit H10 created (InterStepState.CustomerBusinessUnitId).</param>
/// <param name="DryRun">Read everything, write nothing, return the plan.</param>
public sealed record SecureRecordSetupRequest(
    SecureRecordSetupTarget Target,
    SecureRecordOwnerRoleSet RoleSet,
    IReadOnlyList<Guid> BffApplicationUserIds,
    Guid CustomerBusinessUnitId,
    bool DryRun);

/// <summary>
/// The configured objects, for the handler's gate evidence. <paramref name="Notes"/> are read-only observations that are
/// not writes (S22: an external-invite column that is not in the environment yet).
/// </summary>
public sealed record SecureRecordSetupState(
    Guid BusinessUnitId,
    Guid OwnerTeamId,
    Guid RoleId,
    int PrivilegeCount,
    IReadOnlyList<string> LockedTables,
    IReadOnlyList<string>? Notes = null);

/// <summary>How a setup ended. <see cref="Actions"/> lists the writes performed (or, on a dry run, planned).</summary>
public abstract record SecureRecordSetupOutcome(IReadOnlyList<string> Actions)
{
    /// <summary>The environment is in the codified state (verified).</summary>
    public sealed record Applied(IReadOnlyList<string> Actions, SecureRecordSetupState State) : SecureRecordSetupOutcome(Actions);

    /// <summary>Dry run: nothing was written; <see cref="SecureRecordSetupOutcome.Actions"/> is the plan.</summary>
    public sealed record Planned(IReadOnlyList<string> Actions) : SecureRecordSetupOutcome(Actions);

    /// <summary>A step refused. <see cref="SecureRecordSetupOutcome.Actions"/> lists what was written before it.</summary>
    public sealed record Refused(IReadOnlyList<string> Actions, FailureClass Class, string RejectionCode, string Diagnostic)
        : SecureRecordSetupOutcome(Actions);
}

/// <summary>Runs the Secure Record setup against one environment through <see cref="ISecureRecordSetupDataverse"/>.</summary>
public sealed class SecureRecordSetupProcedure
{
    /// <summary>The named, non-default, memberless owner team (owner decision F9) — the BFF's <c>SecureRecordOwnerTeam.DefaultOwnerTeamName</c>.</summary>
    public const string OwnerTeamName = "Secure Record Owners";

    /// <summary>The platform role S8 removes from both teams of the unit — its display name, used in messages only.</summary>
    public const string SystemAdministratorRoleName = "System Administrator";

    /// <summary>
    /// The platform's role template id of the System Administrator role: the same in every environment and carried by each
    /// business unit's copy of the role. S8 and S9 match the role by this id, never by its display name, which an
    /// administrator can change or give to another role.
    /// </summary>
    public static readonly Guid SystemAdministratorRoleTemplateId = Guid.Parse("627090ff-40a3-4053-8790-584edc5be201");

    /// <summary>S10/S11: Read on the columns only the BFF writes; every business unit's default team is a member.</summary>
    public const string ReaderProfileName = "Spaarke BFF-Managed Field Readers";

    /// <summary>S10/S12: Read/Create/Update on those columns; the BFF's application users are its only members.</summary>
    public const string WriterProfileName = "Spaarke BFF-Managed Field Writers";

    /// <summary>S14: the platform's own field-security profile, exempt from the other-writer rule (owner decision F4).</summary>
    public const string SystemAdministratorProfileName = "System Administrator";

    /// <summary>S13/S14: the secure flag only the BFF writes (task 150).</summary>
    public const string SecureFlagColumn = "sprk_issecure";

    /// <summary>
    /// S15/S16: Read on the contact identity-binding columns (INCOMING-141, task 141); every business unit's default team is
    /// a member, because a field-secured column is HIDDEN from anyone without Read (the Console reads both as the user).
    /// </summary>
    public const string IdentityLinkReaderProfileName = "Spaarke Identity Link Readers";

    /// <summary>
    /// S15/S17: Read/Create/Update on the binding columns; the BFF's application users are its only members — every binding
    /// and every systemuser→contact link is written by the BFF (DataverseContactIdentityStore, app-only).
    /// </summary>
    public const string IdentityLinkWriterProfileName = "Spaarke Identity Link Writers";

    /// <summary>
    /// S18: the field-secured binding columns, as (table, column). The uniqueness mirrors
    /// <c>contact.sprk_externalobjectidkey</c>, <c>systemuser.sprk_hometenantidkey</c> and
    /// <c>systemuser.sprk_homeobjectidkey</c> are deliberately NOT secured (Dataverse cannot key a secured column).
    /// Entries three to five are the ADR-028 A9 home-identity columns (spaarke-SPA-external-access-platform-r3 task 044);
    /// the last six are the identity plane and identity collision bookkeeping columns, which only the BFF writes
    /// (scripts/Set-ExternalAccessContactSecuritySchema.ps1).
    /// </summary>
    public static readonly IReadOnlyList<(string Table, string Column)> IdentityLinkColumns =
    [
        ("contact", "sprk_externalobjectid"),
        ("systemuser", "sprk_primarycontact"),
        ("contact", "sprk_externaltenantid"),
        ("systemuser", "sprk_hometenantid"),
        ("systemuser", "sprk_homeobjectid"),
        ("contact", "sprk_identityplane"),
        ("contact", "sprk_identitycollisionon"),
        ("contact", "sprk_identitycollisionoid"),
        ("contact", "sprk_identitycollisionplane"),
        ("contact", "sprk_identitycollisionreason"),
        ("contact", "sprk_identitycollisionparties"),
    ];

    /// <summary>
    /// S19/S20 (ISS-020 / #1565): the profile whose membership lets an identity READ the field-secured
    /// <c>contact.sprk_standinggrant</c>. Without Read the platform silently strips the column from the BFF's app-only
    /// retrieve and every contact reads as "no standing grant" (teams-app-r1 notes/050-standing-grant-field-schema.md §5.4;
    /// <c>SubjectStandingGrantReader</c> fails closed with a WARNING). The BFF's application users are members; the
    /// profile grants Create/Read/Update and its other members (grant-privileged administrators) are an operator decision.
    /// </summary>
    public const string StandingGrantProfileName = "Standing Grant Administrators";

    /// <summary>S19: the standing-grant flag's table.</summary>
    public const string StandingGrantTable = "contact";

    /// <summary>S19: the standing-grant flag's column.</summary>
    public const string StandingGrantColumn = "sprk_standinggrant";

    /// <summary>S21: the table whose reads task 154 (owner decision O2) restricts to access administrators.</summary>
    public const string NoAccessEntryTable = "sprk_noaccessentry";

    /// <summary>S21: the role (in the ROOT unit, shipped in SpaarkeMaster) that reads and authors No Access entries.</summary>
    public const string AccessAdministratorRoleName = "Spaarke Access Administrator";

    /// <summary>S21: the role (in the ROOT unit) that must hold NO Read on <see cref="NoAccessEntryTable"/>.</summary>
    public const string CoreUserRoleName = "Spaarke Core User";

    /// <summary>S22 (ISS-025 / #1803): the table of both external-invite columns.</summary>
    public const string InviteTable = "contact";

    /// <summary>
    /// S22: the pending-invite marker (unified-access-control-r2 task 184). Ships in SpaarkeMaster 1.2.2.0 — absent is a
    /// failure. Readers 4/0/0, Writers 4/4/4 (<c>scripts/Set-ExternalInviteMarkerSchema.ps1 -Verify</c>).
    /// </summary>
    public const string InviteMarkerColumn = "sprk_externalinvitedon";

    /// <summary>
    /// S22: the invite-code hash (unified-access-control-r2 task 185). NOT in SpaarkeMaster 1.2.2.0 — absent is the expected
    /// state and is recorded, not failed. Readers no access, Writers 4/4/4
    /// (<c>scripts/Set-ExternalInviteCodeHashSchema.ps1 -Verify</c>).
    /// </summary>
    public const string InviteCodeHashColumn = "sprk_externalinvitecodehash";

    /// <summary>
    /// S19 (#2317): the access level on the standing-grant table. Secured with the standing-grant lock: Standing Grant
    /// Administrators 4/4/4, BFF-Managed Field Readers 4/0/0, BFF-Managed Field Writers 4/0/4; nobody else may create or
    /// update it (<c>scripts/Set-ExternalAccessContactSecuritySchema.ps1 -Verify</c>).
    /// </summary>
    public const string AccessLevelColumn = "sprk_accesspermissiongrant";

    /// <summary>
    /// S22 (#2317): a BFF-only column — the invited email's hash. Absent is the expected state until the SpaarkeMaster release
    /// that carries it (recorded, not failed). Present: secured; Writers 4/4/4; no other profile may read, create or update it.
    /// </summary>
    public const string InvitedEmailHashColumn = "sprk_externalinvitedemailhash";

    /// <summary>S22 (#2317): a BFF-only column — the invited tenant id. Same lock and absence rule as <see cref="InvitedEmailHashColumn"/>.</summary>
    public const string InvitedTenantIdColumn = "sprk_externalinvitedtenantid";

    /// <summary>
    /// S22 (#2317): when a person's external access was removed. Absent is the expected state until the SpaarkeMaster release
    /// that carries it. Present: secured; Readers 4/0/0; Writers 4/4/4; nobody else may create or update it.
    /// </summary>
    public const string AccessRemovedOnColumn = "sprk_externalaccessremovedon";

    /// <summary>
    /// S18/S19 (#2317): the first SpaarkeMaster release that secures the identity plane, collision and access-level columns
    /// with the locks S18 and S19 verify. An environment on an earlier release is refused until it carries them.
    /// </summary>
    public const string ContactLocksMinimumSpaarkeMasterVersion = "1.2.4.0";

    /// <summary>S18/S19 (#2317): the script that applies those locks to an existing environment (<c>-Apply</c>, then <c>-Verify</c>).</summary>
    public const string ContactSecuritySchemaScript = "scripts/Set-ExternalAccessContactSecuritySchema.ps1";

    /// <summary>S18/S19: the remedy an incomplete contact lock names — the minimum release, or the script for an existing environment.</summary>
    private const string ContactLocksRemedy =
        "It requires SpaarkeMaster " + ContactLocksMinimumSpaarkeMasterVersion + " or later: import it (H6), or in an existing " +
        "environment run " + ContactSecuritySchemaScript + " -Apply (then -Verify, exit 0); then resume. Nothing was written.";

    /// <summary>S22 (#2317): the BFF-only columns, checked as <see cref="InviteCodeHashColumn"/> is.</summary>
    public static readonly IReadOnlyList<string> BffOnlyColumns = [InvitedEmailHashColumn, InvitedTenantIdColumn];

    /// <summary>The depth the codified set grants.</summary>
    public const string BasicDepth = "Basic";

    /// <summary>Field-permission value for "allowed".</summary>
    public const int FieldPermissionAllowed = 4;

    /// <summary>How many offending principals a refusal names.</summary>
    internal const int NamedPrincipalLimit = 5;

    /// <summary>S13 page size.</summary>
    internal const int NullRowPageSize = 500;

    internal const string OwnerTeamDescription =
        "Owns every secure project, matter and work assignment (and the children filed to them). MUST have no members. " +
        "Not the business unit's default team. Created by provisioning (H7b).";

    internal const string RoleDescription =
        "Least-privilege role for the Secure Record OWNER team. Exists only so that team can be an assignment target for " +
        "secure records and the children filed to them (config/secure-record-owner-role.json). MUST NOT be granted to any " +
        "user or any other team. Created by provisioning (H7b).";

    private readonly ISecureRecordSetupDataverse _dataverse;
    private readonly ILogger _logger;

    /// <summary>Creates the procedure over the Dataverse seam.</summary>
    public SecureRecordSetupProcedure(ISecureRecordSetupDataverse dataverse, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(dataverse);
        ArgumentNullException.ThrowIfNull(logger);
        _dataverse = dataverse;
        _logger = logger;
    }

    /// <summary>
    /// Runs every step. Returns <see cref="SecureRecordSetupOutcome.Applied"/>, <see cref="SecureRecordSetupOutcome.Planned"/>
    /// (dry run) or <see cref="SecureRecordSetupOutcome.Refused"/>; throws <see cref="SecureRecordSetupDataverseException"/>
    /// for a Dataverse fault and lets cancellation propagate.
    /// </summary>
    public async Task<SecureRecordSetupOutcome> RunAsync(SecureRecordSetupRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var t = request.Target;
        var set = request.RoleSet;
        var dryRun = request.DryRun;
        var dv = _dataverse;
        var actions = new List<string>();
        var notes = new List<string>();

        SecureRecordSetupOutcome Refuse(FailureClass failureClass, string code, string diagnostic)
        {
            _logger.LogWarning("H7b refused: {Code} — {Diagnostic}", code, diagnostic);
            return new SecureRecordSetupOutcome.Refused(actions.ToArray(), failureClass, code,
                (dryRun ? "(dry run) " : string.Empty) + diagnostic);
        }

        async Task Write(string description, Func<Task> write)
        {
            actions.Add(description);
            if (!dryRun)
            {
                await write().ConfigureAwait(false);
            }
        }

        // ============================ PHASE 1 — read and refuse (no writes) ============================

        // P0 — sprk_noaccessentry (#1364). Without it the BFF's deny-list reader fails closed on every read.
        var probe = await dv.ProbeNoAccessEntryAsync(t, ct).ConfigureAwait(false);
        if (!probe.Present)
        {
            return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.NoAccessEntryMissing,
                $"The environment has no readable sprk_noaccessentry table with the columns the BFF's deny-list reader selects " +
                $"({probe.Detail}). The BFF would deny every read there, so it is not deployed (H9 waits for H7b). The table " +
                "ships in SpaarkeMaster 1.2.0.0 and later: import that package (H6), then resume. Nothing was written.");
        }

        // S0 — org setting. Org-wide, an operator decision: never flipped here.
        if (await dv.ReadShareToPreviousOwnerOnAssignAsync(t, ct).ConfigureAwait(false))
        {
            return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.OrgShareToPreviousOwnerOn,
                "organization.sharetopreviousowneronassign is true: every assignment of a record into the Secure Record " +
                "Owners team would share it back to its previous owner. It is an org-wide setting and an operator decision " +
                "(setup guide §4.1); turn it off, then resume. Nothing was written.");
        }

        var roots = await dv.FindRootBusinessUnitsAsync(t, ct).ConfigureAwait(false);
        if (roots.Count != 1)
        {
            return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.RootBusinessUnitUnresolved,
                $"The environment reports {roots.Count} root business units (expected exactly one). Nothing was written.");
        }
        var root = roots[0];

        // T259 §6 T1 — the customer's own unit (H10): it exists and is a DIRECT child of the root, a sibling of the Secure
        // Record unit. Under any other unit (or as the root) Deep depth there could reach the secure unit.
        var customerUnit = await dv.GetBusinessUnitAsync(t, request.CustomerBusinessUnitId, ct).ConfigureAwait(false);
        if (customerUnit is null)
        {
            return Refuse(FailureClass.QuarantineRequired, SecureRecordSetupRejectionCodes.CustomerBusinessUnitMissing,
                $"The customer's business unit {request.CustomerBusinessUnitId} (recorded by H10) does not exist. H10 created it " +
                "and the BFF's application users in it; find out what removed it (an owner decision) before anything is " +
                "configured. Nothing was written.");
        }
        if (customerUnit.ParentId != root.Id)
        {
            return Refuse(FailureClass.QuarantineRequired, SecureRecordSetupRejectionCodes.CustomerBusinessUnitWrongParent,
                $"The customer's business unit '{customerUnit.Name}' ({customerUnit.Id}) has parent " +
                $"{customerUnit.ParentId?.ToString() ?? "(none — it is the root)"}, not the root unit {root.Id}. It must be a " +
                "DIRECT child of the root, a sibling of the Secure Record unit (INCOMING-145 §6 T1). Re-parenting a unit is " +
                "an owner decision. Nothing was written.");
        }

        // T259 §6 T3 — both BFF application users are in the customer's unit (never the root, never the secure unit).
        foreach (var appUser in request.BffApplicationUserIds.Distinct())
        {
            var appUserUnit = await dv.GetUserBusinessUnitAsync(t, appUser, ct).ConfigureAwait(false);
            if (appUserUnit != customerUnit.Id)
            {
                return Refuse(FailureClass.QuarantineRequired, SecureRecordSetupRejectionCodes.AppUserOutsideCustomerBusinessUnit,
                    $"BFF application user {appUser} is in business unit {appUserUnit?.ToString() ?? "(no such user)"}, not the " +
                    $"customer's unit '{customerUnit.Name}' ({customerUnit.Id}) (INCOMING-145 §6 T3). Moving it strips its " +
                    "roles — an owner decision. Nothing was written.");
            }
        }

        // S4 (root half) — a role of this name in the ROOT unit is copied into every unit, so a unit created below would
        // already hold a replica beside the one S4 creates, assignable everywhere (T218e: why the role never ships).
        var rootRoles = await dv.FindRolesAsync(t, set.RoleName, root.Id, ct).ConfigureAwait(false);
        if (rootRoles.Count > 0)
        {
            return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.RoleIsReplica,
                $"A role named '{set.RoleName}' exists in the ROOT unit ({Ids(rootRoles.Select(r => r.Id))}). Dataverse copies a " +
                "root role into every unit, so it is assignable everywhere — the setup guide (§5.2) creates this role IN the " +
                "Secure Record unit only, which is also why no package carries it. Remove the root role (and whatever " +
                "installed it), then resume. Nothing was written.");
        }

        // S5 (read half) — each table's Read privilege from METADATA; its name must equal the file's, ordinally.
        var wanted = new List<SecureSetupPrivilege>(set.Tables.Count);
        foreach (var table in set.Tables)
        {
            var privileges = await dv.GetReadPrivilegesAsync(t, table.LogicalName, ct).ConfigureAwait(false);
            if (privileges.Count != 1)
            {
                return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.PrivilegeNameMismatch,
                    $"Table '{table.LogicalName}': metadata reports {privileges.Count} Read privileges (expected one). A table " +
                    "of config/secure-record-owner-role.json is missing from the environment (is the package imported?) or " +
                    "the file names a table that does not exist. Nothing was written.");
            }
            if (!string.Equals(privileges[0].Name, table.PrivilegeName, StringComparison.Ordinal))
            {
                return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.PrivilegeNameMismatch,
                    $"Table '{table.LogicalName}': metadata names its Read privilege '{privileges[0].Name}', the file says " +
                    $"'{table.PrivilegeName}'. The file is wrong (casing follows the schema name) or the table was renamed. " +
                    "Nothing was written.");
            }
            wanted.Add(privileges[0]);
        }
        var wantedIds = wanted.Select(p => p.Id).ToHashSet();

        // S1 + §6 T2 — the unit, exactly one, a DIRECT child of the root (a sibling of the customer's unit).
        var units = await dv.FindBusinessUnitsByNameAsync(t, set.BusinessUnitName, ct).ConfigureAwait(false);
        if (units.Count > 1)
        {
            return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.BusinessUnitAmbiguous,
                $"{units.Count} business units are named '{set.BusinessUnitName}' ({Ids(units.Select(u => u.Id))}). The BFF " +
                "refuses an ambiguous Secure Record unit (task 144); rename or remove the extra one. Nothing was written.");
        }
        var unit = units.Count == 1 ? units[0] : null;
        if (unit is not null && unit.ParentId != root.Id)
        {
            return Refuse(FailureClass.QuarantineRequired, SecureRecordSetupRejectionCodes.BusinessUnitWrongParent,
                $"Business unit '{set.BusinessUnitName}' ({unit.Id}) has parent {unit.ParentId?.ToString() ?? "(none — it is the root)"}, " +
                $"not the root unit {root.Id}. It must be a DIRECT child of the root (INCOMING-145 §6 T2): under any other " +
                "unit, users there reach every secure record at Deep depth. Re-parenting a unit is an owner decision. " +
                "Nothing was written.");
        }

        SecureSetupTeam? team = null;
        SecureSetupRole? role = null;
        IReadOnlyList<SecureSetupHeldPrivilege> held = [];
        if (unit is not null)
        {
            // S2 — no users in the unit, ever (enabled or disabled, human or application). Never moved here.
            var users = await dv.ListBusinessUnitUsersAsync(t, unit.Id, NamedPrincipalLimit, ct).ConfigureAwait(false);
            if (users.Count > 0)
            {
                return Refuse(FailureClass.QuarantineRequired, SecureRecordSetupRejectionCodes.BusinessUnitHasUsers,
                    $"Business unit '{set.BusinessUnitName}' holds systemusers ({Ids(users)}). Any user there reads every " +
                    "secure record by depth. Moving users is an owner decision (task 144). Nothing was written.");
            }

            // S3 — the NAMED, non-default owner team: at most one, memberless.
            var teams = await dv.FindNamedOwnerTeamsAsync(t, unit.Id, OwnerTeamName, ct).ConfigureAwait(false);
            if (teams.Count > 1)
            {
                return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.OwnerTeamAmbiguous,
                    $"{teams.Count} owner teams named '{OwnerTeamName}' are in the unit ({Ids(teams.Select(x => x.Id))}). " +
                    "Nothing was written.");
            }
            team = teams.Count == 1 ? teams[0] : null;
            if (team is not null)
            {
                var members = await dv.ListTeamMembersAsync(t, team.Id, NamedPrincipalLimit, ct).ConfigureAwait(false);
                if (members.Count > 0)
                {
                    return Refuse(FailureClass.QuarantineRequired, SecureRecordSetupRejectionCodes.OwnerTeamHasMembers,
                        $"Team '{OwnerTeamName}' ({team.Id}) has members ({Ids(members)}). A member reads every secure record " +
                        "by ownership; removing members is an owner decision. Nothing was written.");
                }
            }

            // S4 — the role, at most one, created IN this unit (never a replica of an ancestor unit's role).
            var roles = await dv.FindRolesAsync(t, set.RoleName, unit.Id, ct).ConfigureAwait(false);
            if (roles.Count > 1)
            {
                return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.RoleAmbiguous,
                    $"{roles.Count} roles named '{set.RoleName}' are in the unit ({Ids(roles.Select(r => r.Id))}). " +
                    "Nothing was written.");
            }
            role = roles.Count == 1 ? roles[0] : null;
            if (role is not null && role.ParentRootRoleId is { } parentRoot && parentRoot != role.Id)
            {
                return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.RoleIsReplica,
                    $"Role '{set.RoleName}' ({role.Id}) in the unit is a replica of root role {parentRoot}: it was created in an " +
                    "ancestor unit and is assignable in every unit. The setup guide (§5.2) creates it IN the Secure Record " +
                    "unit. Remove the ancestor role, then resume. Nothing was written.");
            }

            // S5 (depth half) — a listed privilege held at another depth is never widened or narrowed here.
            if (role is not null)
            {
                held = await dv.GetRolePrivilegesAsync(t, role.Id, ct).ConfigureAwait(false);
                foreach (var privilege in wanted)
                {
                    var holding = held.FirstOrDefault(h => h.PrivilegeId == privilege.Id);
                    if (holding is not null && !Same(holding.Depth, BasicDepth))
                    {
                        return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.PrivilegeWrongDepth,
                            $"Role '{set.RoleName}' holds {privilege.Name} at {holding.Depth}; the codified set grants {BasicDepth} " +
                            "only. Changing an existing grant is an owner decision (setup guide §5.1). Nothing was written.");
                    }
                }
            }
        }

        // S10 — the two BFF-managed profiles ship in SpaarkeMaster: exactly one of each.
        var readerProfile = await ResolveProfileAsync(dv, t, ReaderProfileName, ct).ConfigureAwait(false);
        var writerProfile = await ResolveProfileAsync(dv, t, WriterProfileName, ct).ConfigureAwait(false);
        if (readerProfile.Refusal is not null || writerProfile.Refusal is not null)
        {
            return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.FieldProfileUnresolved,
                (readerProfile.Refusal ?? writerProfile.Refusal)!);
        }
        var readerProfileId = readerProfile.Id!.Value;
        var writerProfileId = writerProfile.Id!.Value;

        // S12 (read half) — the writer profile's membership IS the lock: nobody but the BFF's application users.
        var bffUsers = request.BffApplicationUserIds.Distinct().ToArray();
        var writerUsers = await dv.ListProfileUsersAsync(t, writerProfileId, ct).ConfigureAwait(false);
        var writerTeams = await dv.ListProfileTeamsAsync(t, writerProfileId, ct).ConfigureAwait(false);
        var strayWriters = writerUsers.Where(u => !bffUsers.Contains(u.UserId)).ToArray();
        if (strayWriters.Length > 0 || writerTeams.Count > 0)
        {
            return Refuse(FailureClass.QuarantineRequired, SecureRecordSetupRejectionCodes.FieldWriterHasOtherMember,
                $"'{WriterProfileName}' has members other than the BFF's application users ({string.Join(", ", bffUsers)}): " +
                $"users [{Ids(strayWriters.Select(u => u.UserId))}], teams [{Ids(writerTeams)}]. Every one of them could mark " +
                "a record secure or not without provisioning it. Who belongs there is an operator decision; nothing is " +
                "removed here. Nothing was written.");
        }

        // S14 — the lock on sprk_issecure ships in SpaarkeMaster: verify it is whole, and that nobody else may write it.
        var permissions = await dv.ListFieldPermissionsAsync(t, SecureFlagColumn, ct).ConfigureAwait(false);
        var lockedTables = permissions
            .Where(p => p.ProfileId == writerProfileId)
            .Select(p => p.EntityName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (lockedTables.Length == 0)
        {
            return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.FieldLockIncomplete,
                $"'{WriterProfileName}' grants {SecureFlagColumn} on no table: the package's field-security lock (task 150) " +
                "is missing. Import SpaarkeMaster (H6), then resume. Nothing was written.");
        }
        var systemAdministratorProfiles = await dv.FindFieldSecurityProfilesAsync(t, SystemAdministratorProfileName, ct)
            .ConfigureAwait(false);
        foreach (var table in lockedTables)
        {
            var secured = await dv.IsAttributeSecuredAsync(t, table, SecureFlagColumn, ct).ConfigureAwait(false);
            var reader = permissions.FirstOrDefault(p => p.ProfileId == readerProfileId && Same(p.EntityName, table));
            var writer = permissions.First(p => p.ProfileId == writerProfileId && Same(p.EntityName, table));
            if (secured != true
                || reader is null || reader.CanRead != FieldPermissionAllowed
                || writer.CanRead != FieldPermissionAllowed || writer.CanCreate != FieldPermissionAllowed
                || writer.CanUpdate != FieldPermissionAllowed)
            {
                return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.FieldLockIncomplete,
                    $"{table}.{SecureFlagColumn}: IsSecured={secured?.ToString() ?? "(no such column)"}, reader " +
                    $"read={reader?.CanRead.ToString() ?? "(none)"}, writer read/create/update=" +
                    $"{writer.CanRead}/{writer.CanCreate}/{writer.CanUpdate} (expected secured, 4, 4/4/4). The package's lock " +
                    "is incomplete. Nothing was written.");
            }
        }
        // The reader profile is NOT exempt: every default team is its member, so a write grant there is a write grant to
        // every user.
        var otherWriters = permissions
            .Where(p => p.ProfileId != writerProfileId
                        && !systemAdministratorProfiles.Contains(p.ProfileId)
                        && (p.CanCreate == FieldPermissionAllowed || p.CanUpdate == FieldPermissionAllowed))
            .ToArray();
        if (otherWriters.Length > 0)
        {
            return Refuse(FailureClass.QuarantineRequired, SecureRecordSetupRejectionCodes.FieldLockOtherWriter,
                $"Profiles other than '{WriterProfileName}' may create or update {SecureFlagColumn}: " +
                $"{string.Join(", ", otherWriters.Take(NamedPrincipalLimit).Select(p => $"{p.ProfileId} on {p.EntityName}"))}. " +
                "The lock is only as strong as its writer list (owner decision F4). Nothing was written.");
        }

        // S15 — the two identity-link profiles (INCOMING-141) ship in SpaarkeMaster: exactly one of each.
        var linkReaderProfile = await ResolveProfileAsync(dv, t, IdentityLinkReaderProfileName, ct).ConfigureAwait(false);
        var linkWriterProfile = await ResolveProfileAsync(dv, t, IdentityLinkWriterProfileName, ct).ConfigureAwait(false);
        if (linkReaderProfile.Refusal is not null || linkWriterProfile.Refusal is not null)
        {
            return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.FieldProfileUnresolved,
                (linkReaderProfile.Refusal ?? linkWriterProfile.Refusal)!);
        }
        var linkReaderProfileId = linkReaderProfile.Id!.Value;
        var linkWriterProfileId = linkWriterProfile.Id!.Value;

        // S17 (read half) — as S12: the link-writer profile's membership IS the lock on whose grants a caller inherits.
        var linkWriterUsers = await dv.ListProfileUsersAsync(t, linkWriterProfileId, ct).ConfigureAwait(false);
        var linkWriterTeams = await dv.ListProfileTeamsAsync(t, linkWriterProfileId, ct).ConfigureAwait(false);
        var strayLinkWriters = linkWriterUsers.Where(u => !bffUsers.Contains(u.UserId)).ToArray();
        if (strayLinkWriters.Length > 0 || linkWriterTeams.Count > 0)
        {
            return Refuse(FailureClass.QuarantineRequired, SecureRecordSetupRejectionCodes.IdentityLinkWriterHasOtherMember,
                $"'{IdentityLinkWriterProfileName}' has members other than the BFF's application users ({string.Join(", ", bffUsers)}): " +
                $"users [{Ids(strayLinkWriters.Select(u => u.UserId))}], teams [{Ids(linkWriterTeams)}]. Every one of them could " +
                "bind any contact to any identity, or point any user at any contact, and so choose whose grants a caller " +
                "inherits. Who belongs there is an operator decision; nothing is removed here. Nothing was written.");
        }

        // S18 — the lock on the binding columns ships in SpaarkeMaster: verify it is whole, and that nobody else may write.
        foreach (var (table, column) in IdentityLinkColumns)
        {
            var columnPermissions = (await dv.ListFieldPermissionsAsync(t, column, ct).ConfigureAwait(false))
                .Where(p => Same(p.EntityName, table))
                .ToArray();
            var secured = await dv.IsAttributeSecuredAsync(t, table, column, ct).ConfigureAwait(false);
            var reader = columnPermissions.FirstOrDefault(p => p.ProfileId == linkReaderProfileId);
            var writer = columnPermissions.FirstOrDefault(p => p.ProfileId == linkWriterProfileId);
            if (secured != true
                || reader is null || reader.CanRead != FieldPermissionAllowed
                || writer is null || writer.CanRead != FieldPermissionAllowed || writer.CanCreate != FieldPermissionAllowed
                || writer.CanUpdate != FieldPermissionAllowed)
            {
                return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.IdentityLinkLockIncomplete,
                    $"{table}.{column}: IsSecured={secured?.ToString() ?? "(no such column)"}, " +
                    $"'{IdentityLinkReaderProfileName}' read={reader?.CanRead.ToString() ?? "(none)"}, " +
                    $"'{IdentityLinkWriterProfileName}' read/create/update=" +
                    $"{(writer is null ? "(none)" : $"{writer.CanRead}/{writer.CanCreate}/{writer.CanUpdate}")} " +
                    "(expected secured, 4, 4/4/4). The package's identity-binding lock (task 141) is incomplete. " +
                    ContactLocksRemedy);
            }
            var otherLinkWriters = columnPermissions
                .Where(p => p.ProfileId != linkWriterProfileId
                            && !systemAdministratorProfiles.Contains(p.ProfileId)
                            && (p.CanCreate == FieldPermissionAllowed || p.CanUpdate == FieldPermissionAllowed))
                .ToArray();
            if (otherLinkWriters.Length > 0)
            {
                return Refuse(FailureClass.QuarantineRequired, SecureRecordSetupRejectionCodes.IdentityLinkLockOtherWriter,
                    $"Profiles other than '{IdentityLinkWriterProfileName}' may create or update {table}.{column}: " +
                    $"{Ids(otherLinkWriters.Select(p => p.ProfileId))}. The lock is only as strong as its writer list — a " +
                    "client write would decide whose grants a caller inherits. Nothing was written.");
            }
        }

        // S19 — the standing-grant profile ships in SpaarkeMaster: exactly one, and the shipped lock on the flag is whole:
        // secured, Standing Grant Administrators 4/4/4. No other profile may create or update the flag — the Readers profile
        // included (every default team is its member). System Administrator is the platform's own profile (owner decision F4).
        var standingProfile = await ResolveProfileAsync(dv, t, StandingGrantProfileName, ct).ConfigureAwait(false);
        if (standingProfile.Refusal is not null)
        {
            return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.FieldProfileUnresolved, standingProfile.Refusal);
        }
        var standingProfileId = standingProfile.Id!.Value;
        var standingSecured = await dv.IsAttributeSecuredAsync(t, StandingGrantTable, StandingGrantColumn, ct).ConfigureAwait(false);
        var standingPermissions = (await dv.ListFieldPermissionsAsync(t, StandingGrantColumn, ct).ConfigureAwait(false))
            .Where(p => Same(p.EntityName, StandingGrantTable))
            .ToArray();
        var standingGrant = standingPermissions.FirstOrDefault(p => p.ProfileId == standingProfileId);
        if (standingSecured != true
            || standingGrant is null || standingGrant.CanRead != FieldPermissionAllowed
            || standingGrant.CanCreate != FieldPermissionAllowed || standingGrant.CanUpdate != FieldPermissionAllowed)
        {
            return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.StandingGrantLockIncomplete,
                $"{StandingGrantTable}.{StandingGrantColumn}: IsSecured={standingSecured?.ToString() ?? "(no such column)"}, " +
                $"'{StandingGrantProfileName}' read/create/update={Rights(standingGrant)} (expected secured, 4/4/4). The " +
                "package's standing-grant field and profile (teams-app-r1 task 050; SpaarkeMaster 1.2.0.0 and later) are " +
                "incomplete: import SpaarkeMaster (H6), then resume. Nothing was written.");
        }
        var standingOtherWriters = standingPermissions
            .Where(p => p.ProfileId != standingProfileId
                        && !systemAdministratorProfiles.Contains(p.ProfileId)
                        && (p.CanCreate == FieldPermissionAllowed || p.CanUpdate == FieldPermissionAllowed))
            .ToArray();
        if (standingOtherWriters.Length > 0)
        {
            return Refuse(FailureClass.QuarantineRequired, SecureRecordSetupRejectionCodes.StandingGrantLockOtherWriter,
                $"Profiles other than '{StandingGrantProfileName}' may create or update {StandingGrantTable}.{StandingGrantColumn}: " +
                $"{Ids(standingOtherWriters.Select(p => p.ProfileId))} (the Readers profile included — every default team is its " +
                "member). Only Standing Grant Administrators and System Administrator may create or update the standing grant; " +
                "who was granted more is an operator decision. Nothing was written.");
        }

        // S19 (#2317) — the access level ships secured with the same lock: Standing Grant Administrators 4/4/4, Readers 4/0/0,
        // Writers 4/0/4 (the BFF updates it). No other profile may create or update it — the Readers profile included (every
        // default team is its member; it may only read). System Administrator is the platform's own profile (owner decision F4).
        var levelSecured = await dv.IsAttributeSecuredAsync(t, StandingGrantTable, AccessLevelColumn, ct).ConfigureAwait(false);
        var levelPermissions = (await dv.ListFieldPermissionsAsync(t, AccessLevelColumn, ct).ConfigureAwait(false))
            .Where(p => Same(p.EntityName, StandingGrantTable))
            .ToArray();
        var levelStanding = levelPermissions.FirstOrDefault(p => p.ProfileId == standingProfileId);
        var levelReader = levelPermissions.FirstOrDefault(p => p.ProfileId == readerProfileId);
        var levelWriter = levelPermissions.FirstOrDefault(p => p.ProfileId == writerProfileId);
        if (levelSecured != true
            || levelStanding is null || levelStanding.CanRead != FieldPermissionAllowed
            || levelStanding.CanCreate != FieldPermissionAllowed || levelStanding.CanUpdate != FieldPermissionAllowed
            || levelReader is null || levelReader.CanRead != FieldPermissionAllowed
            || levelWriter is null || levelWriter.CanRead != FieldPermissionAllowed || levelWriter.CanUpdate != FieldPermissionAllowed)
        {
            return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.AccessLevelLockIncomplete,
                $"{StandingGrantTable}.{AccessLevelColumn}: IsSecured={levelSecured?.ToString() ?? "(no such column)"}, " +
                $"'{StandingGrantProfileName}' read/create/update={Rights(levelStanding)}, " +
                $"'{ReaderProfileName}' read={levelReader?.CanRead.ToString() ?? "(none)"}, " +
                $"'{WriterProfileName}' read/update={(levelWriter is null ? "(none)" : $"{levelWriter.CanRead}/{levelWriter.CanUpdate}")} " +
                "(expected secured, 4/4/4, 4, 4/4). The package's access-level lock is incomplete. " + ContactLocksRemedy);
        }
        var levelOtherWriters = levelPermissions
            .Where(p => p.ProfileId != standingProfileId
                        && p.ProfileId != writerProfileId
                        && !systemAdministratorProfiles.Contains(p.ProfileId)
                        && (p.CanCreate == FieldPermissionAllowed || p.CanUpdate == FieldPermissionAllowed))
            .ToArray();
        if (levelOtherWriters.Length > 0)
        {
            return Refuse(FailureClass.QuarantineRequired, SecureRecordSetupRejectionCodes.AccessLevelLockOtherWriter,
                $"Profiles other than '{StandingGrantProfileName}' and '{WriterProfileName}' may create or update " +
                $"{StandingGrantTable}.{AccessLevelColumn}: {Ids(levelOtherWriters.Select(p => p.ProfileId))} (the Readers profile " +
                "included — every default team is its member). Only the standing-grant administrators and the BFF set an " +
                "access level; who was granted more is an operator decision. Nothing was written.");
        }

        var standingUsers = await dv.ListProfileUsersAsync(t, standingProfileId, ct).ConfigureAwait(false);

        // S21 — task 154 (owner decision O2) ships in SpaarkeMaster: the Access Administrator role holds Read on No Access
        // entries at Global and Spaarke Core User holds none. Verified, never repaired: changing an existing role is an
        // owner decision (scripts/Set-NoAccessEntryRolePrivileges.ps1 is the operator's repair).
        var entryReads = await dv.GetReadPrivilegesAsync(t, NoAccessEntryTable, ct).ConfigureAwait(false);
        if (entryReads.Count != 1)
        {
            return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.NoAccessEntryRolesIncomplete,
                $"Table '{NoAccessEntryTable}': metadata reports {entryReads.Count} Read privileges (expected one). Nothing was written.");
        }
        var entryRead = entryReads[0];
        var accessAdministrators = await dv.FindRolesAsync(t, AccessAdministratorRoleName, root.Id, ct).ConfigureAwait(false);
        var coreUsers = await dv.FindRolesAsync(t, CoreUserRoleName, root.Id, ct).ConfigureAwait(false);
        if (accessAdministrators.Count != 1 || coreUsers.Count != 1)
        {
            return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.NoAccessEntryRolesIncomplete,
                $"The root unit holds {accessAdministrators.Count} '{AccessAdministratorRoleName}' and {coreUsers.Count} " +
                $"'{CoreUserRoleName}' roles (expected one of each). Both ship in SpaarkeMaster (unified-access-control-r2 task 154): " +
                "import the package (H6), then resume. Nothing was written.");
        }
        var accessAdministratorHolding = (await dv.GetRolePrivilegesAsync(t, accessAdministrators[0].Id, ct).ConfigureAwait(false))
            .FirstOrDefault(h => h.PrivilegeId == entryRead.Id);
        var coreUserHolding = (await dv.GetRolePrivilegesAsync(t, coreUsers[0].Id, ct).ConfigureAwait(false))
            .FirstOrDefault(h => h.PrivilegeId == entryRead.Id);
        if (accessAdministratorHolding is null || !Same(accessAdministratorHolding.Depth, "Global") || coreUserHolding is not null)
        {
            return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.NoAccessEntryRolesIncomplete,
                $"'{AccessAdministratorRoleName}' holds {entryRead.Name} at {accessAdministratorHolding?.Depth ?? "(none)"} " +
                $"(expected Global); '{CoreUserRoleName}' holds it at {coreUserHolding?.Depth ?? "(none)"} (expected none). " +
                "Task 154 (owner decision O2) restricts No Access entries to access administrators. Run " +
                "scripts/Set-NoAccessEntryRolePrivileges.ps1 (dry run, then -Apply) or re-import SpaarkeMaster. Nothing was written.");
        }

        // S22 (ISS-025 / #1803) — the external-invite columns' locks, VERIFIED ONLY (the columns, their grants and the profiles
        // ship in SpaarkeMaster; H7b never secures a column and writes no field-permission row). Expectations are those of
        // scripts/Set-ExternalInviteMarkerSchema.ps1 -Verify and scripts/Set-ExternalInviteCodeHashSchema.ps1 -Verify.
        //   sprk_externalinvitedon     : secured; Readers 4/0/0; Writers 4/4/4; nobody else may create or update. ABSENT = fail
        //                                (it ships in 1.2.2.0).
        //   sprk_externalinvitecodehash: ABSENT is expected until the SpaarkeMaster release that carries it (recorded, logged,
        //                                not failed). Present: secured; Writers 4/4/4; NO other profile may read, create or
        //                                update it — the Readers profile has no row or a 0/0/0 row. System Administrator is
        //                                the platform's own profile and is exempt (owner decision F4).
        var markerSecured = await dv.IsAttributeSecuredAsync(t, InviteTable, InviteMarkerColumn, ct).ConfigureAwait(false);
        var markerPermissions = (await dv.ListFieldPermissionsAsync(t, InviteMarkerColumn, ct).ConfigureAwait(false))
            .Where(p => Same(p.EntityName, InviteTable))
            .ToArray();
        var markerReader = markerPermissions.FirstOrDefault(p => p.ProfileId == readerProfileId);
        var markerWriter = markerPermissions.FirstOrDefault(p => p.ProfileId == writerProfileId);
        if (markerSecured != true
            || markerReader is null || markerReader.CanRead != FieldPermissionAllowed
            || markerWriter is null || markerWriter.CanRead != FieldPermissionAllowed
            || markerWriter.CanCreate != FieldPermissionAllowed || markerWriter.CanUpdate != FieldPermissionAllowed)
        {
            return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.InviteMarkerLockIncomplete,
                $"{InviteTable}.{InviteMarkerColumn}: IsSecured={markerSecured?.ToString() ?? "(no such column)"}, " +
                $"'{ReaderProfileName}' read={markerReader?.CanRead.ToString() ?? "(none)"}, " +
                $"'{WriterProfileName}' read/create/update=" +
                $"{(markerWriter is null ? "(none)" : $"{markerWriter.CanRead}/{markerWriter.CanCreate}/{markerWriter.CanUpdate}")} " +
                "(expected secured, 4, 4/4/4). The package's pending-invite lock (unified-access-control-r2 task 184) is " +
                "incomplete — the column ships in SpaarkeMaster 1.2.2.0 and later: import SpaarkeMaster (H6), then resume. " +
                "Nothing was written.");
        }
        var markerOtherWriters = markerPermissions
            .Where(p => p.ProfileId != writerProfileId
                        && !systemAdministratorProfiles.Contains(p.ProfileId)
                        && (p.CanCreate == FieldPermissionAllowed || p.CanUpdate == FieldPermissionAllowed))
            .ToArray();
        if (markerOtherWriters.Length > 0)
        {
            return Refuse(FailureClass.QuarantineRequired, SecureRecordSetupRejectionCodes.InviteMarkerLockOtherWriter,
                $"Profiles other than '{WriterProfileName}' may create or update {InviteTable}.{InviteMarkerColumn}: " +
                $"{Ids(markerOtherWriters.Select(p => p.ProfileId))} (the Readers profile included — every default team is its " +
                "member). Whoever can write the marker can open a contact to an email bind. Nothing was written.");
        }

        var codeHashSecured = await dv.IsAttributeSecuredAsync(t, InviteTable, InviteCodeHashColumn, ct).ConfigureAwait(false);
        if (codeHashSecured is null)
        {
            var note = $"{InviteTable}.{InviteCodeHashColumn}: absent — ships in a later SpaarkeMaster release " +
                       "(unified-access-control-r2 task 185); its lock is not verified in this environment yet.";
            notes.Add(note);
            _logger.LogInformation("H7b S22: {Note}", note);
        }
        else
        {
            var hashPermissions = (await dv.ListFieldPermissionsAsync(t, InviteCodeHashColumn, ct).ConfigureAwait(false))
                .Where(p => Same(p.EntityName, InviteTable))
                .ToArray();
            var hashWriter = hashPermissions.FirstOrDefault(p => p.ProfileId == writerProfileId);
            if (codeHashSecured != true
                || hashWriter is null || hashWriter.CanRead != FieldPermissionAllowed
                || hashWriter.CanCreate != FieldPermissionAllowed || hashWriter.CanUpdate != FieldPermissionAllowed)
            {
                return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.InviteCodeHashLockIncomplete,
                    $"{InviteTable}.{InviteCodeHashColumn}: IsSecured={codeHashSecured}, '{WriterProfileName}' read/create/update=" +
                    $"{(hashWriter is null ? "(none)" : $"{hashWriter.CanRead}/{hashWriter.CanCreate}/{hashWriter.CanUpdate}")} " +
                    "(expected secured, 4/4/4). The package's invite-code lock (unified-access-control-r2 task 185) is " +
                    "incomplete: import SpaarkeMaster (H6), then resume. Nothing was written.");
            }
            var hashOtherAccess = hashPermissions
                .Where(p => p.ProfileId != writerProfileId
                            && !systemAdministratorProfiles.Contains(p.ProfileId)
                            && (p.CanRead > 0 || p.CanCreate > 0 || p.CanUpdate > 0))
                .ToArray();
            if (hashOtherAccess.Length > 0)
            {
                return Refuse(FailureClass.QuarantineRequired, SecureRecordSetupRejectionCodes.InviteCodeHashLockOtherAccess,
                    $"Profiles other than '{WriterProfileName}' can read, create or update {InviteTable}.{InviteCodeHashColumn}: " +
                    $"{string.Join(", ", hashOtherAccess.Take(NamedPrincipalLimit).Select(p => $"{p.ProfileId} read/create/update={p.CanRead}/{p.CanCreate}/{p.CanUpdate}"))}" +
                    $" (the Readers profile must hold no row or 0/0/0 — every default team is its member). Only the BFF may " +
                    "touch the invite-code hash; who was granted access is an operator decision. Nothing was written.");
            }
            notes.Add($"{InviteTable}.{InviteCodeHashColumn}: present, secured, Writers 4/4/4, no other access.");
        }

        // S22 (#2317) — the BFF-only columns, as the invite-code hash: ABSENT is expected until the SpaarkeMaster release that
        // carries them (recorded, not failed). Present: secured; Writers 4/4/4; NO other profile may read, create or update.
        foreach (var column in BffOnlyColumns)
        {
            var secured = await dv.IsAttributeSecuredAsync(t, InviteTable, column, ct).ConfigureAwait(false);
            if (secured is null)
            {
                var note = $"{InviteTable}.{column}: absent — ships in a later SpaarkeMaster release; its lock is not " +
                           "verified in this environment yet.";
                notes.Add(note);
                _logger.LogInformation("H7b S22: {Note}", note);
                continue;
            }

            var columnPermissions = (await dv.ListFieldPermissionsAsync(t, column, ct).ConfigureAwait(false))
                .Where(p => Same(p.EntityName, InviteTable))
                .ToArray();
            var columnWriter = columnPermissions.FirstOrDefault(p => p.ProfileId == writerProfileId);
            if (secured != true
                || columnWriter is null || columnWriter.CanRead != FieldPermissionAllowed
                || columnWriter.CanCreate != FieldPermissionAllowed || columnWriter.CanUpdate != FieldPermissionAllowed)
            {
                return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.BffOnlyColumnLockIncomplete,
                    $"{InviteTable}.{column}: IsSecured={secured}, '{WriterProfileName}' read/create/update={Rights(columnWriter)} " +
                    "(expected secured, 4/4/4). The package's lock on this BFF-only column is incomplete: import " +
                    "SpaarkeMaster (H6), then resume. Nothing was written.");
            }
            var columnOtherAccess = columnPermissions
                .Where(p => p.ProfileId != writerProfileId
                            && !systemAdministratorProfiles.Contains(p.ProfileId)
                            && (p.CanRead > 0 || p.CanCreate > 0 || p.CanUpdate > 0))
                .ToArray();
            if (columnOtherAccess.Length > 0)
            {
                return Refuse(FailureClass.QuarantineRequired, SecureRecordSetupRejectionCodes.BffOnlyColumnLockOtherAccess,
                    $"Profiles other than '{WriterProfileName}' can read, create or update {InviteTable}.{column}: " +
                    $"{string.Join(", ", columnOtherAccess.Take(NamedPrincipalLimit).Select(p => $"{p.ProfileId} read/create/update={Rights(p)}"))}" +
                    " (the Readers profile must hold no row or 0/0/0 — every default team is its member). Only the BFF may " +
                    "touch this column; who was granted access is an operator decision. Nothing was written.");
            }
            notes.Add($"{InviteTable}.{column}: present, secured, Writers 4/4/4, no other access.");
        }

        // S22 (#2317) — the removed-on marker: ABSENT is expected until the SpaarkeMaster release that carries it. Present:
        // secured; Readers 4/0/0; Writers 4/4/4; nobody else may create or update it (the Readers profile included).
        var removedSecured = await dv.IsAttributeSecuredAsync(t, InviteTable, AccessRemovedOnColumn, ct).ConfigureAwait(false);
        if (removedSecured is null)
        {
            var note = $"{InviteTable}.{AccessRemovedOnColumn}: absent — ships in a later SpaarkeMaster release; its lock is " +
                       "not verified in this environment yet.";
            notes.Add(note);
            _logger.LogInformation("H7b S22: {Note}", note);
        }
        else
        {
            var removedPermissions = (await dv.ListFieldPermissionsAsync(t, AccessRemovedOnColumn, ct).ConfigureAwait(false))
                .Where(p => Same(p.EntityName, InviteTable))
                .ToArray();
            var removedReader = removedPermissions.FirstOrDefault(p => p.ProfileId == readerProfileId);
            var removedWriter = removedPermissions.FirstOrDefault(p => p.ProfileId == writerProfileId);
            if (removedSecured != true
                || removedReader is null || removedReader.CanRead != FieldPermissionAllowed
                || removedWriter is null || removedWriter.CanRead != FieldPermissionAllowed
                || removedWriter.CanCreate != FieldPermissionAllowed || removedWriter.CanUpdate != FieldPermissionAllowed)
            {
                return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.AccessRemovedOnLockIncomplete,
                    $"{InviteTable}.{AccessRemovedOnColumn}: IsSecured={removedSecured}, " +
                    $"'{ReaderProfileName}' read={removedReader?.CanRead.ToString() ?? "(none)"}, " +
                    $"'{WriterProfileName}' read/create/update={Rights(removedWriter)} (expected secured, 4, 4/4/4). The " +
                    "package's lock is incomplete: import SpaarkeMaster (H6), then resume. Nothing was written.");
            }
            var removedOtherWriters = removedPermissions
                .Where(p => p.ProfileId != writerProfileId
                            && !systemAdministratorProfiles.Contains(p.ProfileId)
                            && (p.CanCreate == FieldPermissionAllowed || p.CanUpdate == FieldPermissionAllowed))
                .ToArray();
            if (removedOtherWriters.Length > 0)
            {
                return Refuse(FailureClass.QuarantineRequired, SecureRecordSetupRejectionCodes.AccessRemovedOnLockOtherWriter,
                    $"Profiles other than '{WriterProfileName}' may create or update {InviteTable}.{AccessRemovedOnColumn}: " +
                    $"{Ids(removedOtherWriters.Select(p => p.ProfileId))} (the Readers profile included — every default team " +
                    "is its member). Only the BFF records that a person's external access was removed. Nothing was written.");
            }
            notes.Add($"{InviteTable}.{AccessRemovedOnColumn}: present, secured, Readers 4/0/0, Writers 4/4/4, no other writer.");
        }

        // §6 T4 — the ROOT unit's default team must not reach the secure unit by depth.
        var rootDefaultTeams = await dv.FindDefaultTeamsAsync(t, root.Id, ct).ConfigureAwait(false);
        if (rootDefaultTeams.Count != 1)
        {
            return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.RootBusinessUnitUnresolved,
                $"The root unit {root.Id} reports {rootDefaultTeams.Count} default teams (expected one). Nothing was written.");
        }
        foreach (var rootRole in await dv.ListTeamRolesAsync(t, rootDefaultTeams[0].Id, ct).ConfigureAwait(false))
        {
            var reaching = (await dv.GetRolePrivilegesAsync(t, rootRole.Id, ct).ConfigureAwait(false))
                .Where(h => wantedIds.Contains(h.PrivilegeId) && IsDeepOrGlobal(h.Depth))
                .ToArray();
            if (reaching.Length > 0)
            {
                return Refuse(FailureClass.QuarantineRequired, SecureRecordSetupRejectionCodes.RootDefaultTeamReachesSecureUnit,
                    $"The root unit's default team holds role '{rootRole.Name}' ({rootRole.Id}) with " +
                    $"{string.Join(", ", reaching.Take(NamedPrincipalLimit).Select(h => $"{h.Name ?? h.PrivilegeId.ToString()} at {h.Depth}"))}: " +
                    "every root-unit user reads every secure record by depth (INCOMING-145 §6 T4). Users belong in the " +
                    "customer's own unit; which roles the root default team holds is an owner decision. Nothing was written.");
            }
        }

        // S13 (read half) — the tables carrying the lock, and their identity for the NULL-flag repair.
        var lockedIdentities = new List<SecureSetupTableIdentity>(lockedTables.Length);
        foreach (var table in lockedTables)
        {
            lockedIdentities.Add(await dv.GetTableIdentityAsync(t, table, ct).ConfigureAwait(false));
        }

        // ============================ PHASE 2 — write what is missing ============================

        // S1 — the unit.
        Guid? businessUnitId = unit?.Id;
        if (businessUnitId is null)
        {
            await Write($"create business unit '{set.BusinessUnitName}' under the root unit {root.Id}", async () =>
                businessUnitId = await dv.CreateBusinessUnitAsync(t, set.BusinessUnitName, root.Id, ct).ConfigureAwait(false))
                .ConfigureAwait(false);
        }

        // S3 — the named owner team.
        Guid? teamId = team?.Id;
        if (teamId is null)
        {
            await Write($"create the Owner team '{OwnerTeamName}' (memberless) in '{set.BusinessUnitName}'", async () =>
                teamId = await dv.CreateOwnerTeamAsync(t, businessUnitId!.Value, OwnerTeamName, OwnerTeamDescription, ct)
                    .ConfigureAwait(false))
                .ConfigureAwait(false);
        }

        // S4 — the role, in the unit.
        Guid? roleId = role?.Id;
        if (roleId is null)
        {
            await Write($"create the role '{set.RoleName}' in '{set.BusinessUnitName}' (setup guide §5.2)", async () =>
                roleId = await dv.CreateRoleAsync(t, businessUnitId!.Value, set.RoleName, RoleDescription, ct)
                    .ConfigureAwait(false))
                .ConfigureAwait(false);
            if (roleId is { } created)
            {
                held = await dv.GetRolePrivilegesAsync(t, created, ct).ConfigureAwait(false);   // what creation injected
            }
        }

        // S5 — each MISSING Read at Basic.
        var missing = wanted.Where(w => held.All(h => h.PrivilegeId != w.Id)).ToArray();
        if (missing.Length > 0)
        {
            await Write($"add Read at {BasicDepth} to '{set.RoleName}': {string.Join(", ", missing.Select(p => p.Name))}",
                () => dv.AddBasicPrivilegesAsync(t, roleId!.Value, businessUnitId!.Value, missing, ct)).ConfigureAwait(false);
        }

        // S6 — strip everything outside the file. ALWAYS after S5 (setup guide §5.4); keep-list = the file's set.
        if (roleId is { } roleToStrip)
        {
            var current = dryRun ? held : await dv.GetRolePrivilegesAsync(t, roleToStrip, ct).ConfigureAwait(false);
            foreach (var outside in current.Where(h => !wantedIds.Contains(h.PrivilegeId)).ToArray())
            {
                await Write($"remove {outside.Name ?? outside.PrivilegeId.ToString()} ({outside.Depth}) from '{set.RoleName}' — not in the codified set",
                    () => dv.RemovePrivilegeAsync(t, roleToStrip, outside.PrivilegeId, ct)).ConfigureAwait(false);
            }
        }
        if (dryRun && (role is null || missing.Length > 0))
        {
            actions.Add($"then remove what Dataverse injects into '{set.RoleName}' on create and on AddPrivilegesRole " +
                        "(SDK/plugin reads, the SharePoint four — setup guide §5.4)");
        }

        // S7 — the role on the named team.
        if (teamId is { } namedTeam && roleId is { } ownerRole)
        {
            var namedTeamRoles = await dv.ListTeamRolesAsync(t, namedTeam, ct).ConfigureAwait(false);
            if (namedTeamRoles.All(r => r.Id != ownerRole))
            {
                await Write($"give '{set.RoleName}' to '{OwnerTeamName}'",
                    () => dv.AssociateTeamRoleAsync(t, namedTeam, ownerRole, ct)).ConfigureAwait(false);
            }
        }
        else
        {
            actions.Add($"give '{set.RoleName}' to '{OwnerTeamName}'");   // dry run: one of the two does not exist yet
        }

        // S8 — contain: the role off the unit's DEFAULT team; System Administrator off both teams (setup guide §5.5).
        Guid? defaultTeamId = null;
        if (businessUnitId is { } unitId)
        {
            var defaultTeams = await dv.FindDefaultTeamsAsync(t, unitId, ct).ConfigureAwait(false);
            if (defaultTeams.Count != 1)
            {
                return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.VerifyFailed,
                    $"Business unit '{set.BusinessUnitName}' ({unitId}) reports {defaultTeams.Count} default teams (expected one).");
            }
            defaultTeamId = defaultTeams[0].Id;
            foreach (var teamRole in await dv.ListTeamRolesAsync(t, defaultTeamId.Value, ct).ConfigureAwait(false))
            {
                if (teamRole.Id == roleId || IsSystemAdministrator(teamRole))
                {
                    var defaultTeam = defaultTeamId.Value;
                    await Write($"remove '{teamRole.Name}' from the unit's default team",
                        () => dv.DisassociateTeamRoleAsync(t, defaultTeam, teamRole.Id, ct)).ConfigureAwait(false);
                }
            }
            if (teamId is { } namedTeamForAdmin)
            {
                foreach (var teamRole in await dv.ListTeamRolesAsync(t, namedTeamForAdmin, ct).ConfigureAwait(false))
                {
                    if (IsSystemAdministrator(teamRole))
                    {
                        await Write($"remove '{teamRole.Name}' from '{OwnerTeamName}'",
                            () => dv.DisassociateTeamRoleAsync(t, namedTeamForAdmin, teamRole.Id, ct)).ConfigureAwait(false);
                    }
                }
            }
        }
        else
        {
            actions.Add($"remove '{SystemAdministratorRoleName}' from the new unit's default team if it arrives holding it");
        }

        // S11 — every business unit's default team reads the BFF-managed columns (a new unit is a re-run of this step).
        var allDefaultTeams = await dv.FindDefaultTeamsAsync(t, null, ct).ConfigureAwait(false);
        var readerTeams = await dv.ListProfileTeamsAsync(t, readerProfileId, ct).ConfigureAwait(false);
        foreach (var defaultTeam in allDefaultTeams.Where(d => !readerTeams.Contains(d.Id)).ToArray())
        {
            await Write($"add default team '{defaultTeam.Name}' ({defaultTeam.Id}) to '{ReaderProfileName}'",
                () => dv.AssociateProfileTeamAsync(t, readerProfileId, defaultTeam.Id, ct)).ConfigureAwait(false);
        }
        if (businessUnitId is null)
        {
            actions.Add($"add the new unit's default team to '{ReaderProfileName}'");
        }

        // S12 — the BFF's application users are writer-profile members.
        foreach (var bffUser in bffUsers.Where(u => writerUsers.All(w => w.UserId != u)).ToArray())
        {
            await Write($"add BFF application user {bffUser} to '{WriterProfileName}'",
                () => dv.AssociateProfileUserAsync(t, writerProfileId, bffUser, ct)).ConfigureAwait(false);
        }

        // S16 — as S11: every business unit's default team reads the identity-binding columns.
        var linkReaderTeams = await dv.ListProfileTeamsAsync(t, linkReaderProfileId, ct).ConfigureAwait(false);
        foreach (var defaultTeam in allDefaultTeams.Where(d => !linkReaderTeams.Contains(d.Id)).ToArray())
        {
            await Write($"add default team '{defaultTeam.Name}' ({defaultTeam.Id}) to '{IdentityLinkReaderProfileName}'",
                () => dv.AssociateProfileTeamAsync(t, linkReaderProfileId, defaultTeam.Id, ct)).ConfigureAwait(false);
        }
        if (businessUnitId is null)
        {
            actions.Add($"add the new unit's default team to '{IdentityLinkReaderProfileName}'");
        }

        // S17 — as S12: the BFF's application users are the link-writer profile's members.
        foreach (var bffUser in bffUsers.Where(u => linkWriterUsers.All(w => w.UserId != u)).ToArray())
        {
            await Write($"add BFF application user {bffUser} to '{IdentityLinkWriterProfileName}'",
                () => dv.AssociateProfileUserAsync(t, linkWriterProfileId, bffUser, ct)).ConfigureAwait(false);
        }

        // S20 — the BFF's application users are standing-grant profile members (Read is what the BFF needs; membership is the
        // only way to get it). Other members are grant-privileged administrators and are never touched.
        foreach (var bffUser in bffUsers.Where(u => standingUsers.All(m => m.UserId != u)).ToArray())
        {
            await Write($"add BFF application user {bffUser} to '{StandingGrantProfileName}'",
                () => dv.AssociateProfileUserAsync(t, standingProfileId, bffUser, ct)).ConfigureAwait(false);
        }

        // S13 — no NULL secure flag may remain (a new environment has none; an upgraded one has the pre-column rows). It
        // runs before the BFF deploy because H9 waits for H7b, and a task-150 BFF refuses an EMPTY flag.
        foreach (var table in lockedIdentities)
        {
            var refusal = await RepairNullFlagsAsync(dv, t, table, dryRun, actions, ct).ConfigureAwait(false);
            if (refusal is not null)
            {
                return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.VerifyFailed, refusal);
            }
        }

        if (dryRun)
        {
            return new SecureRecordSetupOutcome.Planned(actions.ToArray());
        }

        // ============================ PHASE 3 — verify (S9) ============================

        var problems = await VerifyAsync(dv, t, set, wantedIds, businessUnitId!.Value, teamId!.Value, roleId!.Value,
            defaultTeamId!.Value, [(readerProfileId, ReaderProfileName), (linkReaderProfileId, IdentityLinkReaderProfileName)],
            [(writerProfileId, WriterProfileName), (linkWriterProfileId, IdentityLinkWriterProfileName)],
            standingProfileId, bffUsers, lockedIdentities, ct).ConfigureAwait(false);
        if (problems.Count > 0)
        {
            return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.VerifyFailed,
                "The re-read state is not the codified one: " + string.Join("; ", problems) +
                ". Dataverse may still be applying a write; resume re-runs every step (each reads first).");
        }

        return new SecureRecordSetupOutcome.Applied(actions.ToArray(), new SecureRecordSetupState(
            businessUnitId.Value, teamId.Value, roleId.Value, wanted.Count, lockedTables, notes.ToArray()));
    }

    private static async Task<string?> RepairNullFlagsAsync(
        ISecureRecordSetupDataverse dv, SecureRecordSetupTarget t, SecureSetupTableIdentity table, bool dryRun,
        List<string> actions, CancellationToken ct)
    {
        var rows = await dv.ListRowsWithNullColumnAsync(t, table, SecureFlagColumn, NullRowPageSize, ct).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return null;
        }
        if (dryRun)
        {
            actions.Add($"set {SecureFlagColumn} = false on {rows.Count}{(rows.Count == NullRowPageSize ? "+" : string.Empty)} " +
                        $"row(s) of {table.LogicalName} where it is NULL");
            return null;
        }

        var repaired = new HashSet<Guid>();
        while (rows.Count > 0)
        {
            foreach (var row in rows)
            {
                if (!repaired.Add(row))
                {
                    return $"{table.LogicalName} row {row} still reads {SecureFlagColumn} = NULL after it was set to false";
                }
                await dv.SetColumnFalseAsync(t, table, row, SecureFlagColumn, ct).ConfigureAwait(false);
            }
            rows = await dv.ListRowsWithNullColumnAsync(t, table, SecureFlagColumn, NullRowPageSize, ct).ConfigureAwait(false);
        }
        actions.Add($"set {SecureFlagColumn} = false on {repaired.Count} row(s) of {table.LogicalName} where it was NULL");
        return null;
    }

    private static async Task<List<string>> VerifyAsync(
        ISecureRecordSetupDataverse dv, SecureRecordSetupTarget t, SecureRecordOwnerRoleSet set, IReadOnlySet<Guid> wantedIds,
        Guid businessUnitId, Guid teamId, Guid roleId, Guid defaultTeamId,
        IReadOnlyList<(Guid Id, string Name)> readerProfiles, IReadOnlyList<(Guid Id, string Name)> writerProfiles,
        Guid standingProfileId, IReadOnlyList<Guid> bffUsers, IReadOnlyList<SecureSetupTableIdentity> lockedTables, CancellationToken ct)
    {
        var problems = new List<string>();

        var held = await dv.GetRolePrivilegesAsync(t, roleId, ct).ConfigureAwait(false);
        var heldIds = held.Select(h => h.PrivilegeId).ToHashSet();
        var absent = wantedIds.Where(id => !heldIds.Contains(id)).ToArray();
        var extra = held.Where(h => !wantedIds.Contains(h.PrivilegeId)).ToArray();
        var wrongDepth = held.Where(h => wantedIds.Contains(h.PrivilegeId) && !Same(h.Depth, BasicDepth)).ToArray();
        if (absent.Length > 0 || extra.Length > 0 || wrongDepth.Length > 0 || held.Count != wantedIds.Count)
        {
            problems.Add($"'{set.RoleName}' holds {held.Count} privileges, the file lists {wantedIds.Count} " +
                         $"(missing [{Ids(absent)}], outside the file [{string.Join(", ", extra.Take(NamedPrincipalLimit).Select(h => h.Name ?? h.PrivilegeId.ToString()))}], " +
                         $"not at {BasicDepth} [{string.Join(", ", wrongDepth.Take(NamedPrincipalLimit).Select(h => h.Name ?? h.PrivilegeId.ToString()))}])");
        }

        var teamHolders = await dv.ListRoleTeamHoldersAsync(t, roleId, ct).ConfigureAwait(false);
        if (teamHolders.Count != 1 || teamHolders[0] != teamId)
        {
            problems.Add($"'{set.RoleName}' is held by teams [{Ids(teamHolders)}], expected only '{OwnerTeamName}' ({teamId})");
        }
        var userHolders = await dv.ListRoleUserHoldersAsync(t, roleId, ct).ConfigureAwait(false);
        if (userHolders.Count > 0)
        {
            problems.Add($"'{set.RoleName}' is held directly by users [{Ids(userHolders)}]");
        }

        var members = await dv.ListTeamMembersAsync(t, teamId, NamedPrincipalLimit, ct).ConfigureAwait(false);
        if (members.Count > 0)
        {
            problems.Add($"'{OwnerTeamName}' has members [{Ids(members)}]");
        }
        var users = await dv.ListBusinessUnitUsersAsync(t, businessUnitId, NamedPrincipalLimit, ct).ConfigureAwait(false);
        if (users.Count > 0)
        {
            problems.Add($"'{set.BusinessUnitName}' holds users [{Ids(users)}]");
        }

        foreach (var (teamLabel, id) in new[] { ("the unit's default team", defaultTeamId), ($"'{OwnerTeamName}'", teamId) })
        {
            var admin = (await dv.ListTeamRolesAsync(t, id, ct).ConfigureAwait(false))
                .Where(IsSystemAdministrator).ToArray();
            if (admin.Length > 0)
            {
                problems.Add($"{teamLabel} still holds '{SystemAdministratorRoleName}'");
            }
        }

        // S11/S16: every default team in each reader profile; S12/S17: each writer profile = the BFF's application users.
        var allDefaultTeams = await dv.FindDefaultTeamsAsync(t, null, ct).ConfigureAwait(false);
        foreach (var (readerProfileId, readerProfileName) in readerProfiles)
        {
            var readerTeams = await dv.ListProfileTeamsAsync(t, readerProfileId, ct).ConfigureAwait(false);
            var unread = allDefaultTeams.Where(d => !readerTeams.Contains(d.Id)).Select(d => d.Id).ToArray();
            if (unread.Length > 0)
            {
                problems.Add($"default teams [{Ids(unread)}] are not in '{readerProfileName}'");
            }
        }

        foreach (var (writerProfileId, writerProfileName) in writerProfiles)
        {
            var writerUsers = (await dv.ListProfileUsersAsync(t, writerProfileId, ct).ConfigureAwait(false))
                .Select(u => u.UserId).ToHashSet();
            var writerTeams = await dv.ListProfileTeamsAsync(t, writerProfileId, ct).ConfigureAwait(false);
            if (!writerUsers.SetEquals(bffUsers) || writerTeams.Count > 0)
            {
                problems.Add($"'{writerProfileName}' members are users [{Ids(writerUsers)}] and teams [{Ids(writerTeams)}], " +
                             $"expected exactly the BFF's application users [{Ids(bffUsers)}]");
            }
        }

        // S20: every BFF application user is a standing-grant profile member (other members are allowed).
        var standingMembers = (await dv.ListProfileUsersAsync(t, standingProfileId, ct).ConfigureAwait(false))
            .Select(u => u.UserId).ToHashSet();
        var notStanding = bffUsers.Where(u => !standingMembers.Contains(u)).ToArray();
        if (notStanding.Length > 0)
        {
            problems.Add($"BFF application users [{Ids(notStanding)}] are not members of '{StandingGrantProfileName}'");
        }

        foreach (var table in lockedTables)
        {
            var nulls = await dv.ListRowsWithNullColumnAsync(t, table, SecureFlagColumn, 1, ct).ConfigureAwait(false);
            if (nulls.Count > 0)
            {
                problems.Add($"{table.LogicalName} still has rows with {SecureFlagColumn} NULL");
            }
        }

        return problems;
    }

    private static async Task<(Guid? Id, string? Refusal)> ResolveProfileAsync(
        ISecureRecordSetupDataverse dv, SecureRecordSetupTarget t, string name, CancellationToken ct)
    {
        var profiles = await dv.FindFieldSecurityProfilesAsync(t, name, ct).ConfigureAwait(false);
        return profiles.Count == 1
            ? (profiles[0], null)
            : (null, $"Field-security profile '{name}': found {profiles.Count} (expected exactly one). It ships in " +
                     "SpaarkeMaster (unified-access-control-r2 tasks 133/141/150) — import the package (H6), then resume. " +
                     "Nothing was written.");
    }

    private static bool IsDeepOrGlobal(string depth) => Same(depth, "Deep") || Same(depth, "Global");

    private static bool IsSystemAdministrator(SecureSetupRole role) => role.RoleTemplateId == SystemAdministratorRoleTemplateId;

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>A field permission's read/create/update, or "(none)" when there is no row.</summary>
    private static string Rights(SecureSetupFieldPermission? p)
        => p is null ? "(none)" : $"{p.CanRead}/{p.CanCreate}/{p.CanUpdate}";

    private static string Ids(IEnumerable<Guid> ids)
    {
        var list = ids.ToArray();
        var shown = string.Join(", ", list.Take(NamedPrincipalLimit));
        return list.Length > NamedPrincipalLimit ? $"{shown}, +{list.Length - NamedPrincipalLimit} more" : shown;
    }
}
