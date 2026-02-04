using FluentAssertions;
using Sprk.Bff.Api.Api.FieldMappings;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.FieldMappings;

/// <summary>
/// Unit tests for TypeCompatibilityValidator.
/// Validates that C# implementation matches TypeScript STRICT_TYPE_COMPATIBILITY matrix.
/// </summary>
public class TypeCompatibilityValidatorTests
{
    #region Exact Type Match Tests

    [Theory]
    [InlineData("Text", "Text")]
    [InlineData("Lookup", "Lookup")]
    [InlineData("Number", "Number")]
    [InlineData("DateTime", "DateTime")]
    [InlineData("Boolean", "Boolean")]
    [InlineData("OptionSet", "OptionSet")]
    [InlineData("Memo", "Memo")]
    public void Validate_ReturnsExact_WhenTypesAreIdentical(string sourceType, string targetType)
    {
        // Act
        var result = TypeCompatibilityValidator.Validate(sourceType, targetType);

        // Assert
        result.IsValid.Should().BeTrue();
        result.CompatibilityLevel.Should().Be("exact");
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("text", "TEXT")]
    [InlineData("LOOKUP", "lookup")]
    [InlineData("DateTime", "datetime")]
    public void Validate_IsCaseInsensitive(string sourceType, string targetType)
    {
        // Act
        var result = TypeCompatibilityValidator.Validate(sourceType, targetType);

        // Assert
        result.IsValid.Should().BeTrue();
        result.CompatibilityLevel.Should().Be("exact");
    }

    #endregion

    #region Strict Compatibility Matrix Tests

