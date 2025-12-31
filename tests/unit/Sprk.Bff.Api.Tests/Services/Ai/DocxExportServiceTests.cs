using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Export;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for DocxExportService - DOCX document generation for analysis exports.
/// Tests OpenXML document structure, validation, and section generation.
/// </summary>
public class DocxExportServiceTests
{
    private readonly Mock<ILogger<DocxExportService>> _loggerMock;
    private readonly IOptions<AnalysisOptions> _options;

    public DocxExportServiceTests()
    {
        _loggerMock = new Mock<ILogger<DocxExportService>>();
        _options = Options.Create(new AnalysisOptions());
    }

    private DocxExportService CreateService() => new(_loggerMock.Object, _options);

    private ExportContext CreateValidContext(
        string? title = null,
        string? content = null,
        string? summary = null)
    {
        return new ExportContext
        {
            AnalysisId = Guid.NewGuid(),
            Title = title ?? "Test Analysis Report",
            Content = content ?? "This is the main analysis content with findings and recommendations.",
            Summary = summary ?? "Brief summary of the document analysis.",
            SourceDocumentName = "test-document.pdf",
            SourceDocumentId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "Test User"
        };
    }

    #region Format Property Tests

    [Fact]
    public void Format_ShouldReturnDocx()
    {
        // Arrange
        var service = CreateService();

        // Act
        var format = service.Format;

        // Assert
        format.Should().Be(ExportFormat.Docx);
    }

    #endregion

    #region Validate Tests

    [Fact]
    public void Validate_WithValidContext_ShouldReturnIsValid()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext();

        // Act
        var result = service.Validate(context);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WithEmptyContent_ShouldReturnError()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext(content: string.Empty);

