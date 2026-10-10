// -----------------------------------------------------------------------------
// L2GraphAppRolesRegistry.cs
//
// Production IGraphAppRolesRegistry impl — a compiled, byte-for-byte mirror of
// the (Value, AppRoleId) pairs in
// src/server/api/Sprk.Bff.Api/Infrastructure/Auth/GraphAppRoles.cs.
//
// TASK 261 (G31, owner 2026-10-09 "ensure this is accurately scoped"): the
// catalog is the stamp identity's evidence-backed set — 4 roles, down from 15.
// FileStorageContainer.Selected is the only Entra grant; Mail.Read,
// Mail.ReadWrite and Mail.Send are Exchange-scoped (H14a). Each row's call site
// and Microsoft Learn citation is in
// projects/customer-provisioning-orchestration-r1/notes/t261-stamp-graph-least-privilege.md;
// tests/Spaarke.ArchTests/TenantIsolation/StampGraphAppRoleEvidenceTests.cs
// fails when this file, GraphAppRoles.cs and the note disagree.
//
// DO NOT hand-edit a GUID here without a fresh live re-enumeration AND the
// matching edit to the BFF source. GUIDs re-read live 2026-10-09 from the
// Microsoft Graph resource SP's appRoles (tenant a221a95e-…; constant across
// tenants). History: task 143 corrected a wrong GroupMember.ReadWrite.All GUID;
// task 144 added User.Invite.All; task 261 removed 11 roles (see the note).
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
        // SPE / Documents — the only Entra grant H10 makes.
        new GraphAppRoleEntry("FileStorageContainer.Selected", "40dc41bc-0f7e-42ff-89bd-d9516947e474"),

        // Email / Communication — Exchange-scoped (H14a), never Entra.
        new GraphAppRoleEntry("Mail.Read", "810c84a8-4a9e-49e6-bf7d-12d183f40d01"),
        new GraphAppRoleEntry("Mail.ReadWrite", "e2a3a72e-5f79-4c64-b1b1-878b674786c9"),
        new GraphAppRoleEntry("Mail.Send", "b633e1c5-b582-4048-a93e-9f11b44c7e96"),
    };

    /// <inheritdoc/>
    public IReadOnlyList<GraphAppRoleEntry> GetAll() => Roles;
}
