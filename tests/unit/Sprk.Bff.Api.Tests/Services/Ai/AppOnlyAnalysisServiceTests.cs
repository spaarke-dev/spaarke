using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for AppOnlyAnalysisService - app-only document analysis.
/// Tests the document analysis flow with mocked dependencies.
/// </summary>
public class AppOnlyAnalysisServiceTests
{
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ISpeFileOperations> _speFileOperationsMock;
    private readonly Mock<ITextExtractor> _textExtractorMock;
    private readonly Mock<IPlaybookService> _playbookServiceMock;
    private readonly Mock<IScopeResolverService> _scopeResolverMock;
    private readonly Mock<IToolHandlerRegistry> _toolHandlerRegistryMock;
    private readonly Mock<ILogger<AppOnlyAnalysisService>> _loggerMock;
    private readonly AppOnlyAnalysisService _service;

    public AppOnlyAnalysisServiceTests()
    {
        _dataverseServiceMock = new Mock<IDataverseService>();
        _speFileOperationsMock = new Mock<ISpeFileOperations>();
        _textExtractorMock = new Mock<ITextExtractor>();
        _playbookServiceMock = new Mock<IPlaybookService>();
        _scopeResolverMock = new Mock<IScopeResolverService>();
        _toolHandlerRegistryMock = new Mock<IToolHandlerRegistry>();
        _loggerMock = new Mock<ILogger<AppOnlyAnalysisService>>();

        _service = new AppOnlyAnalysisService(
            _dataverseServiceMock.Object,
            _speFileOperationsMock.Object,
            _textExtractorMock.Object,
            _playbookServiceMock.Object,
            _scopeResolverMock.Object,
            _toolHandlerRegistryMock.Object,
            _loggerMock.Object);
    }

    #region Helper Methods

    private static DocumentEntity CreateDocument(Guid? id = null, string? graphDriveId = null, string? graphItemId = null)
    {
        return new DocumentEntity
        {
            Id = (id ?? Guid.NewGuid()).ToString(),
            Name = "Test Document.pdf",
            FileName = "Test Document.pdf",
            GraphDriveId = graphDriveId ?? "drive-123",
            GraphItemId = graphItemId ?? "item-456",
            Status = DocumentStatus.Active
        };
    }

    private static PlaybookResponse CreatePlaybook(Guid? id = null)
    {
        return new PlaybookResponse
        {
            Id = id ?? Guid.NewGuid(),
            Name = "Document Profile",
            Description = "Test playbook",
            IsPublic = true,
            ToolIds = [Guid.NewGuid()]
        };
    }

    private static ResolvedScopes CreateScopesWithTools()
    {
        var tools = new[]
        {
            new AnalysisTool
            {
                Id = Guid.NewGuid(),
                Name = "Summary",
                Type = ToolType.Summary,
                HandlerClass = "SummaryHandler"
            }
        };
        return new ResolvedScopes([], [], tools);
    }