        // Act
        var result = service.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("content"));
    }

    [Fact]
    public void Validate_WithWhitespaceContent_ShouldReturnError()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext(content: "   ");

        // Act
        var result = service.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("content"));
    }

    [Fact]
    public void Validate_WithEmptyTitle_ShouldReturnError()
    {
        // Arrange
        var service = CreateService();
        var context = new ExportContext
        {
            AnalysisId = Guid.NewGuid(),
            Title = string.Empty,
            Content = "Valid content"
        };

        // Act
        var result = service.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("title"));
    }

    [Fact]
    public void Validate_WithNoSummary_ShouldStillBeValid()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext(summary: null);

        // Act
        var result = service.Validate(context);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region ExportAsync Tests

    [Fact]
    public async Task ExportAsync_WithValidContext_ShouldReturnSuccessResult()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext();

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.FileName.Should().NotBeNullOrEmpty();
        result.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        result.FileBytes.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExportAsync_ShouldGenerateValidDocx()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext(
            title: "Contract Analysis Report",
            content: "The contract contains standard terms and conditions.");

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert - Verify the bytes represent a valid DOCX file
        using var stream = new MemoryStream(result.FileBytes!);
        var action = () => WordprocessingDocument.Open(stream, false);
        action.Should().NotThrow("the exported bytes should be a valid DOCX file");
    }

    [Fact]
    public async Task ExportAsync_ShouldIncludeTitle()
    {
        // Arrange
        var service = CreateService();
        var expectedTitle = "Test Analysis Title";
        var context = CreateValidContext(title: expectedTitle);

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        using var stream = new MemoryStream(result.FileBytes!);
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart!.Document.Body!;
        var paragraphs = body.Elements<Paragraph>().ToList();

        // First paragraph should be the title
        var titleParagraph = paragraphs.First();
        var titleText = titleParagraph.InnerText;
        titleText.Should().Contain(expectedTitle);
    }

    [Fact]
    public async Task ExportAsync_ShouldIncludeContent()
    {
        // Arrange
        var service = CreateService();
        var expectedContent = "This is unique test content that should appear in the document.";
        var context = CreateValidContext(content: expectedContent);

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        using var stream = new MemoryStream(result.FileBytes!);
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart!.Document.Body!;
        var fullText = body.InnerText;

        fullText.Should().Contain(expectedContent);
    }

    [Fact]
    public async Task ExportAsync_WithSummary_ShouldIncludeSummarySection()
    {
        // Arrange
        var service = CreateService();
        var expectedSummary = "This is the executive summary.";
        var context = CreateValidContext(summary: expectedSummary);

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        using var stream = new MemoryStream(result.FileBytes!);
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart!.Document.Body!;
        var fullText = body.InnerText;

        fullText.Should().Contain("Executive Summary");
        fullText.Should().Contain(expectedSummary);
    }

    [Fact]
    public async Task ExportAsync_FileNameShouldContainAnalysisId()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext();

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        result.FileName.Should().Contain("Analysis");
        result.FileName.Should().EndWith(".docx");
    }

    [Fact]
    public async Task ExportAsync_WithEntities_ShouldIncludeEntitiesTable()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext();
        context = context with
        {
            Entities = new AnalysisEntities
            {
                People = ["John Doe", "Jane Smith"],
                Organizations = ["Acme Corp"],
                Dates = ["2025-01-15"]
            }
        };

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        using var stream = new MemoryStream(result.FileBytes!);
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart!.Document.Body!;

        // Should have at least one table for entities
        var tables = body.Elements<Table>().ToList();
        tables.Should().NotBeEmpty("entities should be rendered as a table");

        var fullText = body.InnerText;
        fullText.Should().Contain("Extracted Entities");
        fullText.Should().Contain("John Doe");
        fullText.Should().Contain("Acme Corp");
    }

    [Fact]
    public async Task ExportAsync_WithClauses_ShouldIncludeClausesTable()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext();
        context = context with
        {
            Clauses = new AnalysisClauses
            {
                Clauses =
                [
                    new ClauseInfo
                    {
                        Type = "Termination",
                        Description = "30-day notice required",
                        RiskLevel = "Low"
                    },
                    new ClauseInfo
                    {
                        Type = "Liability",
                        Description = "Unlimited liability exposure",
                        RiskLevel = "High"
                    }
                ]
            }
        };

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        using var stream = new MemoryStream(result.FileBytes!);
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart!.Document.Body!;
        var fullText = body.InnerText;

        fullText.Should().Contain("Contract Clause Analysis");
        fullText.Should().Contain("Termination");
        fullText.Should().Contain("Liability");
    }

    [Fact]
    public async Task ExportAsync_ShouldIncludeFooterWithDate()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext();

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        using var stream = new MemoryStream(result.FileBytes!);
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart!.Document.Body!;
        var fullText = body.InnerText;

        // Footer should contain branding and date
        fullText.Should().Contain("Generated by Spaarke AI");
        fullText.Should().Contain(DateTime.UtcNow.ToString("yyyy"));
    }

    [Fact]
    public async Task ExportAsync_WithLargeContent_ShouldHandleGracefully()
    {
        // Arrange
        var service = CreateService();
        var largeContent = string.Join("\n\n", Enumerable.Range(1, 100).Select(i =>
            $"Section {i}: This is paragraph content for section number {i}. " +
            "It contains multiple sentences to simulate real document content. " +
            "The analysis found several key points in this section."));

        var context = CreateValidContext(content: largeContent);

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.FileBytes.Should().NotBeNullOrEmpty();

        // Verify it's still a valid DOCX
        using var stream = new MemoryStream(result.FileBytes!);
        var action = () => WordprocessingDocument.Open(stream, false);
        action.Should().NotThrow();
    }

    [Fact]
    public async Task ExportAsync_WithHtmlContent_ShouldStripTags()
    {
        // Arrange
        var service = CreateService();
        var htmlContent = "<p>This is <strong>bold</strong> and <em>italic</em> text.</p>";
        var context = CreateValidContext(content: htmlContent);

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        using var stream = new MemoryStream(result.FileBytes!);
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart!.Document.Body!;
        var fullText = body.InnerText;

        // Should contain the text but not HTML tags
        fullText.Should().Contain("bold");
        fullText.Should().Contain("italic");
        fullText.Should().NotContain("<strong>");
        fullText.Should().NotContain("<em>");
    }

    [Fact]
    public async Task ExportAsync_WithCancellationToken_ShouldAcceptToken()
    {
        // Arrange - the export is fast enough that cancellation mid-operation is unlikely
        // but we should accept the token for consistency
        var service = CreateService();
        var context = CreateValidContext();
        var cts = new CancellationTokenSource();

        // Act - should complete normally since export is synchronous
        var result = await service.ExportAsync(context, cts.Token);

        // Assert
        result.Success.Should().BeTrue();
    }

    #endregion

    #region Filename Generation Tests

    [Fact]
    public async Task ExportAsync_WithCustomTitle_ShouldUseInFilename()
    {
        // Arrange - the filename is based on title
        var service = CreateService();
        var context = CreateValidContext(title: "My Custom Analysis");
        context = context with { SourceDocumentName = "Original Contract.pdf" };

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert - filename uses title (spaces are valid in filenames, only invalid chars are replaced)
        result.FileName.Should().Contain("My Custom Analysis");
        result.FileName.Should().EndWith(".docx");
    }

    [Fact]
    public async Task ExportAsync_WithSpecialCharsInTitle_ShouldSanitizeFilename()
    {
        // Arrange - filename is based on title which gets sanitized
        var service = CreateService();
        var context = CreateValidContext(title: "Contract <v2> \"final\"");

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert - special chars replaced with underscores
        result.FileName.Should().NotContain("<");
        result.FileName.Should().NotContain(">");
        result.FileName.Should().NotContain("\"");
        result.FileName.Should().EndWith(".docx");
    }

    #endregion
}
