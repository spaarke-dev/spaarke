using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for DocumentIntelligenceService JSON parsing functionality.
/// Tests the ParseStructuredResponse method and related fallback logic.
/// </summary>
public class DocumentIntelligenceServiceParsingTests
{
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<ITextExtractor> _textExtractorMock;
    private readonly Mock<ISpeFileOperations> _speFileOperationsMock;
    private readonly Mock<ILogger<DocumentIntelligenceService>> _loggerMock;
    private readonly DocumentIntelligenceService _service;

    public DocumentIntelligenceServiceParsingTests()
    {
        _openAiClientMock = new Mock<IOpenAiClient>();
        _textExtractorMock = new Mock<ITextExtractor>();
        _speFileOperationsMock = new Mock<ISpeFileOperations>();
        _loggerMock = new Mock<ILogger<DocumentIntelligenceService>>();
        var options = Options.Create(new DocumentIntelligenceOptions
        {
            StructuredOutputEnabled = true,
            StructuredAnalysisPromptTemplate = "Test prompt: {documentText}"
        });

        _service = new DocumentIntelligenceService(
            _openAiClientMock.Object,
            _textExtractorMock.Object,
            _speFileOperationsMock.Object,
            options,
            _loggerMock.Object);
    }

    #region ParseStructuredResponse - Valid JSON Tests

    [Fact]
    public void ParseStructuredResponse_ValidJson_ReturnsPopulatedResult()
    {
        // Arrange
        var validJson = """
            {
              "summary": "This is a contract between Acme Corp and Smith Associates.",
              "tldr": [
                "Legal services agreement effective January 15, 2025",
                "$450/hour rate with $5,000 monthly retainer"
              ],
              "keywords": "Acme Corporation, Smith Associates, legal services, contract",
              "entities": {
                "organizations": ["Acme Corporation", "Smith Associates"],
                "people": ["John Smith"],
                "amounts": ["$450 per hour", "$5,000 monthly retainer"],
                "dates": ["January 15, 2025"],
                "documentType": "contract",
                "references": ["M-2025-0042"]
              }
            }
            """;

        // Act
        var result = _service.ParseStructuredResponse(validJson);

        // Assert
        result.ParsedSuccessfully.Should().BeTrue();
        result.Summary.Should().Be("This is a contract between Acme Corp and Smith Associates.");
        result.TlDr.Should().HaveCount(2);
        result.TlDr[0].Should().Contain("Legal services agreement");
        result.Keywords.Should().Contain("Acme Corporation");
        result.Entities.Should().NotBeNull();
        result.Entities.Organizations.Should().Contain("Acme Corporation");
        result.Entities.People.Should().Contain("John Smith");
        result.Entities.Amounts.Should().Contain("$450 per hour");
        result.Entities.Dates.Should().Contain("January 15, 2025");
        result.Entities.DocumentType.Should().Be("contract");
        result.Entities.References.Should().Contain("M-2025-0042");
        result.RawResponse.Should().Be(validJson);
    }

    [Fact]
    public void ParseStructuredResponse_ValidJsonWithMarkdownCodeBlock_ReturnsPopulatedResult()
    {
        // Arrange - AI sometimes wraps JSON in markdown code blocks
        var jsonWithCodeBlock = """
            ```json
            {
              "summary": "Test summary",
              "tldr": ["Point 1"],
              "keywords": "test",
              "entities": {
                "organizations": [],
                "people": [],
                "amounts": [],
                "dates": [],
                "documentType": "other",
                "references": []
              }
            }
            ```
            """;

        // Act
        var result = _service.ParseStructuredResponse(jsonWithCodeBlock);

        // Assert
        result.ParsedSuccessfully.Should().BeTrue();
        result.Summary.Should().Be("Test summary");
    }

    [Fact]
    public void ParseStructuredResponse_MinimalValidJson_ReturnsResultWithDefaults()
    {
        // Arrange - Only required fields
        var minimalJson = """
            {
              "summary": "Brief summary"
            }
            """;

        // Act
        var result = _service.ParseStructuredResponse(minimalJson);

        // Assert
        result.ParsedSuccessfully.Should().BeTrue();
        result.Summary.Should().Be("Brief summary");
        result.TlDr.Should().BeEmpty();
        result.Keywords.Should().BeEmpty();
        result.Entities.Should().NotBeNull();
    }

    #endregion

    #region ParseStructuredResponse - Fallback Tests

    [Fact]
    public void ParseStructuredResponse_MalformedJson_ReturnsFallbackResult()
    {
        // Arrange
        var malformedJson = "{ invalid json without closing brace";

        // Act
        var result = _service.ParseStructuredResponse(malformedJson);

        // Assert
        result.ParsedSuccessfully.Should().BeFalse();
        result.Summary.Should().Be(malformedJson); // Raw text used as summary
        result.RawResponse.Should().Be(malformedJson);
    }

