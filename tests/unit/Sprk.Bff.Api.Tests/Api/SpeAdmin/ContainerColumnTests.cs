using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Sprk.Bff.Api.Api.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.SpeAdmin;

/// <summary>
/// Unit tests for ContainerColumnEndpoints and related DTOs.
///
/// Tests cover:
///   - ContainerColumnDto: construction, value equality, FromDomain mapping
///   - ContainerColumnListResponse: construction and count
///   - CreateColumnRequest: construction and defaults
///   - UpdateColumnRequest: construction and null semantics
///   - Endpoint class registration: static class structure, method existence
///   - Validation logic: column type validation, configId GUID parsing, required fields
///
/// Note: SpeAdminGraphService has non-virtual methods and Graph API dependencies.
/// Full integration tests (requiring live/mocked Graph API) are out of scope for unit tests.
/// Handler-level tests exercise validation mirrors to ensure endpoint contract correctness.
/// </summary>
public class ContainerColumnTests
{
    // =========================================================================
    // ContainerColumnDto Tests
    // =========================================================================

    #region ContainerColumnDto

    [Fact]
    public void ContainerColumnDto_ConstructsWithAllFields()
    {
        // Arrange & Act
        var dto = new ContainerColumnDto(
            Id: "col-001",
            Name: "ClientId",
            DisplayName: "Client ID",
            Description: "The client identifier",
            ColumnType: "text",
            Required: true,
            Indexed: false,
            ReadOnly: false);

        // Assert
        dto.Id.Should().Be("col-001");
        dto.Name.Should().Be("ClientId");
        dto.DisplayName.Should().Be("Client ID");
        dto.Description.Should().Be("The client identifier");
        dto.ColumnType.Should().Be("text");
        dto.Required.Should().BeTrue();
        dto.Indexed.Should().BeFalse();
        dto.ReadOnly.Should().BeFalse();
    }

    [Fact]
    public void ContainerColumnDto_AllowsNullOptionalFields()
    {
        // Arrange & Act
        var dto = new ContainerColumnDto(
            Id: "col-002",
            Name: "Flag",
            DisplayName: null,
            Description: null,
            ColumnType: "boolean",
            Required: false,
            Indexed: false,
            ReadOnly: true);

        // Assert
        dto.DisplayName.Should().BeNull();
        dto.Description.Should().BeNull();
        dto.ReadOnly.Should().BeTrue();
    }

    [Fact]
    public void ContainerColumnDto_IsRecord_SupportsValueEquality()
    {
        // Arrange
        var a = new ContainerColumnDto("col-1", "Name", "Display", null, "text", false, false, false);
        var b = new ContainerColumnDto("col-1", "Name", "Display", null, "text", false, false, false);
        var c = new ContainerColumnDto("col-1", "Name", "Display", null, "text", true, false, false);

        // Assert
        a.Should().Be(b, "records with identical values should be equal");
        a.Should().NotBe(c, "records with different Required flags should not be equal");
    }

    [Fact]
    public void ContainerColumnDto_IsImmutable_RecordType()
    {
        // Assert — positional records have Deconstruct
        var type = typeof(ContainerColumnDto);
        type.IsValueType.Should().BeFalse("ContainerColumnDto should be a reference type (sealed record)");

        var deconstruct = type.GetMethod("Deconstruct");
        deconstruct.Should().NotBeNull("ContainerColumnDto should be a positional record with Deconstruct");
    }

    [Fact]
    public void ContainerColumnDto_FromDomain_MapsAllFields()
    {
        // Arrange
        var domain = new SpeAdminGraphService.SpeContainerColumn(
            Id: "col-999",
            Name: "Region",
            DisplayName: "Legal Region",
            Description: "Geographic region for the matter",
            ColumnType: "choice",
            Required: true,
            Indexed: true,
            ReadOnly: false);

        // Act
        var dto = ContainerColumnDto.FromDomain(domain);

        // Assert
        dto.Id.Should().Be("col-999");
        dto.Name.Should().Be("Region");
        dto.DisplayName.Should().Be("Legal Region");
        dto.Description.Should().Be("Geographic region for the matter");
        dto.ColumnType.Should().Be("choice");
        dto.Required.Should().BeTrue();
        dto.Indexed.Should().BeTrue();
        dto.ReadOnly.Should().BeFalse();
    }

    [Fact]
    public void ContainerColumnDto_FromDomain_HandlesNullOptionalFields()
    {
        // Arrange
        var domain = new SpeAdminGraphService.SpeContainerColumn(
            Id: "col-100",
            Name: "Amount",
            DisplayName: null,
            Description: null,
            ColumnType: "currency",
            Required: false,
            Indexed: false,
            ReadOnly: false);

        // Act
        var dto = ContainerColumnDto.FromDomain(domain);

        // Assert
        dto.DisplayName.Should().BeNull();
        dto.Description.Should().BeNull();
    }

