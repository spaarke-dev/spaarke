using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Telemetry;
using Xunit;
using ConversationMessage = Sprk.Bff.Api.Services.Ai.ConversationMessage;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for PlaybookExecutionEngine.
/// Tests conversational, batch, and (post-R6 task 025 / D-A-17) chat-Summarize execution modes.
/// </summary>
public class PlaybookExecutionEngineTests
{
    private readonly Mock<IAiPlaybookBuilderService> _builderServiceMock;
    private readonly Mock<IPlaybookOrchestrationService> _orchestrationServiceMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly Mock<INodeService> _nodeServiceMock;
    private readonly Mock<IGenericEntityService> _entityServiceMock;
    private readonly Mock<IRagService> _ragServiceMock;
    private readonly StubChatSummarizeOpenAiClient _openAiClient;
    private readonly R5SummarizeTelemetry _summarizeTelemetry;
    private readonly Mock<ILogger<PlaybookExecutionEngine>> _loggerMock;
    private readonly PlaybookExecutionEngine _engine;

    public PlaybookExecutionEngineTests()
    {
        _builderServiceMock = new Mock<IAiPlaybookBuilderService>();
        _orchestrationServiceMock = new Mock<IPlaybookOrchestrationService>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _nodeServiceMock = new Mock<INodeService>();
        _entityServiceMock = new Mock<IGenericEntityService>();
        _ragServiceMock = new Mock<IRagService>();
        _openAiClient = new StubChatSummarizeOpenAiClient();
        _summarizeTelemetry = new R5SummarizeTelemetry();
        _loggerMock = new Mock<ILogger<PlaybookExecutionEngine>>();

        _engine = new PlaybookExecutionEngine(
            _builderServiceMock.Object,
            _orchestrationServiceMock.Object,
            _httpContextAccessorMock.Object,
            _nodeServiceMock.Object,
            _entityServiceMock.Object,
            _ragServiceMock.Object,
            _openAiClient,
            _summarizeTelemetry,
            _loggerMock.Object);
    }

    private static ConversationContext CreateConversationContext(
        string message,
        ConversationMessage[]? history = null,
        CanvasNode[]? nodes = null) => new()
        {
            CurrentMessage = message,
            History = history ?? [],
            SessionState = new SessionState
            {
                SessionId = Guid.NewGuid().ToString("N"),
                CanvasState = new CanvasState
                {
                    Nodes = nodes ?? [],
                    Edges = []
                }
            }
        };

    private static ConversationMessage CreateHistoryMessage(ConversationRole role, string content) => new()
    {
        Role = role,
        Content = content,
        Timestamp = DateTimeOffset.UtcNow
    };

    private static CanvasNode CreateNode(string type, string? label = null) => new()
    {
        Id = Guid.NewGuid().ToString("N")[..8],
        Type = type,
        Label = label ?? $"Test {type} node"
    };

    #region ExecuteConversationalAsync Tests

    [Fact]
    public async Task ExecuteConversationalAsync_ValidContext_ReturnsResults()
    {
        // Arrange
        var context = CreateConversationContext("Add a node");

        _builderServiceMock
            .Setup(x => x.ProcessMessageAsync(
                It.IsAny<BuilderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateMockBuilderStream());

        // Act
        var results = new List<BuilderResult>();
        await foreach (var result in _engine.ExecuteConversationalAsync(context, CancellationToken.None))
        {
            results.Add(result);
        }

        // Assert
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.Type == BuilderResultType.Thinking);
        results.Should().Contain(r => r.Type == BuilderResultType.StateUpdate);
    }