    [Fact]
    public void ParseStructuredResponse_EmptyResponse_ReturnsFallbackResult()
    {
        // Arrange
        var emptyResponse = "";

        // Act
        var result = _service.ParseStructuredResponse(emptyResponse);

        // Assert
        result.ParsedSuccessfully.Should().BeFalse();
        result.Summary.Should().BeEmpty();
    }

    [Fact]
    public void ParseStructuredResponse_WhitespaceOnly_ReturnsFallbackResult()
    {
        // Arrange
        var whitespaceResponse = "   \n\t   ";

        // Act
        var result = _service.ParseStructuredResponse(whitespaceResponse);

        // Assert
        result.ParsedSuccessfully.Should().BeFalse();
    }

    [Fact]
    public void ParseStructuredResponse_PlainTextResponse_ReturnsFallbackResult()
    {
        // Arrange - AI returns prose instead of JSON
        var proseResponse = "This document is a legal contract between two parties. It establishes terms for ongoing services.";

        // Act
        var result = _service.ParseStructuredResponse(proseResponse);

        // Assert
        result.ParsedSuccessfully.Should().BeFalse();
        result.Summary.Should().Be(proseResponse); // Prose becomes the summary
        result.RawResponse.Should().Be(proseResponse);
    }

    [Fact]
    public void ParseStructuredResponse_PartialJson_ReturnsFallbackResult()
    {
        // Arrange - Truncated JSON response
        var partialJson = """
            {
              "summary": "This is a summary",
              "tldr": ["Point 1",
            """;

        // Act
        var result = _service.ParseStructuredResponse(partialJson);

        // Assert
        result.ParsedSuccessfully.Should().BeFalse();
        result.Summary.Should().Be(partialJson);
    }

    [Fact]
    public void ParseStructuredResponse_JsonArray_ReturnsFallbackResult()
    {
        // Arrange - AI returns array instead of object
        var arrayJson = """["item1", "item2"]""";

        // Act
        var result = _service.ParseStructuredResponse(arrayJson);

        // Assert
        result.ParsedSuccessfully.Should().BeFalse();
    }

    #endregion

    #region ParseStructuredResponse - Edge Cases

    [Fact]
    public void ParseStructuredResponse_NullResponse_ReturnsFallbackResult()
    {
        // Act
        var result = _service.ParseStructuredResponse(null!);

        // Assert
        result.ParsedSuccessfully.Should().BeFalse();
    }

    [Fact]
    public void ParseStructuredResponse_CaseInsensitive_ParsesCorrectly()
    {
        // Arrange - JSON with different casing
        var mixedCaseJson = """
            {
              "Summary": "Test",
              "TLDR": ["Point"],
              "Keywords": "test",
              "Entities": {
                "Organizations": [],
                "People": [],
                "Amounts": [],
                "Dates": [],
                "DocumentType": "other",
                "References": []
              }
            }
            """;

        // Act
        var result = _service.ParseStructuredResponse(mixedCaseJson);

        // Assert
        result.ParsedSuccessfully.Should().BeTrue();
        result.Summary.Should().Be("Test");
    }

    [Fact]
    public void ParseStructuredResponse_RawResponseAlwaysPopulated()
    {
        // Arrange
        var validJson = """{"summary": "Test"}""";

        // Act
        var result = _service.ParseStructuredResponse(validJson);

        // Assert
        result.RawResponse.Should().Be(validJson);
    }

    #endregion
}

/// <summary>
/// Tests for DocumentAnalysisResult model factory methods.
/// </summary>
public class DocumentAnalysisResultTests
{
    [Fact]
    public void Success_CreatesPopulatedResult()
    {
        // Arrange
        var entities = new ExtractedEntities
        {
            Organizations = ["Acme Corp"],
            DocumentType = "contract"
        };

        // Act
        var result = DocumentAnalysisResult.Success(
            summary: "Test summary",
            tldr: ["Point 1", "Point 2"],
            keywords: "test, keywords",
            entities: entities,
            rawResponse: "raw json");

        // Assert
        result.ParsedSuccessfully.Should().BeTrue();
        result.Summary.Should().Be("Test summary");
        result.TlDr.Should().HaveCount(2);
        result.Keywords.Should().Be("test, keywords");
        result.Entities.Organizations.Should().Contain("Acme Corp");
        result.RawResponse.Should().Be("raw json");
    }

    [Fact]
    public void Fallback_CreatesFallbackResult()
    {
        // Act
        var result = DocumentAnalysisResult.Fallback("raw response", "fallback summary");

        // Assert
        result.ParsedSuccessfully.Should().BeFalse();
        result.Summary.Should().Be("fallback summary");
        result.RawResponse.Should().Be("raw response");
        result.TlDr.Should().BeEmpty();
        result.Keywords.Should().BeEmpty();
        result.Entities.Should().NotBeNull();
    }
}

