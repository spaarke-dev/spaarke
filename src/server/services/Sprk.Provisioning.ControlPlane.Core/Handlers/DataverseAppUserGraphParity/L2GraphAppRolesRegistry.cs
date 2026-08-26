// -----------------------------------------------------------------------------
// L2GraphAppRolesRegistry.cs
//
// Production IGraphAppRolesRegistry impl — a compiled, byte-for-byte mirror of
// the 14 (Value, AppRoleId) pairs in
// src/server/api/Sprk.Bff.Api/Infrastructure/Auth/GraphAppRoles.cs as of
// 2026-08-17 (r1 task 005 — all 14 GUIDs populated via live `az ad sp show`
// enumeration against tenant a221a95e-6abc-4434-aecc-e48338a1b2f2).
//
// DO NOT hand-edit a GUID here without a fresh live re-enumeration AND the
// matching edit to the BFF source (see IGraphAppRolesRegistry.cs file header
// for the drift-guard rationale + task 067 forward-reference).
//
// CORRECTION (2026-08-20, task 143 -- H10 live verification): GroupMember.
// ReadWrite.All's AppRoleId was WRONG (last 4 hex chars "6571" instead of
// "6695"), mirrored verbatim from the then-wrong BFF source. Live-verified
// against the real Microsoft Graph resource SP appRoles collection -- see
// GraphAppRoles.cs's matching correction comment for full evidence trail.
//
// ADDITION (2026-08-20, task 144 -- H11 live verification): a 15th role,
// User.Invite.All (AppRoleId "09850681-111b-4a89-9bed-3f2cae46d706"), was
// added to close a genuine grant-catalog gap H11's B2BGuest identity preset
// needs (POST /invitations) that the pre-existing 14-role catalog did not
// cover. See GraphAppRoles.cs's matching addition comment for full evidence
// trail (Microsoft Learn least-privileged-permission citation + live GUID
// ground-truthing).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

/// <inheritdoc cref="IGraphAppRolesRegistry"/>
public sealed class L2GraphAppRolesRegistry : IGraphAppRolesRegistry
{
    /// <inheritdoc/>
    public string GraphResourceAppId => "00000003-0000-0000-c000-000000000000";

    // Mirrors Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles.All verbatim
    // (Value + AppRoleId only — see IGraphAppRolesRegistry.cs for why the
    // richer BFF-side fields are not mirrored).
    private static readonly IReadOnlyList<GraphAppRoleEntry> Roles = new[]
    {
        // SPE / Documents
        new GraphAppRoleEntry("FileStorageContainer.Selected", "40dc41bc-0f7e-42ff-89bd-d9516947e474"),
        new GraphAppRoleEntry("Files.Read.All", "01d4889c-1287-42c6-ac1f-5d1e02578ef6"),
        new GraphAppRoleEntry("Files.ReadWrite.All", "75359482-378d-4052-8f01-80520e7db3cd"),
        new GraphAppRoleEntry("Sites.Read.All", "332a536c-c7ef-4017-ab91-336970924f0d"),
        new GraphAppRoleEntry("Sites.ReadWrite.All", "9492366f-7969-46a4-8d15-ed1a20078fff"),

        // Core / Directory
        new GraphAppRoleEntry("User.Read.All", "df021288-bdef-4463-88db-98f22de89214"),
        new GraphAppRoleEntry("Group.Read.All", "5b567255-7703-4780-807c-7be8301ae99b"),

        // Email / Communication
        new GraphAppRoleEntry("Mail.Read", "810c84a8-4a9e-49e6-bf7d-12d183f40d01"),
        new GraphAppRoleEntry("Mail.ReadWrite", "e2a3a72e-5f79-4c64-b1b1-878b674786c9"),
        new GraphAppRoleEntry("Mail.Send", "b633e1c5-b582-4048-a93e-9f11b44c7e96"),
        new GraphAppRoleEntry("MailboxSettings.Read", "40f97065-369a-49f4-947c-6a255697ae91"),

        // Self-Service Registration subsystem
        new GraphAppRoleEntry("User.ReadWrite.All", "741f803b-c850-494e-b5df-cde7c675a1ca"),
        new GraphAppRoleEntry("GroupMember.ReadWrite.All", "dbaae8cf-10b5-4b86-a4a1-f871c94c6695"),
        new GraphAppRoleEntry("Directory.ReadWrite.All", "19dbc75e-c2e2-444c-a770-ec69d8559fc7"),

        // Customer Provisioning (H11 B2BGuest identity preset) — added 2026-08-20, task 144.
        new GraphAppRoleEntry("User.Invite.All", "09850681-111b-4a89-9bed-3f2cae46d706"),
    };

    /// <inheritdoc/>
    public IReadOnlyList<GraphAppRoleEntry> GetAll() => Roles;
}