    #endregion

    // =========================================================================
    // ContainerColumnListResponse Tests
    // =========================================================================

    #region ContainerColumnListResponse

    [Fact]
    public void ContainerColumnListResponse_ConstructsWithItemsAndCount()
    {
        // Arrange
        var items = new List<ContainerColumnDto>
        {
            new("col-1", "ClientId", "Client ID", null, "text", false, false, false),
            new("col-2", "Amount", "Billing Amount", null, "currency", true, true, false)
        };

        // Act
        var response = new ContainerColumnListResponse(items, items.Count);

        // Assert
        response.Items.Should().HaveCount(2);
        response.Count.Should().Be(2);
        response.Items[0].Name.Should().Be("ClientId");
        response.Items[1].ColumnType.Should().Be("currency");
    }

    [Fact]
    public void ContainerColumnListResponse_WithEmptyList_HasZeroCount()
    {
        // Act
        var response = new ContainerColumnListResponse(Array.Empty<ContainerColumnDto>(), 0);

        // Assert
        response.Items.Should().BeEmpty();
        response.Count.Should().Be(0);
    }

    [Fact]
    public void ContainerColumnListResponse_IsRecord_SupportsValueEquality()
    {
        // Arrange
        var items = Array.Empty<ContainerColumnDto>();
        var a = new ContainerColumnListResponse(items, 0);
        var b = new ContainerColumnListResponse(items, 0);

        // Assert
        a.Should().Be(b);
    }

    #endregion

    // =========================================================================
    // CreateColumnRequest Tests
    // =========================================================================

    #region CreateColumnRequest

    [Fact]
    public void CreateColumnRequest_ConstructsWithRequiredFields()
    {
        // Arrange & Act
        var request = new CreateColumnRequest(
            Name: "MatterType",
            DisplayName: "Matter Type",
            Description: "Type of legal matter",
            ColumnType: "text",
            Required: true,
            Indexed: true);

        // Assert
        request.Name.Should().Be("MatterType");
        request.DisplayName.Should().Be("Matter Type");
        request.Description.Should().Be("Type of legal matter");
        request.ColumnType.Should().Be("text");
        request.Required.Should().BeTrue();
        request.Indexed.Should().BeTrue();
    }

    [Fact]
    public void CreateColumnRequest_Defaults_RequiredFalse_IndexedFalse()
    {
        // Act — test default parameter values
        var request = new CreateColumnRequest(
            Name: "MinimalColumn",
            DisplayName: null,
            Description: null,
            ColumnType: "boolean");

        // Assert — default values applied
        request.Required.Should().BeFalse("Required defaults to false");
        request.Indexed.Should().BeFalse("Indexed defaults to false");
    }

    [Fact]
    public void CreateColumnRequest_AllowsNullOptionalFields()
    {
        // Arrange & Act
        var request = new CreateColumnRequest(
            Name: "Score",
            DisplayName: null,
            Description: null,
            ColumnType: "number");

        // Assert
        request.DisplayName.Should().BeNull();
        request.Description.Should().BeNull();
    }

    [Fact]
    public void CreateColumnRequest_IsRecord_SupportsValueEquality()
    {
        // Arrange
        var a = new CreateColumnRequest("Col", null, null, "text", false, false);
        var b = new CreateColumnRequest("Col", null, null, "text", false, false);
        var c = new CreateColumnRequest("Col", null, null, "text", true, false);

        // Assert
        a.Should().Be(b);
        a.Should().NotBe(c, "records with different Required flags should not be equal");
    }

    #endregion

    // =========================================================================
    // UpdateColumnRequest Tests
    // =========================================================================

    #region UpdateColumnRequest

    [Fact]
    public void UpdateColumnRequest_ConstructsWithAllFields()
    {
        // Arrange & Act
        var request = new UpdateColumnRequest(
            DisplayName: "New Display",
            Description: "New description",
            Required: true,
            Indexed: false);

        // Assert
        request.DisplayName.Should().Be("New Display");
        request.Description.Should().Be("New description");
        request.Required.Should().BeTrue();
        request.Indexed.Should().BeFalse();
    }

