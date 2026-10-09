// -----------------------------------------------------------------------------
// FakeSecureRecordSetupDataverse.cs
//
// T256 (H7b) — the test implementation of ISecureRecordSetupDataverse (ADR-010: production + fake): a small in-memory
// Dataverse environment that behaves the way unified-access-control-r2 recorded the real one behaving
// (SECURE-PROJECT-ENVIRONMENT-SETUP.md §5.4–§5.5):
//   - creating a business unit creates its default team, which may arrive holding System Administrator;
//   - creating a role silently adds platform privileges at Global (SDK/plugin reads + the SharePoint four);
//   - AddPrivilegesRole adds what was asked at Basic AND re-injects the SharePoint four.
// Every write is counted, so "a second run writes nothing" and "a dry run writes nothing" are assertions, not hopes.
// No HTTP, no Moq (ADR-038).
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers.SecureRecordSetup;

public sealed class FakeSecureRecordSetupDataverse : ISecureRecordSetupDataverse
{
    internal const string SystemAdministrator = "System Administrator";

    // ---- platform privileges the real environment injects (guide §5.4) ----
    internal static readonly (Guid Id, string Name)[] InjectedOnCreate =
    [
        (G("0001"), "prvReadSdkMessage"), (G("0002"), "prvReadSdkMessageProcessingStep"),
        (G("0003"), "prvReadSdkMessageProcessingStepImage"), (G("0004"), "prvReadPluginAssembly"),
        (G("0005"), "prvReadPluginType"), (G("0006"), "prvReadSharePointData"), (G("0007"), "prvWriteSharePointData"),
        (G("0008"), "prvCreateSharePointData"), (G("0009"), "prvReadSharePointDocument"),
    ];

    internal static readonly (Guid Id, string Name)[] SharePointFour = InjectedOnCreate[5..];

    private static Guid G(string suffix) => Guid.Parse($"99999999-0000-0000-0000-00000000{suffix}");

    // ---- state ----
    internal bool ShareToPreviousOwnerOnAssign { get; set; }
    internal bool NoAccessEntryPresent { get; set; } = true;
    internal bool NewUnitDefaultTeamGetsSystemAdministrator { get; set; } = true;
    internal bool AddPrivilegesIsIgnored { get; set; }
    internal bool SetColumnFalseIsIgnored { get; set; }

    /// <summary>Profiles whose user/team associations are accepted but do not stick (verify-phase tests).</summary>
    internal HashSet<Guid> ProfileAssociationsIgnored { get; } = [];

