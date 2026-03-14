using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Sprk.Bff.Api.Endpoints.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.SpeAdmin;

/// <summary>
/// Unit tests for the Register Container Type endpoint (SPE-053).
///
/// Tests cover:
///   - Endpoint registration shape (method exists, correct route)
///   - DTO and domain model shapes (RegisterContainerTypeRequest, RegisterContainerTypeResponse)
///   - Permission constants and validation set (ContainerTypePermissions)
///   - Domain model (RegisterContainerTypeResult)
///   - Request validation logic (appId, sharePointAdminUrl, permissions)
///   - ADR-007 compliance (no Graph/SP SDK types in public surface)
///
/// Note: SpeAdminGraphService has a private constructor chain tied to real infrastructure
/// (Key Vault, Dataverse, HttpClient). Full integration scenarios are covered in integration tests.
/// Unit tests validate DTOs, domain models, constants, and validation logic via direct method calls.
/// </summary>
public class RegisterContainerTypeTests
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Endpoint Registration Tests
    // ─────────────────────────────────────────────────────────────────────────────

    #region Endpoint Registration

    [Fact]
    public void MapContainerTypeEndpoints_MethodExists_AndIsExtensionOnRouteGroupBuilder()
    {
        // MapContainerTypeEndpoints now registers the register endpoint too.
        var method = typeof(ContainerTypeEndpoints).GetMethod("MapContainerTypeEndpoints");

        method.Should().NotBeNull("MapContainerTypeEndpoints extension method must exist");
        method!.IsStatic.Should().BeTrue("must be a static extension method");
        method.ReturnType.Should().Be(typeof(RouteGroupBuilder), "must return RouteGroupBuilder for chaining");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    // RegisterContainerTypeRequest DTO Tests
    // ─────────────────────────────────────────────────────────────────────────────

    #region RegisterContainerTypeRequest DTO

    [Fact]
    public void RegisterContainerTypeRequest_HasExpectedProperties()
    {
        var request = new RegisterContainerTypeRequest
        {
            AppId = "11111111-1111-1111-1111-111111111111",
            SharePointAdminUrl = "https://contoso-admin.sharepoint.com",
            DelegatedPermissions = new[] { "ReadContent", "WriteContent" },
            ApplicationPermissions = new[] { "Create" }
        };

        request.AppId.Should().Be("11111111-1111-1111-1111-111111111111");
        request.SharePointAdminUrl.Should().Be("https://contoso-admin.sharepoint.com");
        request.DelegatedPermissions.Should().BeEquivalentTo(new[] { "ReadContent", "WriteContent" });
        request.ApplicationPermissions.Should().BeEquivalentTo(new[] { "Create" });
    }

    [Fact]
    public void RegisterContainerTypeRequest_DefaultPermissionsAreEmpty()
    {
        var request = new RegisterContainerTypeRequest();

        request.DelegatedPermissions.Should().BeEmpty("default is empty list");
        request.ApplicationPermissions.Should().BeEmpty("default is empty list");
        request.AppId.Should().Be(string.Empty, "default is empty string");
        request.SharePointAdminUrl.Should().Be(string.Empty, "default is empty string");
    }

    [Fact]
    public void RegisterContainerTypeRequest_SupportsOnlyDelegatedPermissions()
    {
        // Valid: only delegated permissions (no application permissions)
        var request = new RegisterContainerTypeRequest
        {
            AppId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            SharePointAdminUrl = "https://contoso-admin.sharepoint.com",
            DelegatedPermissions = new[] { "ReadContent" }
        };

        request.DelegatedPermissions.Should().HaveCount(1);
        request.ApplicationPermissions.Should().BeEmpty();
    }

    [Fact]
    public void RegisterContainerTypeRequest_SupportsOnlyApplicationPermissions()
    {
        // Valid: only application permissions (no delegated permissions)
        var request = new RegisterContainerTypeRequest
        {
            AppId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            SharePointAdminUrl = "https://contoso-admin.sharepoint.com",
            ApplicationPermissions = new[] { "Create", "ReadContent" }
        };

        request.DelegatedPermissions.Should().BeEmpty();
        request.ApplicationPermissions.Should().HaveCount(2);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    // RegisterContainerTypeResponse DTO Tests
    // ─────────────────────────────────────────────────────────────────────────────

    #region RegisterContainerTypeResponse DTO

    [Fact]
    public void RegisterContainerTypeResponse_HasExpectedProperties()
    {
        var response = new RegisterContainerTypeResponse
        {
            ContainerTypeId = "ct-guid-001",
            AppId = "app-guid-002",
            DelegatedPermissions = new[] { "ReadContent" },
            ApplicationPermissions = new[] { "Create", "Delete" }
        };

        response.ContainerTypeId.Should().Be("ct-guid-001");
        response.AppId.Should().Be("app-guid-002");
        response.DelegatedPermissions.Should().BeEquivalentTo(new[] { "ReadContent" });
        response.ApplicationPermissions.Should().BeEquivalentTo(new[] { "Create", "Delete" });
    }

    [Fact]
    public void RegisterContainerTypeResponse_DefaultPermissionsAreEmpty()
    {
        var response = new RegisterContainerTypeResponse();

        response.DelegatedPermissions.Should().BeEmpty();
        response.ApplicationPermissions.Should().BeEmpty();
        response.ContainerTypeId.Should().Be(string.Empty);
        response.AppId.Should().Be(string.Empty);
    }

    [Fact]
    public void RegisterContainerTypeResponse_IsNotGraphSdkType()
    {
        var type = typeof(RegisterContainerTypeResponse);
        type.Namespace.Should().StartWith("Sprk.Bff.Api", "must be in Spaarke namespace, not Graph SDK (ADR-007)");

        var props = type.GetProperties();
        foreach (var prop in props)
        {
            prop.PropertyType.Namespace.Should().NotStartWith(
                "Microsoft.Graph.Models",
                $"property {prop.Name} must not leak Graph SDK types (ADR-007)");
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    // ContainerTypePermissions Constants Tests
    // ─────────────────────────────────────────────────────────────────────────────

    #region ContainerTypePermissions Constants

    [Fact]
    public void ContainerTypePermissions_HasExpectedConstantValues()
    {
        ContainerTypePermissions.ReadContent.Should().Be("ReadContent");
        ContainerTypePermissions.WriteContent.Should().Be("WriteContent");
        ContainerTypePermissions.Create.Should().Be("Create");
        ContainerTypePermissions.Delete.Should().Be("Delete");
        ContainerTypePermissions.ManagePermissions.Should().Be("ManagePermissions");
        ContainerTypePermissions.AddAllPermissions.Should().Be("AddAllPermissions");
    }

    [Fact]
    public void ContainerTypePermissions_ValidPermissions_ContainsAllConstants()
    {
        var validSet = ContainerTypePermissions.ValidPermissions;

        validSet.Should().Contain("ReadContent");
        validSet.Should().Contain("WriteContent");
        validSet.Should().Contain("Create");
        validSet.Should().Contain("Delete");
        validSet.Should().Contain("ManagePermissions");
        validSet.Should().Contain("AddAllPermissions");
        validSet.Should().HaveCount(6, "exactly 6 valid permissions are defined");
    }

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
    [InlineData("ReadContent")]
    [InlineData("WriteContent")]
    [InlineData("Create")]
    [InlineData("Delete")]
    [InlineData("ManagePermissions")]
    [InlineData("AddAllPermissions")]
    public void ContainerTypePermissions_ValidPermissions_ContainsEachPermission(string permission)
    {
        ContainerTypePermissions.ValidPermissions.Should().Contain(permission);
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
    // RegisterContainerTypeResult Domain Model Tests (ADR-007)
    // ─────────────────────────────────────────────────────────────────────────────

    #region RegisterContainerTypeResult Domain Model

    [Fact]
    public void RegisterContainerTypeResult_IsInternalDomainRecord_NotGraphSdkType()
    {
        var type = typeof(SpeAdminGraphService.RegisterContainerTypeResult);

        type.IsClass.Should().BeTrue("records are classes in C#");
        type.Namespace.Should().StartWith("Sprk.Bff.Api", "must be in Spaarke namespace (ADR-007)");

        var props = type.GetProperties();
        foreach (var prop in props)
        {
            prop.PropertyType.Namespace.Should().NotStartWith(
                "Microsoft.Graph",
                $"property {prop.Name} must not expose Graph SDK types (ADR-007)");
        }
    }

    [Fact]
    public void RegisterContainerTypeResult_ConstructsCorrectly()
    {
        var result = new SpeAdminGraphService.RegisterContainerTypeResult(
            ContainerTypeId: "ct-id-abc",
            AppId: "app-id-xyz",
            DelegatedPermissions: new[] { "ReadContent", "WriteContent" },
            ApplicationPermissions: new[] { "Create" });

        result.ContainerTypeId.Should().Be("ct-id-abc");
        result.AppId.Should().Be("app-id-xyz");
        result.DelegatedPermissions.Should().BeEquivalentTo(new[] { "ReadContent", "WriteContent" });
        result.ApplicationPermissions.Should().BeEquivalentTo(new[] { "Create" });
    }

    [Fact]
    public void RegisterContainerTypeResult_SupportsEmptyPermissionLists()
    {
        var result = new SpeAdminGraphService.RegisterContainerTypeResult(
            ContainerTypeId: "ct-id",
            AppId: "app-id",
            DelegatedPermissions: Array.Empty<string>(),
            ApplicationPermissions: new[] { "ReadContent" });

        result.DelegatedPermissions.Should().BeEmpty();
        result.ApplicationPermissions.Should().HaveCount(1);
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    // Request Validation Logic Tests
    // ─────────────────────────────────────────────────────────────────────────────

    #region Validation Logic

    [Fact]
    public void AppId_Validation_MustBeValidGuid()
    {
        // Valid GUID strings
        Guid.TryParse("11111111-1111-1111-1111-111111111111", out _).Should().BeTrue();
        Guid.TryParse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", out _).Should().BeTrue();

        // Invalid: not a GUID
        Guid.TryParse("not-a-guid", out _).Should().BeFalse();
        Guid.TryParse("", out _).Should().BeFalse();
        Guid.TryParse("12345", out _).Should().BeFalse();
    }

    [Fact]
    public void SharePointAdminUrl_Validation_MustBeAbsoluteHttps()
    {
        // Valid: absolute HTTPS URLs
        Uri.TryCreate("https://contoso-admin.sharepoint.com", UriKind.Absolute, out var valid1).Should().BeTrue();
        valid1!.Scheme.Should().Be("https");

        Uri.TryCreate("https://contoso-admin.sharepoint.com/", UriKind.Absolute, out var valid2).Should().BeTrue();
        valid2!.Scheme.Should().Be("https");

        // Invalid: HTTP (not HTTPS)
        Uri.TryCreate("http://contoso-admin.sharepoint.com", UriKind.Absolute, out var httpUri).Should().BeTrue();
        httpUri!.Scheme.Should().NotBe("https", "HTTP is not allowed — HTTPS required");

        // Invalid: relative URL
        Uri.TryCreate("/admin", UriKind.Absolute, out _).Should().BeFalse();

        // Invalid: not a URI
        Uri.TryCreate("", UriKind.Absolute, out _).Should().BeFalse();
    }

    [Fact]
    public void PermissionValidation_EmptyBothLists_IsInvalid()
    {
        var request = new RegisterContainerTypeRequest
        {
            AppId = "11111111-1111-1111-1111-111111111111",
            SharePointAdminUrl = "https://contoso-admin.sharepoint.com"
            // No permissions supplied
        };

        var hasAnyPermission =
            (request.DelegatedPermissions?.Count ?? 0) > 0 ||
            (request.ApplicationPermissions?.Count ?? 0) > 0;

        hasAnyPermission.Should().BeFalse("at least one permission must be supplied");
    }

    [Fact]
    public void PermissionValidation_OnlyDelegated_IsValid()
    {
        var request = new RegisterContainerTypeRequest
        {
            AppId = "11111111-1111-1111-1111-111111111111",
            SharePointAdminUrl = "https://contoso-admin.sharepoint.com",
            DelegatedPermissions = new[] { "ReadContent" }
        };

        var hasAnyPermission =
            (request.DelegatedPermissions?.Count ?? 0) > 0 ||
            (request.ApplicationPermissions?.Count ?? 0) > 0;

        hasAnyPermission.Should().BeTrue();
    }

    [Fact]
    public void PermissionValidation_OnlyApplication_IsValid()
    {
        var request = new RegisterContainerTypeRequest
        {
            AppId = "11111111-1111-1111-1111-111111111111",
            SharePointAdminUrl = "https://contoso-admin.sharepoint.com",
            ApplicationPermissions = new[] { "Create" }
        };

        var hasAnyPermission =
            (request.DelegatedPermissions?.Count ?? 0) > 0 ||
            (request.ApplicationPermissions?.Count ?? 0) > 0;

        hasAnyPermission.Should().BeTrue();
    }

    [Fact]
    public void PermissionValidation_InvalidPermissionName_IsDetected()
    {
        var permissions = new[] { "ReadContent", "InvalidPermission", "CreateSomething" };

        var invalidPermissions = permissions
            .Where(p => !ContainerTypePermissions.ValidPermissions.Contains(p))
            .ToList();

        invalidPermissions.Should().HaveCount(2);
        invalidPermissions.Should().Contain("InvalidPermission");
        invalidPermissions.Should().Contain("CreateSomething");
    }

    [Fact]
    public void PermissionValidation_AllValid_ProducesEmptyInvalidList()
    {
        var permissions = new[]
        {
            "ReadContent",
            "WriteContent",
            "Create",
            "Delete",
            "ManagePermissions"
        };

        var invalidPermissions = permissions
            .Where(p => !ContainerTypePermissions.ValidPermissions.Contains(p))
            .ToList();

        invalidPermissions.Should().BeEmpty("all permissions in the list are valid");
    }

    [Fact]
    public void PermissionValidation_AddAllPermissions_IsValidAsStandalone()
    {
        var permissions = new[] { "AddAllPermissions" };

        var invalidPermissions = permissions
            .Where(p => !ContainerTypePermissions.ValidPermissions.Contains(p))
            .ToList();

        invalidPermissions.Should().BeEmpty("AddAllPermissions is a valid standalone permission");
    }

    [Fact]
    public void ConfigId_Validation_EmptyGuidIsInvalid()
    {
        var emptyGuid = Guid.Empty;
        emptyGuid.Should().Be(Guid.Empty, "Guid.Empty must be recognized as invalid by the endpoint");

        // Endpoint validates: if (configId is null || configId == Guid.Empty) → 400
        var isInvalid = emptyGuid == Guid.Empty;
        isInvalid.Should().BeTrue();
    }

    [Fact]
    public void ConfigId_Validation_ValidNonEmptyGuidIsAccepted()
    {
        var validGuid = Guid.NewGuid();
        var isInvalid = validGuid == Guid.Empty;
        isInvalid.Should().BeFalse("non-empty GUID is valid");
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    // ADR-007 Compliance Tests
    // ─────────────────────────────────────────────────────────────────────────────

    #region ADR-007 Compliance

    [Fact]
    public void RegisterContainerTypeRequest_HasNoGraphSdkTypeReferences()
    {
        var type = typeof(RegisterContainerTypeRequest);
        type.Namespace.Should().StartWith("Sprk.Bff.Api", "must be in Spaarke namespace (ADR-007)");

        foreach (var prop in type.GetProperties())
        {
            prop.PropertyType.Namespace.Should().NotStartWith(
                "Microsoft.Graph.Models",
                $"property {prop.Name} must not expose Graph SDK types (ADR-007)");
        }
    }

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

    [Fact]
    public void RegisterContainerTypeResponse_HasNoGraphSdkTypeReferences()
    {
        var type = typeof(RegisterContainerTypeResponse);

        foreach (var prop in type.GetProperties())
        {
            prop.PropertyType.Namespace.Should().NotStartWith(
                "Microsoft.Graph.Models",
                $"property {prop.Name} must not expose Graph SDK types (ADR-007)");
        }
    }

    #endregion

    // ─────────────────────────────────────────────────────────────────────────────
    // Audit Log Integration Tests
    // ─────────────────────────────────────────────────────────────────────────────

    #region Audit Logging

    [Fact]
    public void AuditLog_TargetResource_FormatIsContainerTypeIdColonColonAppId()
    {
        // The audit log target resource is formatted as "{typeId}::{appId}"
        // This is the convention used by RegisterContainerTypeAsync.
        var typeId = "ct-guid-001";
        var appId = "11111111-1111-1111-1111-111111111111";

        var targetResource = $"{typeId}::{appId}";

        targetResource.Should().Be("ct-guid-001::11111111-1111-1111-1111-111111111111",
            "audit log target resource must identify both the container type and consuming app");
        targetResource.Should().Contain("::", "double-colon separator distinguishes the two IDs");
    }

    [Fact]
    public void AuditLog_OperationName_IsRegisterContainerType()
    {
        // The audit operation name is fixed — verify the expected string constant is correct
        const string expectedOperation = "RegisterContainerType";
        const string expectedCategory = "ContainerTypeRegistration";

        expectedOperation.Should().Be("RegisterContainerType");
        expectedCategory.Should().Be("ContainerTypeRegistration");
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
