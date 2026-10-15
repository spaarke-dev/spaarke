// -----------------------------------------------------------------------------
// EntraAppRegPermissionCatalog.cs
//
// Task 130 (Wave G-3, H3 Graph SDK port) — single source of truth for the 6
// DELEGATED (OAuth2PermissionScope) permissions the BFF API app-registration
// requests, ported from the permission-id variables in
// scripts/Register-EntraAppRegistrations.ps1 ($GraphFilesReadWriteAll,
// $GraphFileStorageContainerSelectedDelegated, ... lines ~287-296, consumed by its
// `az ad app permission add` calls). Shared by BOTH
// GraphAppRegistrationProvisioner (requiredResourceAccess reconciler, both
// tenancy models since task 222) and GraphAdminConsentVerifier (expected-scope-value check against
// oauth2PermissionGrants) so the two collaborators can never drift apart on
// "what does admin consent need to cover".
//
// 2026-10-14 (ISS-018 F6, #1543): the Files.ReadWrite.All id here was the Graph
// APPLICATION-role id (75359482-...), not the delegated scope id; corrected to
// 863451e7-.... FileStorageContainer.Selected (delegated, 085ca537-...) was added
// because OBO SharePoint Embedded calls need it. Ids read live from the Microsoft
// Graph service principal's oauth2PermissionScopes.
//
// SCOPE NOTE (Path C — pivot to comply, per root CLAUDE.md §6.5, documented in
// task 130's completion notes): these are DELEGATED scopes (OAuth2PermissionScope
// / requiredResourceAccess "Scope" type), a DIFFERENT concern from the 14
// application-only (app-role) permissions in
// Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles.cs, which H10
// (H10DataverseAppUserGraphParityHandler + GraphRestAppRoleGranter) already
// grants onto the customer's UAMI service principal — NOT onto this
// app-registration's service principal. Do NOT merge the two catalogs; see
// design.md §4.1's H3 SDK-surface table (line ~197: "Microsoft.Graph 6.x
// (Applications/ServicePrincipals/Oauth2PermissionGrants)" — no
// AppRoleAssignedTo) for the authoritative H3 scope boundary.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;

/// <summary>
/// One delegated (OAuth2PermissionScope) permission the BFF API app-reg
/// requests via <c>requiredResourceAccess</c>. <see cref="Name"/> is a
/// diagnostic label only; <see cref="ResourceAppId"/> + <see cref="PermissionId"/>
/// form the match key Graph uses.
/// </summary>
public sealed record EntraAppRegPermission(
    string ResourceAppId,
    string PermissionId,
    string ScopeValue,
    string Name);

/// <summary>
/// The 6 delegated permissions requested by the BFF API app-registration.
/// Ported verbatim (GUIDs + scope values) from
/// scripts/Register-EntraAppRegistrations.ps1's <c>$Graph*</c> / <c>$DynamicsCrm*</c> id variables
/// — do NOT re-derive from memory; any change requires the same live
/// re-enumeration discipline GraphAppRoles.cs documents for the app-role catalog.
/// </summary>
public static class EntraAppRegPermissionCatalog
{
    /// <summary>Microsoft Graph resource appId (constant across every tenant).</summary>
    public const string GraphResourceAppId = "00000003-0000-0000-c000-000000000000";

    /// <summary>Dynamics CRM (Dataverse) resource appId (constant across every tenant).</summary>
    public const string DynamicsCrmResourceAppId = "00000007-0000-0000-c000-000000000000";

    /// <summary>The 6 delegated permissions, in the script's original declaration order.</summary>
    public static readonly IReadOnlyList<EntraAppRegPermission> All = new[]
    {
        new EntraAppRegPermission(GraphResourceAppId, "863451e7-0667-486c-a5d6-d135439485f0",
            "Files.ReadWrite.All", "Graph:Files.ReadWrite.All (delegated)"),
        new EntraAppRegPermission(GraphResourceAppId, "085ca537-6565-41c2-aca7-db852babc212",
            "FileStorageContainer.Selected", "Graph:FileStorageContainer.Selected (delegated)"),
        new EntraAppRegPermission(GraphResourceAppId, "89fe6a52-be36-487e-b7d8-d061c450a026",
            "Sites.ReadWrite.All", "Graph:Sites.ReadWrite.All (delegated)"),
        new EntraAppRegPermission(GraphResourceAppId, "e1fe6dd8-ba31-4d61-89e7-88639da4683d",
            "User.Read", "Graph:User.Read (delegated)"),
        new EntraAppRegPermission(GraphResourceAppId, "e383f46e-2787-4529-855e-0e479a3ffac0",
            "Mail.Send", "Graph:Mail.Send (delegated)"),
        new EntraAppRegPermission(DynamicsCrmResourceAppId, "78ce3f0f-a1ce-49c2-8cde-64b5c0896db4",
            "user_impersonation", "Dynamics:user_impersonation (delegated)"),
    };

    /// <summary>The scope VALUES only (for oauth2PermissionGrants scope-string membership checks).</summary>
    public static readonly IReadOnlyList<string> ScopeValues = All.Select(p => p.ScopeValue).ToArray();
}
