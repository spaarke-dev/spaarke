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
    [InlineData("TL;DR", "sprk_filetldr")]
    [InlineData("tl;dr", "sprk_filetldr")]
    [InlineData("TL;dr", "sprk_filetldr")]
    [InlineData("Summary", "sprk_filesummary")]
    [InlineData("summary", "sprk_filesummary")]
    [InlineData("Keywords", "sprk_filekeywords")]
    [InlineData("keywords", "sprk_filekeywords")]
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
        result.Should().HaveCount(6);
        result.Should().ContainKey("sprk_filetldr");
        result.Should().ContainKey("sprk_filesummary");
        result.Should().ContainKey("sprk_filekeywords");
        result.Should().ContainKey("sprk_documenttype");
        result.Should().ContainKey("sprk_entities");
        result.Should().ContainKey("sprk_searchprofile");

        result["sprk_filetldr"].Should().Be("Brief summary");
        result["sprk_filesummary"].Should().Be("Detailed summary");
        result["sprk_filekeywords"].Should().Be("contract, agreement, terms");
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
        result.Should().ContainKey("sprk_filetldr");
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
        result.Should().ContainKey("sprk_filetldr");
        result.Should().NotContainKey("sprk_filesummary");
        result.Should().NotContainKey("sprk_filekeywords");
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
        types.Should().HaveCount(6);
        types.Should().Contain("TL;DR");
        types.Should().Contain("Summary");
        types.Should().Contain("Keywords");
        types.Should().Contain("Document Type");
        types.Should().Contain("Entities");
        types.Should().Contain("searchprofile");
    }

    #endregion

    #region BuildSearchProfile Tests

    [Fact]
    public void BuildSearchProfile_AllFieldsPresent_ReturnsFullProfile()
    {
        // Arrange
        var outputs = new Dictionary<string, string>
        {
            ["documenttype"] = "Contract",
            ["tldr"] = "A services agreement between two parties",
            ["entities"] = "[{\"name\":\"Acme Corp\",\"type\":\"Organization\"},{\"name\":\"Beta LLC\",\"type\":\"Organization\"}]",
            ["keywords"] = "services, agreement, consulting"
        };

        // Act
        var result = DocumentProfileFieldMapper.BuildSearchProfile(
            outputs,
            parentEntityName: "Acme Corp",
            parentEntityType: "account",
            fileName: "ServiceAgreement_2025.pdf");

        // Assert
        result.Should().NotBeNull();
        result.Should().Be("Contract | A services agreement between two parties | Acme Corp, Beta LLC | services, agreement, consulting | account: Acme Corp | ServiceAgreement_2025");
    }

    [Fact]
    public void BuildSearchProfile_PartialFields_ThreeOfSix_ReturnsProfile()
    {
        // Arrange
        var outputs = new Dictionary<string, string>
        {
            ["documenttype"] = "Invoice",
            ["keywords"] = "billing, payment"
        };

        // Act
        var result = DocumentProfileFieldMapper.BuildSearchProfile(
            outputs,
            fileName: "Invoice_001.xlsx");

        // Assert
        result.Should().NotBeNull();
        result.Should().Be("Invoice | billing, payment | Invoice_001");
    }

    [Fact]
    public void BuildSearchProfile_MinimalTwoParts_ReturnsProfile()
    {
        // Arrange
        var outputs = new Dictionary<string, string>
        {
            ["tldr"] = "Brief document description"
        };

        // Act
        var result = DocumentProfileFieldMapper.BuildSearchProfile(
            outputs,
            fileName: "report.pdf");

        // Assert
        result.Should().NotBeNull();
        result.Should().Be("Brief document description | report");
    }

    [Fact]
    public void BuildSearchProfile_OnlyOnePart_ReturnsNull()
    {
        // Arrange
        var outputs = new Dictionary<string, string>
        {
            ["tldr"] = "Brief document description"
        };

        // Act
        var result = DocumentProfileFieldMapper.BuildSearchProfile(outputs);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void BuildSearchProfile_EmptyDictionary_ReturnsNull()
    {
        // Arrange
        var outputs = new Dictionary<string, string>();

        // Act
        var result = DocumentProfileFieldMapper.BuildSearchProfile(outputs);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void BuildSearchProfile_MalformedEntityJson_SkipsEntities()
    {
        // Arrange
        var outputs = new Dictionary<string, string>
        {
            ["documenttype"] = "Contract",
            ["entities"] = "this is not valid JSON {{{",
            ["keywords"] = "legal, binding"
        };

        // Act
        var result = DocumentProfileFieldMapper.BuildSearchProfile(outputs);

        // Assert
        result.Should().NotBeNull();
        result.Should().Be("Contract | legal, binding");
        result.Should().NotContain("this is not valid JSON");
    }

    [Fact]
    public void BuildSearchProfile_LegacyKeyNames_AreRecognized()
    {
        // Arrange — uses "Document Type" and "TL;DR" (legacy) instead of "documenttype"/"tldr"
        var outputs = new Dictionary<string, string>
        {
            ["Document Type"] = "Memo",
            ["TL;DR"] = "Internal communication about Q3 goals"
        };

        // Act
        var result = DocumentProfileFieldMapper.BuildSearchProfile(outputs);

        // Assert
        result.Should().NotBeNull();
        result.Should().Be("Memo | Internal communication about Q3 goals");
    }

    [Fact]
    public void BuildSearchProfile_ParentEntityPartiallyProvided_Skipped()
    {
        // Arrange — only parentEntityType, no parentEntityName
        var outputs = new Dictionary<string, string>
        {
            ["documenttype"] = "Report",
            ["keywords"] = "quarterly, finance"
        };

        // Act
        var result = DocumentProfileFieldMapper.BuildSearchProfile(
            outputs,
            parentEntityType: "account");

        // Assert
        result.Should().NotBeNull();
        result.Should().Be("Report | quarterly, finance");
        result.Should().NotContain("account");
    }

    [Fact]
    public void BuildSearchProfile_FileNameWithoutExtension_HandledCorrectly()
    {
        // Arrange — file name with no extension
        var outputs = new Dictionary<string, string>
        {
            ["keywords"] = "notes"
        };

        // Act
        var result = DocumentProfileFieldMapper.BuildSearchProfile(
            outputs,
            fileName: "README");

        // Assert
        result.Should().NotBeNull();
        result.Should().Be("notes | README");
    }

    [Fact]
    public void BuildSearchProfile_EntitiesWithNoNameProperty_SkipsEntities()
    {
        // Arrange — valid JSON array but objects lack "name" property
        var outputs = new Dictionary<string, string>
        {
            ["documenttype"] = "Letter",
            ["entities"] = "[{\"type\":\"Organization\"},{\"type\":\"Person\"}]",
            ["keywords"] = "correspondence"
        };

        // Act
        var result = DocumentProfileFieldMapper.BuildSearchProfile(outputs);

        // Assert
        result.Should().NotBeNull();
        result.Should().Be("Letter | correspondence");
    }

    [Fact]
    public void BuildSearchProfile_WhitespaceValues_AreTrimmed()
    {
        // Arrange
        var outputs = new Dictionary<string, string>
        {
            ["documenttype"] = "  Contract  ",
            ["keywords"] = "  legal, binding  "
        };

        // Act
        var result = DocumentProfileFieldMapper.BuildSearchProfile(outputs);

        // Assert
        result.Should().NotBeNull();
        result.Should().Be("Contract | legal, binding");
    }

    #endregion
}