    [Fact]
    public async Task ExecuteConversationalAsync_WithHistory_PassesHistoryToBuilder()
    {
        // Arrange
        var history = new[]
        {
            CreateHistoryMessage(ConversationRole.User, "Create a playbook"),
            CreateHistoryMessage(ConversationRole.Assistant, "I'll create that for you.")
        };
        var context = CreateConversationContext("Add an action node", history);

        _builderServiceMock
            .Setup(x => x.ProcessMessageAsync(
                It.IsAny<BuilderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateMockBuilderStream());

        // Act
        var results = new List<BuilderResult>();
        await foreach (var result in _engine.ExecuteConversationalAsync(context, CancellationToken.None))
        {
            results.Add(result);
        }

        // Assert
        _builderServiceMock.Verify(
            x => x.ProcessMessageAsync(
                It.Is<BuilderRequest>(r =>
                    r.ChatHistory != null &&
                    r.ChatHistory.Length == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteConversationalAsync_WithCanvasState_PassesStateToBuilder()
    {
        // Arrange
        var nodes = new[] { CreateNode("action", "Test Action") };
        var context = CreateConversationContext("Modify the action", nodes: nodes);

        _builderServiceMock
            .Setup(x => x.ProcessMessageAsync(
                It.IsAny<BuilderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateMockBuilderStream());

        // Act
        var results = new List<BuilderResult>();
        await foreach (var result in _engine.ExecuteConversationalAsync(context, CancellationToken.None))
        {
            results.Add(result);
        }

        // Assert
        _builderServiceMock.Verify(
            x => x.ProcessMessageAsync(
                It.Is<BuilderRequest>(r =>
                    r.CanvasState.Nodes.Length == 1 &&
                    r.CanvasState.Nodes[0].Type == "action"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteConversationalAsync_NullContext_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in _engine.ExecuteConversationalAsync(null!, CancellationToken.None))
            {
                // Should not reach here
            }
        });
    }

    [Fact]
    public async Task ExecuteConversationalAsync_EmptyMessage_ThrowsArgumentException()
    {
        // Arrange
        var context = new ConversationContext
        {
            CurrentMessage = "",
            SessionState = new SessionState
            {
                SessionId = "test",
                CanvasState = new CanvasState { Nodes = [], Edges = [] }
            }
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in _engine.ExecuteConversationalAsync(context, CancellationToken.None))
            {
                // Should not reach here
            }
        });
    }

    [Fact]
    public async Task ExecuteConversationalAsync_BuilderReturnsCanvasOperation_ConvertsToBuilderResult()
    {
        // Arrange
        var context = CreateConversationContext("Add a node");
        var patch = new CanvasPatch
        {
            Operation = CanvasPatchOperation.AddNode,
            Node = CreateNode("action")
        };

        _builderServiceMock
            .Setup(x => x.ProcessMessageAsync(
                It.IsAny<BuilderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateMockBuilderStreamWithOperation(patch));

        // Act
        var results = new List<BuilderResult>();
        await foreach (var result in _engine.ExecuteConversationalAsync(context, CancellationToken.None))
        {
            results.Add(result);
        }

        // Assert
        results.Should().Contain(r => r.Type == BuilderResultType.CanvasOperation);
        var operationResult = results.First(r => r.Type == BuilderResultType.CanvasOperation);
        operationResult.Patch.Should().NotBeNull();
        operationResult.Patch!.Operation.Should().Be(CanvasPatchOperation.AddNode);
    }

    [Fact]
    public async Task ExecuteConversationalAsync_BuilderReturnsError_ConvertsToErrorResult()
    {
        // Arrange
        var context = CreateConversationContext("Do something invalid");

        _builderServiceMock
            .Setup(x => x.ProcessMessageAsync(
                It.IsAny<BuilderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateMockBuilderStreamWithError("Test error"));

        // Act
        var results = new List<BuilderResult>();
        await foreach (var result in _engine.ExecuteConversationalAsync(context, CancellationToken.None))
        {
            results.Add(result);
        }

        // Assert
        results.Should().Contain(r => r.Type == BuilderResultType.Error);
        var errorResult = results.First(r => r.Type == BuilderResultType.Error);
        errorResult.Error.Should().NotBeNull();
        errorResult.Error!.Message.Should().Be("Test error");
    }

    [Fact]
    public async Task ExecuteConversationalAsync_UpdatesSessionState()
    {
        // Arrange
        var context = CreateConversationContext("Add a node");
        var originalLastActive = context.SessionState.LastActiveAt;

        _builderServiceMock
            .Setup(x => x.ProcessMessageAsync(
                It.IsAny<BuilderRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateMockBuilderStream());

        // Act
        var results = new List<BuilderResult>();
        await foreach (var result in _engine.ExecuteConversationalAsync(context, CancellationToken.None))
        {
            results.Add(result);
        }

        // Assert
        var stateUpdate = results.FirstOrDefault(r => r.Type == BuilderResultType.StateUpdate);
        stateUpdate.Should().NotBeNull();
        stateUpdate!.UpdatedState.Should().NotBeNull();
        stateUpdate.UpdatedState!.LastActiveAt.Should().BeOnOrAfter(originalLastActive);
    }

    #endregion

    #region ExecuteBatchAsync Tests

    [Fact]
    public async Task ExecuteBatchAsync_ValidRequest_DelegatesToOrchestrationService()
    {
        // Arrange
        var request = new PlaybookRunRequest
        {
            PlaybookId = Guid.NewGuid(),
            DocumentIds = [Guid.NewGuid()]
        };

        var httpContext = new DefaultHttpContext();
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        _orchestrationServiceMock
            .Setup(x => x.ExecuteAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateMockPlaybookStream(request.PlaybookId));

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _engine.ExecuteBatchAsync(request, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        events.Should().NotBeEmpty();
        _orchestrationServiceMock.Verify(
            x => x.ExecuteAsync(
                It.Is<PlaybookRunRequest>(r => r.PlaybookId == request.PlaybookId),
                httpContext,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteBatchAsync_NoHttpContext_ThrowsInvalidOperationException()
    {
        // Arrange
        var request = new PlaybookRunRequest
        {
            PlaybookId = Guid.NewGuid(),
            DocumentIds = [Guid.NewGuid()]
        };

        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns((HttpContext?)null);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in _engine.ExecuteBatchAsync(request, CancellationToken.None))
            {
                // Should not reach here
            }
        });
    }

    #endregion

    #region DetermineExecutionMode Tests

    [Fact]
    public void DetermineExecutionMode_CanvasStateOnly_ReturnsConversational()
    {
        // Arrange
        var playbookId = Guid.NewGuid();

        // Act
        var mode = _engine.DetermineExecutionMode(
            playbookId,
            hasCanvasState: true,
            hasDocuments: false);

        // Assert
        mode.Should().Be(ExecutionMode.Conversational);
    }

    [Fact]
    public void DetermineExecutionMode_DocumentsOnly_ReturnsBatch()
    {
        // Arrange
        var playbookId = Guid.NewGuid();

        // Act
        var mode = _engine.DetermineExecutionMode(
            playbookId,
            hasCanvasState: false,
            hasDocuments: true);

        // Assert
        mode.Should().Be(ExecutionMode.Batch);
    }

    [Fact]
    public void DetermineExecutionMode_BothCanvasAndDocuments_ReturnsBatch()
    {
        // Arrange
        var playbookId = Guid.NewGuid();

        // Act
        var mode = _engine.DetermineExecutionMode(
            playbookId,
            hasCanvasState: true,
            hasDocuments: true);

        // Assert
        mode.Should().Be(ExecutionMode.Batch);
    }

    [Fact]
    public void DetermineExecutionMode_Neither_ReturnsConversationalDefault()
    {
        // Arrange
        var playbookId = Guid.NewGuid();

        // Act
        var mode = _engine.DetermineExecutionMode(
            playbookId,
            hasCanvasState: false,
            hasDocuments: false);

        // Assert
        mode.Should().Be(ExecutionMode.Conversational);
    }

    #endregion

    #region ConversationContext Tests

    [Fact]
    public void ConversationContext_DefaultHistory_IsEmptyArray()
    {
        // Act
        var context = new ConversationContext
        {
            CurrentMessage = "test",
            SessionState = new SessionState
            {
                SessionId = "test",
                CanvasState = new CanvasState { Nodes = [], Edges = [] }
            }
        };

        // Assert
        context.History.Should().NotBeNull();
        context.History.Should().BeEmpty();
    }

    [Fact]
    public void SessionState_DefaultTimestamps_AreSet()
    {
        // Act
        var state = new SessionState
        {
            SessionId = "test",
            CanvasState = new CanvasState { Nodes = [], Edges = [] }
        };

        // Assert
        state.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        state.LastActiveAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    #endregion

    #region BuilderResult Factory Tests

    [Fact]
    public void BuilderResult_ThinkingFactory_CreatesCorrectType()
    {
        // Act
        var result = BuilderResult.Thinking("Processing...");

        // Assert
        result.Type.Should().Be(BuilderResultType.Thinking);
        result.Text.Should().Be("Processing...");
    }

    [Fact]
    public void BuilderResult_MessageFactory_CreatesCorrectType()
    {
        // Act
        var result = BuilderResult.Message("Hello");

        // Assert
        result.Type.Should().Be(BuilderResultType.Message);
        result.Text.Should().Be("Hello");
    }

    [Fact]
    public void BuilderResult_OperationFactory_CreatesCorrectType()
    {
        // Arrange
        var patch = new CanvasPatch { Operation = CanvasPatchOperation.AddNode };

        // Act
        var result = BuilderResult.Operation(patch);

        // Assert
        result.Type.Should().Be(BuilderResultType.CanvasOperation);
        result.Patch.Should().Be(patch);
    }

    [Fact]
    public void BuilderResult_ClarificationFactory_CreatesCorrectType()
    {
        // Act
        var result = BuilderResult.Clarification("Which node?", ["Node A", "Node B"]);

        // Assert
        result.Type.Should().Be(BuilderResultType.Clarification);
        result.ClarificationQuestion.Should().Be("Which node?");
        result.ClarificationOptions.Should().HaveCount(2);
    }

    [Fact]
    public void BuilderResult_ErrorFactory_CreatesCorrectType()
    {
        // Act
        var result = BuilderResult.ErrorResult("Something went wrong", "ERR_001");

        // Assert
        result.Type.Should().Be(BuilderResultType.Error);
        result.Error.Should().NotBeNull();
        result.Error!.Message.Should().Be("Something went wrong");
        result.Error.Code.Should().Be("ERR_001");
    }

    [Fact]
    public void BuilderResult_CompleteFactory_CreatesCorrectType()
    {
        // Act
        var result = BuilderResult.Complete();

        // Assert
        result.Type.Should().Be(BuilderResultType.Complete);
    }

    #endregion

    #region Helper Methods

    private static async IAsyncEnumerable<BuilderStreamChunk> CreateMockBuilderStream()
    {
        yield return BuilderStreamChunk.Message("Processing your request...");
        yield return BuilderStreamChunk.Complete();
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<BuilderStreamChunk> CreateMockBuilderStreamWithOperation(CanvasPatch patch)
    {
        yield return BuilderStreamChunk.Message("Adding node...");
        yield return BuilderStreamChunk.Operation(patch);
        yield return BuilderStreamChunk.Complete();
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<BuilderStreamChunk> CreateMockBuilderStreamWithError(string error)
    {
        yield return BuilderStreamChunk.ErrorChunk(error);
        yield return BuilderStreamChunk.Complete();
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<PlaybookStreamEvent> CreateMockPlaybookStream(Guid playbookId)
    {
        var runId = Guid.NewGuid();
        yield return PlaybookStreamEvent.RunStarted(runId, playbookId, 1);
        yield return PlaybookStreamEvent.RunCompleted(runId, playbookId, new PlaybookRunMetrics
        {
            TotalNodes = 1,
            CompletedNodes = 1
        });
        await Task.CompletedTask;
    }

    #endregion

    #region ExecuteChatSummarizeAsync Tests (R6 Pillar 4 / D-A-17)

    // Tests below cover the chat-Summarize streaming pipeline that R6 task 025 moved into
    // PlaybookExecutionEngine. Coverage migrated from the pre-R6 SessionSummarizeOrchestratorTests
    // (the orchestrator is now a thin pass-through; this is where the moved logic lives).

    private const string ChatTenantId = "tenant-abc";
    private const string ChatSessionId = "session-xyz";
    private const string ChatFileId1 = "file-001";
    private const string ChatFileId2 = "file-002";
    private static readonly Guid ChatPlaybookId = Guid.Parse("44285d15-1360-f111-ab0b-70a8a59455f4");
    private static readonly Guid ChatActionId = Guid.Parse("eeb05bfd-1260-f111-ab0b-70a8a59455f4");

    /// <summary>
    /// Configure the engine's FK-chain stubs so ExecuteChatSummarizeAsync resolves
    /// playbook → node → action through the post-R6 FK chain (no alternate-key).
    /// </summary>
    private void StubFkChain(string systemPrompt = "You are the R6 chat-Summarize assistant.",
        string outputSchemaJson = """{"type":"object","additionalProperties":false,"required":["tldr"],"properties":{"tldr":{"type":"array","items":{"type":"string"}}}}""")
    {
        _nodeServiceMock
            .Setup(n => n.GetNodesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new PlaybookNodeDto { Id = Guid.NewGuid(), PlaybookId = ChatPlaybookId, ActionId = ChatActionId } });

        var actionEntity = new Entity("sprk_analysisaction", ChatActionId)
        {
            ["sprk_analysisactionid"] = ChatActionId,
            ["sprk_name"] = "Summarize Document for Chat",
            ["sprk_actioncode"] = "SUM-CHAT@v1",
            ["sprk_systemprompt"] = systemPrompt,
            ["sprk_outputschemajson"] = outputSchemaJson
        };
        _entityServiceMock
            .Setup(e => e.RetrieveAsync(
                "sprk_analysisaction",
                It.IsAny<Guid>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(actionEntity);

        _ragServiceMock
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagSearchResponse
            {
                Query = "default",
                Results = new[]
                {
                    new RagSearchResult { Id = "chunk-1", DocumentName = "f1.pdf", Content = "Lorem ipsum.", Score = 0.9 }
                }
            });
    }

    private static ChatSummarizeRequest BuildEngineRequest(
        IReadOnlyList<string>? fileIds,
        SummarizeInvocationPath path = SummarizeInvocationPath.DirectEndpoint,
        params string[] sessionFileIds)
    {
        var manifest = sessionFileIds
            .Select(id => new ChatSessionFile(
                FileId: id, FileName: $"{id}.pdf", ContentType: "application/pdf",
                SizeBytes: 1024, SearchDocumentIdsCsv: $"doc-{id}-1",
                UploadedAt: DateTimeOffset.UtcNow))
            .ToList();
        return new ChatSummarizeRequest(
            TenantId: ChatTenantId,
            SessionId: ChatSessionId,
            FileIds: fileIds,
            StyleHint: null,
            UploadedFiles: manifest,
            Path: path);
    }

    private static async Task<List<AnalysisChunk>> CollectChunks(IAsyncEnumerable<AnalysisChunk> source)
    {
        var list = new List<AnalysisChunk>();
        await foreach (var chunk in source)
        {
            list.Add(chunk);
        }
        return list;
    }

    // ─── (a) FK-chain resolution — no alternate-key lookup (FR-26) ────────────────────────────

    [Fact]
    public async Task ExecuteChatSummarizeAsync_ResolvesActionViaFkChain_NoAlternateKey()
    {
        StubFkChain();
        _openAiClient.TokensToYield = new[] { "{\"tldr\":[\"x\"]}" };

        var request = BuildEngineRequest(new[] { ChatFileId1 }, sessionFileIds: ChatFileId1);
        _ = await CollectChunks(_engine.ExecuteChatSummarizeAsync(ChatPlaybookId, request, CancellationToken.None));

        _nodeServiceMock.Verify(n => n.GetNodesAsync(ChatPlaybookId, It.IsAny<CancellationToken>()), Times.Once,
            "engine resolves playbook → node via INodeService.GetNodesAsync (FK chain — post-R6 task 024)");
        _entityServiceMock.Verify(
            e => e.RetrieveAsync("sprk_analysisaction", ChatActionId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "engine loads action by FK-resolved ID, NOT by alternate key");
        _entityServiceMock.Verify(
            e => e.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "FR-26 invariant: NO alternate-key lookup remains in the chat-summarize path");
    }

    // ─── (b) Single-file does NOT emit combined-summary interjection — FR-04 negative ─────────

    [Fact]
    public async Task ExecuteChatSummarizeAsync_SingleFile_DoesNotEmitCombinedSummaryInterjection()
    {
        StubFkChain();
        _openAiClient.TokensToYield = new[] { "{\"tldr\":[\"point\"]}" };

        var request = BuildEngineRequest(new[] { ChatFileId1 }, sessionFileIds: ChatFileId1);
        var chunks = await CollectChunks(_engine.ExecuteChatSummarizeAsync(ChatPlaybookId, request, CancellationToken.None));

        chunks.Should().NotContain(c => c.Type == "text" && c.Content == PlaybookExecutionEngine.CombinedSummaryInterjection);
    }

    // ─── (c) Multi-file emits combined-summary interjection BEFORE stream — FR-04 positive ────

    [Fact]
    public async Task ExecuteChatSummarizeAsync_MultiFile_EmitsCombinedSummaryInterjectionBeforePlaybookStream()
    {
        StubFkChain();
        _openAiClient.TokensToYield = new[] { "{\"tldr\":[\"a\",\"b\"]}" };

        var request = BuildEngineRequest(new[] { ChatFileId1, ChatFileId2 }, sessionFileIds: new[] { ChatFileId1, ChatFileId2 });
        var chunks = await CollectChunks(_engine.ExecuteChatSummarizeAsync(ChatPlaybookId, request, CancellationToken.None));

        chunks.Should().NotBeEmpty();
        chunks[0].Type.Should().Be("text");
        chunks[0].Content.Should().Be(PlaybookExecutionEngine.CombinedSummaryInterjection);

        // And the interjection precedes any delta or completion chunk.
        var firstStructured = chunks.FirstOrDefault(c => c.Type is "delta" or "complete");
        firstStructured.Should().NotBeNull();
        chunks.IndexOf(chunks[0]).Should().BeLessThan(chunks.IndexOf(firstStructured!));
    }

    // ─── (d) ADR-014 — RagService gets BOTH tenantId AND sessionId filters ───────────────────

    [Fact]
    public async Task ExecuteChatSummarizeAsync_PropagatesTenantAndSessionIdToRagSearchOptions()
    {
        StubFkChain();
        _openAiClient.TokensToYield = new[] { "{\"tldr\":[\"x\"]}" };

        RagSearchOptions? observedOptions = null;
        _ragServiceMock
            .Setup(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, RagSearchOptions, CancellationToken>((_, opts, _) => observedOptions = opts)
            .ReturnsAsync(new RagSearchResponse { Query = "q", Results = Array.Empty<RagSearchResult>() });

        var request = BuildEngineRequest(new[] { ChatFileId1 }, sessionFileIds: ChatFileId1);
        _ = await CollectChunks(_engine.ExecuteChatSummarizeAsync(ChatPlaybookId, request, CancellationToken.None));

        observedOptions.Should().NotBeNull();
        observedOptions!.TenantId.Should().Be(ChatTenantId);
        observedOptions.SessionId.Should().Be(ChatSessionId);
    }

    // ─── (e) NFR-02 — hard cap 20 files (engine-side defense-in-depth) ────────────────────────

    [Fact]
    public async Task ExecuteChatSummarizeAsync_RejectsMoreThanTwentyFileIds()
    {
        StubFkChain();
        var tooMany = Enumerable.Range(1, ChatSession.MaxUploadedFiles + 1).Select(i => $"f-{i}").ToList();
        var request = BuildEngineRequest(tooMany, sessionFileIds: ChatFileId1);

        var act = async () => { await foreach (var _ in _engine.ExecuteChatSummarizeAsync(ChatPlaybookId, request, CancellationToken.None)) { } };
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*NFR-02*");
    }

    // ─── (f) Mid-stream exception yields FromError + terminates gracefully ───────────────────

    [Fact]
    public async Task ExecuteChatSummarizeAsync_MidStreamException_YieldsFromErrorAndTerminates()
    {
        StubFkChain();
        _openAiClient.ThrowMidStream = true;
        _openAiClient.TokensToYield = new[] { "{\"tld" }; // emits one token, then the next MoveNext throws

        var request = BuildEngineRequest(new[] { ChatFileId1 }, sessionFileIds: ChatFileId1);
        var chunks = await CollectChunks(_engine.ExecuteChatSummarizeAsync(ChatPlaybookId, request, CancellationToken.None));

        chunks.Should().Contain(c => c.Type == "error");
        chunks.Last().Type.Should().Be("error");
        chunks.Last().Error.Should().NotBeNullOrEmpty();
    }

    // ─── (g) Decline path — empty file selection emits a decline-style error chunk ───────────

    [Fact]
    public async Task ExecuteChatSummarizeAsync_NoFilesInSession_EmitsDecline()
    {
        StubFkChain();
        var request = BuildEngineRequest(fileIds: null /* default to session */, sessionFileIds: Array.Empty<string>());

        var chunks = await CollectChunks(_engine.ExecuteChatSummarizeAsync(ChatPlaybookId, request, CancellationToken.None));

        chunks.Should().ContainSingle();
        chunks[0].Type.Should().Be("error");
        chunks[0].Error.Should().Contain("No files are available");

        // Should short-circuit before any FK resolution or RAG call.
        _nodeServiceMock.Verify(n => n.GetNodesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _ragServiceMock.Verify(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── (h) FK-chain broken — node has empty ActionId → engine surfaces clear error ─────────

    [Fact]
    public async Task ExecuteChatSummarizeAsync_NodeWithEmptyActionId_EmitsFromError()
    {
        // Simulate a broken FK chain — node exists but ActionId is Guid.Empty (the pre-task-024 state).
        _nodeServiceMock
            .Setup(n => n.GetNodesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new PlaybookNodeDto { Id = Guid.NewGuid(), PlaybookId = ChatPlaybookId, ActionId = Guid.Empty } });

        var request = BuildEngineRequest(new[] { ChatFileId1 }, sessionFileIds: ChatFileId1);
        var chunks = await CollectChunks(_engine.ExecuteChatSummarizeAsync(ChatPlaybookId, request, CancellationToken.None));

        chunks.Should().Contain(c => c.Type == "error");
        chunks.Last().Error.Should().Contain("FK chain", "engine surfaces broken FK chain cleanly");
    }

    // ─── (i) Argument validation — empty tenant + empty session + Guid.Empty playbookId ─────

    [Fact]
    public async Task ExecuteChatSummarizeAsync_EmptyTenantId_Throws()
    {
        StubFkChain();
        var request = new ChatSummarizeRequest(
            TenantId: "", SessionId: ChatSessionId, FileIds: null, StyleHint: null,
            UploadedFiles: Array.Empty<ChatSessionFile>(),
            Path: SummarizeInvocationPath.DirectEndpoint);

        var act = async () => { await foreach (var _ in _engine.ExecuteChatSummarizeAsync(ChatPlaybookId, request, CancellationToken.None)) { } };
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteChatSummarizeAsync_EmptySessionId_Throws()
    {
        StubFkChain();
        var request = new ChatSummarizeRequest(
            TenantId: ChatTenantId, SessionId: "", FileIds: null, StyleHint: null,
            UploadedFiles: Array.Empty<ChatSessionFile>(),
            Path: SummarizeInvocationPath.DirectEndpoint);

        var act = async () => { await foreach (var _ in _engine.ExecuteChatSummarizeAsync(ChatPlaybookId, request, CancellationToken.None)) { } };
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteChatSummarizeAsync_EmptyPlaybookId_Throws()
    {
        StubFkChain();
        var request = BuildEngineRequest(new[] { ChatFileId1 }, sessionFileIds: ChatFileId1);

        var act = async () => { await foreach (var _ in _engine.ExecuteChatSummarizeAsync(Guid.Empty, request, CancellationToken.None)) { } };
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─── (j) Stream shape — happy path ends with Type="complete" ─────────────────────────────

    [Fact]
    public async Task ExecuteChatSummarizeAsync_HappyPath_EndsWithCompleteChunk()
    {
        StubFkChain();
        _openAiClient.TokensToYield = new[] { "{\"tldr\":[\"a\"]}" };

        var request = BuildEngineRequest(new[] { ChatFileId1 }, sessionFileIds: ChatFileId1);
        var chunks = await CollectChunks(_engine.ExecuteChatSummarizeAsync(ChatPlaybookId, request, CancellationToken.None));

        chunks.Should().Contain(c => c.Type == "complete");
        chunks.Last().Type.Should().Be("complete");
    }

    #endregion

    #region Chat-Summarize test doubles

    /// <summary>
    /// Stub <see cref="IOpenAiClient"/> for streaming chat-Summarize tests. Only the streaming
    /// method is needed; all other interface members throw to make accidental use visible.
    /// </summary>
    private sealed class StubChatSummarizeOpenAiClient : IOpenAiClient
    {
        public IReadOnlyList<string> TokensToYield { get; set; } = Array.Empty<string>();
        public bool ThrowMidStream { get; set; }

        public async IAsyncEnumerable<string> StreamStructuredCompletionAsync(
            IEnumerable<global::OpenAI.Chat.ChatMessage> messages,
            BinaryData jsonSchema,
            string schemaName,
            string? model = null,
            int? maxOutputTokens = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var i = 0;
            foreach (var token in TokensToYield)
            {
                if (ThrowMidStream && i > 0)
                {
                    throw new InvalidOperationException("simulated mid-stream failure");
                }
                yield return token;
                i++;
                await Task.Yield();
            }

            if (ThrowMidStream && TokensToYield.Count > 0)
            {
                // If only one token was queued and we still want to fail, simulate the failure
                // on the MoveNext following the last yield.
                throw new InvalidOperationException("simulated mid-stream failure");
            }
        }

        public IAsyncEnumerable<string> StreamCompletionAsync(string prompt, string? model = null, int? maxOutputTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by chat-summarize engine tests.");
        public Task<string> GetCompletionAsync(string prompt, string? model = null, int? maxOutputTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by chat-summarize engine tests.");
        public IAsyncEnumerable<string> StreamVisionCompletionAsync(string prompt, byte[] imageBytes, string mediaType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by chat-summarize engine tests.");
        public Task<string> GetVisionCompletionAsync(string prompt, byte[] imageBytes, string mediaType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by chat-summarize engine tests.");
        public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, string? model = null, int? dimensions = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by chat-summarize engine tests.");
        public Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(IEnumerable<string> texts, string? model = null, int? dimensions = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by chat-summarize engine tests.");
        public Task<ChatCompletionResult> GetChatCompletionWithToolsAsync(IEnumerable<global::OpenAI.Chat.ChatMessage> messages, IEnumerable<global::OpenAI.Chat.ChatTool> tools, string? model = null, int? maxOutputTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by chat-summarize engine tests.");
        public Task<T> GetStructuredCompletionAsync<T>(IEnumerable<global::OpenAI.Chat.ChatMessage> messages, BinaryData jsonSchema, string schemaName, string deploymentName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by chat-summarize engine tests.");
        public Task<string> GetStructuredCompletionRawAsync(string prompt, BinaryData jsonSchema, string schemaName, string? model = null, int? maxOutputTokens = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not used by chat-summarize engine tests.");
    }

    #endregion
}
