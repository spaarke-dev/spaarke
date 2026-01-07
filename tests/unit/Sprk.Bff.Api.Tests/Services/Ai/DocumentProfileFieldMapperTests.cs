using FluentAssertions;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for DocumentProfileFieldMapper.
/// </summary>
public class DocumentProfileFieldMapperTests
{
    #region GetFieldName Tests

    [Theory]
    [InlineData("TL;DR", "sprk_tldr")]
    [InlineData("tl;dr", "sprk_tldr")]
    [InlineData("TL;dr", "sprk_tldr")]
    [InlineData("Summary", "sprk_summary")]
    [InlineData("summary", "sprk_summary")]
    [InlineData("Keywords", "sprk_keywords")]
    [InlineData("keywords", "sprk_keywords")]
    [InlineData("Document Type", "sprk_documenttype")]
    [InlineData("document type", "sprk_documenttype")]
    [InlineData("Entities", "sprk_entities")]
    [InlineData("entities", "sprk_entities")]
    public void GetFieldName_ValidOutputType_ReturnsCorrectFieldName(string outputTypeName, string expectedFieldName)
    {
        // Act
        var result = DocumentProfileFieldMapper.GetFieldName(outputTypeName);

        // Assert
        result.Should().Be(expectedFieldName);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("InvalidType")]
    [InlineData("")]
    public void GetFieldName_InvalidOutputType_ReturnsNull(string outputTypeName)
    {
        // Act
        var result = DocumentProfileFieldMapper.GetFieldName(outputTypeName);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetFieldName_NullOutputType_ReturnsNull()
    {
        // Act
        var result = DocumentProfileFieldMapper.GetFieldName(null!);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region PrepareValue Tests

    [Fact]
    public void PrepareValue_TextTypes_ReturnsValueAsIs()
    {
        // Arrange
        var value = "This is a summary";

        // Act
        var result = DocumentProfileFieldMapper.PrepareValue("Summary", value);

        // Assert
        result.Should().Be(value);
    }

    [Fact]
    public void PrepareValue_EntitiesWithValidJson_ReturnsJsonAsIs()
    {
        // Arrange
        var json = "{\"parties\":[\"Acme Corp\",\"Beta LLC\"],\"dates\":[\"2024-01-15\"]}";

        // Act
        var result = DocumentProfileFieldMapper.PrepareValue("Entities", json);

        // Assert
        result.Should().Be(json);
    }

    [Fact]
    public void PrepareValue_EntitiesWithInvalidJson_WrapsInStructure()
    {
        // Arrange
        var invalidJson = "Not a JSON string";

        // Act
        var result = DocumentProfileFieldMapper.PrepareValue("Entities", invalidJson);

        // Assert
        result.Should().NotBeNull();
        result!.ToString().Should().Contain("raw");
        result!.ToString().Should().Contain("Not a JSON string");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PrepareValue_NullOrWhitespace_ReturnsNull(string? value)
    {
        // Act
        var result = DocumentProfileFieldMapper.PrepareValue("Summary", value);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region CreateFieldMapping Tests

    [Fact]
    public void CreateFieldMapping_ValidOutputs_CreatesCorrectMapping()
    {
        // Arrange
        var outputs = new Dictionary<string, string?>
        {
            ["TL;DR"] = "Brief summary",
            ["Summary"] = "Detailed summary",
            ["Keywords"] = "contract, agreement, terms",
            ["Document Type"] = "Contract",
            ["Entities"] = "{\"parties\":[\"Acme Corp\"]}"
        };

        // Act
        var result = DocumentProfileFieldMapper.CreateFieldMapping(outputs);

        // Assert
        result.Should().HaveCount(5);
        result.Should().ContainKey("sprk_tldr");
        result.Should().ContainKey("sprk_summary");
        result.Should().ContainKey("sprk_keywords");
        result.Should().ContainKey("sprk_documenttype");
        result.Should().ContainKey("sprk_entities");

        result["sprk_tldr"].Should().Be("Brief summary");
        result["sprk_summary"].Should().Be("Detailed summary");
        result["sprk_keywords"].Should().Be("contract, agreement, terms");
        result["sprk_documenttype"].Should().Be("Contract");
        result["sprk_entities"].Should().Be("{\"parties\":[\"Acme Corp\"]}");
    }

    [Fact]
    public void CreateFieldMapping_EmptyOutputs_ReturnsEmptyDictionary()
    {
        // Arrange
        var outputs = new Dictionary<string, string?>();

        // Act
        var result = DocumentProfileFieldMapper.CreateFieldMapping(outputs);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void CreateFieldMapping_UnmappableOutputs_SkipsThem()
    {
        // Arrange
        var outputs = new Dictionary<string, string?>
        {
            ["TL;DR"] = "Brief summary",
            ["UnknownType"] = "Should be skipped",
            ["InvalidOutput"] = "Also skipped"
        };

        // Act
        var result = DocumentProfileFieldMapper.CreateFieldMapping(outputs);

        // Assert
        result.Should().HaveCount(1);
        result.Should().ContainKey("sprk_tldr");
        result.Should().NotContainKey("UnknownType");
        result.Should().NotContainKey("InvalidOutput");
    }

    [Fact]
    public void CreateFieldMapping_NullValues_SkipsThem()
    {
        // Arrange
        var outputs = new Dictionary<string, string?>
        {
            ["TL;DR"] = "Brief summary",
            ["Summary"] = null,
            ["Keywords"] = ""
        };

        // Act
        var result = DocumentProfileFieldMapper.CreateFieldMapping(outputs);

        // Assert
        result.Should().HaveCount(1);
        result.Should().ContainKey("sprk_tldr");
        result.Should().NotContainKey("sprk_summary");
        result.Should().NotContainKey("sprk_keywords");
    }

    #endregion

    #region IsMappable Tests

    [Theory]
    [InlineData("TL;DR", true)]
    [InlineData("Summary", true)]
    [InlineData("Keywords", true)]
    [InlineData("Document Type", true)]
    [InlineData("Entities", true)]
    [InlineData("UnknownType", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsMappable_ReturnsCorrectResult(string? outputTypeName, bool expected)
    {
        // Act
        var result = DocumentProfileFieldMapper.IsMappable(outputTypeName);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region SupportedOutputTypes Tests

    [Fact]
    public void SupportedOutputTypes_ContainsAllExpectedTypes()
    {
        // Act
        var types = DocumentProfileFieldMapper.SupportedOutputTypes;

        // Assert
        types.Should().HaveCount(5);
        types.Should().Contain("TL;DR");
        types.Should().Contain("Summary");
        types.Should().Contain("Keywords");
        types.Should().Contain("Document Type");
        types.Should().Contain("Entities");
    }

    #endregion
}
