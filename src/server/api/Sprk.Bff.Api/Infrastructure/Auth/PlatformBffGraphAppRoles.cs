namespace Sprk.Bff.Api.Infrastructure.Auth;

/// <summary>
/// The Microsoft Graph application roles Spaarke's OWN platform BFF identity (<c>mi-bff-api-{env}</c>: dev, demo, prod)
/// needs — the identity that runs the features a customer stamp does not: demo self-service registration and the SPE
/// Admin security pages. Task 261 (G31) split this from <see cref="GraphAppRoles"/> (the customer STAMP set) so that
/// rebuilding a platform/demo BFF cannot silently lose a role it calls with, and a stamp can never be granted these.
/// </summary>
/// <remarks>
/// <para><b>Grant</b>: an operator runs <c>scripts/Grant-GraphAppRoles.ps1 -CatalogPath …/PlatformBffGraphAppRoles.cs</c>
/// against the platform identity (add-only; nothing is removed). Nothing in L2 reads this class — it is not mirrored,
/// and H10 never grants it. <c>StampGraphAppRoleEvidenceTests</c> checks it against §7.2 of
/// <c>projects/customer-provisioning-orchestration-r1/notes/t261-stamp-graph-least-privilege.md</c>.</para>
/// <para><b>Contents</b>: the stamp set (SPE + mailbox, as Entra grants here — the platform BFF has no Exchange-scoped
/// policy) plus the least-privileged Microsoft Learn application role for each platform-only call, with the call site
/// in the note. The roles the live identities hold today are broader (<c>User.ReadWrite.All</c>,
/// <c>Group.ReadWrite.All</c>, <c>SecurityEvents.Read.All</c> alone, <c>FileStorageContainerTypeReg.Selected</c>); the
/// note's §7.2 lists each and what the code actually needs. Removing them is an owner step.</para>
/// <para>GUIDs re-read live 2026-10-09 from the Microsoft Graph service principal's <c>appRoles</c>.</para>
/// </remarks>
public static class PlatformBffGraphAppRoles
{
    /// <summary>SharePoint Embedded app-only calls (same as the stamp set).</summary>
    public const string FileStorageContainerSelected = "FileStorageContainer.Selected";

    /// <summary>Inbound mail read (platform BFF: an Entra grant).</summary>
    public const string MailRead = "Mail.Read";

    /// <summary>Inbound mail mark-as-read.</summary>
    public const string MailReadWrite = "Mail.ReadWrite";

    /// <summary>Shared-mailbox send.</summary>
    public const string MailSend = "Mail.Send";

    /// <summary>Demo registration: <c>POST /users</c>.</summary>
    public const string UserCreate = "User.Create";

    /// <summary>Demo registration: <c>GET /users?$filter</c>, <c>GET /users/{id}</c>.</summary>
    public const string UserReadAll = "User.Read.All";

    /// <summary>Demo registration: <c>POST /users/{id}/assignLicense</c>.</summary>
    public const string LicenseAssignmentReadWriteAll = "LicenseAssignment.ReadWrite.All";

    /// <summary>Demo expiry: <c>PATCH /users/{id} {accountEnabled:false}</c> (with User.Read.All).</summary>
    public const string UserEnableDisableAccountAll = "User.EnableDisableAccount.All";

    /// <summary>Demo registration: add/remove/list members of the demo security group.</summary>
    public const string GroupMemberReadWriteAll = "GroupMember.ReadWrite.All";

    /// <summary>SPE Admin security page: <c>GET /security/alerts_v2</c>.</summary>
    public const string SecurityAlertReadAll = "SecurityAlert.Read.All";

    /// <summary>SPE Admin security page: <c>GET /security/secureScores</c>.</summary>
    public const string SecurityEventsReadAll = "SecurityEvents.Read.All";

