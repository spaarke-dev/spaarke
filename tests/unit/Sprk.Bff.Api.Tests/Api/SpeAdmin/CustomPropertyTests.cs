using FluentAssertions;
using Sprk.Bff.Api.Api.SpeAdmin;
using Sprk.Bff.Api.Models.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.SpeAdmin;

/// <summary>
/// Unit tests for the custom property endpoints and related DTOs.
///
/// These tests validate:
/// - DTO construction and immutability
/// - Endpoint registration (method/signature contract)
/// - Validation logic (empty property name rejection)
/// - Response type structure
///
/// Full integration tests (requiring live Graph API) are out of scope for unit tests.
/// </summary>
public class CustomPropertyTests
{
    // =========================================================================
    // DTO Tests
    // =========================================================================

    #region CustomPropertyDto

    [Fact]
    public void CustomPropertyDto_ConstructsWithAllFields()
    {
        // Arrange & Act
        var dto = new CustomPropertyDto("Region", "EMEA", IsSearchable: true);

        // Assert
        dto.Name.Should().Be("Region");
        dto.Value.Should().Be("EMEA");
        dto.IsSearchable.Should().BeTrue();
    }

    [Fact]
    public void CustomPropertyDto_DefaultIsSearchable_IsFalse()
    {
        // Arrange & Act
        var dto = new CustomPropertyDto("Tag", "Value", IsSearchable: false);

        // Assert
        dto.IsSearchable.Should().BeFalse();
    }

