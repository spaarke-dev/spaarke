using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Export;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for AnalysisOrchestrationService - main analysis orchestration.
/// Tests the coordination logic between Dataverse, SPE, and OpenAI.
/// </summary>
public class AnalysisOrchestrationServiceTests
{
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ISpeFileOperations> _speFileOperationsMock;
    private readonly Mock<ITextExtractor> _textExtractorMock;
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<IScopeResolverService> _scopeResolverMock;
    private readonly Mock<IAnalysisContextBuilder> _contextBuilderMock;
    private readonly Mock<IWorkingDocumentService> _workingDocumentServiceMock;
    private readonly ExportServiceRegistry _exportRegistry;
    private readonly Mock<IRagService> _ragServiceMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly Mock<ILogger<AnalysisOrchestrationService>> _loggerMock;
    private readonly IOptions<AnalysisOptions> _options;
    private readonly AnalysisOrchestrationService _service;
    private readonly HttpContext _mockHttpContext;

    public AnalysisOrchestrationServiceTests()
    {
        _dataverseServiceMock = new Mock<IDataverseService>();
        _speFileOperationsMock = new Mock<ISpeFileOperations>();
        _textExtractorMock = new Mock<ITextExtractor>();
        _openAiClientMock = new Mock<IOpenAiClient>();
        _scopeResolverMock = new Mock<IScopeResolverService>();
        _contextBuilderMock = new Mock<IAnalysisContextBuilder>();
        _workingDocumentServiceMock = new Mock<IWorkingDocumentService>();
        _ragServiceMock = new Mock<IRagService>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _loggerMock = new Mock<ILogger<AnalysisOrchestrationService>>();
        _mockHttpContext = new DefaultHttpContext();

        _options = Options.Create(new AnalysisOptions
        {
            Enabled = true,
            MaxChatHistoryMessages = 10
        });

        // Create export registry with empty services for basic tests
        _exportRegistry = new ExportServiceRegistry(Array.Empty<IExportService>());

        _service = new AnalysisOrchestrationService(
            _dataverseServiceMock.Object,
            _speFileOperationsMock.Object,
            _textExtractorMock.Object,
            _openAiClientMock.Object,
            _scopeResolverMock.Object,
            _contextBuilderMock.Object,
            _workingDocumentServiceMock.Object,
            _exportRegistry,
            _ragServiceMock.Object,
            _httpContextAccessorMock.Object,
            _options,
            _loggerMock.Object);
    }

    private static AnalysisExecuteRequest CreateRequest(Guid? documentId = null, Guid? actionId = null) => new()
    {
        DocumentIds = [documentId ?? Guid.NewGuid()],
        ActionId = actionId ?? Guid.NewGuid(),
        OutputType = AnalysisOutputType.Document
    };

    private static DocumentEntity CreateDocument(Guid? id = null) => new()
    {
        Id = (id ?? Guid.NewGuid()).ToString(),
        Name = "Test Document.pdf",
        Status = DocumentStatus.Active
    };

