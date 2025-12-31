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
/// Unit tests for PdfExportService - direct PDF generation using QuestPDF.
/// Tests service behavior, validation, and PDF generation.
/// </summary>
public class PdfExportServiceTests
{
    private readonly Mock<ILogger<PdfExportService>> _loggerMock;

    public PdfExportServiceTests()
    {
        _loggerMock = new Mock<ILogger<PdfExportService>>();

        // Configure QuestPDF license for tests
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    private PdfExportService CreateService(AnalysisOptions? options = null)
    {
        options ??= new AnalysisOptions { EnablePdfExport = true };

        return new PdfExportService(
            _loggerMock.Object,
            Options.Create(options));
    }

    private static ExportContext CreateValidContext(
        string? title = null,
        string? content = null,
        string? summary = null)
    {
        return new ExportContext
        {
            AnalysisId = Guid.NewGuid(),
            Title = title ?? "Test Analysis Report",
            Content = content ?? "This is the main analysis content.",
            Summary = summary ?? "Brief summary.",
            SourceDocumentName = "test-document.pdf",
            SourceDocumentId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "Test User"
        };
    }

    #region Format Property Tests

    [Fact]
    public void Format_ShouldReturnPdf()
    {
        // Arrange
        var service = CreateService();

        // Act
        var format = service.Format;

        // Assert
        format.Should().Be(ExportFormat.Pdf);
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
    public void Validate_WhenPdfExportDisabled_ShouldReturnError()
    {
        // Arrange
        var options = new AnalysisOptions { EnablePdfExport = false };
        var service = CreateService(options);
        var context = CreateValidContext();

        // Act
        var result = service.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not enabled"));
    }

    #endregion

    #region ExportAsync Tests

    [Fact]
    public async Task ExportAsync_WithValidContext_ShouldReturnPdfBytes()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext();

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.FileBytes.Should().NotBeNullOrEmpty();
        result.ContentType.Should().Be("application/pdf");
        result.FileName.Should().EndWith(".pdf");
    }

    [Fact]
    public async Task ExportAsync_ShouldGenerateValidPdf()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext();

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.FileBytes.Should().NotBeNull();
        result.FileBytes!.Should().HaveCountGreaterThan(100); // PDF should have meaningful content

        // PDF magic bytes: %PDF
        result.FileBytes!.Take(4).Should().BeEquivalentTo(new byte[] { 0x25, 0x50, 0x44, 0x46 });
    }

    [Fact]
    public async Task ExportAsync_WhenPdfExportDisabled_ShouldFail()
    {
        // Arrange
        var options = new AnalysisOptions { EnablePdfExport = false };
        var service = CreateService(options);
        var context = CreateValidContext();

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not enabled");
    }

    [Fact]
    public async Task ExportAsync_WithEntities_ShouldGeneratePdf()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext() with
        {
            Entities = new AnalysisEntities
            {
                People = ["John Doe", "Jane Smith"],
                Organizations = ["Acme Corp", "Tech Inc"],
                Dates = ["January 1, 2025"],
                Amounts = ["$100,000"],
                References = ["Contract #123"]
            }
        };

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.FileBytes.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExportAsync_WithClauses_ShouldGeneratePdf()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext() with
        {
            Clauses = new AnalysisClauses
            {
                Clauses =
                [
                    new ClauseInfo { Type = "Termination", Description = "30 days notice required", RiskLevel = "Low" },
                    new ClauseInfo { Type = "Liability", Description = "Unlimited liability", RiskLevel = "High" },
                    new ClauseInfo { Type = "Payment", Description = "Net 30 terms", RiskLevel = "Medium" }
                ]
            }
        };

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.FileBytes.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExportAsync_WithSpecialCharsInTitle_ShouldSanitizeFilename()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext(title: "Report <test> \"final\"");

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.FileName.Should().NotContain("<");
        result.FileName.Should().NotContain(">");
        result.FileName.Should().NotContain("\"");
        result.FileName.Should().EndWith(".pdf");
    }

    [Fact]
    public async Task ExportAsync_WithLongTitle_ShouldTruncateFilename()
    {
        // Arrange
        var service = CreateService();
        var longTitle = new string('A', 100); // Very long title
        var context = CreateValidContext(title: longTitle);

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        // Filename should be truncated to 50 chars + date + .pdf (~63 chars total)
        // Format: {title 50 chars}_{yyyyMMdd}.pdf
        result.FileName!.Length.Should().BeLessThan(70);
        result.FileName.Should().StartWith(new string('A', 50));
    }

    [Fact]
    public async Task ExportAsync_WithSummary_ShouldGeneratePdf()
    {
        // Arrange
        var service = CreateService();
        var context = CreateValidContext(
            summary: "This is a detailed executive summary that provides an overview of the analysis findings.");

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.FileBytes.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExportAsync_WithoutSummary_ShouldGeneratePdf()
    {
        // Arrange
        var service = CreateService();
        var context = new ExportContext
        {
            AnalysisId = Guid.NewGuid(),
            Title = "Test Report",
            Content = "Main content here.",
            Summary = null, // No summary
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.FileBytes.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExportAsync_FilenameIncludesDate()
    {
        // Arrange
        var service = CreateService();
        var specificDate = new DateTimeOffset(2025, 12, 29, 10, 30, 0, TimeSpan.Zero);
        var context = CreateValidContext() with { CreatedAt = specificDate };

        // Act
        var result = await service.ExportAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.FileName.Should().Contain("20251229");
    }

    #endregion
}