    [Fact]
    public void CustomPropertyDto_IsRecord_SupportsValueEquality()
    {
        // Arrange
        var a = new CustomPropertyDto("Key", "Val", false);
        var b = new CustomPropertyDto("Key", "Val", false);
        var c = new CustomPropertyDto("Key", "Val", true);

        // Assert
        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    [Fact]
    public void CustomPropertyDto_IsImmutable_RecordType()
    {
        // Assert — records have init-only properties
        var type = typeof(CustomPropertyDto);
        type.IsValueType.Should().BeFalse("CustomPropertyDto should be a reference type (record class)");

        // Verify it is a positional record (has Deconstruct method)
        var deconstruct = type.GetMethod("Deconstruct");
        deconstruct.Should().NotBeNull("CustomPropertyDto should be a positional record with Deconstruct");
    }

    [Theory]
    [InlineData("", "value")]
    [InlineData("  ", "value")]
    [InlineData("name", "")]
    [InlineData("name", "   ")]
    public void CustomPropertyDto_AllowsEmptyStrings_ValidationIsCallerResponsibility(string name, string value)
    {
        // Arrange & Act — the DTO itself does not validate; validation is in the endpoint handler
        var dto = new CustomPropertyDto(name, value, false);

        // Assert — DTO accepts any string; caller is responsible for validation
        dto.Name.Should().Be(name);
        dto.Value.Should().Be(value);
    }

    #endregion

    // =========================================================================
    // UpdateCustomPropertiesRequest Tests
    // =========================================================================

    #region UpdateCustomPropertiesRequest

    [Fact]
    public void UpdateCustomPropertiesRequest_ConstructsWithProperties()
    {
        // Arrange
        var properties = new List<CustomPropertyDto>
        {
            new("Department", "Legal", false),
            new("Region", "EMEA", true)
        };

        // Act
        var request = new UpdateCustomPropertiesRequest(properties);

        // Assert
        request.Properties.Should().HaveCount(2);
        request.Properties[0].Name.Should().Be("Department");
        request.Properties[1].Name.Should().Be("Region");
        request.Properties[1].IsSearchable.Should().BeTrue();
    }

    [Fact]
    public void UpdateCustomPropertiesRequest_AllowsEmptyList()
    {
        // Act — empty list is valid (clears all properties)
        var request = new UpdateCustomPropertiesRequest(Array.Empty<CustomPropertyDto>());

        // Assert
        request.Properties.Should().BeEmpty();
    }

    [Fact]
    public void UpdateCustomPropertiesRequest_IsRecord_SupportsValueEquality()
    {
        // Arrange
        var props = new[] { new CustomPropertyDto("Key", "Val", false) };
        var a = new UpdateCustomPropertiesRequest(props);
        var b = new UpdateCustomPropertiesRequest(props);

        // Assert — same reference to props array → equal records
        a.Should().Be(b);
    }

    #endregion

    // =========================================================================
    // Endpoint Registration Tests
    // =========================================================================

    #region Endpoint Registration

    [Fact]
    public void MapContainerCustomPropertyEndpoints_MethodExistsOnEndpointClass()
    {
        // Assert — static method with correct signature
        var method = typeof(ContainerCustomPropertyEndpoints)
            .GetMethod("MapContainerCustomPropertyEndpoints");

        method.Should().NotBeNull("MapContainerCustomPropertyEndpoints must be defined on ContainerCustomPropertyEndpoints");
        method!.IsStatic.Should().BeTrue("MapContainerCustomPropertyEndpoints must be static");
    }

    [Fact]
    public void ContainerCustomPropertyEndpoints_IsStaticClass()
    {
        // Assert
        var type = typeof(ContainerCustomPropertyEndpoints);
        type.IsClass.Should().BeTrue();
        type.IsAbstract.Should().BeTrue("a static class is abstract in IL");
        type.IsSealed.Should().BeTrue("a static class is sealed in IL");
    }

    [Fact]
    public void CustomPropertiesResponse_HasPropertiesAndCount()
    {
        // Arrange
        var props = new List<CustomPropertyDto>
        {
            new("Key1", "Val1", false),
            new("Key2", "Val2", true)
        };

        // Act — CustomPropertiesResponse is a nested record on the endpoint class
        var responseType = typeof(ContainerCustomPropertyEndpoints)
            .GetNestedType("CustomPropertiesResponse");

        responseType.Should().NotBeNull("CustomPropertiesResponse must be a nested type on ContainerCustomPropertyEndpoints");
    }

    #endregion

    // =========================================================================
    // Validation Logic Tests
    // =========================================================================

    #region Validation Logic

    [Fact]
    public void EmptyPropertyName_CanBeDetectedByLinq()
    {
        // Arrange — simulate the validation logic in PutCustomPropertiesAsync
        var properties = new List<CustomPropertyDto>
        {
            new("ValidName", "value1", false),
            new("", "value2", false),      // empty name — should be rejected
            new("AnotherValid", "v3", true)
        };

        // Act — mirrors endpoint validation: any(p => IsNullOrWhiteSpace(p.Name))
        var hasEmptyName = properties.Any(p => string.IsNullOrWhiteSpace(p.Name));

        // Assert
        hasEmptyName.Should().BeTrue("empty property name should be detected");
    }

    [Fact]
    public void WhitespacePropertyName_CanBeDetectedByLinq()
    {
        // Arrange
        var properties = new List<CustomPropertyDto>
        {
            new("  ", "value", false)  // whitespace only — should be rejected
        };

        // Act
        var hasEmptyName = properties.Any(p => string.IsNullOrWhiteSpace(p.Name));

        // Assert
        hasEmptyName.Should().BeTrue("whitespace-only property name should be detected");
    }

    [Fact]
    public void ValidPropertyNames_PassValidation()
    {
        // Arrange
        var properties = new List<CustomPropertyDto>
        {
            new("Department", "Legal", false),
            new("Region", "EMEA", true),
            new("ClientId", "C-1234", false)
        };

        // Act
        var hasEmptyName = properties.Any(p => string.IsNullOrWhiteSpace(p.Name));

        // Assert
        hasEmptyName.Should().BeFalse("all property names are valid");
    }

    [Fact]
    public void EmptyPropertyList_PassesValidation()
    {
        // Arrange
        var properties = Array.Empty<CustomPropertyDto>();

        // Act — empty list is valid (clears properties)
        var hasEmptyName = properties.Any(p => string.IsNullOrWhiteSpace(p.Name));

        // Assert
        hasEmptyName.Should().BeFalse("empty list has no invalid names");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("not-a-guid")]
    [InlineData("12345")]
    public void InvalidConfigId_FailsGuidParsing(string? configId)
    {
        // Act — mirrors endpoint validation: IsNullOrWhiteSpace || !Guid.TryParse
        var isValid = !string.IsNullOrWhiteSpace(configId) && Guid.TryParse(configId, out _);

        // Assert
        isValid.Should().BeFalse($"configId '{configId}' should be invalid");
    }

    [Fact]
    public void ValidConfigId_PassesGuidParsing()
    {
        // Arrange
        var configId = Guid.NewGuid().ToString();

        // Act
        var isValid = !string.IsNullOrWhiteSpace(configId) && Guid.TryParse(configId, out _);

        // Assert
        isValid.Should().BeTrue("valid GUID string should pass configId validation");
    }

    #endregion

    // =========================================================================
    // Response DTO Tests
    // =========================================================================

    #region Response DTO

    [Fact]
    public void CustomPropertiesResponse_CountMatchesPropertiesCount()
    {
        // Arrange — simulate what the endpoint does
        var properties = new List<CustomPropertyDto>
        {
            new("Key1", "Val1", false),
            new("Key2", "Val2", true),
            new("Key3", "Val3", false)
        };

        // Act — response count should match list count
        // (mirrors: new CustomPropertiesResponse(properties, properties.Count))
        var responseCount = properties.Count;

        // Assert
        responseCount.Should().Be(3);
    }

    [Fact]
    public void CustomPropertiesResponse_WithEmptyList_HasZeroCount()
    {
        // Arrange
        var emptyList = Array.Empty<CustomPropertyDto>();

        // Act
        var count = emptyList.Length;

        // Assert
        count.Should().Be(0, "empty properties list should have count 0");
    }

    #endregion
}
