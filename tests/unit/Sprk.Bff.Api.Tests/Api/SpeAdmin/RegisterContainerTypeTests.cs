using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.SpeAdmin;

/// <summary>
/// Unit tests for the Register Container Type endpoint (SPE-053).
///
/// Tests cover:
///   - Permission constants and validation set (ContainerTypePermissions)
///   - ADR-007 compliance: SharePoint SDK type isolation on RegisterContainerTypeResult (Graph SDK
///     isolation for nested domain records under this facade is covered generically by
///     tests/Spaarke.ArchTests/ADR007_NestedDomainRecordTests.cs — task 042)
///
/// The SharePoint REST URL-construction tests that used to live here were removed 2026-10-04: register no
/// longer calls the SharePoint REST API (it issues a delegated Graph grant), and those tests only re-derived
/// the URL locally rather than exercising production. The grant's verb, path, body and the legacy-name →
/// Graph permission mapping are pinned in tests/integration/contract/SpeAdmin/SpeAdminIdentityAndGrantContractTests.cs.
///
/// Unit tests validate DTOs, domain models, constants, and validation logic via direct method calls.
/// </summary>
public class RegisterContainerTypeTests
{
    // ─────────────────────────────────────────────────────────────────────────────
    // ContainerTypePermissions Constants Tests
    // ─────────────────────────────────────────────────────────────────────────────

    #region ContainerTypePermissions Constants

    [Fact]
    public void ContainerTypePermissions_ValidPermissions_UsesOrdinalComparison()
    {
        // Exact case is required — "readcontent" is not valid (case-sensitive)
        var validSet = ContainerTypePermissions.ValidPermissions;

        validSet.Should().Contain("ReadContent", "exact case must be accepted");
        validSet.Should().NotContain("readcontent", "lowercase should not match (ordinal)");
        validSet.Should().NotContain("READCONTENT", "uppercase should not match (ordinal)");
    }

    [Theory]
    [InlineData("readcontent")]
    [InlineData("WRITECONTENT")]
    [InlineData("InvalidPermission")]
    [InlineData("")]
    [InlineData("Files.Read.All")]
    [InlineData("FileStorageContainer.Selected")]
    public void ContainerTypePermissions_ValidPermissions_RejectsInvalidValues(string invalidPermission)
    {
        ContainerTypePermissions.ValidPermissions.Should().NotContain(invalidPermission);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    // ADR-007 Compliance Tests
    // ─────────────────────────────────────────────────────────────────────────────

    #region ADR-007 Compliance

    [Fact]
    public void RegisterContainerTypeResult_HasNoSharePointSdkTypeReferences()
    {
        var type = typeof(SpeAdminGraphService.RegisterContainerTypeResult);

        foreach (var prop in type.GetProperties())
        {
            prop.PropertyType.FullName.Should().NotContain(
                "Microsoft.SharePoint",
                $"property {prop.Name} must not expose SharePoint SDK types (ADR-007)");
            prop.PropertyType.FullName.Should().NotContain(
                "Microsoft.Graph.Models",
                $"property {prop.Name} must not expose Graph SDK types (ADR-007)");
        }
    }

    #endregion
}