    private static AnalysisAction CreateAction() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Summarize",
        SystemPrompt = "You are a helpful assistant."
    };

    private static ResolvedScopes CreateEmptyScopes() => new([], [], []);

    #region ExecuteAnalysisAsync Tests

    [Fact]
    public async Task ExecuteAnalysisAsync_Success_YieldsMetadataChunksAndCompleted()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var request = CreateRequest(documentId);
        var document = CreateDocument(documentId);
        var action = CreateAction();
        var scopes = CreateEmptyScopes();

        SetupMocks(documentId, document, action, scopes);
        SetupOpenAiStreaming(["Hello ", "World!"]);

        // Act
        var chunks = new List<AnalysisStreamChunk>();
        await foreach (var chunk in _service.ExecuteAnalysisAsync(request, _mockHttpContext, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Should().HaveCountGreaterOrEqualTo(3); // metadata + at least 1 chunk + done
        chunks[0].Type.Should().Be("metadata");
        chunks[0].AnalysisId.Should().NotBeNull();
        chunks[0].DocumentName.Should().Be("Test Document.pdf");
        chunks.Last().Type.Should().Be("done");
        chunks.Last().Done.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAnalysisAsync_DocumentNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var request = CreateRequest();
        _dataverseServiceMock
            .Setup(x => x.GetDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentEntity?)null);

        // Act & Assert
        var chunks = new List<AnalysisStreamChunk>();
        var act = async () =>
        {
            await foreach (var chunk in _service.ExecuteAnalysisAsync(request, _mockHttpContext, CancellationToken.None))
            {
                chunks.Add(chunk);
            }
        };

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task ExecuteAnalysisAsync_ActionNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var request = CreateRequest(documentId);
        var document = CreateDocument(documentId);

        _dataverseServiceMock
            .Setup(x => x.GetDocumentAsync(documentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        _scopeResolverMock
            .Setup(x => x.ResolveScopesAsync(It.IsAny<Guid[]>(), It.IsAny<Guid[]>(), It.IsAny<Guid[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEmptyScopes());

        _scopeResolverMock
            .Setup(x => x.GetActionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AnalysisAction?)null);

        // Act & Assert
        var chunks = new List<AnalysisStreamChunk>();
        var act = async () =>
        {
            await foreach (var chunk in _service.ExecuteAnalysisAsync(request, _mockHttpContext, CancellationToken.None))
            {
                chunks.Add(chunk);
            }
        };

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*Action*not found*");
    }

    [Fact]
    public async Task ExecuteAnalysisAsync_WithPlaybook_ResolvesPlaybookScopes()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();
        var request = new AnalysisExecuteRequest
        {
            DocumentIds = [documentId],
            ActionId = Guid.NewGuid(),
            PlaybookId = playbookId,
            OutputType = AnalysisOutputType.Document
        };
        var document = CreateDocument(documentId);
        var action = CreateAction();
        var scopes = CreateEmptyScopes();

        _dataverseServiceMock
            .Setup(x => x.GetDocumentAsync(documentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        _scopeResolverMock
            .Setup(x => x.ResolvePlaybookScopesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scopes);

        _scopeResolverMock
            .Setup(x => x.GetActionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(action);

        SetupContextBuilder(action, scopes);
        SetupOpenAiStreaming(["Response"]);
        SetupWorkingDocumentService();

        // Act
        var chunks = new List<AnalysisStreamChunk>();
        await foreach (var chunk in _service.ExecuteAnalysisAsync(request, _mockHttpContext, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        // Assert
        _scopeResolverMock.Verify(
            x => x.ResolvePlaybookScopesAsync(playbookId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAnalysisAsync_StreamsContentChunks()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var request = CreateRequest(documentId);
        var document = CreateDocument(documentId);
        var action = CreateAction();
        var scopes = CreateEmptyScopes();

        SetupMocks(documentId, document, action, scopes);
        SetupOpenAiStreaming(["Chunk1", "Chunk2", "Chunk3"]);

        // Act
        var chunks = new List<AnalysisStreamChunk>();
        await foreach (var chunk in _service.ExecuteAnalysisAsync(request, _mockHttpContext, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        // Assert
        var contentChunks = chunks.Where(c => c.Type == "chunk").ToList();
        contentChunks.Should().HaveCount(3);
        contentChunks[0].Content.Should().Be("Chunk1");
        contentChunks[1].Content.Should().Be("Chunk2");
        contentChunks[2].Content.Should().Be("Chunk3");
    }

    [Fact]
    public async Task ExecuteAnalysisAsync_CompletedChunk_ContainsTokenUsage()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var request = CreateRequest(documentId);
        var document = CreateDocument(documentId);
        var action = CreateAction();
        var scopes = CreateEmptyScopes();

        SetupMocks(documentId, document, action, scopes);
        SetupOpenAiStreaming(["Short response"]);

        // Act
        var chunks = new List<AnalysisStreamChunk>();
        await foreach (var chunk in _service.ExecuteAnalysisAsync(request, _mockHttpContext, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        // Assert
        var doneChunk = chunks.Last();
        doneChunk.TokenUsage.Should().NotBeNull();
        doneChunk.TokenUsage!.Input.Should().BeGreaterThan(0);
        doneChunk.TokenUsage.Output.Should().BeGreaterThan(0);
    }

    #endregion

    #region ContinueAnalysisAsync Tests

    [Fact]
    public async Task ContinueAnalysisAsync_AnalysisNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var analysisId = Guid.NewGuid();

        // Act & Assert
        var chunks = new List<AnalysisStreamChunk>();
        var act = async () =>
        {
            await foreach (var chunk in _service.ContinueAnalysisAsync(analysisId, "Continue", _mockHttpContext, CancellationToken.None))
            {
                chunks.Add(chunk);
            }
        };

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*{analysisId}*not found*");
    }

    #endregion

    #region SaveWorkingDocumentAsync Tests

    [Fact]
    public async Task SaveWorkingDocumentAsync_AnalysisNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var analysisId = Guid.NewGuid();
        var request = new AnalysisSaveRequest
        {
            FileName = "output.md",
            Format = SaveDocumentFormat.Md
        };

        // Act & Assert
        var act = async () => await _service.SaveWorkingDocumentAsync(analysisId, request, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*{analysisId}*not found*");
    }

    #endregion

    #region ExportAnalysisAsync Tests

    [Fact]
    public async Task ExportAnalysisAsync_AnalysisNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var analysisId = Guid.NewGuid();
        var request = new AnalysisExportRequest
        {
            Format = ExportFormat.Email
        };

        // Act & Assert
        var act = async () => await _service.ExportAnalysisAsync(analysisId, request, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*{analysisId}*not found*");
    }

    #endregion

    #region GetAnalysisAsync Tests

    [Fact]
    public async Task GetAnalysisAsync_AnalysisNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        var analysisId = Guid.NewGuid();

        // Act & Assert
        var act = async () => await _service.GetAnalysisAsync(analysisId, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage($"*{analysisId}*not found*");
    }

    #endregion

    #region Helper Methods

    private void SetupMocks(Guid documentId, DocumentEntity document, AnalysisAction action, ResolvedScopes scopes)
    {
        _dataverseServiceMock
            .Setup(x => x.GetDocumentAsync(documentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);

        _scopeResolverMock
            .Setup(x => x.ResolveScopesAsync(It.IsAny<Guid[]>(), It.IsAny<Guid[]>(), It.IsAny<Guid[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(scopes);

        _scopeResolverMock
            .Setup(x => x.GetActionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(action);

        SetupContextBuilder(action, scopes);
        SetupWorkingDocumentService();
    }

    private void SetupContextBuilder(AnalysisAction action, ResolvedScopes scopes)
    {
        _contextBuilderMock
            .Setup(x => x.BuildSystemPrompt(action, scopes.Skills))
            .Returns("System prompt");

        _contextBuilderMock
            .Setup(x => x.BuildUserPromptAsync(It.IsAny<string>(), scopes.Knowledge, It.IsAny<CancellationToken>()))
            .ReturnsAsync("User prompt");
    }

    private void SetupOpenAiStreaming(IEnumerable<string> tokens)
    {
        _openAiClientMock
            .Setup(x => x.StreamCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable(tokens));
    }

    private void SetupWorkingDocumentService()
    {
        _workingDocumentServiceMock
            .Setup(x => x.UpdateWorkingDocumentAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _workingDocumentServiceMock
            .Setup(x => x.FinalizeAnalysisAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static async IAsyncEnumerable<string> AsyncEnumerable(IEnumerable<string> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    #endregion
}

/// <summary>
/// Tests for AnalysisStreamChunk model factory methods.
/// </summary>
public class AnalysisStreamChunkTests
{
    [Fact]
    public void Metadata_CreatesMetadataChunk()
    {
        // Arrange
        var analysisId = Guid.NewGuid();
        var docName = "test.pdf";

        // Act
        var chunk = AnalysisStreamChunk.Metadata(analysisId, docName);

        // Assert
        chunk.Type.Should().Be("metadata");
        chunk.AnalysisId.Should().Be(analysisId);
        chunk.DocumentName.Should().Be(docName);
        chunk.Done.Should().BeFalse();
        chunk.Content.Should().BeNull();
    }

    [Fact]
    public void TextChunk_CreatesContentChunk()
    {
        // Act
        var chunk = AnalysisStreamChunk.TextChunk("Hello world");

        // Assert
        chunk.Type.Should().Be("chunk");
        chunk.Content.Should().Be("Hello world");
        chunk.Done.Should().BeFalse();
    }

    [Fact]
    public void Completed_CreatesDoneChunk()
    {
        // Arrange
        var analysisId = Guid.NewGuid();
        var tokenUsage = new TokenUsage(1000, 500);

        // Act
        var chunk = AnalysisStreamChunk.Completed(analysisId, tokenUsage);

        // Assert
        chunk.Type.Should().Be("done");
        chunk.Done.Should().BeTrue();
        chunk.AnalysisId.Should().Be(analysisId);
        chunk.TokenUsage.Should().Be(tokenUsage);
    }

    [Fact]
    public void FromError_CreatesErrorChunk()
    {
        // Act
        var chunk = AnalysisStreamChunk.FromError("Something failed");

        // Assert
        chunk.Type.Should().Be("error");
        chunk.Done.Should().BeTrue();
        chunk.Error.Should().Be("Something failed");
    }
}

/// <summary>
/// Tests for Analysis request/response models.
/// </summary>
public class AnalysisModelsTests
{
    [Fact]
    public void AnalysisExecuteRequest_CanBeCreated()
    {
        // Arrange & Act
        var request = new AnalysisExecuteRequest
        {
            DocumentIds = [Guid.NewGuid(), Guid.NewGuid()],
            ActionId = Guid.NewGuid(),
            SkillIds = [Guid.NewGuid()],
            KnowledgeIds = [Guid.NewGuid()],
            ToolIds = [Guid.NewGuid()],
            OutputType = AnalysisOutputType.Document,
            PlaybookId = Guid.NewGuid()
        };

        // Assert
        request.DocumentIds.Should().HaveCount(2);
        request.SkillIds.Should().HaveCount(1);
        request.KnowledgeIds.Should().HaveCount(1);
        request.ToolIds.Should().HaveCount(1);
        request.PlaybookId.Should().NotBeNull();
    }

    [Fact]
    public void AnalysisOutputType_HasExpectedValues()
    {
        // Assert - Matches the actual enum from AnalysisExecuteRequest.cs
        ((int)AnalysisOutputType.Document).Should().Be(0);
        ((int)AnalysisOutputType.Email).Should().Be(1);
        ((int)AnalysisOutputType.Teams).Should().Be(2);
        ((int)AnalysisOutputType.Notification).Should().Be(3);
        ((int)AnalysisOutputType.Workflow).Should().Be(4);
    }

    [Fact]
    public void SaveDocumentFormat_HasExpectedValues()
    {
        // Assert - Matches the actual enum from AnalysisSaveRequest.cs
        ((int)SaveDocumentFormat.Docx).Should().Be(0);
        ((int)SaveDocumentFormat.Pdf).Should().Be(1);
        ((int)SaveDocumentFormat.Md).Should().Be(2);
        ((int)SaveDocumentFormat.Txt).Should().Be(3);
    }

    [Fact]
    public void ExportFormat_HasExpectedValues()
    {
        // Assert
        ((int)ExportFormat.Email).Should().Be(0);
        ((int)ExportFormat.Teams).Should().Be(1);
        ((int)ExportFormat.Pdf).Should().Be(2);
        ((int)ExportFormat.Docx).Should().Be(3);
    }

    [Fact]
    public void TokenUsage_CanBeCreated()
    {
        // Act
        var usage = new TokenUsage(1500, 750);

        // Assert
        usage.Input.Should().Be(1500);
        usage.Output.Should().Be(750);
    }

    [Fact]
    public void AnalysisDetailResult_CanBeCreated()
    {
        // Arrange & Act
        var result = new AnalysisDetailResult
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            DocumentName = "test.pdf",
            Action = new AnalysisActionInfo(Guid.NewGuid(), "Summarize"),
            Status = "Completed",
            WorkingDocument = "# Summary",
            FinalOutput = "# Final Summary",
            ChatHistory = [new ChatMessageInfo("user", "Hello", DateTime.UtcNow)],
            TokenUsage = new TokenUsage(1000, 500),
            StartedOn = DateTime.UtcNow.AddMinutes(-5),
            CompletedOn = DateTime.UtcNow
        };

        // Assert
        result.Id.Should().NotBeEmpty();
        result.DocumentName.Should().Be("test.pdf");
        result.Status.Should().Be("Completed");
        result.ChatHistory.Should().HaveCount(1);
    }
}