    internal List<SecureSetupBusinessUnit> Units { get; } = [];
    internal Dictionary<Guid, Guid> UserUnit { get; } = [];
    internal List<SecureSetupTeam> Teams { get; } = [];
    internal Dictionary<Guid, HashSet<Guid>> TeamMembers { get; } = [];
    internal List<SecureSetupRole> Roles { get; } = [];
    internal Dictionary<Guid, Dictionary<Guid, (string Name, string Depth)>> RolePrivileges { get; } = [];
    internal Dictionary<Guid, HashSet<Guid>> TeamRoles { get; } = [];
    internal Dictionary<Guid, HashSet<Guid>> UserRoles { get; } = [];
    internal Dictionary<string, List<SecureSetupPrivilege>> TableReadPrivileges { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, List<Guid>> Profiles { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<Guid, HashSet<Guid>> ProfileTeams { get; } = [];
    internal Dictionary<Guid, HashSet<Guid>> ProfileUsers { get; } = [];
    internal Dictionary<Guid, Guid?> UserApplicationId { get; } = [];
    /// <summary>Field permissions per column logical name (as Dataverse filters <c>fieldpermissions</c> by attributelogicalname).</summary>
    internal Dictionary<string, List<SecureSetupFieldPermission>> FieldPermissionsByColumn { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The <c>sprk_issecure</c> permissions (S14).</summary>
    internal List<SecureSetupFieldPermission> FieldPermissions => ColumnPermissions(SecureRecordSetupProcedure.SecureFlagColumn);

    /// <summary><c>IsSecured</c> per <c>"table.column"</c> (see <see cref="ColumnKey"/>); absent = no such column.</summary>
    internal Dictionary<string, bool?> SecuredColumns { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, HashSet<Guid>> NullFlagRows { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal int Reads { get; private set; }
    internal List<string> Writes { get; } = [];

    /// <summary>Optional fault: thrown by every member whose name the predicate accepts.</summary>
    internal Func<string, Exception?>? Fault { get; set; }

    internal Guid RootUnitId { get; private set; }
    internal Guid RootDefaultTeamId { get; private set; }
    internal Guid ReaderProfileId { get; private set; }
    internal Guid WriterProfileId { get; private set; }
    internal Guid SystemAdministratorProfileId { get; private set; }
    internal Guid LinkReaderProfileId { get; private set; }
    internal Guid LinkWriterProfileId { get; private set; }
    internal Guid BffAppUser { get; } = Guid.Parse("b0000000-0000-0000-0000-0000000000f1");
    internal Guid MiAppUser { get; } = Guid.Parse("b0000000-0000-0000-0000-0000000000f2");
    internal static readonly string[] LockedTables = ["sprk_invoice", "sprk_matter", "sprk_project", "sprk_workassignment"];

    /// <summary>
    /// A freshly provisioned environment after H6: one root unit and its default team (holding no role), the package's
    /// metadata for every table of <paramref name="roleSet"/>, the two BFF-managed profiles with their sprk_issecure
    /// permissions on four tables (secured), the two identity-link profiles with their permissions on
    /// contact.sprk_externalobjectid and systemuser.sprk_primarycontact (secured) exactly as SpaarkeMaster ships them, the
    /// platform System Administrator profile (read/create/update on every secured column, as the platform grants it),
    /// and H10's two application users.
    /// </summary>
    internal static FakeSecureRecordSetupDataverse NewEnvironment(SecureRecordOwnerRoleSet roleSet)
    {
        var dv = new FakeSecureRecordSetupDataverse();
        dv.RootUnitId = Guid.NewGuid();
        dv.Units.Add(new SecureSetupBusinessUnit(dv.RootUnitId, "spaarke-acme", null));
        dv.RootDefaultTeamId = dv.AddTeam(dv.RootUnitId, "spaarke-acme", isDefault: true);

        foreach (var table in roleSet.Tables)
        {
            dv.TableReadPrivileges[table.LogicalName] = [new SecureSetupPrivilege(Guid.NewGuid(), table.PrivilegeName)];
        }

        dv.ReaderProfileId = dv.AddProfile(SecureRecordSetupProcedure.ReaderProfileName);
        dv.WriterProfileId = dv.AddProfile(SecureRecordSetupProcedure.WriterProfileName);
        dv.SystemAdministratorProfileId = dv.AddProfile(SecureRecordSetupProcedure.SystemAdministratorProfileName);
        foreach (var table in LockedTables)
        {
            dv.FieldPermissions.Add(new SecureSetupFieldPermission(dv.ReaderProfileId, table, 4, 0, 0));
            dv.FieldPermissions.Add(new SecureSetupFieldPermission(dv.WriterProfileId, table, 4, 4, 4));
            dv.FieldPermissions.Add(new SecureSetupFieldPermission(dv.SystemAdministratorProfileId, table, 4, 4, 4));
            dv.SecuredColumns[ColumnKey(table, SecureRecordSetupProcedure.SecureFlagColumn)] = true;
            dv.NullFlagRows[table] = [];
        }

        dv.LinkReaderProfileId = dv.AddProfile(SecureRecordSetupProcedure.IdentityLinkReaderProfileName);
        dv.LinkWriterProfileId = dv.AddProfile(SecureRecordSetupProcedure.IdentityLinkWriterProfileName);
        foreach (var (table, column) in SecureRecordSetupProcedure.IdentityLinkColumns)
        {
            var permissions = dv.ColumnPermissions(column);
            permissions.Add(new SecureSetupFieldPermission(dv.LinkReaderProfileId, table, 4, 0, 0));
            permissions.Add(new SecureSetupFieldPermission(dv.LinkWriterProfileId, table, 4, 4, 4));
            permissions.Add(new SecureSetupFieldPermission(dv.SystemAdministratorProfileId, table, 4, 4, 4));
            dv.SecuredColumns[ColumnKey(table, column)] = true;
        }

        dv.UserApplicationId[dv.BffAppUser] = Guid.NewGuid();
        dv.UserApplicationId[dv.MiAppUser] = Guid.NewGuid();
        dv.UserUnit[dv.BffAppUser] = dv.RootUnitId;
        dv.UserUnit[dv.MiAppUser] = dv.RootUnitId;
        return dv;
    }

    internal Guid AddTeam(Guid unitId, string name, bool isDefault)
    {
        var id = Guid.NewGuid();
        Teams.Add(new SecureSetupTeam(id, name, unitId, isDefault));
        TeamMembers[id] = [];
        TeamRoles[id] = [];
        return id;
    }

    internal static string ColumnKey(string table, string column) => $"{table}.{column}";

    internal List<SecureSetupFieldPermission> ColumnPermissions(string column)
    {
        if (!FieldPermissionsByColumn.TryGetValue(column, out var permissions))
        {
            FieldPermissionsByColumn[column] = permissions = [];
        }
        return permissions;
    }

    internal Guid AddProfile(string name)
    {
        var id = Guid.NewGuid();
        if (!Profiles.TryGetValue(name, out var ids))
        {
            Profiles[name] = ids = [];
        }
        ids.Add(id);
        ProfileTeams[id] = [];
        ProfileUsers[id] = [];
        return id;
    }

    internal Guid AddRole(Guid unitId, string name, Guid? parentRootRoleId = null)
    {
        var id = Guid.NewGuid();
        Roles.Add(new SecureSetupRole(id, name, unitId, parentRootRoleId ?? id));
        RolePrivileges[id] = [];
        return id;
    }

    internal Guid SystemAdministratorRoleIn(Guid unitId)
    {
        var existing = Roles.FirstOrDefault(r => r.BusinessUnitId == unitId && r.Name == SystemAdministrator);
        return existing?.Id ?? AddRole(unitId, SystemAdministrator);
    }

    internal SecureSetupBusinessUnit? Unit(string name) => Units.SingleOrDefault(u => u.Name == name);

    internal SecureSetupTeam? Team(Guid unitId, string name, bool isDefault)
        => Teams.SingleOrDefault(t => t.BusinessUnitId == unitId && t.Name == name && t.IsDefault == isDefault);

    internal SecureSetupTeam DefaultTeamOf(Guid unitId) => Teams.Single(t => t.BusinessUnitId == unitId && t.IsDefault);

    internal SecureSetupRole? Role(Guid unitId, string name) => Roles.SingleOrDefault(r => r.BusinessUnitId == unitId && r.Name == name);

    private void Read(string member)
    {
        Reads++;
        if (Fault?.Invoke(member) is { } ex)
        {
            throw ex;
        }
    }

    private void Write(string description)
    {
        if (Fault?.Invoke(description.Split(' ')[0]) is { } ex)
        {
            throw ex;
        }
        Writes.Add(description);
    }

    // ---------------- reads ----------------

    public Task<SecureSetupNoAccessEntryProbe> ProbeNoAccessEntryAsync(SecureRecordSetupTarget target, CancellationToken cancellationToken)
    {
        Read(nameof(ProbeNoAccessEntryAsync));
        return Task.FromResult(NoAccessEntryPresent
            ? new SecureSetupNoAccessEntryProbe(true, "present")
            : new SecureSetupNoAccessEntryProbe(false, "404 NotFound: Resource not found for the segment 'sprk_noaccessentries'."));
    }

    public Task<bool> ReadShareToPreviousOwnerOnAssignAsync(SecureRecordSetupTarget target, CancellationToken cancellationToken)
    {
        Read(nameof(ReadShareToPreviousOwnerOnAssignAsync));
        return Task.FromResult(ShareToPreviousOwnerOnAssign);
    }

    public Task<IReadOnlyList<SecureSetupBusinessUnit>> FindRootBusinessUnitsAsync(SecureRecordSetupTarget target, CancellationToken cancellationToken)
    {
        Read(nameof(FindRootBusinessUnitsAsync));
        return Task.FromResult<IReadOnlyList<SecureSetupBusinessUnit>>(Units.Where(u => u.ParentId is null).Take(2).ToArray());
    }

    public Task<IReadOnlyList<SecureSetupBusinessUnit>> FindBusinessUnitsByNameAsync(SecureRecordSetupTarget target, string name, CancellationToken cancellationToken)
    {
        Read(nameof(FindBusinessUnitsByNameAsync));
        return Task.FromResult<IReadOnlyList<SecureSetupBusinessUnit>>(
            Units.Where(u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray());
    }

    public Task<IReadOnlyList<Guid>> ListBusinessUnitUsersAsync(SecureRecordSetupTarget target, Guid businessUnitId, int top, CancellationToken cancellationToken)
    {
        Read(nameof(ListBusinessUnitUsersAsync));
        return Task.FromResult<IReadOnlyList<Guid>>(UserUnit.Where(kv => kv.Value == businessUnitId).Select(kv => kv.Key).Take(top).ToArray());
    }

    public Task<IReadOnlyList<SecureSetupTeam>> FindNamedOwnerTeamsAsync(SecureRecordSetupTarget target, Guid businessUnitId, string name, CancellationToken cancellationToken)
    {
        Read(nameof(FindNamedOwnerTeamsAsync));
        return Task.FromResult<IReadOnlyList<SecureSetupTeam>>(Teams
            .Where(t => t.BusinessUnitId == businessUnitId && !t.IsDefault && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray());
    }

    public Task<IReadOnlyList<SecureSetupTeam>> FindDefaultTeamsAsync(SecureRecordSetupTarget target, Guid? businessUnitId, CancellationToken cancellationToken)
    {
        Read(nameof(FindDefaultTeamsAsync));
        return Task.FromResult<IReadOnlyList<SecureSetupTeam>>(Teams
            .Where(t => t.IsDefault && (businessUnitId is null || t.BusinessUnitId == businessUnitId)).ToArray());
    }

    public Task<IReadOnlyList<Guid>> ListTeamMembersAsync(SecureRecordSetupTarget target, Guid teamId, int top, CancellationToken cancellationToken)
    {
        Read(nameof(ListTeamMembersAsync));
        return Task.FromResult<IReadOnlyList<Guid>>(TeamMembers[teamId].Take(top).ToArray());
    }

    public Task<IReadOnlyList<SecureSetupRole>> FindRolesAsync(SecureRecordSetupTarget target, string name, Guid businessUnitId, CancellationToken cancellationToken)
    {
        Read(nameof(FindRolesAsync));
        return Task.FromResult<IReadOnlyList<SecureSetupRole>>(Roles
            .Where(r => r.BusinessUnitId == businessUnitId && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray());
    }

    public Task<IReadOnlyList<SecureSetupRole>> ListTeamRolesAsync(SecureRecordSetupTarget target, Guid teamId, CancellationToken cancellationToken)
    {
        Read(nameof(ListTeamRolesAsync));
        return Task.FromResult<IReadOnlyList<SecureSetupRole>>(TeamRoles[teamId].Select(id => Roles.Single(r => r.Id == id)).ToArray());
    }

    public Task<IReadOnlyList<Guid>> ListRoleTeamHoldersAsync(SecureRecordSetupTarget target, Guid roleId, CancellationToken cancellationToken)
    {
        Read(nameof(ListRoleTeamHoldersAsync));
        return Task.FromResult<IReadOnlyList<Guid>>(TeamRoles.Where(kv => kv.Value.Contains(roleId)).Select(kv => kv.Key).ToArray());
    }

    public Task<IReadOnlyList<Guid>> ListRoleUserHoldersAsync(SecureRecordSetupTarget target, Guid roleId, CancellationToken cancellationToken)
    {
        Read(nameof(ListRoleUserHoldersAsync));
        return Task.FromResult<IReadOnlyList<Guid>>(UserRoles.Where(kv => kv.Value.Contains(roleId)).Select(kv => kv.Key).ToArray());
    }

    public Task<IReadOnlyList<SecureSetupPrivilege>> GetReadPrivilegesAsync(SecureRecordSetupTarget target, string logicalName, CancellationToken cancellationToken)
    {
        Read(nameof(GetReadPrivilegesAsync));
        return Task.FromResult<IReadOnlyList<SecureSetupPrivilege>>(
            TableReadPrivileges.TryGetValue(logicalName, out var privileges) ? privileges.ToArray() : []);
    }

    public Task<IReadOnlyList<SecureSetupHeldPrivilege>> GetRolePrivilegesAsync(SecureRecordSetupTarget target, Guid roleId, CancellationToken cancellationToken)
    {
        Read(nameof(GetRolePrivilegesAsync));
        return Task.FromResult<IReadOnlyList<SecureSetupHeldPrivilege>>(RolePrivileges[roleId]
            .Select(kv => new SecureSetupHeldPrivilege(kv.Key, kv.Value.Name, kv.Value.Depth)).ToArray());
    }

    public Task<IReadOnlyList<Guid>> FindFieldSecurityProfilesAsync(SecureRecordSetupTarget target, string name, CancellationToken cancellationToken)
    {
        Read(nameof(FindFieldSecurityProfilesAsync));
        return Task.FromResult<IReadOnlyList<Guid>>(Profiles.TryGetValue(name, out var ids) ? ids.Take(2).ToArray() : []);
    }

    public Task<IReadOnlyList<Guid>> ListProfileTeamsAsync(SecureRecordSetupTarget target, Guid profileId, CancellationToken cancellationToken)
    {
        Read(nameof(ListProfileTeamsAsync));
        return Task.FromResult<IReadOnlyList<Guid>>(ProfileTeams[profileId].ToArray());
    }

    public Task<IReadOnlyList<SecureSetupProfileUser>> ListProfileUsersAsync(SecureRecordSetupTarget target, Guid profileId, CancellationToken cancellationToken)
    {
        Read(nameof(ListProfileUsersAsync));
        return Task.FromResult<IReadOnlyList<SecureSetupProfileUser>>(ProfileUsers[profileId]
            .Select(u => new SecureSetupProfileUser(u, UserApplicationId.GetValueOrDefault(u))).ToArray());
    }

    public Task<IReadOnlyList<SecureSetupFieldPermission>> ListFieldPermissionsAsync(SecureRecordSetupTarget target, string attributeLogicalName, CancellationToken cancellationToken)
    {
        Read(nameof(ListFieldPermissionsAsync));
        return Task.FromResult<IReadOnlyList<SecureSetupFieldPermission>>(
            FieldPermissionsByColumn.TryGetValue(attributeLogicalName, out var permissions) ? permissions.ToArray() : []);
    }

    public Task<bool?> IsAttributeSecuredAsync(SecureRecordSetupTarget target, string tableLogicalName, string attributeLogicalName, CancellationToken cancellationToken)
    {
        Read(nameof(IsAttributeSecuredAsync));
        return Task.FromResult(SecuredColumns.GetValueOrDefault(ColumnKey(tableLogicalName, attributeLogicalName)));
    }

    public Task<SecureSetupTableIdentity> GetTableIdentityAsync(SecureRecordSetupTarget target, string tableLogicalName, CancellationToken cancellationToken)
    {
        Read(nameof(GetTableIdentityAsync));
        return Task.FromResult(new SecureSetupTableIdentity(tableLogicalName, tableLogicalName + "s", tableLogicalName + "id"));
    }

    public Task<IReadOnlyList<Guid>> ListRowsWithNullColumnAsync(SecureRecordSetupTarget target, SecureSetupTableIdentity table, string attributeLogicalName, int top, CancellationToken cancellationToken)
    {
        Read(nameof(ListRowsWithNullColumnAsync));
        return Task.FromResult<IReadOnlyList<Guid>>(NullFlagRows.TryGetValue(table.LogicalName, out var rows) ? rows.Take(top).ToArray() : []);
    }

    // ---------------- writes ----------------

    public Task<Guid> CreateBusinessUnitAsync(SecureRecordSetupTarget target, string name, Guid parentId, CancellationToken cancellationToken)
    {
        Write($"CreateBusinessUnit {name}");
        var id = Guid.NewGuid();
        Units.Add(new SecureSetupBusinessUnit(id, name, parentId));
        var defaultTeam = AddTeam(id, name, isDefault: true);
        if (NewUnitDefaultTeamGetsSystemAdministrator)
        {
            TeamRoles[defaultTeam].Add(SystemAdministratorRoleIn(id));
        }
        return Task.FromResult(id);
    }

    public Task<Guid> CreateOwnerTeamAsync(SecureRecordSetupTarget target, Guid businessUnitId, string name, string description, CancellationToken cancellationToken)
    {
        Write($"CreateOwnerTeam {name}");
        return Task.FromResult(AddTeam(businessUnitId, name, isDefault: false));
    }

    public Task<Guid> CreateRoleAsync(SecureRecordSetupTarget target, Guid businessUnitId, string name, string description, CancellationToken cancellationToken)
    {
        Write($"CreateRole {name}");
        var id = AddRole(businessUnitId, name);
        foreach (var (privilegeId, privilegeName) in InjectedOnCreate)
        {
            RolePrivileges[id][privilegeId] = (privilegeName, "Global");
        }
        return Task.FromResult(id);
    }

    public Task AddBasicPrivilegesAsync(SecureRecordSetupTarget target, Guid roleId, Guid businessUnitId, IReadOnlyList<SecureSetupPrivilege> privileges, CancellationToken cancellationToken)
    {
        Write($"AddBasicPrivileges {privileges.Count}");
        if (!AddPrivilegesIsIgnored)
        {
            foreach (var privilege in privileges)
            {
                RolePrivileges[roleId][privilege.Id] = (privilege.Name, "Basic");
            }
        }
        foreach (var (privilegeId, privilegeName) in SharePointFour)
        {
            RolePrivileges[roleId][privilegeId] = (privilegeName, "Global");   // re-injected every time (guide §5.4)
        }
        return Task.CompletedTask;
    }

    public Task RemovePrivilegeAsync(SecureRecordSetupTarget target, Guid roleId, Guid privilegeId, CancellationToken cancellationToken)
    {
        Write($"RemovePrivilege {privilegeId}");
        RolePrivileges[roleId].Remove(privilegeId);
        return Task.CompletedTask;
    }

    public Task AssociateTeamRoleAsync(SecureRecordSetupTarget target, Guid teamId, Guid roleId, CancellationToken cancellationToken)
    {
        Write($"AssociateTeamRole {teamId} {roleId}");
        TeamRoles[teamId].Add(roleId);
        return Task.CompletedTask;
    }

    public Task DisassociateTeamRoleAsync(SecureRecordSetupTarget target, Guid teamId, Guid roleId, CancellationToken cancellationToken)
    {
        Write($"DisassociateTeamRole {teamId} {roleId}");
        TeamRoles[teamId].Remove(roleId);
        return Task.CompletedTask;
    }

    public Task AssociateProfileTeamAsync(SecureRecordSetupTarget target, Guid profileId, Guid teamId, CancellationToken cancellationToken)
    {
        Write($"AssociateProfileTeam {profileId} {teamId}");
        if (!ProfileAssociationsIgnored.Contains(profileId))
        {
            ProfileTeams[profileId].Add(teamId);
        }
        return Task.CompletedTask;
    }

    public Task AssociateProfileUserAsync(SecureRecordSetupTarget target, Guid profileId, Guid userId, CancellationToken cancellationToken)
    {
        Write($"AssociateProfileUser {profileId} {userId}");
        if (!ProfileAssociationsIgnored.Contains(profileId))
        {
            ProfileUsers[profileId].Add(userId);
        }
        return Task.CompletedTask;
    }

    public Task SetColumnFalseAsync(SecureRecordSetupTarget target, SecureSetupTableIdentity table, Guid rowId, string attributeLogicalName, CancellationToken cancellationToken)
    {
        Write($"SetColumnFalse {table.LogicalName} {rowId}");
        if (!SetColumnFalseIsIgnored)
        {
            NullFlagRows[table.LogicalName].Remove(rowId);
        }
        return Task.CompletedTask;
    }
}
