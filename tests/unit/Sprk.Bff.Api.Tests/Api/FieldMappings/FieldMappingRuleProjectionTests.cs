using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Api.FieldMappings.Dtos;
using Sprk.Bff.Api.Models.FieldMapping;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.FieldMappings;

/// <summary>
/// Unit tests for the field-mapping rule DTO projection (<c>MapRuleEntityToDto</c>) and the
/// push engine's tolerance of the additive rule shape (<c>ApplyMappingRule</c>), both on
/// <see cref="FieldMappingEndpoints"/>.
/// </summary>
/// <remarks>
/// Added by project set-regarding-and-field-mapping-resolver-r2, task 003 (spec FR-16, NFR-01, NFR-07).
///
/// Covers the 5 additive <see cref="FieldMappingRuleDto"/> fields introduced by task 002
/// (mappingType/defaultValue/expression/isRequired/compatibilityMode), which mirror the new
/// <see cref="FieldMappingRuleEntity"/> columns (sprk_mapping_type, sprk_expression, plus the
/// already-read defaultValue/isRequired/compatibilitymode).
///
/// IMPLEMENTATION NOTE — direct call, not reflection.
///   MapRuleEntityToDto and ApplyMappingRule were changed from private to internal static on
///   FieldMappingEndpoints (accessibility-only change; no behavior change) so this test assembly
///   — which already has InternalsVisibleTo via Sprk.Bff.Api.csproj — can call them directly.
///   This is the cleaner seam over reflection (task 003 POML constraint: "do NOT introduce
///   reflection hacks if a cleaner seam exists"), and does not touch PushFieldMappingsAsync's
///   behavior, satisfying the sibling constraint "Do NOT modify the push endpoint's behavior."
///
/// @see Sprk.Bff.Api.Api.FieldMappings.FieldMappingEndpoints.MapRuleEntityToDto
/// @see Sprk.Bff.Api.Api.FieldMappings.FieldMappingEndpoints.ApplyMappingRule
/// @see Sprk.Bff.Api.Models.FieldMapping.FieldMappingRuleDto
/// @see Spaarke.Dataverse.FieldMappingRuleEntity
/// </remarks>
public class FieldMappingRuleProjectionTests
{
    /// <summary>
    /// Builds a representative FieldMappingRuleEntity with sensible defaults, overridable per test.
    /// </summary>
    private static FieldMappingRuleEntity BuildEntity(
        int mappingType = 0,
        string? defaultValue = null,
        string? expression = null,
        bool isRequired = false,
        int compatibilityMode = 0) => new()
        {
            Id = Guid.NewGuid(),
            Name = "Test Rule",
            SourceField = "sprk_sourcefield",
            SourceFieldType = 0, // Text
            TargetField = "sprk_targetfield",
            TargetFieldType = 0, // Text
            MappingType = mappingType,
            DefaultValue = defaultValue,
            Expression = expression,
            IsRequired = isRequired,
            CompatibilityMode = compatibilityMode
        };

    #region MapRuleEntityToDto — MappingType Projection (Copy/Default/Concat/Template)

    [Theory]
    [InlineData(0, "Copy")]
    [InlineData(1, "Default")]
    [InlineData(2, "Concat")]
    [InlineData(3, "Template")]
    public void MapRuleEntityToDto_GivenMappingTypeInt_ProjectsExpectedString(int mappingType, string expected)
    {
        // Arrange
        var entity = BuildEntity(mappingType: mappingType);

        // Act
        var dto = FieldMappingEndpoints.MapRuleEntityToDto(entity);

        // Assert
        dto.MappingType.Should().Be(expected);
    }

    [Fact]
    public void MapRuleEntityToDto_UnknownMappingTypeInt_DefaultsToCopy()
    {
        // Arrange — an out-of-range int (e.g., a future/undeployed choice value) must not throw
        var entity = BuildEntity(mappingType: 99);

        // Act
        var dto = FieldMappingEndpoints.MapRuleEntityToDto(entity);

        // Assert
        dto.MappingType.Should().Be("Copy", "unknown mapping-type ints fall back to the safe Copy default");
    }

    #endregion

    #region MapRuleEntityToDto — DefaultValue / Expression / IsRequired Projection

    [Fact]
    public void MapRuleEntityToDto_DefaultRule_ProjectsDefaultValueVerbatim()
    {
        // Arrange
        var entity = BuildEntity(mappingType: 1, defaultValue: "N/A", isRequired: true);

        // Act
        var dto = FieldMappingEndpoints.MapRuleEntityToDto(entity);

        // Assert
        dto.MappingType.Should().Be("Default");
        dto.DefaultValue.Should().Be("N/A");
        dto.IsRequired.Should().BeTrue();
    }

    [Fact]
    public void MapRuleEntityToDto_ConcatRule_ProjectsExpressionVerbatim()
    {
        // Arrange
        var entity = BuildEntity(mappingType: 2, expression: "{sourcefield1} {sourcefield2}");

        // Act
        var dto = FieldMappingEndpoints.MapRuleEntityToDto(entity);

        // Assert
        dto.MappingType.Should().Be("Concat");
        dto.Expression.Should().Be("{sourcefield1} {sourcefield2}");
    }

