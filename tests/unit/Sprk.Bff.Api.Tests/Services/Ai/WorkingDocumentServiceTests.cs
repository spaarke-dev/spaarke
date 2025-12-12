using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for WorkingDocumentService - working document state management.
/// Tests Phase 1 stub behavior and version tracking.
/// </summary>
public class WorkingDocumentServiceTests
{
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ILogger<WorkingDocumentService>> _loggerMock;

    public WorkingDocumentServiceTests()
    {
        _dataverseServiceMock = new Mock<IDataverseService>();
        _loggerMock = new Mock<ILogger<WorkingDocumentService>>();
    }

    private WorkingDocumentService CreateService(int maxWorkingVersions = 50)
    {
        var options = Options.Create(new AnalysisOptions
        {
            MaxWorkingVersions = maxWorkingVersions
        });
        return new WorkingDocumentService(_dataverseServiceMock.Object, options, _loggerMock.Object);
    }

    #region UpdateWorkingDocumentAsync Tests

    [Fact]
    public async Task UpdateWorkingDocumentAsync_Phase1_CompletesWithoutError()
    {
        // Arrange
        var service = CreateService();
        var analysisId = Guid.NewGuid();
        var content = "Updated working document content.";

        // Act
        var act = async () => await service.UpdateWorkingDocumentAsync(analysisId, content, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UpdateWorkingDocumentAsync_LargeContent_HandlesGracefully()
    {
        // Arrange
        var service = CreateService();
        var analysisId = Guid.NewGuid();
        var content = new string('X', 100000); // 100KB content

        // Act
        var act = async () => await service.UpdateWorkingDocumentAsync(analysisId, content, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UpdateWorkingDocumentAsync_EmptyContent_HandlesGracefully()
    {
        // Arrange
        var service = CreateService();
        var analysisId = Guid.NewGuid();

        // Act
        var act = async () => await service.UpdateWorkingDocumentAsync(analysisId, string.Empty, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region FinalizeAnalysisAsync Tests

    [Fact]
    public async Task FinalizeAnalysisAsync_Phase1_CompletesWithoutError()
    {
        // Arrange
        var service = CreateService();
        var analysisId = Guid.NewGuid();

        // Act
        var act = async () => await service.FinalizeAnalysisAsync(analysisId, 1000, 500, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FinalizeAnalysisAsync_ZeroTokens_CompletesWithoutError()
    {
        // Arrange
        var service = CreateService();
        var analysisId = Guid.NewGuid();

        // Act
        var act = async () => await service.FinalizeAnalysisAsync(analysisId, 0, 0, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FinalizeAnalysisAsync_LargeTokenCounts_CompletesWithoutError()
    {
        // Arrange
        var service = CreateService();
        var analysisId = Guid.NewGuid();

        // Act
        var act = async () => await service.FinalizeAnalysisAsync(analysisId, 128000, 64000, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region SaveToSpeAsync Tests

    [Fact]
    public async Task SaveToSpeAsync_Phase1_ReturnsStubResult()
    {
        // Arrange
        var service = CreateService();
        var analysisId = Guid.NewGuid();
        var fileName = "analysis-output.md";
        var content = System.Text.Encoding.UTF8.GetBytes("# Analysis Output");

        // Act
        var result = await service.SaveToSpeAsync(analysisId, fileName, content, "text/markdown", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.DocumentId.Should().NotBeEmpty();
        result.DriveId.Should().Be("stub-drive-id");
        result.ItemId.Should().Be("stub-item-id");
        result.WebUrl.Should().Contain(fileName);
    }

    [Fact]
    public async Task SaveToSpeAsync_DifferentContentTypes_ReturnsResult()
    {
        // Arrange
        var service = CreateService();
        var analysisId = Guid.NewGuid();
        var content = new byte[] { 0x50, 0x4B, 0x03, 0x04 }; // DOCX magic bytes

        // Act
        var result = await service.SaveToSpeAsync(
            analysisId,
            "output.docx",
            content,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.WebUrl.Should().Contain("output.docx");
    }

    [Fact]
    public async Task SaveToSpeAsync_EmptyContent_StillReturnsResult()
    {
        // Arrange
        var service = CreateService();
        var analysisId = Guid.NewGuid();

        // Act
        var result = await service.SaveToSpeAsync(analysisId, "empty.txt", [], "text/plain", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.DocumentId.Should().NotBeEmpty();
    }

    #endregion

    #region CreateWorkingVersionAsync Tests

    [Fact]
    public async Task CreateWorkingVersionAsync_FirstVersion_ReturnsVersionId()
    {
        // Arrange
        var service = CreateService();
        var analysisId = Guid.NewGuid();

        // Act
        var result = await service.CreateWorkingVersionAsync(analysisId, "Content v1", 100, CancellationToken.None);

        // Assert
        result.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateWorkingVersionAsync_MultipleVersions_ReturnsDifferentIds()
    {
        // Arrange
        var service = CreateService();
        var analysisId = Guid.NewGuid();

        // Act
        var v1 = await service.CreateWorkingVersionAsync(analysisId, "Content v1", 100, CancellationToken.None);
        var v2 = await service.CreateWorkingVersionAsync(analysisId, "Content v2", 50, CancellationToken.None);
        var v3 = await service.CreateWorkingVersionAsync(analysisId, "Content v3", -25, CancellationToken.None);

        // Assert
        v1.Should().NotBeEmpty();
        v2.Should().NotBeEmpty();
        v3.Should().NotBeEmpty();
        v1.Should().NotBe(v2);
        v2.Should().NotBe(v3);
    }

    [Fact]
    public async Task CreateWorkingVersionAsync_ExceedsMaxVersions_ReturnsEmptyGuid()
    {
        // Arrange - Set max to 3 versions
        var service = CreateService(maxWorkingVersions: 3);
        var analysisId = Guid.NewGuid();

        // Act - Create 4 versions (1 over limit)
        await service.CreateWorkingVersionAsync(analysisId, "V1", 0, CancellationToken.None);
        await service.CreateWorkingVersionAsync(analysisId, "V2", 0, CancellationToken.None);
        await service.CreateWorkingVersionAsync(analysisId, "V3", 0, CancellationToken.None);
        var fourthVersion = await service.CreateWorkingVersionAsync(analysisId, "V4", 0, CancellationToken.None);

        // Assert
        fourthVersion.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateWorkingVersionAsync_DifferentAnalyses_TrackVersionsSeparately()
    {
        // Arrange
        var service = CreateService(maxWorkingVersions: 2);
        var analysis1 = Guid.NewGuid();
        var analysis2 = Guid.NewGuid();

        // Act - Create 2 versions for each analysis
        var a1v1 = await service.CreateWorkingVersionAsync(analysis1, "A1V1", 0, CancellationToken.None);
        var a1v2 = await service.CreateWorkingVersionAsync(analysis1, "A1V2", 0, CancellationToken.None);
        var a2v1 = await service.CreateWorkingVersionAsync(analysis2, "A2V1", 0, CancellationToken.None);
        var a2v2 = await service.CreateWorkingVersionAsync(analysis2, "A2V2", 0, CancellationToken.None);

        // Assert - Both should succeed since they're separate analyses
        a1v1.Should().NotBeEmpty();
        a1v2.Should().NotBeEmpty();
        a2v1.Should().NotBeEmpty();
        a2v2.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateWorkingVersionAsync_NegativeTokenDelta_Allowed()
    {
        // Arrange
        var service = CreateService();
        var analysisId = Guid.NewGuid();

        // Act
        var result = await service.CreateWorkingVersionAsync(analysisId, "Shorter content", -500, CancellationToken.None);

        // Assert
        result.Should().NotBeEmpty();
    }

    #endregion
}

/// <summary>
/// Tests for SavedDocumentResult model.
/// </summary>
public class SavedDocumentResultTests
{
    [Fact]
    public void SavedDocumentResult_CanBeCreated()
    {
        // Arrange & Act
        var result = new SavedDocumentResult
        {
            DocumentId = Guid.NewGuid(),
            DriveId = "drive-123",
            ItemId = "item-456",
            WebUrl = "https://example.com/doc"
        };

        // Assert
        result.DocumentId.Should().NotBeEmpty();
        result.DriveId.Should().Be("drive-123");
        result.ItemId.Should().Be("item-456");
        result.WebUrl.Should().Be("https://example.com/doc");
    }
}
