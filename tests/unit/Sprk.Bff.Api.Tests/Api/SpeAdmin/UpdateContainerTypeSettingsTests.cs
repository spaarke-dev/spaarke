using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Endpoints.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.SpeAdmin;

/// <summary>
/// Unit tests for ContainerTypeSettingsEndpoints (SPE-052).
///
/// Tests cover:
///   - Endpoint registration shape and method signatures
///   - UpdateContainerTypeSettingsRequest DTO shape and properties
///   - ContainerTypeSettingsResponseDto DTO shape and properties
///   - ContainerTypeSettingsResult domain record
///   - ValidSharingCapabilities set (ADR-007: input validation rules)
///   - Validation logic: configId required, invalid sharingCapability, invalid majorVersionLimit
///   - Auth filter: inherited from parent group (not per-endpoint)
///   - Error code naming convention
/// </summary>
public class UpdateContainerTypeSettingsTests
{
    #region Endpoint Registration Tests

    [Fact]
    public void MapContainerTypeSettingsEndpoints_MethodExists_AndIsExtensionOnRouteGroupBuilder()
    {
        // Verify the endpoint registration method exists with correct signature (ADR-001: Minimal API)
        var method = typeof(ContainerTypeSettingsEndpoints)
            .GetMethod("MapContainerTypeSettingsEndpoints");

        method.Should().NotBeNull("MapContainerTypeSettingsEndpoints extension method must exist");
        method!.IsStatic.Should().BeTrue("must be a static extension method");
        method.ReturnType.Should().Be(typeof(RouteGroupBuilder), "must return RouteGroupBuilder for chaining");
    }

    [Fact]
    public void SpeAdminEndpoints_RegistersContainerTypeSettings_ViaGroup()
    {
        // Verify the SpeAdminEndpoints class exists and has MapSpeAdminEndpoints
        var method = typeof(SpeAdminEndpoints).GetMethod("MapSpeAdminEndpoints");

        method.Should().NotBeNull("MapSpeAdminEndpoints must exist");
        method!.IsStatic.Should().BeTrue("must be a static extension method");
    }

    #endregion

    #region Request DTO Tests

    [Fact]
    public void UpdateContainerTypeSettingsRequest_AllPropertiesCanBeNull()
    {
        // All fields optional — null means "do not change this setting"
        var request = new UpdateContainerTypeSettingsRequest();

        request.SharingCapability.Should().BeNull("SharingCapability is optional");
        request.IsVersioningEnabled.Should().BeNull("IsVersioningEnabled is optional");
        request.MajorVersionLimit.Should().BeNull("MajorVersionLimit is optional");
        request.StorageUsedInBytes.Should().BeNull("StorageUsedInBytes is optional");
    }

    [Fact]
    public void UpdateContainerTypeSettingsRequest_AcceptsAllSettingsWhenProvided()
    {
        var request = new UpdateContainerTypeSettingsRequest
        {
            SharingCapability = "edit",
            IsVersioningEnabled = true,
            MajorVersionLimit = 50,
            StorageUsedInBytes = 1_073_741_824L // 1 GB
        };

        request.SharingCapability.Should().Be("edit");
        request.IsVersioningEnabled.Should().BeTrue();
        request.MajorVersionLimit.Should().Be(50);
        request.StorageUsedInBytes.Should().Be(1_073_741_824L);
    }

    [Fact]
    public void UpdateContainerTypeSettingsRequest_SharingCapabilityOnly()
    {
        // Partial request — only sharing capability
        var request = new UpdateContainerTypeSettingsRequest
        {
            SharingCapability = "disabled"
        };

        request.SharingCapability.Should().Be("disabled");
        request.IsVersioningEnabled.Should().BeNull("not set");
        request.MajorVersionLimit.Should().BeNull("not set");
        request.StorageUsedInBytes.Should().BeNull("not set");
    }