    [Fact]
    public void MapRuleEntityToDto_TemplateRule_ProjectsExpressionVerbatim()
    {
        // Arrange
        var entity = BuildEntity(mappingType: 3, expression: "Matter: {name}");

        // Act
        var dto = FieldMappingEndpoints.MapRuleEntityToDto(entity);

        // Assert
        dto.MappingType.Should().Be("Template");
        dto.Expression.Should().Be("Matter: {name}");
    }

    [Fact]
    public void MapRuleEntityToDto_CopyRule_DefaultValueAndExpressionRemainNull()
    {
        // Arrange — a plain Copy rule has no DefaultValue/Expression configured
        var entity = BuildEntity(mappingType: 0);

        // Act
        var dto = FieldMappingEndpoints.MapRuleEntityToDto(entity);

        // Assert
        dto.MappingType.Should().Be("Copy");
        dto.DefaultValue.Should().BeNull();
        dto.Expression.Should().BeNull();
        dto.IsRequired.Should().BeFalse();
    }

    #endregion

    #region MapRuleEntityToDto — CompatibilityMode Projection (Strict/Resolve)

    [Theory]
    [InlineData(0, "Strict")]
    [InlineData(1, "Resolve")]
    public void MapRuleEntityToDto_GivenCompatibilityModeInt_ProjectsExpectedString(int compatibilityMode, string expected)
    {
        // Arrange
        var entity = BuildEntity(compatibilityMode: compatibilityMode);

        // Act
        var dto = FieldMappingEndpoints.MapRuleEntityToDto(entity);

        // Assert
        dto.CompatibilityMode.Should().Be(expected);
    }

    [Fact]
    public void MapRuleEntityToDto_UnknownCompatibilityModeInt_DefaultsToStrict()
    {
        // Arrange
        var entity = BuildEntity(compatibilityMode: 42);

        // Act
        var dto = FieldMappingEndpoints.MapRuleEntityToDto(entity);

        // Assert
        dto.CompatibilityMode.Should().Be("Strict", "unknown compatibility-mode ints fall back to the safe Strict default");
    }

    #endregion

    #region Push Regression — ApplyMappingRule tolerates the extended rule shape (no deserialization break)

    /// <summary>
    /// PushFieldMappingsAsync shares MapRuleEntityToDto (via QueryProfileWithRulesByEntityPairAsync)
    /// and feeds the resulting FieldMappingRuleDto[] straight into ApplyMappingRule. This test proves
    /// that a rule carrying all 5 new fields still flows through the existing Copy-style
    /// SourceField/TargetField engine correctly — the additive fields are inert until a later task
    /// wires the engine to branch on MappingType (spec FR-16).
    /// </summary>
    [Fact]
    public void ApplyMappingRule_RuleWithAllNewFieldsPopulated_StillMapsSourceToTargetCorrectly()
    {
        // Arrange — a rule as it would be projected for a Default/Resolve/Required configuration
        var rule = new FieldMappingRuleDto
        {
            Id = Guid.NewGuid(),
            SourceField = "sprk_client",
            SourceFieldType = "Text",
            TargetField = "sprk_regardingaccount",
            TargetFieldType = "Text",
            Priority = 1,
            MappingType = "Default",
            DefaultValue = "Unknown Client",
            Expression = "{sprk_client}",
            IsRequired = true,
            CompatibilityMode = "Resolve"
        };
        var sourceValues = new Dictionary<string, object?> { ["sprk_client"] = "Acme Corp" };
        var updatePayload = new Dictionary<string, object?>();

        // Act
        var result = FieldMappingEndpoints.ApplyMappingRule(rule, sourceValues, updatePayload);

        // Assert — push path is unaffected by the additive fields
        result.Status.Should().Be(FieldMappingStatus.Mapped);
        result.SourceField.Should().Be("sprk_client");
        result.TargetField.Should().Be("sprk_regardingaccount");
        result.ErrorMessage.Should().BeNull();
        updatePayload.Should().ContainKey("sprk_regardingaccount")
            .WhoseValue.Should().Be("Acme Corp");
    }

    [Fact]
    public void ApplyMappingRule_RuleWithNewFieldsAndNullSourceValue_SkipsGracefully()
    {
        // Arrange — source value absent; the new fields must not cause a throw or a different code path
        var rule = new FieldMappingRuleDto
        {
            Id = Guid.NewGuid(),
            SourceField = "sprk_optionalfield",
            SourceFieldType = "Text",
            TargetField = "sprk_targetfield",
            TargetFieldType = "Text",
            Priority = 1,
            MappingType = "Concat",
            Expression = "{sprk_optionalfield}",
            IsRequired = false,
            CompatibilityMode = "Resolve"
        };
        var sourceValues = new Dictionary<string, object?> { ["sprk_optionalfield"] = null };
        var updatePayload = new Dictionary<string, object?>();

        // Act
        var result = FieldMappingEndpoints.ApplyMappingRule(rule, sourceValues, updatePayload);

        // Assert
        result.Status.Should().Be(FieldMappingStatus.Skipped);
        result.ErrorMessage.Should().Be("Source value is null");
        updatePayload.Should().BeEmpty();
    }

    #endregion
}