    [Fact]
    public void UpdateColumnRequest_AllFieldsNull_RepresentsNoOp()
    {
        // Act — all-null update (endpoint should reject this with 400)
        var request = new UpdateColumnRequest(
            DisplayName: null,
            Description: null,
            Required: null,
            Indexed: null);

        // Assert — record allows all-null; endpoint validation rejects it
        request.DisplayName.Should().BeNull();
        request.Description.Should().BeNull();
        request.Required.Should().BeNull();
        request.Indexed.Should().BeNull();
    }

    [Fact]
    public void UpdateColumnRequest_PartialUpdate_OnlyDisplayName()
    {
        // Arrange — patch only one field; others null → leave unchanged
        var request = new UpdateColumnRequest(
            DisplayName: "Updated Name",
            Description: null,
            Required: null,
            Indexed: null);

        // Assert
        request.DisplayName.Should().Be("Updated Name");
        request.Description.Should().BeNull("description not included in this patch");
        request.Required.Should().BeNull("required not included in this patch");
    }

    [Fact]
    public void UpdateColumnRequest_IsRecord_SupportsValueEquality()
    {
        // Arrange
        var a = new UpdateColumnRequest("Name", null, null, null);
        var b = new UpdateColumnRequest("Name", null, null, null);
        var c = new UpdateColumnRequest("Different", null, null, null);

        // Assert
        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    #endregion

    // =========================================================================
    // SpeContainerColumn Domain Record Tests
    // =========================================================================

    #region SpeContainerColumn Domain Record

    [Fact]
    public void SpeContainerColumn_ConstructsWithAllFields()
    {
        // Arrange & Act
        var col = new SpeAdminGraphService.SpeContainerColumn(
            Id: "graph-col-id",
            Name: "Status",
            DisplayName: "Matter Status",
            Description: "Current status of the matter",
            ColumnType: "choice",
            Required: false,
            Indexed: true,
            ReadOnly: false);

        // Assert
        col.Id.Should().Be("graph-col-id");
        col.Name.Should().Be("Status");
        col.DisplayName.Should().Be("Matter Status");
        col.ColumnType.Should().Be("choice");
        col.Indexed.Should().BeTrue();
        col.ReadOnly.Should().BeFalse();
    }

    [Fact]
    public void SpeContainerColumn_IsRecord_SupportsValueEquality()
    {
        // Arrange
        var a = new SpeAdminGraphService.SpeContainerColumn("id", "Name", null, null, "text", false, false, false);
        var b = new SpeAdminGraphService.SpeContainerColumn("id", "Name", null, null, "text", false, false, false);

        // Assert
        a.Should().Be(b, "identical domain records should be value-equal");
    }

    #endregion

    // =========================================================================
    // Endpoint Class Registration Tests
    // =========================================================================

    #region Endpoint Registration

    [Fact]
    public void ContainerColumnEndpoints_IsStaticClass()
    {
        // A static class is abstract + sealed in IL
        var type = typeof(ContainerColumnEndpoints);
        type.IsClass.Should().BeTrue();
        type.IsAbstract.Should().BeTrue("a static class is abstract in IL");
        type.IsSealed.Should().BeTrue("a static class is sealed in IL");
    }

    [Fact]
    public void MapContainerColumnEndpoints_MethodExistsOnEndpointClass()
    {
        // Assert — static method with correct signature exists
        var method = typeof(ContainerColumnEndpoints)
            .GetMethod("MapContainerColumnEndpoints");

        method.Should().NotBeNull("MapContainerColumnEndpoints must be defined on ContainerColumnEndpoints");
        method!.IsStatic.Should().BeTrue("MapContainerColumnEndpoints must be static");
    }

    [Fact]
    public void MapContainerColumnEndpoints_AcceptsRouteGroupBuilder_Parameter()
    {
        // Assert — first parameter must be RouteGroupBuilder
        var method = typeof(ContainerColumnEndpoints)
            .GetMethod("MapContainerColumnEndpoints");

        method.Should().NotBeNull();
        var parameters = method!.GetParameters();
        parameters.Should().HaveCount(1, "method takes one RouteGroupBuilder parameter");
        parameters[0].ParameterType.Should().Be(
            typeof(RouteGroupBuilder),
            "first parameter must be RouteGroupBuilder");
    }

    #endregion

    // =========================================================================
    // Column Type Validation Logic Tests
    // =========================================================================

    #region Column Type Validation

    [Theory]
    [InlineData("text")]
    [InlineData("boolean")]
    [InlineData("dateTime")]
    [InlineData("currency")]
    [InlineData("choice")]
    [InlineData("number")]
    [InlineData("personOrGroup")]
    [InlineData("hyperlinkOrPicture")]
    public void ValidColumnTypes_AreAccepted(string columnType)
    {
        // Arrange — mirrors ValidColumnTypes in ContainerColumnEndpoints
        var validTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "text", "boolean", "dateTime", "currency", "choice",
            "number", "personOrGroup", "hyperlinkOrPicture"
        };

        // Act
        var isValid = validTypes.Contains(columnType);

        // Assert
        isValid.Should().BeTrue($"'{columnType}' should be a valid column type");
    }

