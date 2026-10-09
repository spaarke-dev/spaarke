// -----------------------------------------------------------------------------
// ISecureRecordSetupDataverse.cs
//
// T256 (H7b) — the ONE seam between the Secure Record setup procedure and the customer's Dataverse (INCOMING-145
// §2.5; ADR-010: a production Web API implementation + the test fake). Each member is one Dataverse read or one write,
// so the procedure's read-then-write logic — and its "second run writes nothing" property — is tested against an
// in-memory environment, while the Web API shapes are pinned separately (DataverseWebApiSecureRecordSetupTests).
//
// FAULTS: a member throws SecureRecordSetupDataverseException (auth / rate-limited / other) for any HTTP or transport
// fault, and lets the caller's cancellation propagate. "Not found" is data, not a fault, where the step needs to know
// (an absent table's privileges, an absent attribute, a missing deny-list table).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;

/// <summary>Which customer environment, and who signs in to it (the BFF app registration — the identity H6/H7 use).</summary>
/// <param name="DataverseUrl">The environment H5 adopted (InterStepState.DataverseEnvUrl) — pinned, never an ambient profile.</param>
/// <param name="TenantId">The run's tenant (§4D I1 — explicit).</param>
/// <param name="ClientId">The BFF app registration (InterStepState.BffAppRegId).</param>
public sealed record SecureRecordSetupTarget(string DataverseUrl, string TenantId, string ClientId);

/// <summary>A business unit.</summary>
public sealed record SecureSetupBusinessUnit(Guid Id, string Name, Guid? ParentId);

/// <summary>A team.</summary>
public sealed record SecureSetupTeam(Guid Id, string Name, Guid BusinessUnitId, bool IsDefault);

/// <summary>A security role. <paramref name="ParentRootRoleId"/> ≠ <paramref name="Id"/> means a replica of an ancestor unit's role.</summary>
public sealed record SecureSetupRole(Guid Id, string Name, Guid BusinessUnitId, Guid? ParentRootRoleId);

/// <summary>A privilege from entity metadata.</summary>
public sealed record SecureSetupPrivilege(Guid Id, string Name);

/// <summary>A privilege a role holds, at a depth (<c>Basic</c>, <c>Local</c>, <c>Deep</c>, <c>Global</c>).</summary>
public sealed record SecureSetupHeldPrivilege(Guid PrivilegeId, string? Name, string Depth);

/// <summary>A user member of a field-security profile.</summary>
public sealed record SecureSetupProfileUser(Guid UserId, Guid? ApplicationId);

/// <summary>One field permission on a column: which profile, which table, and its Read/Create/Update grants (4 = allowed).</summary>
public sealed record SecureSetupFieldPermission(Guid ProfileId, string EntityName, int CanRead, int CanCreate, int CanUpdate);

/// <summary>A table's Web API entity set and primary key.</summary>
public sealed record SecureSetupTableIdentity(string LogicalName, string EntitySetName, string PrimaryIdAttribute);

/// <summary>The <c>sprk_noaccessentry</c> probe: present, or missing with the environment's answer.</summary>
public sealed record SecureSetupNoAccessEntryProbe(bool Present, string Detail);

/// <summary>Why a Dataverse call failed.</summary>
public enum SecureRecordSetupFaultKind
{
    /// <summary>Token acquisition failed, or 401/403.</summary>
    Auth = 1,

    /// <summary>429.</summary>
    RateLimited = 2,

    /// <summary>Any other status, a transport fault, a timeout or an unreadable body.</summary>
    Other = 3,
}

/// <summary>A Dataverse call failed. The handler maps <see cref="Kind"/> to a Resumable rejection code.</summary>
public sealed class SecureRecordSetupDataverseException : Exception
{
    /// <summary>Creates the exception.</summary>
    public SecureRecordSetupDataverseException(SecureRecordSetupFaultKind kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }

    /// <summary>The fault class.</summary>
    public SecureRecordSetupFaultKind Kind { get; }
}

/// <summary>Dataverse reads and writes the Secure Record setup needs — one member per call.</summary>
public interface ISecureRecordSetupDataverse
{
    // ---------------- reads ----------------

