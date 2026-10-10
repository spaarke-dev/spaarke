// -----------------------------------------------------------------------------
// ControlPlaneGraphAppRoles.cs
//
// The Microsoft Graph application roles the L2 control-plane Worker identity
// (sprk-controlplane-{env}-uami) needs for the Graph calls it makes AS ITSELF —
// task 261 (G31). Until task 261 the L2 identity was granted the BFF's stamp
// catalog (GraphAppRoles.cs) through Grant-ControlPlaneIdentity.ps1, which mixed
// two different identities' needs: shrinking the stamp catalog would have
// stripped H11 of User.Invite.All, and the BFF catalog never carried what H3 and
// H10 need (AppRoleAssignment.ReadWrite.All, Application.ReadWrite.OwnedBy) —
// both would 403 on a fresh control plane.
//
// CONSUMERS (no runtime code reads this list — L2 never grants itself):
//   - scripts/provisioning/Grant-ControlPlaneIdentity.ps1 (Section B) passes this
//     file to scripts/Grant-GraphAppRoles.ps1, whose regex parser reads the
//     three flat shapes below (public const value, private const Id, new
//     GraphAppRole(...) row) — keep them flat.
//   - scripts/provisioning-prereqs/prereqs.yaml PRQ-E-07 (the Id* constants).
//   - tests/Spaarke.ArchTests/TenantIsolation/StampGraphAppRoleEvidenceTests.cs
//     (this list = the note's documented control-plane set).
//
// EVIDENCE: projects/customer-provisioning-orchestration-r1/notes/t261-stamp-graph-least-privilege.md
// §4 (call site file:line + Microsoft Learn least-privileged application
// permission per call). GUIDs re-read live 2026-10-09 from the Microsoft Graph
// resource SP's appRoles in tenant a221a95e-… (constant across tenants).
//
// NOT here: the SPE calls H8/H13 T6/H0 make (they sign in as the SPE owning app
// through MI-FIC, not as the Worker), and Exchange (the Exchange admin app).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers;

/// <summary>
/// The L2 control-plane Worker identity's Microsoft Graph application roles — each one the least-privileged
/// permission (Microsoft Learn) for a Graph call the Worker makes as itself. Granted by the operator with
/// <c>scripts/provisioning/Grant-ControlPlaneIdentity.ps1</c>; never by L2 code.
/// </summary>
public static class ControlPlaneGraphAppRoles
{
    // ── Role VALUE constants (byte-identical to Microsoft Graph AppRole.Value) ─────────────────

    /// <summary>H3 — create and manage the customer BFF app registration and its service principal.</summary>
    public const string ApplicationReadWriteOwnedBy = "Application.ReadWrite.OwnedBy";

    /// <summary>H10 + H3 — grant and remove app-role assignments (stamp UAMI Graph roles; the BFF keyless-proof role).</summary>
    public const string AppRoleAssignmentReadWriteAll = "AppRoleAssignment.ReadWrite.All";

    /// <summary>H3 consent check (oauth2PermissionGrants) and every directory read (service principals, organization, users, groups).</summary>
    public const string DirectoryReadAll = "Directory.Read.All";

    /// <summary>H11 — B2B guest invitations.</summary>
    public const string UserInviteAll = "User.Invite.All";

    /// <summary>H11 — native account creation.</summary>
    public const string UserCreate = "User.Create";

    /// <summary>H11 — licence assignment for native accounts.</summary>
    public const string LicenseAssignmentReadWriteAll = "LicenseAssignment.ReadWrite.All";

    /// <summary>H11 — add each user to the environment security group.</summary>
    public const string GroupMemberReadWriteAll = "GroupMember.ReadWrite.All";

    /// <summary>H14b — mailbox change subscriptions created as the Worker.</summary>
    public const string MailRead = "Mail.Read";

    // ── Application-permission IDs (live, 2026-10-09) ─────────────────────────────────────────

    private const string IdApplicationReadWriteOwnedBy = "18a4783c-866b-4cc7-a460-3d5e5662c884";
    private const string IdAppRoleAssignmentReadWriteAll = "06b708a9-e830-4db3-a914-8e69da51d44f";
    private const string IdDirectoryReadAll = "7ab1d382-f21e-4acd-a863-ba3e13f7da61";
    private const string IdUserInviteAll = "09850681-111b-4a89-9bed-3f2cae46d706";
    private const string IdUserCreate = "4240f680-4f73-4082-a766-aa916a2dc9b3";
    private const string IdLicenseAssignmentReadWriteAll = "5facf0c1-8979-4e95-abcf-ff3d079771c0";
    private const string IdGroupMemberReadWriteAll = "dbaae8cf-10b5-4b86-a4a1-f871c94c6695";
    private const string IdMailRead = "810c84a8-4a9e-49e6-bf7d-12d183f40d01";

    /// <summary>Well-known Microsoft Graph resource service principal appId (constant across every tenant).</summary>
    public const string GraphResourceAppId = "00000003-0000-0000-c000-000000000000";

    /// <summary>One Graph application role the Worker needs, with the handler and call that need it.</summary>
    public sealed record GraphAppRole(string Value, string DisplayName, string AppRoleId, string Handler, string WhyRequired);

    /// <summary>The control-plane Worker's Graph application roles — exactly the set the task 261 note documents.</summary>
    public static readonly IReadOnlyList<GraphAppRole> All = new[]
    {
        new GraphAppRole(ApplicationReadWriteOwnedBy, "Manage apps that this app creates or owns", IdApplicationReadWriteOwnedBy,
            "H3", "POST/PATCH /applications, addPassword, federatedIdentityCredentials POST/DELETE, POST /servicePrincipals (GraphAppRegistrationProvisioner)."),
        new GraphAppRole(AppRoleAssignmentReadWriteAll, "Manage app permission grants and app role assignments", IdAppRoleAssignmentReadWriteAll,
            "H10, H3", "POST/DELETE /servicePrincipals/{stamp UAMI}/appRoleAssignments (H10); POST/DELETE /servicePrincipals/{bff}/appRoleAssignedTo (H3)."),
        new GraphAppRole(DirectoryReadAll, "Read directory data", IdDirectoryReadAll,
            "H3, H10, H11, H13, H14a", "GET /servicePrincipals/{id}/oauth2PermissionGrants (least privileged); pairs with AppRoleAssignment.ReadWrite.All for the POSTs; GET servicePrincipals, organization, users, groups."),
        new GraphAppRole(UserInviteAll, "Invite guest users to the organization", IdUserInviteAll,
            "H11", "POST /invitations (GraphRestB2BInvitationClient, B2BGuest preset)."),
        new GraphAppRole(UserCreate, "Create users", IdUserCreate,
            "H11", "POST /users (GraphRestUserProvisioner, NativeAccount preset)."),
        new GraphAppRole(LicenseAssignmentReadWriteAll, "Manage all license assignments", IdLicenseAssignmentReadWriteAll,
            "H11", "POST /users/{id}/assignLicense (GraphRestUserProvisioner)."),
        new GraphAppRole(GroupMemberReadWriteAll, "Read and write all group memberships", IdGroupMemberReadWriteAll,
            "H11", "POST /groups/{id}/members/$ref (GraphRestEnvironmentSecurityGroupClient)."),
        new GraphAppRole(MailRead, "Read mail in all mailboxes", IdMailRead,
            "H14b", "POST/PATCH/GET /subscriptions on users/{mailbox}/mailFolders/.../messages (GraphRestSubscriptionCreator)."),
    };
}
