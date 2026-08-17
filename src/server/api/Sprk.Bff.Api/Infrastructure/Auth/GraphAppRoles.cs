namespace Sprk.Bff.Api.Infrastructure.Auth;

/// <summary>
/// Single source of truth for the Microsoft Graph <b>application</b> (app-only) permissions —
/// "app roles" — that the Spaarke BFF identity must hold. Refactor ask #4
/// (<c>code-quality-and-assurance-r3</c> task 062): the expected-role list previously lived only as
/// prose + a dynamic <c>az</c> enumeration in <c>docs/guides/auth-deployment-setup.md</c> §5 and as a
/// second hardcoded list in <c>scripts/Setup-EntraInfrastructure.ps1</c>. This class is the canonical
/// machine-consumable list; the runbook and the provisioning scripts (and
/// <c>customer-provisioning-orchestration-r1</c>'s H10 tooling) reference it as authoritative.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope</b>: these are the <b>application</b> (app-only) roles the BFF requests via the
/// <c>.default</c> scope (see <see cref="Infrastructure.Graph.GraphClientFactory"/>). The <c>.default</c>
/// grant surfaces exactly the app roles that have been admin-consented to the identity, so this list
/// is the contract that admin-consent must satisfy. It is <b>NOT</b> the delegated-scope list — those
/// live in <c>scripts/Register-EntraAppRegistrations.ps1</c> (app-registration creation) and are a
/// deliberately separate concern; do not merge the two.
/// </para>
/// <para>
/// <b>Grant target (per BFF Auth Surface Map, task 019, §C GAP #4)</b>: app-only Graph is authenticated
/// by the <b>User-Assigned Managed Identity</b> service principal (<c>mi-bff-api-{env}</c>), so these
/// roles MUST be granted on the UAMI SP — not on the app registration and not on the retired
/// <c>56ae2188…</c> principal. Adding a role = exactly ONE edit here; the operator/H10 tooling then
/// replays the grant onto the UAMI SP.
/// </para>
/// <para>
/// <b>AppRoleId (GUID) provenance</b>: per this task's escalation rule, GUIDs are NOT shipped from
/// memory. Only the three GUIDs authoritatively present in-repo (from
/// <c>scripts/Setup-EntraInfrastructure.ps1</c>, the Microsoft Graph well-known application-permission
/// IDs for the self-service-registration subsystem) are populated. The remaining
/// <see cref="GraphAppRole.AppRoleId"/> values are <c>null</c> pending confirmation from a live §5a
/// enumeration of the Graph resource SP (<see cref="GraphResourceAppId"/>) — a wrong GUID in the
/// single source of truth would propagate to every future provisioning run, which is worse than the
/// current drift. <see cref="Value"/> is the stable match key in the interim.
/// </para>
/// </remarks>
public static class GraphAppRoles
{
    // ── Role VALUE constants ──────────────────────────────────────────────────────────────────
    // Byte-identical to the strings Microsoft Graph exposes as AppRole.Value and to the literals
    // in the provisioning scripts + runbook §5. Reference these instead of re-typing the string.

    /// <summary>SharePoint Embedded — access files in explicitly-selected file-storage containers.</summary>
    public const string FileStorageContainerSelected = "FileStorageContainer.Selected";

    /// <summary>Read files in all site collections (app-only).</summary>
    public const string FilesReadAll = "Files.Read.All";

    /// <summary>Read and write files in all site collections (app-only).</summary>
    public const string FilesReadWriteAll = "Files.ReadWrite.All";

    /// <summary>Read items in all site collections (app-only).</summary>
    public const string SitesReadAll = "Sites.Read.All";

    /// <summary>Read and write items in all site collections (app-only).</summary>
    public const string SitesReadWriteAll = "Sites.ReadWrite.All";

    /// <summary>Read all users' full profiles (app-only).</summary>
    public const string UserReadAll = "User.Read.All";

    /// <summary>Read all groups (app-only).</summary>
    public const string GroupReadAll = "Group.Read.All";

    /// <summary>Read mail in all mailboxes (app-only) — Email/Communication only.</summary>
    public const string MailRead = "Mail.Read";

    /// <summary>Read and write mail in all mailboxes (app-only) — Email/Communication only.</summary>
    public const string MailReadWrite = "Mail.ReadWrite";

    /// <summary>Send mail as any user (app-only) — Email/Communication only.</summary>
    public const string MailSend = "Mail.Send";

    /// <summary>Read all users' mailbox settings (app-only) — Email/Communication only.</summary>
    public const string MailboxSettingsRead = "MailboxSettings.Read";

    /// <summary>Read and write all users' full profiles (app-only) — Self-Service Registration subsystem.</summary>
    public const string UserReadWriteAll = "User.ReadWrite.All";

    /// <summary>Read and write all group memberships (app-only) — Self-Service Registration subsystem.</summary>
    public const string GroupMemberReadWriteAll = "GroupMember.ReadWrite.All";

    /// <summary>Read and write directory data (app-only) — Self-Service Registration subsystem.</summary>
    public const string DirectoryReadWriteAll = "Directory.ReadWrite.All";

    // ── Well-known Microsoft Graph application-permission (app role) IDs ─────────────────────
    // Enumerated 2026-08-17 via `az ad sp show --id 00000003-0000-0000-c000-000000000000
    //   --query "appRoles[?value=='<name>'].id"` (r1 task 005 — H10 escalation gate; discovery
    //   report §12). These are the app-role definition IDs on the Microsoft Graph resource
    //   service principal (constant across every tenant); do NOT change without a live
    //   re-enumeration.

