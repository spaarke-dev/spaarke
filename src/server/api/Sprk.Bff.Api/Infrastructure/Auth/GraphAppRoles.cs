namespace Sprk.Bff.Api.Infrastructure.Auth;

/// <summary>
/// Single source of truth for the Microsoft Graph <b>application</b> (app-only) permissions —
/// "app roles" — that a customer stamp's BFF identity holds. Refactor ask #4
/// (<c>code-quality-and-assurance-r3</c> task 062) made this the canonical machine-consumable list;
/// <c>customer-provisioning-orchestration-r1</c>'s H10 grants it (through its L2 mirror,
/// <c>L2GraphAppRolesRegistry</c>) and the H13 T3 probe checks it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Evidence-backed (task 261 / G31, owner 2026-10-09: "ensure this is accurately scoped").</b> Every row
/// here is the least-privileged Microsoft Graph application permission, per Microsoft Learn, for an app-only
/// call the stamp BFF actually makes; the call sites, Learn citations and the live evidence are in
/// <c>projects/customer-provisioning-orchestration-r1/notes/t261-stamp-graph-least-privilege.md</c>, and
/// <c>tests/Spaarke.ArchTests/TenantIsolation/StampGraphAppRoleEvidenceTests.cs</c> fails when this list and that
/// note's documented set differ. <b>Adding a role = a new note row (call site + Learn URL) and an edit here.</b>
/// </para>
/// <para>
/// Every Model 1 stamp lives in Spaarke's own tenant, so a tenant-wide role on a stamp identity reaches
/// Spaarke's directory, SharePoint and mailboxes. The stamp set is therefore:
/// </para>
/// <list type="bullet">
///   <item><see cref="FileStorageContainerSelected"/> — granted in Entra by H10. Every app-only SharePoint
///   Embedded call (containers, drive items, versions, preview, upload sessions, drive delta, drive
///   subscriptions) needs only this plus the container-type application permission H8 grants; app-only calls
///   reach no SharePoint site, OneDrive or <c>/shares</c> URL. Its reach across customers on the one Model 1
///   container type is bounded by <c>SpeContainerOwnershipGuard</c>, not by Entra.</item>
///   <item><see cref="MailRead"/>, <see cref="MailReadWrite"/>, <see cref="MailSend"/> — the mailbox roles. On a
///   stamp they are NOT Entra grants: H14a grants them through Exchange "RBAC for Applications", scoped to the
///   customer's mail-enabled security group (T251 / owner D26). Exchange adds the two sources together, so an
///   Entra grant of any of them would reach every mailbox in the tenant; H10 never grants them and H13 T3 fails
///   when one is present in Entra.</item>
/// </list>
/// <para>
/// <b>Removed by task 261</b> (no app-only caller on a stamp — see the note): <c>Directory.ReadWrite.All</c>,
/// <c>User.ReadWrite.All</c>, <c>GroupMember.ReadWrite.All</c>, <c>User.Read.All</c>, <c>Group.Read.All</c> (the
/// demo self-service registration feature, which is off on every stamp —
/// <see cref="Infrastructure.DI.RegistrationModule.IsDemoProvisioningEnabled"/>); <c>User.Invite.All</c> (H11's
/// invitations run as the L2 Worker identity, not the stamp); <c>Files.Read.All</c>,
/// <c>Files.ReadWrite.All</c>, <c>Sites.Read.All</c>, <c>Sites.ReadWrite.All</c> (no app-only call leaves an SPE
/// container); <c>MailboxSettings.Read</c> (no caller). H10 now REMOVES any Graph app role on the stamp identity
/// that is not in <see cref="All"/>.
/// </para>
/// <para>
/// <b>Not this list:</b> the delegated scopes of the BFF app registration (H3's
/// <c>EntraAppRegPermissionCatalog</c>); the L2 control-plane identity's own roles
/// (<c>Sprk.Provisioning.ControlPlane.Handlers.ControlPlaneGraphAppRoles</c>, granted by
/// <c>scripts/provisioning/Grant-ControlPlaneIdentity.ps1</c>); and the extra roles of Spaarke's own platform
/// BFF (<c>spaarke-bff-dev</c>), whose demo registration and SPE-admin security pages are platform-only features
/// (the note lists them).
/// </para>
/// <para>
/// <b>AppRoleId (GUID) provenance</b>: GUIDs are never shipped from memory. Each one here was re-read live on
/// 2026-10-09 (task 261) from the Microsoft Graph resource service principal's <c>appRoles</c> in tenant
/// <c>a221a95e-6abc-4434-aecc-e48338a1b2f2</c> (first enumerated by r1 task 005, 2026-08-17). They are constant
/// across tenants. <see cref="Value"/> is the stable match key. Do NOT change a GUID without a fresh live
/// re-enumeration.
/// </para>
/// </remarks>
public static class GraphAppRoles
{
    // ── Role VALUE constants ──────────────────────────────────────────────────────────────────
    // Byte-identical to the strings Microsoft Graph exposes as AppRole.Value.