    /// <summary>Probes <c>sprk_noaccessentries</c> with the BFF deny-list reader's own <c>$select</c>.</summary>
    Task<SecureSetupNoAccessEntryProbe> ProbeNoAccessEntryAsync(SecureRecordSetupTarget target, CancellationToken cancellationToken);

    /// <summary>S0: <c>organization.sharetopreviousowneronassign</c>.</summary>
    Task<bool> ReadShareToPreviousOwnerOnAssignAsync(SecureRecordSetupTarget target, CancellationToken cancellationToken);

    /// <summary>The business units with no parent (expected: exactly one).</summary>
    Task<IReadOnlyList<SecureSetupBusinessUnit>> FindRootBusinessUnitsAsync(SecureRecordSetupTarget target, CancellationToken cancellationToken);

    /// <summary>S1: the business units with this name (at most two returned — enough to see ambiguity).</summary>
    Task<IReadOnlyList<SecureSetupBusinessUnit>> FindBusinessUnitsByNameAsync(SecureRecordSetupTarget target, string name, CancellationToken cancellationToken);

    /// <summary>S2: up to <paramref name="top"/> systemusers of the unit — enabled or disabled, human or application.</summary>
    Task<IReadOnlyList<Guid>> ListBusinessUnitUsersAsync(SecureRecordSetupTarget target, Guid businessUnitId, int top, CancellationToken cancellationToken);

    /// <summary>S3: the unit's NAMED, non-default OWNER teams with this name (at most two returned).</summary>
    Task<IReadOnlyList<SecureSetupTeam>> FindNamedOwnerTeamsAsync(SecureRecordSetupTarget target, Guid businessUnitId, string name, CancellationToken cancellationToken);

    /// <summary>Default teams — of one unit, or of every unit when <paramref name="businessUnitId"/> is null (S11).</summary>
    Task<IReadOnlyList<SecureSetupTeam>> FindDefaultTeamsAsync(SecureRecordSetupTarget target, Guid? businessUnitId, CancellationToken cancellationToken);

    /// <summary>S3/S9: up to <paramref name="top"/> members of the team, of any principal kind.</summary>
    Task<IReadOnlyList<Guid>> ListTeamMembersAsync(SecureRecordSetupTarget target, Guid teamId, int top, CancellationToken cancellationToken);

    /// <summary>S4: the roles of this name in the unit (at most two returned).</summary>
    Task<IReadOnlyList<SecureSetupRole>> FindRolesAsync(SecureRecordSetupTarget target, string name, Guid businessUnitId, CancellationToken cancellationToken);

    /// <summary>S7/S8/T4: the roles a team holds.</summary>
    Task<IReadOnlyList<SecureSetupRole>> ListTeamRolesAsync(SecureRecordSetupTarget target, Guid teamId, CancellationToken cancellationToken);

    /// <summary>S9: the teams holding the role.</summary>
    Task<IReadOnlyList<Guid>> ListRoleTeamHoldersAsync(SecureRecordSetupTarget target, Guid roleId, CancellationToken cancellationToken);

    /// <summary>S9: the users holding the role directly.</summary>
    Task<IReadOnlyList<Guid>> ListRoleUserHoldersAsync(SecureRecordSetupTarget target, Guid roleId, CancellationToken cancellationToken);

    /// <summary>S5: the table's Read privileges from entity metadata (empty when the table does not exist).</summary>
    Task<IReadOnlyList<SecureSetupPrivilege>> GetReadPrivilegesAsync(SecureRecordSetupTarget target, string logicalName, CancellationToken cancellationToken);

    /// <summary>S5/S6/S9/T4: what the role holds (<c>RetrieveRolePrivilegesRole</c>).</summary>
    Task<IReadOnlyList<SecureSetupHeldPrivilege>> GetRolePrivilegesAsync(SecureRecordSetupTarget target, Guid roleId, CancellationToken cancellationToken);

    /// <summary>S10/S14: field-security profiles with this name (at most two returned).</summary>
    Task<IReadOnlyList<Guid>> FindFieldSecurityProfilesAsync(SecureRecordSetupTarget target, string name, CancellationToken cancellationToken);