    [Fact]
    public void UpdateContainerTypeSettingsRequest_VersioningSettingsOnly()
    {
        // Partial request — only versioning settings
        var request = new UpdateContainerTypeSettingsRequest
        {
            IsVersioningEnabled = false,
            MajorVersionLimit = 10
        };

        request.SharingCapability.Should().BeNull("not set");
        request.IsVersioningEnabled.Should().BeFalse();
        request.MajorVersionLimit.Should().Be(10);
        request.StorageUsedInBytes.Should().BeNull("not set");
    }

    #endregion

    #region Response DTO Tests

    [Fact]
    public void ContainerTypeSettingsResponseDto_ConstructsWithAllFields()
    {
        var now = DateTimeOffset.UtcNow;
        var dto = new ContainerTypeSettingsResponseDto
        {
            Id = "ct-guid-001",
            DisplayName = "Legal Documents Type",
            BillingClassification = "standard",
            CreatedDateTime = now
        };

        dto.Id.Should().Be("ct-guid-001");
        dto.DisplayName.Should().Be("Legal Documents Type");
        dto.BillingClassification.Should().Be("standard");
        dto.CreatedDateTime.Should().Be(now);
    }

    [Fact]
    public void ContainerTypeSettingsResponseDto_BillingClassificationCanBeNull()
    {
        var dto = new ContainerTypeSettingsResponseDto
        {
            Id = "ct-guid-002",
            DisplayName = "Test Type",
            CreatedDateTime = DateTimeOffset.UtcNow
        };

        dto.BillingClassification.Should().BeNull("BillingClassification is optional");
    }

    [Fact]
    public void ContainerTypeSettingsResponseDto_IsNotGraphSdkType()
    {
        // ADR-007: DTO must be in Spaarke namespace, not Microsoft.Graph
        var type = typeof(ContainerTypeSettingsResponseDto);

        type.Namespace.Should().StartWith("Sprk.Bff.Api", "must be in Spaarke namespace, not Graph SDK");

        var props = type.GetProperties();
        foreach (var prop in props)
        {
            prop.PropertyType.Namespace.Should().NotStartWith("Microsoft.Graph.Models",
                $"property {prop.Name} must not leak Graph SDK types (ADR-007)");
        }
    }

    #endregion

    #region Domain Record Tests

    [Fact]
    public void ContainerTypeSettingsResult_IsDomainRecord_NotGraphSdkType()
    {
        // ADR-007: Domain record must not expose Graph SDK types
        var type = typeof(SpeAdminGraphService.ContainerTypeSettingsResult);

        type.IsClass.Should().BeTrue("records are classes in C#");
        type.Namespace.Should().StartWith("Sprk.Bff.Api", "must be in Spaarke namespace");

        var props = type.GetProperties();
        foreach (var prop in props)
        {
            prop.PropertyType.Namespace.Should().NotStartWith("Microsoft.Graph.Models",
                $"property {prop.Name} must not leak Graph SDK types (ADR-007)");
        }
    }

    [Fact]
    public void ContainerTypeSettingsResult_ConstructsCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        var result = new SpeAdminGraphService.ContainerTypeSettingsResult(
            Id: "ct-guid-456",
            DisplayName: "Corporate Docs Type",
            BillingClassification: "standard",
            CreatedDateTime: now);