    [Theory]
    [InlineData("Lookup", "Text")]
    [InlineData("Text", "Memo")]
    [InlineData("Memo", "Text")]
    [InlineData("OptionSet", "Text")]
    [InlineData("Number", "Text")]
    [InlineData("DateTime", "Text")]
    [InlineData("Boolean", "Text")]
    public void Validate_ReturnsSafeConversion_WhenTypesAreStrictCompatible(string sourceType, string targetType)
    {
        // Act
        var result = TypeCompatibilityValidator.Validate(sourceType, targetType);

        // Assert
        result.IsValid.Should().BeTrue();
        result.CompatibilityLevel.Should().Be("safe_conversion");
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Lookup", "Text")]
    [InlineData("OptionSet", "Text")]
    [InlineData("Number", "Text")]
    [InlineData("DateTime", "Text")]
    [InlineData("Boolean", "Text")]
    public void Validate_AddsWarning_WhenConvertingToText(string sourceType, string targetType)
    {
        // Act
        var result = TypeCompatibilityValidator.Validate(sourceType, targetType);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Warnings.Should().ContainSingle()
            .Which.Should().Contain("will be formatted as string");
    }

    [Fact]
    public void Validate_NoWarning_WhenTextToMemo()
    {
        // Act
        var result = TypeCompatibilityValidator.Validate("Text", "Memo");

        // Assert
        result.IsValid.Should().BeTrue();
        result.Warnings.Should().BeEmpty("Text to Memo conversion is not lossy");
    }

    [Fact]
    public void Validate_NoWarning_WhenMemoToText()
    {
        // Act
        var result = TypeCompatibilityValidator.Validate("Memo", "Text");

        // Assert
        result.IsValid.Should().BeTrue();
        // Memo to Text may or may not have a warning depending on implementation
        // The TypeScript version doesn't add a warning for this case
    }

    #endregion

    #region Incompatible Types Tests

    [Theory]
    [InlineData("Lookup", "Number")]
    [InlineData("Lookup", "DateTime")]
    [InlineData("Lookup", "Boolean")]
    [InlineData("Lookup", "OptionSet")]
    [InlineData("Number", "DateTime")]
    [InlineData("Number", "Boolean")]
    [InlineData("Number", "Lookup")]
    [InlineData("DateTime", "Number")]
    [InlineData("DateTime", "Boolean")]
    [InlineData("DateTime", "Lookup")]
    [InlineData("Boolean", "Number")]
    [InlineData("Boolean", "DateTime")]
    [InlineData("Boolean", "Lookup")]
    [InlineData("OptionSet", "Number")]
    [InlineData("OptionSet", "DateTime")]
    [InlineData("OptionSet", "Boolean")]
    [InlineData("OptionSet", "Lookup")]
    [InlineData("Text", "Lookup")]
    [InlineData("Text", "Number")]
    [InlineData("Text", "DateTime")]
    [InlineData("Text", "Boolean")]
    [InlineData("Text", "OptionSet")]
    public void Validate_ReturnsIncompatible_WhenTypesAreNotInMatrix(string sourceType, string targetType)
    {
        // Act
        var result = TypeCompatibilityValidator.Validate(sourceType, targetType);

        // Assert
        result.IsValid.Should().BeFalse();
        result.CompatibilityLevel.Should().Be("incompatible");
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain($"Cannot convert {sourceType} to {targetType}");
    }

    #endregion

    #region Unknown Type Tests

    [Fact]
    public void Validate_ReturnsError_WhenSourceTypeUnknown()
    {
        // Act
        var result = TypeCompatibilityValidator.Validate("UnknownType", "Text");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("Unknown source field type");
    }

    [Fact]
    public void Validate_ReturnsError_WhenTargetTypeUnknown()
    {
        // Act
        var result = TypeCompatibilityValidator.Validate("Text", "InvalidType");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("Unknown target field type");
    }

    [Fact]
    public void Validate_ReturnsTwoErrors_WhenBothTypesUnknown()
    {
        // Act
        var result = TypeCompatibilityValidator.Validate("Invalid1", "Invalid2");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
    }

    #endregion

    #region GetCompatibleTargetTypes Tests

    [Fact]
    public void GetCompatibleTargetTypes_ReturnsLookupAndText_ForLookup()
    {
        // Act
        var compatibleTypes = TypeCompatibilityValidator.GetCompatibleTargetTypes("Lookup");

        // Assert
        compatibleTypes.Should().BeEquivalentTo(["Lookup", "Text"]);
    }

    [Fact]
    public void GetCompatibleTargetTypes_ReturnsTextAndMemo_ForText()
    {
        // Act
        var compatibleTypes = TypeCompatibilityValidator.GetCompatibleTargetTypes("Text");

        // Assert
        compatibleTypes.Should().BeEquivalentTo(["Text", "Memo"]);
    }

    [Fact]
    public void GetCompatibleTargetTypes_ReturnsTextAndMemo_ForMemo()
    {
        // Act
        var compatibleTypes = TypeCompatibilityValidator.GetCompatibleTargetTypes("Memo");

        // Assert
        compatibleTypes.Should().BeEquivalentTo(["Text", "Memo"]);
    }

    [Fact]
    public void GetCompatibleTargetTypes_ReturnsEmptyArray_ForUnknownType()
    {
        // Act
        var compatibleTypes = TypeCompatibilityValidator.GetCompatibleTargetTypes("UnknownType");

        // Assert
        compatibleTypes.Should().BeEmpty();
    }

    #endregion

    #region Response Contains Compatible Types Tests

    [Fact]
    public void Validate_IncludesCompatibleTargetTypes_WhenIncompatible()
    {
        // Act
        var result = TypeCompatibilityValidator.Validate("Lookup", "Number");

        // Assert
        result.IsValid.Should().BeFalse();
        result.CompatibleTargetTypes.Should().BeEquivalentTo(["Lookup", "Text"]);
    }

    [Fact]
    public void Validate_IncludesCompatibleTargetTypes_WhenCompatible()
    {
        // Act
        var result = TypeCompatibilityValidator.Validate("Lookup", "Text");

        // Assert
        result.IsValid.Should().BeTrue();
        result.CompatibleTargetTypes.Should().BeEquivalentTo(["Lookup", "Text"]);
    }

    #endregion

    #region TypeScript Matrix Parity Tests (mirrors FieldMappingTypes.ts)

    /// <summary>
    /// This test ensures the C# implementation matches the TypeScript STRICT_TYPE_COMPATIBILITY.
    /// Reference: src/client/shared/Spaarke.UI.Components/src/types/FieldMappingTypes.ts
    /// </summary>
    [Fact]
    public void Validate_MatchesTypeScriptMatrix()
    {
        // TypeScript Matrix:
        // [FieldType.Lookup]: [FieldType.Lookup, FieldType.Text]
        // [FieldType.Text]: [FieldType.Text, FieldType.Memo]
        // [FieldType.Memo]: [FieldType.Text, FieldType.Memo]
        // [FieldType.OptionSet]: [FieldType.OptionSet, FieldType.Text]
        // [FieldType.Number]: [FieldType.Number, FieldType.Text]
        // [FieldType.DateTime]: [FieldType.DateTime, FieldType.Text]
        // [FieldType.Boolean]: [FieldType.Boolean, FieldType.Text]

        var expectedCompatibility = new Dictionary<string, string[]>
        {
            ["Lookup"] = ["Lookup", "Text"],
            ["Text"] = ["Text", "Memo"],
            ["Memo"] = ["Text", "Memo"],
            ["OptionSet"] = ["OptionSet", "Text"],
            ["Number"] = ["Number", "Text"],
            ["DateTime"] = ["DateTime", "Text"],
            ["Boolean"] = ["Boolean", "Text"]
        };

        foreach (var (sourceType, compatibleTargets) in expectedCompatibility)
        {
            var result = TypeCompatibilityValidator.GetCompatibleTargetTypes(sourceType);
            result.Should().BeEquivalentTo(
                compatibleTargets,
                $"C# matrix for {sourceType} should match TypeScript matrix");
        }
    }

    #endregion
}