    /// <summary>SharePoint Embedded — access the file storage containers the container type grants.</summary>
    public const string FileStorageContainerSelected = "FileStorageContainer.Selected";

    /// <summary>Read mail — Exchange-scoped on stamps (H14a), never an Entra grant.</summary>
    public const string MailRead = "Mail.Read";

    /// <summary>Read and write mail — Exchange-scoped on stamps (H14a), never an Entra grant.</summary>
    public const string MailReadWrite = "Mail.ReadWrite";

    /// <summary>Send mail — Exchange-scoped on stamps (H14a), never an Entra grant.</summary>
    public const string MailSend = "Mail.Send";

    // ── Microsoft Graph application-permission (app role) IDs ─────────────────────────────────
    // Re-read live 2026-10-09 (task 261): GET /v1.0/servicePrincipals?$filter=appId eq
    // '00000003-0000-0000-c000-000000000000'&$select=appRoles. Constant across every tenant.

    private const string IdFileStorageContainerSelected = "40dc41bc-0f7e-42ff-89bd-d9516947e474";
    private const string IdMailRead = "810c84a8-4a9e-49e6-bf7d-12d183f40d01";
    private const string IdMailReadWrite = "e2a3a72e-5f79-4c64-b1b1-878b674786c9";
    private const string IdMailSend = "b633e1c5-b582-4048-a93e-9f11b44c7e96";

    /// <summary>
    /// Well-known Microsoft Graph resource service principal appId (constant across every tenant).
    /// The verifier enumerates a service principal's <c>appRoleAssignments</c> whose <c>resourceId</c>
    /// is the Graph SP with this appId.
    /// </summary>
    public const string GraphResourceAppId = "00000003-0000-0000-c000-000000000000";

    /// <summary>
    /// One Graph application role a stamp identity needs. <see cref="Value"/> is the stable match key;
    /// <see cref="AppRoleId"/> is the Graph well-known role GUID (nullable for schema stability — H10's
    /// escalation gate refuses a null). <see cref="ModuleConditional"/> roles matter only when their
    /// <see cref="OwningModule"/> is in use (the mailbox roles only with Email/Communication).
    /// <see cref="WhyRequired"/> names the app-only call that needs it.
    /// </summary>
    public sealed record GraphAppRole(
        string Value,
        string DisplayName,
        string? AppRoleId,
        string OwningModule,
        string WhyRequired,
        bool ModuleConditional);

    /// <summary>
    /// The stamp identity's Graph application roles — exactly the set documented in the task 261 note.
    /// H10 grants the Entra ones and removes every other Graph app role; H14a grants the mailbox ones
    /// through Exchange, scoped to the customer's group.
    /// </summary>
    public static readonly IReadOnlyList<GraphAppRole> All = new[]
    {
        // SPE / Documents — every app-only SharePoint Embedded call (Learn: FileStorageContainer.Selected
        // is the only Graph permission SPE needs; container-type permissions do the rest).
        new GraphAppRole(FileStorageContainerSelected, "Access selected file storage containers", IdFileStorageContainerSelected,
            "SPE / Documents", "App-only SharePoint Embedded container, drive-item, upload, delta and drive-subscription calls (SpeContainerOwnershipGuard clients).", false),

        // Email / Communication — module-conditional. Exchange RBAC for Applications on stamps (H14a), not Entra.
        new GraphAppRole(MailRead, "Read mail in all mailboxes", IdMailRead,
            "Email / Communication", "Inbound mail: GET messages/attachments, mail-folder delta, polling, and mailbox change subscriptions.", true),
        new GraphAppRole(MailReadWrite, "Read and write mail in all mailboxes", IdMailReadWrite,
            "Email / Communication", "Inbound mail: PATCH message isRead after processing.", true),
        new GraphAppRole(MailSend, "Send mail as any user", IdMailSend,
            "Email / Communication", "Shared-mailbox sendMail and mailbox verification.", true),
    };
}
