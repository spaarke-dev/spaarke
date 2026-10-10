// -----------------------------------------------------------------------------
// SecureRecordSetupProcedure.cs
//
// T256 (H7b) — the per-environment Secure Record setup, step by step: unified-access-control-r2 INCOMING-145 §2.2
// (S0–S9), its §4 item 2 (S10–S14, task 150's field security), its §6 topology checks (T2, T4) and the
// sprk_noaccessentry prerequisite (#1364) — plus S15–S18, the memberships of the contact identity-binding profiles
// (unified-access-control-r2 INCOMING-141 / task 141; T255), built exactly as S10/S11/S12/S14. Every step READS first
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

/// <summary>The configured objects, for the handler's gate evidence.</summary>
public sealed record SecureRecordSetupState(
    Guid BusinessUnitId,
    Guid OwnerTeamId,
    Guid RoleId,
    int PrivilegeCount,
    IReadOnlyList<string> LockedTables);

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

    /// <summary>The platform role S8 removes from both teams of the unit.</summary>
    public const string SystemAdministratorRoleName = "System Administrator";

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
    /// S18: the field-secured binding columns, as (table, column). The uniqueness mirror
    /// <c>contact.sprk_externalobjectidkey</c> is deliberately NOT secured (Dataverse cannot key a secured column).
    /// </summary>
    public static readonly IReadOnlyList<(string Table, string Column)> IdentityLinkColumns =
    [
        ("contact", "sprk_externalobjectid"),
        ("systemuser", "sprk_primarycontact"),
    ];

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
                    "(expected secured, 4, 4/4/4). The package's identity-binding lock (task 141) is incomplete: import " +
                    "SpaarkeMaster (H6), then resume. Nothing was written.");
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
                if (teamRole.Id == roleId || Same(teamRole.Name, SystemAdministratorRoleName))
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
                    if (Same(teamRole.Name, SystemAdministratorRoleName))
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
            bffUsers, lockedIdentities, ct).ConfigureAwait(false);
        if (problems.Count > 0)
        {
            return Refuse(FailureClass.Resumable, SecureRecordSetupRejectionCodes.VerifyFailed,
                "The re-read state is not the codified one: " + string.Join("; ", problems) +
                ". Dataverse may still be applying a write; resume re-runs every step (each reads first).");
        }

        return new SecureRecordSetupOutcome.Applied(actions.ToArray(), new SecureRecordSetupState(
            businessUnitId.Value, teamId.Value, roleId.Value, wanted.Count, lockedTables));
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
        IReadOnlyList<Guid> bffUsers, IReadOnlyList<SecureSetupTableIdentity> lockedTables, CancellationToken ct)
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
                .Where(r => Same(r.Name, SystemAdministratorRoleName)).ToArray();
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

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Ids(IEnumerable<Guid> ids)
    {
        var list = ids.ToArray();
        var shown = string.Join(", ", list.Take(NamedPrincipalLimit));
        return list.Length > NamedPrincipalLimit ? $"{shown}, +{list.Length - NamedPrincipalLimit} more" : shown;
    }
}
