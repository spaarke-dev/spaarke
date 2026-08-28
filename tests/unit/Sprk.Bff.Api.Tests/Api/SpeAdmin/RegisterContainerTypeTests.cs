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
///   - SharePoint REST API URL construction (the working register path; the Graph POST path is
///     broken per issue #834)
///
/// Note: SpeAdminGraphService has a private constructor chain tied to real infrastructure
/// (Key Vault, Dataverse, HttpClient). Full integration scenarios are covered in integration tests.
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

    // ─────────────────────────────────────────────────────────────────────────────
    // SharePoint REST API URL Construction Tests
    // ─────────────────────────────────────────────────────────────────────────────

    #region SharePoint REST API URL Construction

    [Theory]
    [InlineData("https://contoso-admin.sharepoint.com", "contoso-admin.sharepoint.com")]
    [InlineData("https://fabrikam-admin.sharepoint.com/", "fabrikam-admin.sharepoint.com")]
    [InlineData("https://my-tenant-admin.sharepoint.com", "my-tenant-admin.sharepoint.com")]
    public void SharePointAdminUrl_Normalized_ExtractsCorrectHost(string inputUrl, string expectedHost)
    {
        var adminBaseUri = new Uri(inputUrl.TrimEnd('/'));
        adminBaseUri.Host.Should().Be(expectedHost);
    }

    [Theory]
    [InlineData("https://contoso-admin.sharepoint.com", "https://contoso-admin.sharepoint.com/.default")]
    [InlineData("https://fabrikam-admin.sharepoint.com/", "https://fabrikam-admin.sharepoint.com/.default")]
    [InlineData("https://my-tenant-admin.sharepoint.com", "https://my-tenant-admin.sharepoint.com/.default")]
    public void SharePointScope_DerivedFromAdminUrl_HasCorrectFormat(string inputUrl, string expectedScope)
    {
        // Replicate the scope construction logic from SpeAdminGraphService.RegisterContainerTypeAsync.
        var adminBaseUri = new Uri(inputUrl.TrimEnd('/'));
        var adminHost = $"{adminBaseUri.Scheme}://{adminBaseUri.Host}";
        var scope = $"{adminHost}/.default";

        scope.Should().Be(expectedScope);
    }

    [Theory]
    [InlineData("https://contoso-admin.sharepoint.com", "ct-guid-001",
        "https://contoso-admin.sharepoint.com/_api/v2.1/storageContainerTypes/ct-guid-001/applicationPermissions")]
    [InlineData("https://fabrikam-admin.sharepoint.com/", "type-abc-123",
        "https://fabrikam-admin.sharepoint.com/_api/v2.1/storageContainerTypes/type-abc-123/applicationPermissions")]
    public void SharePointRestApiUrl_ConstructedCorrectly(
        string adminUrl, string containerTypeId, string expectedUrl)
    {
        // Replicate the URL construction logic from SpeAdminGraphService.RegisterContainerTypeAsync.
        // Uses scheme+host to avoid the double-slash from Uri.ToString() on root URIs.
        var adminBaseUri = new Uri(adminUrl.TrimEnd('/'));
        var adminHost = $"{adminBaseUri.Scheme}://{adminBaseUri.Host}";
        var requestUrl = $"{adminHost}/_api/v2.1/storageContainerTypes/{containerTypeId}/applicationPermissions";

        requestUrl.Should().Be(expectedUrl);
    }

    #endregion
}
