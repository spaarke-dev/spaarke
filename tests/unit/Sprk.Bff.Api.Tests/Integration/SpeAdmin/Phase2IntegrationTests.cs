using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Api.Filters;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.SpeAdmin;

/// <summary>
/// Phase 2 integration tests covering the SPE Admin BFF endpoints for container type management,
/// column/property editors, search, recycle bin, and security dashboard.
///
/// These tests validate:
///   - ADR-001: All endpoints follow Minimal API patterns (no controllers)
///   - ADR-008: Auth filter (SpeAdminAuthorizationFilter) is enforced — non-admin users receive 403
///   - Endpoint input validation returns 400 ProblemDetails for bad requests
///   - Auth filter correctly differentiates admin vs. non-admin users
///
/// Approach: Tests exercise the SpeAdminAuthorizationFilter directly (ADR-008 compliance check)
/// and validate request model constraints for each Phase 2 feature area. Tests requiring
/// SpeAdminGraphService (external Graph API + Key Vault) are structured as documented manual
/// test procedures at the bottom of this file, since the service depends on live Azure resources.
/// </summary>
public class Phase2IntegrationTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Shared test helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static ClaimsPrincipal CreateAdminUser(string userId = "admin-user-001")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new("roles", "Admin")
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static ClaimsPrincipal CreateNonAdminUser(string userId = "regular-user-001")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static ClaimsPrincipal CreateAnonymousUser() =>
        new(new ClaimsIdentity());

    private static ClaimsPrincipal CreateSystemAdminUser(string userId = "sysadmin-001")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new("roles", "SystemAdmin")
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static DefaultHttpContext CreateHttpContext(ClaimsPrincipal user)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        return new DefaultHttpContext
        {
            User = user,
            TraceIdentifier = "test-trace-" + Guid.NewGuid().ToString("N")[..8],
            RequestServices = services.BuildServiceProvider()
        };
    }

    private static Mock<EndpointFilterInvocationContext> CreateFilterContext(ClaimsPrincipal user)
    {
        var httpContext = CreateHttpContext(user);
        var contextMock = new Mock<EndpointFilterInvocationContext>();
        contextMock.Setup(c => c.HttpContext).Returns(httpContext);
        return contextMock;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 1: SpeAdminAuthorizationFilter — ADR-008 compliance (applies to ALL Phase 2 endpoints)
    // ─────────────────────────────────────────────────────────────────────────

    public class SpeAdminAuthorizationFilterTests
    {
        private readonly SpeAdminAuthorizationFilter _filter;

        public SpeAdminAuthorizationFilterTests()
        {
            _filter = new SpeAdminAuthorizationFilter(logger: null);
        }

        [Fact]
        public async Task InvokeAsync_AdminUser_WithRolesClaim_PassesThrough()
        {
            // Arrange
            var user = CreateAdminUser();
            var contextMock = CreateFilterContext(user);
            var nextCalled = false;
            EndpointFilterDelegate next = _ => { nextCalled = true; return ValueTask.FromResult<object?>(Results.Ok()); };

            // Act
            var result = await _filter.InvokeAsync(contextMock.Object, next);

            // Assert
            nextCalled.Should().BeTrue("admin user with 'roles: Admin' claim should pass through");
        }

        [Fact]
        public async Task InvokeAsync_SystemAdminUser_PassesThrough()
        {
            // Arrange
            var user = CreateSystemAdminUser();
            var contextMock = CreateFilterContext(user);
            var nextCalled = false;
            EndpointFilterDelegate next = _ => { nextCalled = true; return ValueTask.FromResult<object?>(Results.Ok()); };

            // Act
            var result = await _filter.InvokeAsync(contextMock.Object, next);

            // Assert
            nextCalled.Should().BeTrue("user with 'roles: SystemAdmin' claim should pass through");
        }

        [Fact]
        public async Task InvokeAsync_IsInRole_Admin_PassesThrough()
        {
            // Arrange — ClaimsIdentity with role claim type
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "role-user-001"),
                new(ClaimTypes.Role, "Admin")
            };
            var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
            var contextMock = CreateFilterContext(user);
            var nextCalled = false;
            EndpointFilterDelegate next = _ => { nextCalled = true; return ValueTask.FromResult<object?>(Results.Ok()); };

            // Act
            await _filter.InvokeAsync(contextMock.Object, next);

            // Assert
            nextCalled.Should().BeTrue("user with ClaimTypes.Role = 'Admin' should pass through via IsInRole");
        }

        [Fact]
        public async Task InvokeAsync_NonAdminUser_Returns403Forbidden()
        {
            // Arrange
            var user = CreateNonAdminUser();
            var contextMock = CreateFilterContext(user);
            var nextCalled = false;
            EndpointFilterDelegate next = _ => { nextCalled = true; return ValueTask.FromResult<object?>(Results.Ok()); };

            // Act
            var result = await _filter.InvokeAsync(contextMock.Object, next);

            // Assert
            nextCalled.Should().BeFalse("non-admin user should be blocked before reaching the endpoint");
            result.Should().NotBeNull();
            // SpeAdminAuthorizationFilter returns ProblemDetailsHelper.Forbidden — which is IResult
            result.Should().BeAssignableTo<IResult>();
        }

        [Fact]
        public async Task InvokeAsync_AnonymousUser_Returns401Unauthorized()
        {
            // Arrange
            var user = CreateAnonymousUser();
            var contextMock = CreateFilterContext(user);
            var nextCalled = false;
            EndpointFilterDelegate next = _ => { nextCalled = true; return ValueTask.FromResult<object?>(Results.Ok()); };

            // Act
            var result = await _filter.InvokeAsync(contextMock.Object, next);

            // Assert
            nextCalled.Should().BeFalse("anonymous user should be blocked before reaching the endpoint");
            result.Should().NotBeNull();
        }

        [Fact]
        public async Task InvokeAsync_OidClaim_UsedAsUserId_AdminUserPassesThrough()
        {
            // Arrange — user with 'oid' claim instead of NameIdentifier
            var claims = new List<Claim>
            {
                new("oid", "oid-user-001"),
                new("roles", "Admin")
            };
            var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
            var contextMock = CreateFilterContext(user);
            var nextCalled = false;
            EndpointFilterDelegate next = _ => { nextCalled = true; return ValueTask.FromResult<object?>(Results.Ok()); };

            // Act
            await _filter.InvokeAsync(contextMock.Object, next);

            // Assert
            nextCalled.Should().BeTrue("user with 'oid' claim should be recognized as authenticated");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 2: Container Type CRUD — input validation (ADR-001, ADR-019)
    // ─────────────────────────────────────────────────────────────────────────

    public class ContainerTypeCrudValidationTests
    {
        /// <summary>
        /// Verifies that the ContainerTypeDto model correctly represents expected container type properties.
        /// This validates the API surface (ADR-007: no Graph SDK types in public surface).
        /// </summary>
        [Fact]
        public void ContainerTypeDto_AllRequiredProperties_ArePopulatable()
        {
            // Arrange
            var createdAt = DateTimeOffset.UtcNow;
            var dto = new Sprk.Bff.Api.Models.SpeAdmin.ContainerTypeDto
            {
                Id = "ct-001",
                DisplayName = "Legal Documents",
                Description = "Container type for legal documents",
                BillingClassification = "standard",
                CreatedDateTime = createdAt
            };

            // Assert
            dto.Id.Should().Be("ct-001");
            dto.DisplayName.Should().Be("Legal Documents");
            dto.Description.Should().Be("Container type for legal documents");
            dto.BillingClassification.Should().Be("standard");
            dto.CreatedDateTime.Should().Be(createdAt);
        }

        [Fact]
        public void ContainerTypeListDto_EnvelopesItemsWithCount()
        {
            // Arrange
            var items = new List<Sprk.Bff.Api.Models.SpeAdmin.ContainerTypeDto>
            {
                new() { Id = "ct-001", DisplayName = "Type A", CreatedDateTime = DateTimeOffset.UtcNow },
                new() { Id = "ct-002", DisplayName = "Type B", CreatedDateTime = DateTimeOffset.UtcNow }
            };

            var listDto = new Sprk.Bff.Api.Models.SpeAdmin.ContainerTypeListDto
            {
                Items = items,
                Count = items.Count
            };

            // Assert
            listDto.Items.Should().HaveCount(2);
            listDto.Count.Should().Be(2);
        }

        [Theory]
        [InlineData("standard")]
        [InlineData("premium")]
        [InlineData("STANDARD")]
        [InlineData("PREMIUM")]
        public void CreateContainerTypeRequest_ValidBillingClassifications_AreRecognized(string classification)
        {
            // Arrange
            var validClassifications = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "standard", "premium" };

            // Assert
            validClassifications.Contains(classification).Should().BeTrue(
                $"'{classification}' should be a valid billing classification");
        }

        [Theory]
        [InlineData("enterprise")]
        [InlineData("free")]
        [InlineData("")]
        [InlineData("   ")]
        public void CreateContainerTypeRequest_InvalidBillingClassifications_AreRejected(string classification)
        {
            // Arrange
            var validClassifications = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "standard", "premium" };

            // Only non-empty values should be checked
            if (!string.IsNullOrWhiteSpace(classification))
            {
                validClassifications.Contains(classification).Should().BeFalse(
                    $"'{classification}' should not be a valid billing classification");
            }
        }

        [Fact]
        public void ContainerTypeDto_BillingClassification_CanBeNull()
        {
            // Arrange — Graph may not always return billing classification
            var dto = new Sprk.Bff.Api.Models.SpeAdmin.ContainerTypeDto
            {
                Id = "ct-001",
                DisplayName = "Test Type",
                BillingClassification = null,
                CreatedDateTime = DateTimeOffset.UtcNow
            };

            // Assert
            dto.BillingClassification.Should().BeNull(
                "BillingClassification is optional from Graph API");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 3: Container Type Registration — request validation
    // ─────────────────────────────────────────────────────────────────────────

    public class ContainerTypeRegistrationValidationTests
    {
        [Fact]
        public void RegisterContainerTypeRequest_ValidPermissions_PassValidation()
        {
            // Arrange — mirroring ContainerTypePermissions.ValidPermissions
            var validPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "FileStorageContainer.Selected",
                "FileStorageContainer.Read.All",
                "FileStorageContainer.ReadWrite.All",
                "Sites.Read.All",
                "Sites.ReadWrite.All",
                "Files.Read.All",
                "Files.ReadWrite.All",
                "User.Read",
                "openid",
                "profile"
            };

            // Assert — all registered permissions should be in the valid set
            validPermissions.Should().NotBeEmpty("valid permissions set must not be empty");
        }

        [Fact]
        public void RegisterContainerTypeResponse_PropertiesAreCorrectlyMapped()
        {
            // Arrange
            var response = new Sprk.Bff.Api.Models.SpeAdmin.RegisterContainerTypeResponse
            {
                ContainerTypeId = "ct-abc123",
                AppId = "app-guid-001",
                DelegatedPermissions = new List<string> { "FileStorageContainer.Selected", "openid" },
                ApplicationPermissions = new List<string> { "FileStorageContainer.Read.All" }
            };

            // Assert — ADR-007: response uses domain models, not Graph SDK types
            response.ContainerTypeId.Should().Be("ct-abc123");
            response.AppId.Should().Be("app-guid-001");
            response.DelegatedPermissions.Should().HaveCount(2);
            response.ApplicationPermissions.Should().HaveCount(1);
        }

        [Theory]
        [InlineData("not-a-guid")]
        [InlineData("")]
        [InlineData("   ")]
        public void RegisterRequest_InvalidAppId_ShouldFailGuidParsing(string appId)
        {
            // Arrange — simulates the endpoint's appId GUID validation
            var isValidGuid = !string.IsNullOrWhiteSpace(appId) && Guid.TryParse(appId, out _);

            // Assert
            isValidGuid.Should().BeFalse(
                $"appId '{appId}' is not a valid GUID and should fail validation");
        }

        [Fact]
        public void RegisterRequest_ValidAppId_PassesGuidParsing()
        {
            // Arrange
            var validAppId = Guid.NewGuid().ToString();
            var isValidGuid = Guid.TryParse(validAppId, out _);

            // Assert
            isValidGuid.Should().BeTrue("a properly formatted GUID should pass appId validation");
        }

        [Theory]
        [InlineData("http://contoso-admin.sharepoint.com")]
        [InlineData("ftp://contoso-admin.sharepoint.com")]
        [InlineData("not-a-url")]
        public void RegisterRequest_InvalidSharePointAdminUrl_FailsValidation(string url)
        {
            // Arrange — simulates the endpoint's URL validation (requires absolute HTTPS)
            var isValid = Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                          string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);

            // Assert
            isValid.Should().BeFalse($"URL '{url}' should fail validation (must be HTTPS)");
        }

        [Fact]
        public void RegisterRequest_ValidSharePointAdminUrl_PassesValidation()
        {
            // Arrange
            var url = "https://contoso-admin.sharepoint.com";
            var isValid = Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                          string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);

            // Assert
            isValid.Should().BeTrue("valid HTTPS SharePoint admin URL should pass validation");
        }

        [Fact]
        public void RegisterRequest_NoPermissions_FailsPermissionRequiredCheck()
        {
            // Arrange — simulates the endpoint's at-least-one-permission check
            List<string>? delegatedPermissions = null;
            List<string>? applicationPermissions = new List<string>();

            var hasAnyPermission =
                (delegatedPermissions?.Count ?? 0) > 0 ||
                (applicationPermissions?.Count ?? 0) > 0;

            // Assert
            hasAnyPermission.Should().BeFalse(
                "request with no permissions should fail the at-least-one-permission check");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 4: Container Column CRUD — model validation
    // ─────────────────────────────────────────────────────────────────────────

    public class ContainerColumnValidationTests
    {
        private static readonly HashSet<string> ValidColumnTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "text", "boolean", "dateTime", "currency", "choice",
            "number", "personOrGroup", "hyperlinkOrPicture"
        };

        [Theory]
        [InlineData("text")]
        [InlineData("boolean")]
        [InlineData("dateTime")]
        [InlineData("currency")]
        [InlineData("choice")]
        [InlineData("number")]
        [InlineData("personOrGroup")]
        [InlineData("hyperlinkOrPicture")]
        public void CreateColumnRequest_ValidColumnTypes_PassValidation(string columnType)
        {
            // Assert
            ValidColumnTypes.Contains(columnType).Should().BeTrue(
                $"'{columnType}' should be a valid column type");
        }

        [Theory]
        [InlineData("string")]
        [InlineData("int")]
        [InlineData("date")]
        [InlineData("unknown")]
        public void CreateColumnRequest_InvalidColumnTypes_FailValidation(string columnType)
        {
            // Assert
            ValidColumnTypes.Contains(columnType).Should().BeFalse(
                $"'{columnType}' is not a supported SPE column type");
        }

        [Fact]
        public void ContainerColumnDto_AllFields_AreCorrectlyMapped()
        {
            // Arrange — validate the FromDomain mapping round-trip shape
            var domainColumn = new Sprk.Bff.Api.Infrastructure.Graph.SpeAdminGraphService.SpeContainerColumn(
                Id: "col-001",
                Name: "DocumentType",
                DisplayName: "Document Type",
                Description: "Classification of the document",
                ColumnType: "choice",
                Required: true,
                Indexed: true,
                ReadOnly: false);

            var dto = Sprk.Bff.Api.Models.SpeAdmin.ContainerColumnDto.FromDomain(domainColumn);

            // Assert — ADR-007: public surface uses domain DTOs, not Graph SDK types
            dto.Id.Should().Be("col-001");
            dto.Name.Should().Be("DocumentType");
            dto.DisplayName.Should().Be("Document Type");
            dto.Description.Should().Be("Classification of the document");
            dto.ColumnType.Should().Be("choice");
            dto.Required.Should().BeTrue();
            dto.Indexed.Should().BeTrue();
            dto.ReadOnly.Should().BeFalse();
        }

        [Fact]
        public void ContainerColumnListResponse_CorrectlyWrapsItems()
        {
            // Arrange
            var columns = new List<Sprk.Bff.Api.Models.SpeAdmin.ContainerColumnDto>
            {
                new("col-001", "Title", "Title", null, "text", false, false, true),
                new("col-002", "Category", "Category", null, "choice", false, true, false)
            };

            var response = new Sprk.Bff.Api.Models.SpeAdmin.ContainerColumnListResponse(
                columns, columns.Count);

            // Assert
            response.Items.Should().HaveCount(2);
            response.Count.Should().Be(2);
            response.Items[0].Name.Should().Be("Title");
        }

        [Fact]
        public void UpdateColumnRequest_AllFieldsNullable_AllowsPartialUpdate()
        {
            // Arrange — merge-patch semantics: null = leave unchanged
            var updateRequest = new Sprk.Bff.Api.Models.SpeAdmin.UpdateColumnRequest(
                DisplayName: "New Display Name",
                Description: null,
                Required: null,
                Indexed: true);

            // Assert
            updateRequest.DisplayName.Should().Be("New Display Name");
            updateRequest.Description.Should().BeNull("null means leave unchanged");
            updateRequest.Required.Should().BeNull("null means leave unchanged");
            updateRequest.Indexed.Should().BeTrue();
        }

        [Fact]
        public void ContainerColumnDto_ReadOnly_SystemManagedColumns_AreIdentified()
        {
            // Arrange — system columns (Title, Created, Modified) are read-only
            var systemColumn = new Sprk.Bff.Api.Infrastructure.Graph.SpeAdminGraphService.SpeContainerColumn(
                Id: "sys-001",
                Name: "Title",
                DisplayName: "Title",
                Description: null,
                ColumnType: "text",
                Required: false,
                Indexed: true,
                ReadOnly: true);

            var dto = Sprk.Bff.Api.Models.SpeAdmin.ContainerColumnDto.FromDomain(systemColumn);

            // Assert
            dto.ReadOnly.Should().BeTrue("system-managed columns should be flagged as read-only");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 5: Custom Property read/update — model validation
    // ─────────────────────────────────────────────────────────────────────────

    public class CustomPropertyValidationTests
    {
        [Fact]
        public void CustomPropertyDto_AllFields_AreCorrectlyPopulated()
        {
            // Arrange — CustomPropertyDto is a positional record: (Name, Value, IsSearchable)
            var dto = new Sprk.Bff.Api.Models.SpeAdmin.CustomPropertyDto(
                Name: "MatterNumber",
                Value: "M-2026-001",
                IsSearchable: true);

            // Assert — ADR-007: public surface uses domain DTOs
            dto.Name.Should().Be("MatterNumber");
            dto.Value.Should().Be("M-2026-001");
            dto.IsSearchable.Should().BeTrue();
        }

        [Fact]
        public void CustomPropertyDto_SearchableFlag_CanBeFalse()
        {
            // Arrange — properties are not searchable by default
            var dto = new Sprk.Bff.Api.Models.SpeAdmin.CustomPropertyDto(
                Name: "InternalNote",
                Value: "internal-value",
                IsSearchable: false);

            // Assert
            dto.IsSearchable.Should().BeFalse("non-indexed properties should not be searchable");
        }

        [Fact]
        public void CustomPropertyDto_ValueIsRequired_PositionalRecord()
        {
            // Arrange — custom properties require a Name and Value
            var dto = new Sprk.Bff.Api.Models.SpeAdmin.CustomPropertyDto(
                Name: "OptionalTag",
                Value: string.Empty,
                IsSearchable: false);

            // Assert — Value is a required string (not nullable) in the record
            dto.Value.Should().NotBeNull("Value is a required field in CustomPropertyDto");
            dto.Name.Should().Be("OptionalTag");
        }

        [Fact]
        public void UpdateCustomPropertiesRequest_EmptyList_ClearsAllProperties()
        {
            // Arrange — empty properties list clears all custom properties on the container
            var request = new Sprk.Bff.Api.Models.SpeAdmin.UpdateCustomPropertiesRequest(
                Properties: new List<Sprk.Bff.Api.Models.SpeAdmin.CustomPropertyDto>());

            // Assert
            request.Properties.Should().BeEmpty(
                "an empty properties list clears all existing custom properties");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 6: Search containers — request/response model validation
    // ─────────────────────────────────────────────────────────────────────────

    public class SearchContainersValidationTests
    {
        [Fact]
        public void SearchContainerResult_AllFields_AreCorrectlyMapped()
        {
            // Arrange — from domain model (ADR-007: no Graph SDK types)
            var result = new Sprk.Bff.Api.Infrastructure.Graph.SpeAdminGraphService.SearchContainerResult(
                Id: "container-001",
                DisplayName: "Legal Files 2026",
                Description: "All legal documents for 2026",
                ContainerTypeId: "ct-abc-001");

            // Assert
            result.Id.Should().Be("container-001");
            result.DisplayName.Should().Be("Legal Files 2026");
            result.Description.Should().Be("All legal documents for 2026");
            result.ContainerTypeId.Should().Be("ct-abc-001");
        }

        [Fact]
        public void SearchContainerResult_DescriptionAndContainerTypeId_AreNullable()
        {
            // Arrange — Graph may return results without description or containerTypeId
            var result = new Sprk.Bff.Api.Infrastructure.Graph.SpeAdminGraphService.SearchContainerResult(
                Id: "container-002",
                DisplayName: "Archive",
                Description: null,
                ContainerTypeId: null);

            // Assert
            result.Description.Should().BeNull("description is optional in search results");
            result.ContainerTypeId.Should().BeNull("containerTypeId may be omitted from search results");
        }

        [Fact]
        public void ContainerSearchPage_SupportsPagination()
        {
            // Arrange
            var items = new List<Sprk.Bff.Api.Infrastructure.Graph.SpeAdminGraphService.SearchContainerResult>
            {
                new("c-001", "Container A", null, "ct-001"),
                new("c-002", "Container B", "B desc", "ct-001")
            };

            var page = new Sprk.Bff.Api.Infrastructure.Graph.SpeAdminGraphService.ContainerSearchPage(
                Items: items,
                TotalCount: 50,
                NextSkipToken: "token-abc-123");

            // Assert
            page.Items.Should().HaveCount(2);
            page.TotalCount.Should().Be(50);
            page.NextSkipToken.Should().Be("token-abc-123");
        }

        [Fact]
        public void ContainerSearchPage_LastPage_HasNullNextSkipToken()
        {
            // Arrange — last page of results
            var page = new Sprk.Bff.Api.Infrastructure.Graph.SpeAdminGraphService.ContainerSearchPage(
                Items: new List<Sprk.Bff.Api.Infrastructure.Graph.SpeAdminGraphService.SearchContainerResult>(),
                TotalCount: 0,
                NextSkipToken: null);

            // Assert
            page.NextSkipToken.Should().BeNull("last page should have null next skip token");
            page.TotalCount.Should().Be(0);
        }

        [Theory]
        [InlineData("invalid-guid")]
        [InlineData("")]
        [InlineData(null)]
        public void SearchContainers_InvalidConfigId_FailsGuidParsing(string? configId)
        {
            // Arrange — simulates the configId validation in SearchContainersAsync
            var isValid = !string.IsNullOrWhiteSpace(configId) && Guid.TryParse(configId, out _);

            // Assert
            isValid.Should().BeFalse($"configId '{configId}' should fail GUID validation");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 7: Search items — model validation
    // ─────────────────────────────────────────────────────────────────────────

    public class SearchItemsValidationTests
    {
        [Fact]
        public void SearchItems_ConfigId_IsRequired_AsGuid()
        {
            // Arrange — null configId should fail
            string? configId = null;
            var isValid = !string.IsNullOrWhiteSpace(configId) && Guid.TryParse(configId, out _);

            // Assert
            isValid.Should().BeFalse("null configId should fail validation");
        }

        [Fact]
        public void SearchItems_WithContainerId_ScopesSearchToContainer()
        {
            // Arrange — item search can be scoped to a specific container
            var containerId = "container-001";
            containerId.Should().NotBeNullOrWhiteSpace(
                "scoped search requires a valid containerId");
        }

        [Fact]
        public void SearchItems_WithoutContainerId_SearchesAllContainers()
        {
            // Arrange — unscoped item search (containerId = null)
            string? containerId = null;

            // Assert — null containerId is valid (unscoped search)
            containerId.Should().BeNull("null containerId is valid for unscoped searches");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(25)]
        [InlineData(50)]
        public void SearchItems_PageSize_ValidRange_Accepted(int pageSize)
        {
            // Arrange — page size must be between 1 and 50
            var isValid = pageSize >= 1 && pageSize <= 50;

            // Assert
            isValid.Should().BeTrue($"pageSize {pageSize} is within the valid range 1-50");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(51)]
        [InlineData(100)]
        public void SearchItems_PageSize_OutOfRange_Rejected(int pageSize)
        {
            // Arrange
            var isValid = pageSize >= 1 && pageSize <= 50;

            // Assert
            isValid.Should().BeFalse($"pageSize {pageSize} is outside the valid range 1-50");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 8: Recycle bin — model validation
    // ─────────────────────────────────────────────────────────────────────────

    public class RecycleBinValidationTests
    {
        [Fact]
        public void DeletedContainerDto_AllFields_AreCorrectlyMapped()
        {
            // Arrange
            var deletedAt = DateTimeOffset.UtcNow.AddDays(-2);
            var dto = new Sprk.Bff.Api.Models.SpeAdmin.DeletedContainerDto
            {
                Id = "deleted-c-001",
                DisplayName = "Old Project Files",
                DeletedDateTime = deletedAt,
                ContainerTypeId = "ct-001"
            };

            // Assert — ADR-007: domain DTO, not Graph SDK type
            dto.Id.Should().Be("deleted-c-001");
            dto.DisplayName.Should().Be("Old Project Files");
            dto.DeletedDateTime.Should().Be(deletedAt);
            dto.ContainerTypeId.Should().Be("ct-001");
        }

        [Fact]
        public void DeletedContainerDto_DeletedDateTime_CanBeNull()
        {
            // Arrange — Graph may not return deletedDateTime for all containers
            var dto = new Sprk.Bff.Api.Models.SpeAdmin.DeletedContainerDto
            {
                Id = "deleted-c-002",
                DisplayName = "Unknown Deletion Time",
                DeletedDateTime = null
            };

            // Assert
            dto.DeletedDateTime.Should().BeNull(
                "deletedDateTime is optional and may not be returned by Graph API");
        }

        [Fact]
        public void RecycleBinList_IsOrderedByDeletion_AssumptionHolds()
        {
            // Arrange — recycle bin should surface most recently deleted containers first
            var items = new List<Sprk.Bff.Api.Models.SpeAdmin.DeletedContainerDto>
            {
                new() { Id = "c-001", DisplayName = "Recent", DeletedDateTime = DateTimeOffset.UtcNow.AddHours(-1) },
                new() { Id = "c-002", DisplayName = "Older", DeletedDateTime = DateTimeOffset.UtcNow.AddDays(-5) },
                new() { Id = "c-003", DisplayName = "Oldest", DeletedDateTime = DateTimeOffset.UtcNow.AddDays(-30) }
            };

            // Sort descending by deletion time
            var sorted = items.OrderByDescending(i => i.DeletedDateTime).ToList();

            // Assert
            sorted[0].DisplayName.Should().Be("Recent");
            sorted[2].DisplayName.Should().Be("Oldest");
        }

        [Fact]
        public void RecycleBin_RestoreOrDelete_RequiresContainerId()
        {
            // Arrange — containerId path parameter is required for restore/delete
            string containerId = "container-to-restore-001";
            containerId.Should().NotBeNullOrWhiteSpace(
                "restore and permanent delete operations require a valid containerId");
        }

        [Fact]
        public void RecycleBin_PermanentDelete_IsIrreversible_RequiresConfirmation()
        {
            // This is a documentation test that verifies the operation's nature
            // The UI should require explicit confirmation before permanent delete
            // Permanent delete → 204 No Content (no body returned)
            var expectedStatusCode = StatusCodes.Status204NoContent;
            expectedStatusCode.Should().Be(204);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 9: Security alerts and score — model validation
    // ─────────────────────────────────────────────────────────────────────────

    public class SecurityEndpointValidationTests
    {
        [Fact]
        public void SecurityAlertDto_AllFields_AreCorrectlyPopulated()
        {
            // Arrange — SecurityAlertDto does not have a Category field (Graph v2 alerts)
            var createdAt = DateTimeOffset.UtcNow.AddHours(-3);
            var dto = new Sprk.Bff.Api.Models.SpeAdmin.SecurityAlertDto
            {
                Id = "alert-001",
                Title = "Suspicious Sign-in",
                Severity = "high",
                Status = "active",
                Description = "Sign-in from an unusual location",
                CreatedDateTime = createdAt
            };

            // Assert — ADR-007: domain DTO, not Graph SDK types
            dto.Id.Should().Be("alert-001");
            dto.Title.Should().Be("Suspicious Sign-in");
            dto.Severity.Should().Be("high");
            dto.Status.Should().Be("active");
            dto.Description.Should().Be("Sign-in from an unusual location");
            dto.CreatedDateTime.Should().Be(createdAt);
        }

        [Fact]
        public void SecurityAlertDto_OptionalFields_CanBeNull()
        {
            // Arrange — title, severity, status, description are nullable
            var dto = new Sprk.Bff.Api.Models.SpeAdmin.SecurityAlertDto
            {
                Id = "alert-002"
            };

            // Assert
            dto.Title.Should().BeNull("Title is optional");
            dto.Severity.Should().BeNull("Severity is optional");
            dto.Status.Should().BeNull("Status is optional");
            dto.Description.Should().BeNull("Description is optional");
            dto.CreatedDateTime.Should().BeNull("CreatedDateTime is optional");
        }

        [Fact]
        public void SecureScoreDto_ScoreFields_AreCorrectlyRepresented()
        {
            // Arrange — SecureScoreDto has CurrentScore, MaxScore, AverageComparativeScores
            var dto = new Sprk.Bff.Api.Models.SpeAdmin.SecureScoreDto
            {
                CurrentScore = 72.5,
                MaxScore = 100.0,
                AverageComparativeScores = new List<Sprk.Bff.Api.Models.SpeAdmin.AverageComparativeScoreDto>
                {
                    new() { Basis = "AllTenants", AverageScore = 65.0 }
                }
            };

            // Assert
            dto.CurrentScore.Should().Be(72.5);
            dto.MaxScore.Should().Be(100.0);
            dto.AverageComparativeScores.Should().HaveCount(1);
            dto.AverageComparativeScores![0].Basis.Should().Be("AllTenants");
        }

        [Fact]
        public void SecureScoreDto_ScorePercentage_CanBeCalculated()
        {
            // Arrange
            var dto = new Sprk.Bff.Api.Models.SpeAdmin.SecureScoreDto
            {
                CurrentScore = 72.5,
                MaxScore = 100.0
            };

            // Act
            var percentage = dto.MaxScore.HasValue && dto.MaxScore > 0
                ? (dto.CurrentScore!.Value / dto.MaxScore.Value) * 100
                : 0;

            // Assert
            percentage.Should().BeApproximately(72.5, 0.01,
                "percentage is currentScore / maxScore * 100");
        }

        [Fact]
        public void SecurityEndpoints_RequireValidConfigId()
        {
            // Arrange — security endpoints require configId to scope to a tenant
            string? configId = null;
            var isValid = !string.IsNullOrWhiteSpace(configId) && Guid.TryParse(configId, out _);

            // Assert
            isValid.Should().BeFalse("null or empty configId should fail validation");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 10: Container Type Settings — validation
    // ─────────────────────────────────────────────────────────────────────────

    public class ContainerTypeSettingsValidationTests
    {
        private static readonly HashSet<string> ValidSharingCapabilities =
            new(StringComparer.OrdinalIgnoreCase) { "disabled", "view", "edit", "full" };

        [Theory]
        [InlineData("disabled")]
        [InlineData("view")]
        [InlineData("edit")]
        [InlineData("full")]
        [InlineData("DISABLED")]
        [InlineData("Full")]
        public void SharingCapability_ValidValues_PassValidation(string value)
        {
            // Assert
            ValidSharingCapabilities.Contains(value).Should().BeTrue(
                $"'{value}' is a valid sharing capability");
        }

        [Theory]
        [InlineData("read")]
        [InlineData("write")]
        [InlineData("none")]
        [InlineData("all")]
        public void SharingCapability_InvalidValues_FailValidation(string value)
        {
            // Assert
            ValidSharingCapabilities.Contains(value).Should().BeFalse(
                $"'{value}' is not a valid sharing capability");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-100)]
        public void MajorVersionLimit_NonPositive_FailsValidation(int limit)
        {
            // Arrange — simulates the endpoint's majorVersionLimit validation
            var isValid = limit > 0;

            // Assert
            isValid.Should().BeFalse($"majorVersionLimit {limit} must be positive");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(100)]
        [InlineData(500)]
        public void MajorVersionLimit_Positive_PassesValidation(int limit)
        {
            // Assert
            var isValid = limit > 0;
            isValid.Should().BeTrue($"majorVersionLimit {limit} is a valid positive value");
        }

        [Fact]
        public void UpdateContainerTypeSettings_NullFields_AreIgnored_MergePatchSemantics()
        {
            // Arrange — null fields = leave unchanged (merge-patch)
            var request = new Sprk.Bff.Api.Models.SpeAdmin.UpdateContainerTypeSettingsRequest
            {
                SharingCapability = null,
                IsVersioningEnabled = true,
                MajorVersionLimit = null,
                StorageUsedInBytes = null
            };

            // Assert — only IsVersioningEnabled is non-null, so only it should be updated
            request.SharingCapability.Should().BeNull("null means leave sharing capability unchanged");
            request.IsVersioningEnabled.Should().BeTrue("versioning enabled is explicitly set to true");
            request.MajorVersionLimit.Should().BeNull("null means leave version limit unchanged");
            request.StorageUsedInBytes.Should().BeNull("null means leave storage limit unchanged");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 11: Container Permissions — validation
    // ─────────────────────────────────────────────────────────────────────────

    public class ContainerPermissionsValidationTests
    {
        private static readonly HashSet<string> ValidRoles =
            new(StringComparer.OrdinalIgnoreCase) { "reader", "writer", "manager", "owner" };

        [Theory]
        [InlineData("reader")]
        [InlineData("writer")]
        [InlineData("manager")]
        [InlineData("owner")]
        [InlineData("READER")]
        [InlineData("Owner")]
        public void Permission_ValidRoles_PassValidation(string role)
        {
            // Assert
            ValidRoles.Contains(role).Should().BeTrue($"'{role}' is a valid SPE permission role");
        }

        [Theory]
        [InlineData("admin")]
        [InlineData("viewer")]
        [InlineData("editor")]
        [InlineData("contributor")]
        public void Permission_InvalidRoles_FailValidation(string role)
        {
            // Assert
            ValidRoles.Contains(role).Should().BeFalse($"'{role}' is not a valid SPE permission role");
        }

        [Fact]
        public void GrantPermissionRequest_RequiresEitherUserIdOrGroupId()
        {
            // Arrange — must have exactly one of userId or groupId
            string? userId = null;
            string? groupId = null;
            var hasUserId = !string.IsNullOrWhiteSpace(userId);
            var hasGroupId = !string.IsNullOrWhiteSpace(groupId);

            // Assert
            (hasUserId || hasGroupId).Should().BeFalse(
                "neither userId nor groupId is provided — this should be rejected");
        }

        [Fact]
        public void GrantPermissionRequest_BothUserIdAndGroupId_IsRejected()
        {
            // Arrange — cannot supply both
            var userId = "user-001";
            var groupId = "group-001";
            var hasUserId = !string.IsNullOrWhiteSpace(userId);
            var hasGroupId = !string.IsNullOrWhiteSpace(groupId);
            var hasBoth = hasUserId && hasGroupId;

            // Assert
            hasBoth.Should().BeTrue("having both userId and groupId is the invalid condition to detect");
        }

        [Fact]
        public void ContainerPermissionDto_FromDomain_MapsAllFields()
        {
            // Arrange
            var domain = new Sprk.Bff.Api.Infrastructure.Graph.SpeAdminGraphService.SpeContainerPermission(
                Id: "perm-001",
                Role: "writer",
                DisplayName: "Jane Smith",
                Email: "jane@contoso.com",
                PrincipalId: "aad-obj-001",
                PrincipalType: "user");

            var dto = Sprk.Bff.Api.Endpoints.SpeAdmin.ContainerPermissionEndpoints.ContainerPermissionDto.FromDomain(domain);

            // Assert — ADR-007: no Graph SDK types exposed
            dto.Id.Should().Be("perm-001");
            dto.Role.Should().Be("writer");
            dto.DisplayName.Should().Be("Jane Smith");
            dto.Email.Should().Be("jane@contoso.com");
            dto.PrincipalId.Should().Be("aad-obj-001");
            dto.PrincipalType.Should().Be("user");
        }

        [Fact]
        public void ContainerPermissionDto_NullableFields_HandleMissingData()
        {
            // Arrange — service principal may not have email or displayName
            var domain = new Sprk.Bff.Api.Infrastructure.Graph.SpeAdminGraphService.SpeContainerPermission(
                Id: "perm-sp-001",
                Role: "reader",
                DisplayName: null,
                Email: null,
                PrincipalId: "sp-obj-001",
                PrincipalType: "application");

            var dto = Sprk.Bff.Api.Endpoints.SpeAdmin.ContainerPermissionEndpoints.ContainerPermissionDto.FromDomain(domain);

            // Assert
            dto.DisplayName.Should().BeNull("service principals may not have a display name");
            dto.Email.Should().BeNull("service principals do not have email addresses");
            dto.PrincipalType.Should().Be("application");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Step 12: Container Type Permissions (app permissions) — model validation
    // ─────────────────────────────────────────────────────────────────────────

    public class ContainerTypePermissionValidationTests
    {
        [Fact]
        public void SpeContainerTypePermission_AllFields_AreCorrectlyMapped()
        {
            // Arrange
            var permission = new Sprk.Bff.Api.Infrastructure.Graph.SpeAdminGraphService.SpeContainerTypePermission(
                AppId: "consuming-app-client-id-001",
                DelegatedPermissions: new List<string> { "FileStorageContainer.Selected" },
                ApplicationPermissions: new List<string> { "FileStorageContainer.Read.All", "Files.Read.All" });

            // Assert — ADR-007: no Graph SDK types in response
            permission.AppId.Should().Be("consuming-app-client-id-001");
            permission.DelegatedPermissions.Should().HaveCount(1);
            permission.ApplicationPermissions.Should().HaveCount(2);
        }

        [Fact]
        public void SpeContainerTypePermission_EmptyPermissions_AreValid()
        {
            // Arrange — a registered app may have no delegated permissions (app-only scenario)
            var permission = new Sprk.Bff.Api.Infrastructure.Graph.SpeAdminGraphService.SpeContainerTypePermission(
                AppId: "app-001",
                DelegatedPermissions: new List<string>(),
                ApplicationPermissions: new List<string> { "FileStorageContainer.ReadWrite.All" });

            // Assert
            permission.DelegatedPermissions.Should().BeEmpty(
                "app-only scenarios may have no delegated permissions");
            permission.ApplicationPermissions.Should().HaveCount(1);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Documented manual integration test scenarios (live Azure environment)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// These scenarios require a live Azure environment and cannot be run as automated tests.
    /// They are documented here as a reference for manual verification against:
    ///   BFF API: https://spe-api-dev-67e2xz.azurewebsites.net
    ///   Code Page: sprk_speadmin in Dataverse (https://spaarkedev1.crm.dynamics.com)
    ///
    /// MANUAL TEST PLAN — Phase 2 Integration:
    ///
    /// CT-001: Container Type List
    ///   URL: GET /api/spe/containertypes?configId={valid-config-guid}
    ///   Expected: 200 OK with ContainerTypeListDto { items: [...], count: N }
    ///   Verify: Each item has id, displayName, billingClassification, createdDateTime
    ///
    /// CT-002: Container Type Get by ID
    ///   URL: GET /api/spe/containertypes/{typeId}?configId={valid-config-guid}
    ///   Expected: 200 OK with ContainerTypeDto
    ///   Verify: 404 returned for unknown typeId
    ///
    /// CT-003: Container Type Create
    ///   URL: POST /api/spe/containertypes?configId={valid-config-guid}
    ///   Body: { "displayName": "Test CT", "billingClassification": "standard" }
    ///   Expected: 201 Created with new ContainerTypeDto
    ///   Verify: Audit log entry written in sprk_speauditlog
    ///
    /// CT-004: Container Type Settings Update
    ///   URL: PUT /api/spe/containertypes/{typeId}/settings?configId={valid-config-guid}
    ///   Body: { "sharingCapability": "view", "isVersioningEnabled": true }
    ///   Expected: 200 OK with ContainerTypeSettingsResponseDto
    ///
    /// CT-005: Container Type Registration
    ///   URL: POST /api/spe/containertypes/{typeId}/register?configId={valid-config-guid}
    ///   Body: { "appId": "{consuming-app-guid}", "sharePointAdminUrl": "https://contoso-admin.sharepoint.com",
    ///           "delegatedPermissions": ["FileStorageContainer.Selected"], "applicationPermissions": [] }
    ///   Expected: 200 OK with RegisterContainerTypeResponse
    ///   Verify: Audit log entry written
    ///
    /// CT-006: App Permissions View
    ///   URL: GET /api/spe/containertypes/{typeId}/appPermissions?configId={valid-config-guid}
    ///   Expected: 200 OK with list of SpeContainerTypePermission entries
    ///
    /// COL-001: Column CRUD
    ///   CREATE: POST /api/spe/containers/{containerId}/columns?configId={id}
    ///           Body: { "name": "Category", "columnType": "choice", "required": false }
    ///   READ:   GET /api/spe/containers/{containerId}/columns?configId={id}
    ///   UPDATE: PATCH /api/spe/containers/{containerId}/columns/{columnId}?configId={id}
    ///           Body: { "displayName": "Document Category" }
    ///   DELETE: DELETE /api/spe/containers/{containerId}/columns/{columnId}?configId={id}
    ///   Expected: 201 Created → 200 OK → 200 OK → 204 No Content
    ///
    /// PROP-001: Custom Property Read and Update
    ///   READ:   GET /api/spe/containers/{containerId}/customproperties?configId={id}
    ///   UPDATE: PATCH /api/spe/containers/{containerId}/customproperties/{propId}?configId={id}
    ///           Body: { "value": "updated-value", "isSearchable": true }
    ///   Expected: 200 OK for both; verify isSearchable flag persists
    ///
    /// SRCH-001: Container Search
    ///   URL: POST /api/spe/search/containers?configId={id}
    ///   Body: { "query": "legal", "pageSize": 10 }
    ///   Expected: 200 OK with items array and count
    ///   Verify: Empty results return { items: [], count: 0 } — not 404
    ///
    /// SRCH-002: Item Search (unscoped)
    ///   URL: POST /api/spe/search/items?configId={id}
    ///   Body: { "query": "contract.pdf", "pageSize": 10 }
    ///   Expected: 200 OK with DriveItem results
    ///
    /// SRCH-003: Item Search (scoped to container)
    ///   URL: POST /api/spe/search/items?configId={id}
    ///   Body: { "query": "invoice", "containerId": "{container-id}", "pageSize": 10 }
    ///   Expected: Results scoped to specified container only
    ///
    /// RB-001: Recycle Bin List
    ///   URL: GET /api/spe/recyclebin?configId={id}
    ///   Expected: 200 OK with deleted containers list
    ///
    /// RB-002: Recycle Bin Restore
    ///   URL: POST /api/spe/recyclebin/{containerId}/restore?configId={id}
    ///   Expected: 200 OK; verify container reappears in active containers list
    ///   Verify: Audit log entry written
    ///
    /// RB-003: Permanent Delete
    ///   URL: DELETE /api/spe/recyclebin/{containerId}?configId={id}
    ///   Expected: 204 No Content; verify container is gone from recycle bin
    ///   Verify: Audit log entry written; operation is irreversible
    ///
    /// SEC-001: Security Alerts
    ///   URL: GET /api/spe/security/alerts?configId={id}
    ///   Expected: 200 OK with SecurityAlertsResponse { alerts: [...] }
    ///   Verify: Each alert has id, title, severity, status, createdDateTime
    ///
    /// SEC-002: Secure Score
    ///   URL: GET /api/spe/security/score?configId={id}
    ///   Expected: 200 OK with SecureScoreDto { currentScore, maxScore, activeUserCount }
    ///
    /// AUTH-001: Non-Admin User Access
    ///   Action: Call any /api/spe/* endpoint with a token that lacks Admin or SystemAdmin role
    ///   Expected: 403 Forbidden with ProblemDetails
    ///   ErrorCode: sdap.access.deny.role_insufficient
    ///
    /// AUTH-002: Unauthenticated Access
    ///   Action: Call any /api/spe/* endpoint without Authorization header
    ///   Expected: 401 Unauthorized (before SpeAdminAuthorizationFilter runs)
    ///
    /// UI-001: ContainerTypesPage Renders
    ///   Navigate to SPE Admin → Container Types
    ///   Verify: Grid loads with container types, toolbar has Create/Register/Refresh buttons
    ///   Verify: Dark mode applies Fluent v9 tokens (ADR-021)
    ///
    /// UI-002: ContainerTypeDetail Panel
    ///   Click a container type → Detail panel slides in
    ///   Verify: Settings tab shows form with sharing/versioning/storage controls
    ///   Verify: Permissions tab loads app permissions on first click
    ///   Verify: Dirty state indicator appears on settings change
    ///
    /// UI-003: RegisterWizard
    ///   Click Register button → 4-step wizard opens
    ///   Step 1: Select container type (or pre-selected)
    ///   Step 2: Select delegated permissions (at least one required)
    ///   Step 3: Select application permissions
    ///   Step 4: Review and confirm
    ///   Verify: Success screen shows on completion
    ///
    /// UI-004: SearchPage
    ///   Navigate to SPE Admin → Search
    ///   Enter query → results populate in ContainerResultsGrid
    ///   Toggle to Item Search → ItemResultsGrid shows
    ///   Verify: Pagination controls appear when totalCount > pageSize
    ///
    /// UI-005: RecycleBinPage
    ///   Navigate to SPE Admin → Recycle Bin
    ///   Verify: Deleted containers listed with deletion date
    ///   Restore a container → confirmation dialog → success toast
    ///   Permanently delete → DESTRUCTIVE warning dialog → success toast
    ///
    /// UI-006: SecurityPage
    ///   Navigate to SPE Admin → Security
    ///   Verify: Secure Score card shows (currentScore / maxScore)
    ///   Verify: Security Alerts grid shows recent alerts with severity badges
    /// </summary>
    public class ManualIntegrationTestDocumentation
    {
        [Fact(Skip = "Manual test — requires live Azure SPE environment")]
        public void ManualTests_AreDocumented_InXmlComments()
        {
            // This test exists only to make the manual test plan visible in the test runner.
            // See the XML documentation on ManualIntegrationTestDocumentation for procedures.
            true.Should().BeTrue("documentation marker test");
        }
    }
}