    private const string IdFileStorageContainerSelected = "40dc41bc-0f7e-42ff-89bd-d9516947e474";
    private const string IdMailRead = "810c84a8-4a9e-49e6-bf7d-12d183f40d01";
    private const string IdMailReadWrite = "e2a3a72e-5f79-4c64-b1b1-878b674786c9";
    private const string IdMailSend = "b633e1c5-b582-4048-a93e-9f11b44c7e96";
    private const string IdUserCreate = "4240f680-4f73-4082-a766-aa916a2dc9b3";
    private const string IdUserReadAll = "df021288-bdef-4463-88db-98f22de89214";
    private const string IdLicenseAssignmentReadWriteAll = "5facf0c1-8979-4e95-abcf-ff3d079771c0";
    private const string IdUserEnableDisableAccountAll = "3011c876-62b7-4ada-afa2-506cbbecc68c";
    private const string IdGroupMemberReadWriteAll = "dbaae8cf-10b5-4b86-a4a1-f871c94c6695";
    private const string IdSecurityAlertReadAll = "472e4a4d-bb4a-4026-98d1-0b0d74cb74a5";
    private const string IdSecurityEventsReadAll = "bf394140-e372-4bf9-a898-299cfc7564e5";

    /// <summary>Well-known Microsoft Graph resource service principal appId.</summary>
    public const string GraphResourceAppId = "00000003-0000-0000-c000-000000000000";

    /// <summary>One Graph application role the platform BFF needs, with the call that needs it.</summary>
    public sealed record GraphAppRole(string Value, string DisplayName, string? AppRoleId, string OwningModule, string WhyRequired, bool ModuleConditional);

    /// <summary>The platform BFF identity's Graph application roles — exactly the set the task 261 note §7.2 documents.</summary>
    public static readonly IReadOnlyList<GraphAppRole> All = new[]
    {
        new GraphAppRole(FileStorageContainerSelected, "Access selected file storage containers", IdFileStorageContainerSelected,
            "SPE / Documents", "App-only SharePoint Embedded calls.", false),
        new GraphAppRole(MailRead, "Read mail in all mailboxes", IdMailRead,
            "Email / Communication", "Inbound mail read, delta, polling, mailbox subscriptions.", true),
        new GraphAppRole(MailReadWrite, "Read and write mail in all mailboxes", IdMailReadWrite,
            "Email / Communication", "Inbound mark-as-read PATCH.", true),
        new GraphAppRole(MailSend, "Send mail as any user", IdMailSend,
            "Email / Communication", "Shared-mailbox sendMail, mailbox verification.", true),
        new GraphAppRole(UserCreate, "Create users", IdUserCreate,
            "Demo Registration", "POST /users (GraphUserService.CreateUserAsync).", true),
        new GraphAppRole(UserReadAll, "Read all users' full profiles", IdUserReadAll,
            "Demo Registration", "GET /users filter + assignedLicenses/accountEnabled reads.", true),
        new GraphAppRole(LicenseAssignmentReadWriteAll, "Manage all license assignments", IdLicenseAssignmentReadWriteAll,
            "Demo Registration", "POST /users/{id}/assignLicense.", true),
        new GraphAppRole(UserEnableDisableAccountAll, "Enable and disable user accounts", IdUserEnableDisableAccountAll,
            "Demo Registration", "PATCH /users/{id} accountEnabled=false at demo expiry.", true),
        new GraphAppRole(GroupMemberReadWriteAll, "Read and write all group memberships", IdGroupMemberReadWriteAll,
            "Demo Registration", "Demo group member add/remove/list.", true),
        new GraphAppRole(SecurityAlertReadAll, "Read all security alerts", IdSecurityAlertReadAll,
            "SPE Admin security", "GET /security/alerts_v2.", true),
        new GraphAppRole(SecurityEventsReadAll, "Read your organization's security events", IdSecurityEventsReadAll,
            "SPE Admin security", "GET /security/secureScores.", true),
    };
}