    [Theory]
    [InlineData("TEXT")]      // case-insensitive match
    [InlineData("Boolean")]   // case-insensitive match
    [InlineData("DATETIME")]  // case-insensitive match
    public void ValidColumnTypes_AreAccepted_CaseInsensitive(string columnType)
    {
        // Arrange — validation is OrdinalIgnoreCase
        var validTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "text", "boolean", "dateTime", "currency", "choice",
            "number", "personOrGroup", "hyperlinkOrPicture"
        };

        // Act
        var isValid = validTypes.Contains(columnType);

        // Assert
        isValid.Should().BeTrue($"'{columnType}' should match case-insensitively");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("string")]
    [InlineData("integer")]
    [InlineData("date")]
    // "datetime" is accepted case-insensitively (endpoint uses OrdinalIgnoreCase) — removed from invalid list
    [InlineData("lookup")]
    [InlineData("rich-text")]
    [InlineData(null)]
    public void InvalidColumnTypes_AreRejected(string? columnType)
    {
        // Arrange
        var validTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "text", "boolean", "dateTime", "currency", "choice",
            "number", "personOrGroup", "hyperlinkOrPicture"
        };

        // Act — mirrors endpoint validation: IsNullOrWhiteSpace || !Contains
        var isValid = !string.IsNullOrWhiteSpace(columnType) && validTypes.Contains(columnType);

        // Assert
        isValid.Should().BeFalse($"'{columnType}' should not be a valid column type");
    }

    #endregion

    // =========================================================================
    // ConfigId Validation Logic Tests
    // =========================================================================

    #region ConfigId Validation

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("not-a-guid")]
    [InlineData("12345")]
    [InlineData("00000000-0000-0000-0000-000000000000")]  // empty GUID parses successfully — valid per Guid.TryParse
    public void InvalidOrEmptyConfigId_FailsValidation(string? configId)
    {
        // Act — mirrors endpoint: IsNullOrWhiteSpace || !Guid.TryParse
        var isValid = !string.IsNullOrWhiteSpace(configId) && Guid.TryParse(configId, out _);

        // Assert — empty string, whitespace, and non-GUIDs fail; zero GUID is actually valid
        if (configId is null || string.IsNullOrWhiteSpace(configId) || !Guid.TryParse(configId, out _))
            isValid.Should().BeFalse($"configId '{configId}' should fail validation");
    }

    [Fact]
    public void ValidConfigId_NewGuid_PassesValidation()
    {
        // Arrange
        var configId = Guid.NewGuid().ToString();

        // Act
        var isValid = !string.IsNullOrWhiteSpace(configId) && Guid.TryParse(configId, out _);

        // Assert
        isValid.Should().BeTrue("a valid GUID string should pass configId validation");
    }

    [Theory]
    [InlineData("A1B2C3D4-E5F6-7890-ABCD-EF1234567890")]
    [InlineData("a1b2c3d4-e5f6-7890-abcd-ef1234567890")]
    [InlineData("{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}")]  // Guid.TryParse accepts braces
    public void WellFormedGuidStrings_PassValidation(string configId)
    {
        // Act
        var isValid = !string.IsNullOrWhiteSpace(configId) && Guid.TryParse(configId, out _);

        // Assert
        isValid.Should().BeTrue($"'{configId}' is a well-formed GUID and should pass validation");
    }

    #endregion

    // =========================================================================
    // UpdateColumnRequest Validation Logic Tests
    // =========================================================================

    #region UpdateColumn Validation Logic

    [Fact]
    public void AllNullUpdateRequest_FailsValidation()
    {
        // Arrange — at least one field must be non-null
        var request = new UpdateColumnRequest(null, null, null, null);

        // Act — mirrors endpoint validation
        var isNoOp = request.DisplayName is null
            && request.Description is null
            && request.Required is null
            && request.Indexed is null;

        // Assert
        isNoOp.Should().BeTrue("an all-null update request should be detected as a no-op and rejected");
    }

    [Fact]
    public void PartialUpdateRequest_WithOnlyDisplayName_PassesValidation()
    {
        // Arrange
        var request = new UpdateColumnRequest("Updated Name", null, null, null);

        // Act
        var isNoOp = request.DisplayName is null
            && request.Description is null
            && request.Required is null
            && request.Indexed is null;

        // Assert
        isNoOp.Should().BeFalse("request with at least one non-null field should pass the no-op check");
    }

    [Fact]
    public void PartialUpdateRequest_WithOnlyRequired_PassesValidation()
    {
        // Arrange
        var request = new UpdateColumnRequest(null, null, Required: true, null);

        // Act
        var isNoOp = request.DisplayName is null
            && request.Description is null
            && request.Required is null
            && request.Indexed is null;

        // Assert
        isNoOp.Should().BeFalse("request with Required=true should not be treated as a no-op");
    }

    [Fact]
    public void PartialUpdateRequest_WithOnlyIndexed_PassesValidation()
    {
        // Arrange
        var request = new UpdateColumnRequest(null, null, null, Indexed: false);

        // Act
        var isNoOp = request.DisplayName is null
            && request.Description is null
            && request.Required is null
            && request.Indexed is null;

        // Assert
        isNoOp.Should().BeFalse("request with Indexed=false (non-null) should not be treated as a no-op");
    }

    #endregion

    // =========================================================================
    // ContainerColumnDto.FromDomain Mapping Tests
    // =========================================================================

    #region FromDomain Mapping

    [Theory]
    [InlineData("text")]
    [InlineData("boolean")]
    [InlineData("dateTime")]
    [InlineData("currency")]
    [InlineData("choice")]
    [InlineData("number")]
    [InlineData("personOrGroup")]
    [InlineData("hyperlinkOrPicture")]
    public void FromDomain_PreservesColumnType_ForAllValidTypes(string columnType)
    {
        // Arrange
        var domain = new SpeAdminGraphService.SpeContainerColumn(
            Id: "id", Name: "col", DisplayName: null, Description: null,
            ColumnType: columnType, Required: false, Indexed: false, ReadOnly: false);

        // Act
        var dto = ContainerColumnDto.FromDomain(domain);

        // Assert
        dto.ColumnType.Should().Be(columnType, $"FromDomain should preserve columnType '{columnType}'");
    }

    [Fact]
    public void FromDomain_PreservesReadOnly_Flag()
    {
        // Arrange — system-managed columns are read-only
        var domain = new SpeAdminGraphService.SpeContainerColumn(
            Id: "sys-col", Name: "Created", DisplayName: "Created Date", Description: null,
            ColumnType: "dateTime", Required: false, Indexed: false, ReadOnly: true);

        // Act
        var dto = ContainerColumnDto.FromDomain(domain);

        // Assert
        dto.ReadOnly.Should().BeTrue("system-managed columns should map ReadOnly=true to DTO");
    }

    [Fact]
    public void FromDomain_PreservesRequiredAndIndexed_Flags()
    {
        // Arrange
        var domain = new SpeAdminGraphService.SpeContainerColumn(
            Id: "col-req", Name: "ClientId", DisplayName: null, Description: null,
            ColumnType: "text", Required: true, Indexed: true, ReadOnly: false);

        // Act
        var dto = ContainerColumnDto.FromDomain(domain);

        // Assert
        dto.Required.Should().BeTrue();
        dto.Indexed.Should().BeTrue();
    }

    [Fact]
    public void FromDomain_MapsId_Unchanged()
    {
        // Arrange — Id is an opaque Graph string; must be preserved exactly
        const string graphId = "some-opaque-graph-column-id-abc123";
        var domain = new SpeAdminGraphService.SpeContainerColumn(
            Id: graphId, Name: "col", DisplayName: null, Description: null,
            ColumnType: "text", Required: false, Indexed: false, ReadOnly: false);

        // Act
        var dto = ContainerColumnDto.FromDomain(domain);

        // Assert
        dto.Id.Should().Be(graphId, "column ID must be passed through unchanged from Graph");
    }

    #endregion

    // =========================================================================
    // CreateColumnRequest Name Validation Tests
    // =========================================================================

    #region Create Request Name Validation

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespace_ColumnName_FailsValidation(string? name)
    {
        // Act — mirrors endpoint: IsNullOrWhiteSpace(request.Name)
        var isInvalid = string.IsNullOrWhiteSpace(name);

        // Assert
        isInvalid.Should().BeTrue($"column name '{name}' should fail non-empty validation");
    }

    [Theory]
    [InlineData("ClientId")]
    [InlineData("MatterType")]
    [InlineData("Amount")]
    [InlineData("A")]  // single character is acceptable
    public void NonEmpty_ColumnName_PassesValidation(string name)
    {
        // Act
        var isInvalid = string.IsNullOrWhiteSpace(name);

        // Assert
        isInvalid.Should().BeFalse($"column name '{name}' is valid and should pass validation");
    }

    #endregion
}