    /// <summary>S11/S12: the teams associated with a profile.</summary>
    Task<IReadOnlyList<Guid>> ListProfileTeamsAsync(SecureRecordSetupTarget target, Guid profileId, CancellationToken cancellationToken);

    /// <summary>S12: the users associated with a profile.</summary>
    Task<IReadOnlyList<SecureSetupProfileUser>> ListProfileUsersAsync(SecureRecordSetupTarget target, Guid profileId, CancellationToken cancellationToken);

    /// <summary>S13/S14: every field permission on the column, in every table and profile.</summary>
    Task<IReadOnlyList<SecureSetupFieldPermission>> ListFieldPermissionsAsync(SecureRecordSetupTarget target, string attributeLogicalName, CancellationToken cancellationToken);

    /// <summary>S14: the column's <c>IsSecured</c>, or null when the table or column does not exist.</summary>
    Task<bool?> IsAttributeSecuredAsync(SecureRecordSetupTarget target, string tableLogicalName, string attributeLogicalName, CancellationToken cancellationToken);

    /// <summary>S13: the table's entity set and primary key.</summary>
    Task<SecureSetupTableIdentity> GetTableIdentityAsync(SecureRecordSetupTarget target, string tableLogicalName, CancellationToken cancellationToken);

    /// <summary>S13: up to <paramref name="top"/> rows whose column is NULL.</summary>
    Task<IReadOnlyList<Guid>> ListRowsWithNullColumnAsync(SecureRecordSetupTarget target, SecureSetupTableIdentity table, string attributeLogicalName, int top, CancellationToken cancellationToken);

    // ---------------- writes ----------------

    /// <summary>S1: creates a business unit under <paramref name="parentId"/>; returns its id.</summary>
    Task<Guid> CreateBusinessUnitAsync(SecureRecordSetupTarget target, string name, Guid parentId, CancellationToken cancellationToken);

    /// <summary>S3: creates an OWNER team (teamtype 0) in the unit; returns its id.</summary>
    Task<Guid> CreateOwnerTeamAsync(SecureRecordSetupTarget target, Guid businessUnitId, string name, string description, CancellationToken cancellationToken);

    /// <summary>S4: creates a role IN the unit (setup guide §5.2); returns its id.</summary>
    Task<Guid> CreateRoleAsync(SecureRecordSetupTarget target, Guid businessUnitId, string name, string description, CancellationToken cancellationToken);

    /// <summary>S5: <c>AddPrivilegesRole</c> at <c>Basic</c> depth.</summary>
    Task AddBasicPrivilegesAsync(SecureRecordSetupTarget target, Guid roleId, Guid businessUnitId, IReadOnlyList<SecureSetupPrivilege> privileges, CancellationToken cancellationToken);

    /// <summary>S6: <c>RemovePrivilegeRole</c> (parameter: the entity reference <c>Privilege</c>).</summary>
    Task RemovePrivilegeAsync(SecureRecordSetupTarget target, Guid roleId, Guid privilegeId, CancellationToken cancellationToken);

    /// <summary>S7: gives the team the role.</summary>
    Task AssociateTeamRoleAsync(SecureRecordSetupTarget target, Guid teamId, Guid roleId, CancellationToken cancellationToken);

    /// <summary>S8: takes the role from the team.</summary>
    Task DisassociateTeamRoleAsync(SecureRecordSetupTarget target, Guid teamId, Guid roleId, CancellationToken cancellationToken);

    /// <summary>S11: adds a team to a field-security profile.</summary>
    Task AssociateProfileTeamAsync(SecureRecordSetupTarget target, Guid profileId, Guid teamId, CancellationToken cancellationToken);

    /// <summary>S12: adds a user to a field-security profile.</summary>
    Task AssociateProfileUserAsync(SecureRecordSetupTarget target, Guid profileId, Guid userId, CancellationToken cancellationToken);

    /// <summary>S13: sets the row's column to false (<c>If-Match: *</c> — never an upsert).</summary>
    Task SetColumnFalseAsync(SecureRecordSetupTarget target, SecureSetupTableIdentity table, Guid rowId, string attributeLogicalName, CancellationToken cancellationToken);
}
