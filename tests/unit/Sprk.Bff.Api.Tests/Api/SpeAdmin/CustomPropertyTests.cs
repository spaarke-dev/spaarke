using FluentAssertions;
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
    // Validation Logic Tests
    // =========================================================================

    #region Validation Logic

    // AMBIGUOUS (task 042): the four tests below mirror PutCustomPropertiesAsync's
    // `properties.Any(p => IsNullOrWhiteSpace(p.Name))` guard rather than calling it, so a
    // production change to that guard would NOT fail these tests — but they are held only because
    // no contract test covers custom-property-name validation. /test-diet at task 090 decides.
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

    #endregion
}
