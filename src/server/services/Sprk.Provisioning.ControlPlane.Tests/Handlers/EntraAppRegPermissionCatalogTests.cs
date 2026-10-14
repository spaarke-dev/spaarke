// -----------------------------------------------------------------------------
// EntraAppRegPermissionCatalogTests.cs
//
// ISS-018 F6 (#1543, 2026-10-14) — guards the DELEGATED permission catalog the
// BFF app-registration requests. The catalog once carried the Graph
// APPLICATION-role id (75359482-...) for delegated Files.ReadWrite.All, and
// lacked delegated FileStorageContainer.Selected (needed by OBO SharePoint
// Embedded calls). Ids were read live from the Microsoft Graph service
// principal's oauth2PermissionScopes.
// -----------------------------------------------------------------------------

using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class EntraAppRegPermissionCatalogTests
{
    private const string FilesReadWriteAllDelegatedId = "863451e7-0667-486c-a5d6-d135439485f0";
    private const string FileStorageContainerSelectedDelegatedId = "085ca537-6565-41c2-aca7-db852babc212";

    // Known Microsoft Graph APPLICATION (app-role) ids. None of these may ever
    // appear in this DELEGATED catalog — a role id requested as type=Scope is
    // silently the wrong permission.
    private static readonly string[] GraphApplicationRoleIds =
    [
        "75359482-378d-4052-8f01-80520e7db3cd", // Files.ReadWrite.All (application)
        "b633e1c5-b582-4048-a93e-9f11b44c7e96", // Mail.Send (application)
        "9492366f-7969-46a4-8d15-ed1a20078fff", // Sites.ReadWrite.All (application)
        "40dc41bc-0f7e-42ff-89bd-d9516947e474", // FileStorageContainer.Selected (application)
    ];

    [Fact]
    public void FilesReadWriteAll_UsesDelegatedScopeId_NotTheApplicationRoleId()
    {
        var files = EntraAppRegPermissionCatalog.All.Single(p => p.ScopeValue == "Files.ReadWrite.All");

        files.ResourceAppId.Should().Be(EntraAppRegPermissionCatalog.GraphResourceAppId);
        files.PermissionId.Should().Be(FilesReadWriteAllDelegatedId);
        files.PermissionId.Should().NotBe("75359482-378d-4052-8f01-80520e7db3cd");
    }

    [Fact]
    public void Catalog_ContainsDelegatedFileStorageContainerSelected()
    {
        var fsc = EntraAppRegPermissionCatalog.All.SingleOrDefault(p => p.ScopeValue == "FileStorageContainer.Selected");

        fsc.Should().NotBeNull("OBO SharePoint Embedded calls need delegated FileStorageContainer.Selected");
        fsc!.ResourceAppId.Should().Be(EntraAppRegPermissionCatalog.GraphResourceAppId);
        fsc.PermissionId.Should().Be(FileStorageContainerSelectedDelegatedId);
    }

    [Fact]
    public void NoGraphEntry_UsesAKnownGraphApplicationRoleId()
    {
        var graphIds = EntraAppRegPermissionCatalog.All
            .Where(p => p.ResourceAppId == EntraAppRegPermissionCatalog.GraphResourceAppId)
            .Select(p => p.PermissionId);

        graphIds.Should().NotContain(GraphApplicationRoleIds,
            "this catalog is DELEGATED scopes (type=Scope); application-role ids belong in GraphAppRoles.cs");
    }

    [Fact]
    public void ScopeValues_HasNoDuplicates()
    {
        EntraAppRegPermissionCatalog.ScopeValues.Should().OnlyHaveUniqueItems();
    }
}