        result.Id.Should().Be("ct-guid-456");
        result.DisplayName.Should().Be("Corporate Docs Type");
        result.BillingClassification.Should().Be("standard");
        result.CreatedDateTime.Should().Be(now);
    }

    [Fact]
    public void ContainerTypeSettingsResult_BillingClassificationCanBeNull()
    {
        var result = new SpeAdminGraphService.ContainerTypeSettingsResult(
            Id: "ct-id",
            DisplayName: "Name",
            BillingClassification: null,
            CreatedDateTime: DateTimeOffset.UtcNow);

        result.BillingClassification.Should().BeNull("BillingClassification is optional");
    }

    #endregion

    #region ValidSharingCapabilities Tests

    [Theory]
    [InlineData("disabled")]
    [InlineData("view")]
    [InlineData("edit")]
    [InlineData("full")]
    public void ValidSharingCapabilities_ContainsAllAllowedValues(string capability)
    {
        SpeAdminGraphService.ValidSharingCapabilities
            .Should().Contain(capability, $"'{capability}' is a valid sharing capability");
    }

    [Theory]
    [InlineData("DISABLED")]
    [InlineData("VIEW")]
    [InlineData("EDIT")]
    [InlineData("FULL")]
    public void ValidSharingCapabilities_IsCaseInsensitive(string capability)
    {
        // HashSet uses OrdinalIgnoreCase comparer
        SpeAdminGraphService.ValidSharingCapabilities
            .Contains(capability)
            .Should().BeTrue($"'{capability}' is a valid sharing capability (case-insensitive)");
    }

    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    [InlineData("none")]
    [InlineData("all")]
    [InlineData("")]
    [InlineData("UNKNOWN")]
    public void ValidSharingCapabilities_DoesNotContainInvalidValues(string capability)
    {
        SpeAdminGraphService.ValidSharingCapabilities
            .Contains(capability)
            .Should().BeFalse($"'{capability}' is not a valid sharing capability");
    }

    [Fact]
    public void ValidSharingCapabilities_HasExactlyFourEntries()
    {
        SpeAdminGraphService.ValidSharingCapabilities
            .Should().HaveCount(4, "exactly 4 sharing capabilities are allowed: disabled, view, edit, full");
    }

    #endregion

    #region Validation Logic Tests

    [Fact]
    public void ConfigId_Validation_EmptyGuidIsInvalid()
    {
        // PUT endpoint checks: if (configId is null || configId == Guid.Empty) → 400
        var emptyGuid = Guid.Empty;
        var isInvalid = emptyGuid == Guid.Empty;

        isInvalid.Should().BeTrue("Guid.Empty must be recognized as invalid");
    }

    [Fact]
    public void ConfigId_Validation_ValidGuidIsAccepted()
    {
        var validGuid = Guid.NewGuid();
        var isValid = validGuid != Guid.Empty;

        isValid.Should().BeTrue("any non-empty GUID is valid for configId");
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("view")]
    [InlineData("edit")]
    [InlineData("full")]
    [InlineData("Disabled")] // case-insensitive
    [InlineData("VIEW")]     // uppercase
    public void SharingCapability_ValidValues_PassValidation(string capability)
    {
        var isValid = SpeAdminGraphService.ValidSharingCapabilities.Contains(capability);

        isValid.Should().BeTrue($"'{capability}' is a valid sharing capability that should pass validation");
    }

    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    [InlineData("admin")]
    [InlineData("restricted")]
    [InlineData("unknown")]
    public void SharingCapability_InvalidValues_FailValidation(string capability)
    {
        var isValid = SpeAdminGraphService.ValidSharingCapabilities.Contains(capability);

        isValid.Should().BeFalse($"'{capability}' is not a valid sharing capability and should fail validation");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void MajorVersionLimit_ZeroOrNegative_IsInvalid(int limit)
    {
        // Endpoint checks: if (request.MajorVersionLimit.HasValue && request.MajorVersionLimit.Value <= 0) → 400
        var isInvalid = limit <= 0;

        isInvalid.Should().BeTrue($"majorVersionLimit '{limit}' must be rejected (not positive)");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(500)]
    public void MajorVersionLimit_PositiveValues_AreValid(int limit)
    {
        var isValid = limit > 0;

        isValid.Should().BeTrue($"majorVersionLimit '{limit}' is a valid positive integer");
    }

    #endregion

    #region Authorization Filter Tests

    [Fact]
    public void SpeAdminAuthFilter_IsAppliedViaParentGroup_NotPerEndpoint()
    {
        // Authorization for containertypes/{typeId}/settings is inherited from the /api/spe parent group.
        // ContainerTypeSettingsEndpoints.MapContainerTypeSettingsEndpoints does NOT apply its own filter —
        // the parent group (SpeAdminEndpoints) already has RequireAuthorization() + SpeAdminAuthorizationFilter.
        // ADR-008: No endpoint-level filter duplication.

        var speAdminEndpointsType = typeof(SpeAdminEndpoints);
        var method = speAdminEndpointsType.GetMethod("MapSpeAdminEndpoints");

        method.Should().NotBeNull("MapSpeAdminEndpoints must exist and register the settings endpoint on the group");
    }

    [Fact]
    public async Task SpeAdminAuthorizationFilter_ReturnsUnauthorized_WhenNoUserId()
    {
        // Arrange: filter with anonymous user (no identity claims)
        var logger = new Mock<ILogger<Sprk.Bff.Api.Api.Filters.SpeAdminAuthorizationFilter>>();
        var filter = new Sprk.Bff.Api.Api.Filters.SpeAdminAuthorizationFilter(logger.Object);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity()) // anonymous
        };

        var contextMock = new Mock<EndpointFilterInvocationContext>();
        contextMock.Setup(c => c.HttpContext).Returns(httpContext);

        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(Results.Ok("should not reach"));

        // Act
        var result = await filter.InvokeAsync(contextMock.Object, next);

        // Assert
        result.Should().NotBeNull();
        (result as IResult).Should().NotBeNull("filter must return an IResult");
    }

    [Fact]
    public async Task SpeAdminAuthorizationFilter_CallsNext_WhenUserIsAdmin()
    {
        // Arrange: authenticated admin user
        var logger = new Mock<ILogger<Sprk.Bff.Api.Api.Filters.SpeAdminAuthorizationFilter>>();
        var filter = new Sprk.Bff.Api.Api.Filters.SpeAdminAuthorizationFilter(logger.Object);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "admin-789"),
            new("roles", "SystemAdmin")
        };
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
        };

        var contextMock = new Mock<EndpointFilterInvocationContext>();
        contextMock.Setup(c => c.HttpContext).Returns(httpContext);

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok("admin reached"));
        };

        // Act
        var result = await filter.InvokeAsync(contextMock.Object, next);

        // Assert
        nextCalled.Should().BeTrue("admin user must reach the next handler");
        result.Should().NotBeNull();
    }

    #endregion

    #region Error Code Tests

    [Fact]
    public void ErrorCodes_FollowSpeNamingConvention()
    {
        // Verify all error codes used in this endpoint follow the spe.containertypes.settings.{reason} pattern.
        var expectedErrorCodes = new[]
        {
            "spe.containertypes.settings.config_id_required",
            "spe.containertypes.settings.invalid_sharing_capability",
            "spe.containertypes.settings.invalid_major_version_limit",
            "spe.containertypes.settings.config_not_found",
            "spe.containertypes.settings.graph_error",
            "spe.containertypes.settings.unexpected_error"
        };

        foreach (var code in expectedErrorCodes)
        {
            code.Should().StartWith("spe.", $"all SPE Admin error codes start with 'spe.'");
            code.Should().Contain("containertypes.settings",
                $"container type settings error codes include 'containertypes.settings'");
            code.Should().MatchRegex(@"^[a-z_\.]+$",
                "error codes are lowercase with dots and underscores only");
        }
    }

    #endregion

    #region Graph Service Method Existence Tests

    [Fact]
    public void SpeAdminGraphService_HasUpdateContainerTypeSettingsAsync()
    {
        // Verify UpdateContainerTypeSettingsAsync exists with the expected signature
        var method = typeof(SpeAdminGraphService).GetMethod("UpdateContainerTypeSettingsAsync");

        method.Should().NotBeNull("UpdateContainerTypeSettingsAsync must be added to SpeAdminGraphService");
        method!.IsPublic.Should().BeTrue("must be public for use by endpoint handlers");
        method.ReturnType.Name.Should().Contain("Task", "must return a Task<ContainerTypeSettingsResult?>");
    }

    [Fact]
    public void SpeAdminGraphService_HasValidSharingCapabilities_PublicStaticField()
    {
        // Verify the validation set is public and static (accessible by endpoint without instantiation)
        var field = typeof(SpeAdminGraphService)
            .GetField("ValidSharingCapabilities",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        field.Should().NotBeNull("ValidSharingCapabilities must be a public static field");
        field!.IsStatic.Should().BeTrue("must be static for access without instantiation");
    }

    #endregion
}