/// <summary>
/// Tests for AnalysisChunk model with new Type and Result fields.
/// </summary>
public class AnalysisChunkStructuredTests
{
    [Fact]
    public void FromContent_HasTextType()
    {
        var chunk = AnalysisChunk.FromContent("streaming text");

        chunk.Type.Should().Be("text");
        chunk.Content.Should().Be("streaming text");
        chunk.Done.Should().BeFalse();
    }

    [Fact]
    public void Completed_WithString_HasCompleteType()
    {
        var chunk = AnalysisChunk.Completed("full summary");

        chunk.Type.Should().Be("complete");
        chunk.Done.Should().BeTrue();
        chunk.Summary.Should().Be("full summary");
    }

    [Fact]
    public void Completed_WithResult_HasCompleteTypeAndResult()
    {
        // Arrange
        var result = DocumentAnalysisResult.Success(
            summary: "Structured summary",
            tldr: ["Point 1"],
            keywords: "test",
            entities: new ExtractedEntities());

        // Act
        var chunk = AnalysisChunk.Completed(result);

        // Assert
        chunk.Type.Should().Be("complete");
        chunk.Done.Should().BeTrue();
        chunk.Summary.Should().Be("Structured summary");
        chunk.Result.Should().NotBeNull();
        chunk.Result!.ParsedSuccessfully.Should().BeTrue();
        chunk.Result.TlDr.Should().HaveCount(1);
    }

    [Fact]
    public void FromError_HasErrorType()
    {
        var chunk = AnalysisChunk.FromError("Something went wrong");

        chunk.Type.Should().Be("error");
        chunk.Done.Should().BeTrue();
        chunk.Error.Should().Be("Something went wrong");
    }
}

/// <summary>
/// Tests for ExtractedEntities model.
/// </summary>
public class ExtractedEntitiesTests
{
    [Fact]
    public void DefaultValues_AreEmptyArrays()
    {
        var entities = new ExtractedEntities();

        entities.Organizations.Should().BeEmpty();
        entities.People.Should().BeEmpty();
        entities.Amounts.Should().BeEmpty();
        entities.Dates.Should().BeEmpty();
        entities.References.Should().BeEmpty();
        entities.DocumentType.Should().Be("other");
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var entities = new ExtractedEntities
        {
            Organizations = ["Org1", "Org2"],
            People = ["Person1"],
            Amounts = ["$1,000"],
            Dates = ["2025-01-15"],
            DocumentType = "invoice",
            References = ["INV-001"]
        };

        entities.Organizations.Should().HaveCount(2);
        entities.People.Should().Contain("Person1");
        entities.Amounts.Should().Contain("$1,000");
        entities.Dates.Should().Contain("2025-01-15");
        entities.DocumentType.Should().Be("invoice");
        entities.References.Should().Contain("INV-001");
    }
}

/// <summary>
/// Tests for DocumentTypeMapper - maps AI document type strings to Dataverse choice values.
/// </summary>
public class DocumentTypeMapperTests
{
    [Theory]
    [InlineData("contract", 100000000)]
    [InlineData("invoice", 100000001)]
    [InlineData("proposal", 100000002)]
    [InlineData("report", 100000003)]
    [InlineData("letter", 100000004)]
    [InlineData("memo", 100000005)]
    [InlineData("email", 100000006)]
    [InlineData("agreement", 100000007)]
    [InlineData("statement", 100000008)]
    [InlineData("other", 100000009)]
    public void ToDataverseValue_KnownTypes_ReturnsCorrectValue(string aiType, int expectedValue)
    {
        var result = DocumentTypeMapper.ToDataverseValue(aiType);

        result.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData("CONTRACT")]
    [InlineData("Contract")]
    [InlineData("InVoIcE")]
    [InlineData("PROPOSAL")]
    public void ToDataverseValue_IsCaseInsensitive(string aiType)
    {
        var result = DocumentTypeMapper.ToDataverseValue(aiType);

        result.Should().NotBeNull();
        result.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("unknown_type")]
    [InlineData("document")]
    [InlineData("pdf")]
    [InlineData("spreadsheet")]
    public void ToDataverseValue_UnknownType_ReturnsOther(string aiType)
    {
        var result = DocumentTypeMapper.ToDataverseValue(aiType);

        result.Should().Be(DocumentTypeMapper.DefaultValue);
        result.Should().Be(100000009); // "Other"
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToDataverseValue_NullOrEmpty_ReturnsNull(string? aiType)
    {
        var result = DocumentTypeMapper.ToDataverseValue(aiType);

        result.Should().BeNull();
    }

    [Fact]
    public void ToDataverseValue_WhitespaceAroundValidType_ReturnsCorrectValue()
    {
        var result = DocumentTypeMapper.ToDataverseValue("  contract  ");

        result.Should().Be(100000000);
    }

    [Fact]
    public void DefaultValue_IsOther()
    {
        DocumentTypeMapper.DefaultValue.Should().Be(100000009);
    }
}