    // SPE / Documents
    private const string IdFileStorageContainerSelected = "40dc41bc-0f7e-42ff-89bd-d9516947e474";
    private const string IdFilesReadAll = "01d4889c-1287-42c6-ac1f-5d1e02578ef6";
    private const string IdFilesReadWriteAll = "75359482-378d-4052-8f01-80520e7db3cd";
    private const string IdSitesReadAll = "332a536c-c7ef-4017-ab91-336970924f0d";
    private const string IdSitesReadWriteAll = "9492366f-7969-46a4-8d15-ed1a20078fff";

    // Core / Directory
    private const string IdUserReadAll = "df021288-bdef-4463-88db-98f22de89214";
    private const string IdGroupReadAll = "5b567255-7703-4780-807c-7be8301ae99b";

    // Email / Communication
    private const string IdMailRead = "810c84a8-4a9e-49e6-bf7d-12d183f40d01";

    // Self-Service Registration subsystem — sourced pre-r1 from
    // scripts/Setup-EntraInfrastructure.ps1:78-80.
    private const string IdUserReadWriteAll = "741f803b-c850-494e-b5df-cde7c675a1ca";
    private const string IdGroupMemberReadWriteAll = "dbaae8cf-10b5-4b86-a4a1-f871c94c6571";
    private const string IdDirectoryReadWriteAll = "19dbc75e-c2e2-444c-a770-ec69d8559fc7";

    /// <summary>
    /// Well-known Microsoft Graph resource service principal appId (constant across every tenant).
    /// The verifier enumerates a service principal's <c>appRoleAssignments</c> whose <c>resourceId</c>
    /// is the Graph SP with this appId.
    /// </summary>
    public const string GraphResourceAppId = "00000003-0000-0000-c000-000000000000";

    /// <summary>
    /// One expected Graph application role. <see cref="Value"/> is the stable match key;
    /// <see cref="AppRoleId"/> is the Graph well-known role GUID (nullable pending live §5a
    /// confirmation — see the class remarks). <see cref="ModuleConditional"/> roles are only required
    /// when their <see cref="OwningModule"/> is enabled (e.g. <c>Mail.*</c> only with Email/Communication).
    /// </summary>
    public sealed record GraphAppRole(
        string Value,
        string DisplayName,
        string? AppRoleId,
        string OwningModule,
        string WhyRequired,
        bool ModuleConditional);

    /// <summary>
    /// The canonical expected app-only Graph role set. Covers the §5b baseline (SPE + directory +
    /// Email/Communication) plus the three Self-Service Registration subsystem roles. Adding a role
    /// is a single append here.
    /// </summary>
    public static readonly IReadOnlyList<GraphAppRole> All = new[]
    {
        // SPE / Documents — always required (SharePoint Embedded container + file access)
        new GraphAppRole(FileStorageContainerSelected, "Access selected file storage containers", IdFileStorageContainerSelected,
            "SPE / Documents", "SharePoint Embedded container access for app-only container/drive operations.", false),
        new GraphAppRole(FilesReadAll, "Read files in all site collections", IdFilesReadAll,
            "SPE / Documents", "App-only read of SPE/SharePoint file content (agent grounding, indexing).", false),
        new GraphAppRole(FilesReadWriteAll, "Read and write files in all site collections", IdFilesReadWriteAll,
            "SPE / Documents", "App-only file read/write for document automation + SPE operations.", false),
        new GraphAppRole(SitesReadAll, "Read items in all site collections", IdSitesReadAll,
            "SPE / Documents", "App-only site/drive metadata resolution for SPE containers.", false),
        new GraphAppRole(SitesReadWriteAll, "Read and write items in all site collections", IdSitesReadWriteAll,
            "SPE / Documents", "App-only site/list write for SPE container provisioning.", false),

        // Core directory / user resolution — always required
        new GraphAppRole(UserReadAll, "Read all users' full profiles", IdUserReadAll,
            "Core / Directory", "Resolve user identities for authorization + notification addressing.", false),
        new GraphAppRole(GroupReadAll, "Read all groups", IdGroupReadAll,
            "Core / Authorization", "Resolve group membership for access decisions + membership junction sync.", false),

        // Email / Communication — module-conditional (also requires Exchange ApplicationAccessPolicy)
        new GraphAppRole(MailRead, "Read mail in all mailboxes", IdMailRead,
            "Email / Communication", "App-only inbound mail read for email-to-document automation.", true),
        new GraphAppRole(MailReadWrite, "Read and write mail in all mailboxes", null,
            "Email / Communication", "App-only mail read/write for processing + categorization.", true),
        new GraphAppRole(MailSend, "Send mail as any user", null,
            "Email / Communication", "App-only outbound send for system notifications + communications.", true),
        new GraphAppRole(MailboxSettingsRead, "Read all users' mailbox settings", null,
            "Email / Communication", "App-only mailbox settings read (time zone / working hours) for scheduling.", true),

        // Self-Service Registration subsystem — module-conditional (demo user provisioning)
        new GraphAppRole(UserReadWriteAll, "Read and write all users' full profiles", IdUserReadWriteAll,
            "Self-Service Registration", "Provision/update demo users during self-service registration.", true),
        new GraphAppRole(GroupMemberReadWriteAll, "Read and write all group memberships", IdGroupMemberReadWriteAll,
            "Self-Service Registration", "Add provisioned users to the demo security group.", true),
        new GraphAppRole(DirectoryReadWriteAll, "Read and write directory data", IdDirectoryReadWriteAll,
            "Self-Service Registration", "Directory writes required by the registration provisioning pipeline.", true),
    };
}
