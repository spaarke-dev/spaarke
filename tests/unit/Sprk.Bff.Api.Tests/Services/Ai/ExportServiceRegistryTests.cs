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
/// Unit tests for ExportServiceRegistry - service resolution by export format.
/// </summary>
public class ExportServiceRegistryTests
{
    private readonly Mock<ILogger<DocxExportService>> _docxLoggerMock;
    private readonly IOptions<AnalysisOptions> _options;

    public ExportServiceRegistryTests()
    {
        _docxLoggerMock = new Mock<ILogger<DocxExportService>>();
        _options = Options.Create(new AnalysisOptions());
    }

    private DocxExportService CreateDocxService() => new(_docxLoggerMock.Object, _options);

    #region GetService Tests

    [Fact]
    public void GetService_WithDocxFormat_ShouldReturnDocxService()
    {
        // Arrange
        var docxService = CreateDocxService();
        var services = new IExportService[] { docxService };
        var registry = new ExportServiceRegistry(services);

        // Act
        var result = registry.GetService(ExportFormat.Docx);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeOfType<DocxExportService>();
    }

    [Fact]
    public void GetService_WithUnsupportedFormat_ShouldReturnNull()
    {
        // Arrange
        var docxService = CreateDocxService();
        var services = new IExportService[] { docxService };
        var registry = new ExportServiceRegistry(services);

        // Act - Pdf is not registered
        var result = registry.GetService(ExportFormat.Pdf);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetService_WithNoServices_ShouldReturnNull()
    {
        // Arrange
        var services = Array.Empty<IExportService>();
        var registry = new ExportServiceRegistry(services);

        // Act
        var result = registry.GetService(ExportFormat.Docx);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetService_ShouldBeCaseInsensitiveForEnumValues()
    {
        // Arrange
        var docxService = CreateDocxService();
        var services = new IExportService[] { docxService };
        var registry = new ExportServiceRegistry(services);

        // Act
        var result = registry.GetService(ExportFormat.Docx);

        // Assert
        result.Should().NotBeNull();
        result!.Format.Should().Be(ExportFormat.Docx);
    }

    #endregion

    #region SupportedFormats Tests

    [Fact]
    public void SupportedFormats_ShouldReturnAllRegisteredFormats()
    {
        // Arrange
        var docxService = CreateDocxService();
        var services = new IExportService[] { docxService };
        var registry = new ExportServiceRegistry(services);

        // Act
        var formats = registry.SupportedFormats;

        // Assert
        formats.Should().Contain(ExportFormat.Docx);
    }

    [Fact]
    public void SupportedFormats_WithNoServices_ShouldReturnEmpty()
    {
        // Arrange
        var services = Array.Empty<IExportService>();
        var registry = new ExportServiceRegistry(services);

        // Act
        var formats = registry.SupportedFormats;

        // Assert
        formats.Should().BeEmpty();
    }

    #endregion

    #region IsSupported Tests

    [Fact]
    public void IsSupported_WithRegisteredFormat_ShouldReturnTrue()
    {
        // Arrange
        var docxService = CreateDocxService();
        var services = new IExportService[] { docxService };
        var registry = new ExportServiceRegistry(services);

        // Act
        var result = registry.IsSupported(ExportFormat.Docx);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsSupported_WithUnregisteredFormat_ShouldReturnFalse()
    {
        // Arrange
        var docxService = CreateDocxService();
        var services = new IExportService[] { docxService };
        var registry = new ExportServiceRegistry(services);

        // Act
        var result = registry.IsSupported(ExportFormat.Pdf);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithDuplicateFormats_ShouldUseLastRegistered()
    {
        // Arrange
        var service1 = CreateDocxService();
        var service2 = CreateDocxService();
        var services = new IExportService[] { service1, service2 };

        // Act - Should not throw, last one wins
        var registry = new ExportServiceRegistry(services);
        var result = registry.GetService(ExportFormat.Docx);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullServices_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var action = () => new ExportServiceRegistry(null!);
        action.Should().Throw<ArgumentNullException>();
    }

    #endregion
}