    private void SetupSuccessfulAnalysisFlow(Guid documentId, DocumentEntity document)
    {
        // Setup document retrieval
        _dataverseServiceMock
            .Setup(x => x.GetDocumentAsync(documentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        // Setup text extraction support
        _textExtractorMock
            .Setup(x => x.IsSupported(It.IsAny<string>()))
            .Returns(true);

        // Setup file download
        var testStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Test document content"));
        _speFileOperationsMock
            .Setup(x => x.DownloadFileAsync(document.GraphDriveId!, document.GraphItemId!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(testStream);

        // Setup text extraction
        _textExtractorMock
            .Setup(x => x.ExtractAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TextExtractionResult
            {
                Success = true,
                Text = "Extracted text content from the document."
            });

        // Setup playbook service
        var playbook = CreatePlaybook();
        _playbookServiceMock
            .Setup(x => x.GetByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(playbook);

        // Setup scope resolver
        var scopes = CreateScopesWithTools();
        _scopeResolverMock
            .Setup(x => x.ResolvePlaybookScopesAsync(playbook.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scopes);

        // Setup tool handler registry to return no handlers (tools will be skipped)
        _toolHandlerRegistryMock
            .Setup(x => x.GetHandlersByType(It.IsAny<ToolType>()))
            .Returns(Array.Empty<IAnalysisToolHandler>());

        // Setup document update
        _dataverseServiceMock
            .Setup(x => x.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    #endregion

    #region AnalyzeDocumentAsync Tests

    [Fact]
    public async Task AnalyzeDocumentAsync_ValidDocument_CallsDataverseToGetDocument()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var document = CreateDocument(documentId);
        SetupSuccessfulAnalysisFlow(documentId, document);

        // Act
        var result = await _service.AnalyzeDocumentAsync(documentId);

        // Assert
        _dataverseServiceMock.Verify(
            x => x.GetDocumentAsync(documentId.ToString(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AnalyzeDocumentAsync_ValidDocument_DownloadsFileFromSpe()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var document = CreateDocument(documentId);
        SetupSuccessfulAnalysisFlow(documentId, document);

        // Act
        var result = await _service.AnalyzeDocumentAsync(documentId);

        // Assert
        _speFileOperationsMock.Verify(
            x => x.DownloadFileAsync(document.GraphDriveId!, document.GraphItemId!, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AnalyzeDocumentAsync_ValidDocument_ExtractsText()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var document = CreateDocument(documentId);
        SetupSuccessfulAnalysisFlow(documentId, document);

        // Act
        var result = await _service.AnalyzeDocumentAsync(documentId);

        // Assert
        _textExtractorMock.Verify(
            x => x.ExtractAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AnalyzeDocumentAsync_ValidDocument_LoadsPlaybook()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var document = CreateDocument(documentId);
        SetupSuccessfulAnalysisFlow(documentId, document);

        // Act
        var result = await _service.AnalyzeDocumentAsync(documentId);

        // Assert
        _playbookServiceMock.Verify(
            x => x.GetByNameAsync(AppOnlyAnalysisService.DefaultPlaybookName, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AnalyzeDocumentAsync_DocumentNotFound_ReturnsFailedResult()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        _dataverseServiceMock
            .Setup(x => x.GetDocumentAsync(documentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentEntity?)null);

        // Act
        var result = await _service.AnalyzeDocumentAsync(documentId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
        result.DocumentId.Should().Be(documentId);
    }

    [Fact]
    public async Task AnalyzeDocumentAsync_NoSpeFileReference_ReturnsFailedResult()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var document = new DocumentEntity
        {
            Id = documentId.ToString(),
            Name = "Test Document.pdf",
            GraphDriveId = null,  // No SPE reference
            GraphItemId = null
        };

        _dataverseServiceMock
            .Setup(x => x.GetDocumentAsync(documentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        _dataverseServiceMock
            .Setup(x => x.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.AnalyzeDocumentAsync(documentId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("no file reference");
    }

    [Fact]
    public async Task AnalyzeDocumentAsync_UnsupportedFileType_ReturnsFailedResult()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var document = CreateDocument(documentId);
        document.FileName = "document.xyz"; // Unsupported extension

        _dataverseServiceMock
            .Setup(x => x.GetDocumentAsync(documentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        _textExtractorMock
            .Setup(x => x.IsSupported(".xyz"))
            .Returns(false);

        _dataverseServiceMock
            .Setup(x => x.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.AnalyzeDocumentAsync(documentId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not supported");
    }

    [Fact]
    public async Task AnalyzeDocumentAsync_DownloadFails_ReturnsFailedResult()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var document = CreateDocument(documentId);

        _dataverseServiceMock
            .Setup(x => x.GetDocumentAsync(documentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        _textExtractorMock
            .Setup(x => x.IsSupported(It.IsAny<string>()))
            .Returns(true);

        _speFileOperationsMock
            .Setup(x => x.DownloadFileAsync(document.GraphDriveId!, document.GraphItemId!, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream?)null);

        _dataverseServiceMock
            .Setup(x => x.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.AnalyzeDocumentAsync(documentId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("download");
    }

    [Fact]
    public async Task AnalyzeDocumentAsync_TextExtractionFails_ReturnsFailedResult()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var document = CreateDocument(documentId);

        _dataverseServiceMock
            .Setup(x => x.GetDocumentAsync(documentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        _textExtractorMock
            .Setup(x => x.IsSupported(It.IsAny<string>()))
            .Returns(true);

        var testStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Test"));
        _speFileOperationsMock
            .Setup(x => x.DownloadFileAsync(document.GraphDriveId!, document.GraphItemId!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(testStream);

        _textExtractorMock
            .Setup(x => x.ExtractAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TextExtractionResult
            {
                Success = false,
                ErrorMessage = "Failed to extract text"
            });

        _dataverseServiceMock
            .Setup(x => x.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.AnalyzeDocumentAsync(documentId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("extract");
    }

    [Fact]
    public async Task AnalyzeDocumentAsync_PlaybookNotFound_ReturnsFailedResult()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var document = CreateDocument(documentId);

        _dataverseServiceMock
            .Setup(x => x.GetDocumentAsync(documentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        _textExtractorMock
            .Setup(x => x.IsSupported(It.IsAny<string>()))
            .Returns(true);

        var testStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Test"));
        _speFileOperationsMock
            .Setup(x => x.DownloadFileAsync(document.GraphDriveId!, document.GraphItemId!, It.IsAny<CancellationToken>()))
            .ReturnsAsync(testStream);

        _textExtractorMock
            .Setup(x => x.ExtractAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TextExtractionResult
            {
                Success = true,
                Text = "Extracted text"
            });

        _playbookServiceMock
            .Setup(x => x.GetByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Playbook not found"));

        _dataverseServiceMock
            .Setup(x => x.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.AnalyzeDocumentAsync(documentId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Playbook");
    }

    [Fact]
    public async Task AnalyzeDocumentAsync_WithCustomPlaybookName_LoadsSpecifiedPlaybook()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var document = CreateDocument(documentId);
        var customPlaybookName = "Custom Analysis";
        SetupSuccessfulAnalysisFlow(documentId, document);

        // Override playbook service to verify custom name
        _playbookServiceMock
            .Setup(x => x.GetByNameAsync(customPlaybookName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePlaybook());

        // Act
        var result = await _service.AnalyzeDocumentAsync(documentId, customPlaybookName);

        // Assert
        _playbookServiceMock.Verify(
            x => x.GetByNameAsync(customPlaybookName, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AnalyzeDocumentAsync_UpdatesSummaryStatusToPending()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var document = CreateDocument(documentId);
        SetupSuccessfulAnalysisFlow(documentId, document);

        // Act
        var result = await _service.AnalyzeDocumentAsync(documentId);

        // Assert - Verify status was set to pending (100000001) during processing
        _dataverseServiceMock.Verify(
            x => x.UpdateDocumentAsync(
                documentId.ToString(),
                It.Is<UpdateDocumentRequest>(r => r.SummaryStatus == 100000001),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region AnalyzeDocumentFromStreamAsync Tests

    [Fact]
    public async Task AnalyzeDocumentFromStreamAsync_ValidStream_ExtractsText()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var fileName = "test.pdf";
        var fileStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Test content"));

        _textExtractorMock
            .Setup(x => x.IsSupported(".pdf"))
            .Returns(true);

        _textExtractorMock
            .Setup(x => x.ExtractAsync(It.IsAny<Stream>(), fileName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TextExtractionResult
            {
                Success = true,
                Text = "Extracted text"
            });

        var playbook = CreatePlaybook();
        _playbookServiceMock
            .Setup(x => x.GetByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(playbook);

        _scopeResolverMock
            .Setup(x => x.ResolvePlaybookScopesAsync(playbook.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateScopesWithTools());

        _toolHandlerRegistryMock
            .Setup(x => x.GetHandlersByType(It.IsAny<ToolType>()))
            .Returns(Array.Empty<IAnalysisToolHandler>());

        _dataverseServiceMock
            .Setup(x => x.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.AnalyzeDocumentFromStreamAsync(documentId, fileName, fileStream);

        // Assert
        _textExtractorMock.Verify(
            x => x.ExtractAsync(It.IsAny<Stream>(), fileName, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AnalyzeDocumentFromStreamAsync_UnsupportedFileType_ReturnsFailedResult()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var fileName = "test.xyz";
        var fileStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Test content"));

        _textExtractorMock
            .Setup(x => x.IsSupported(".xyz"))
            .Returns(false);

        // Act
        var result = await _service.AnalyzeDocumentFromStreamAsync(documentId, fileName, fileStream);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not supported");
    }

    [Fact]
    public async Task AnalyzeDocumentFromStreamAsync_DoesNotDownloadFromSpe()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var fileName = "test.pdf";
        var fileStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Test content"));

        _textExtractorMock
            .Setup(x => x.IsSupported(".pdf"))
            .Returns(true);

        _textExtractorMock
            .Setup(x => x.ExtractAsync(It.IsAny<Stream>(), fileName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TextExtractionResult
            {
                Success = true,
                Text = "Extracted text"
            });

        var playbook = CreatePlaybook();
        _playbookServiceMock
            .Setup(x => x.GetByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(playbook);

        _scopeResolverMock
            .Setup(x => x.ResolvePlaybookScopesAsync(playbook.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateScopesWithTools());

        _toolHandlerRegistryMock
            .Setup(x => x.GetHandlersByType(It.IsAny<ToolType>()))
            .Returns(Array.Empty<IAnalysisToolHandler>());

        _dataverseServiceMock
            .Setup(x => x.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.AnalyzeDocumentFromStreamAsync(documentId, fileName, fileStream);

        // Assert - Should NOT call SPE download
        _speFileOperationsMock.Verify(
            x => x.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region DocumentAnalysisResult Tests

    [Fact]
    public void DocumentAnalysisResult_Success_CreatesSuccessfulResult()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var profileUpdate = new UpdateDocumentRequest { Summary = "Test summary" };

        // Act
        var result = Sprk.Bff.Api.Services.Ai.DocumentAnalysisResult.Success(documentId, profileUpdate);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.DocumentId.Should().Be(documentId);
        result.ProfileUpdate.Should().Be(profileUpdate);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void DocumentAnalysisResult_Failed_CreatesFailedResult()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var errorMessage = "Analysis failed";

        // Act
        var result = Sprk.Bff.Api.Services.Ai.DocumentAnalysisResult.Failed(documentId, errorMessage);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.DocumentId.Should().Be(documentId);
        result.ErrorMessage.Should().Be(errorMessage);
        result.ProfileUpdate.Should().BeNull();
    }

    #endregion
}
