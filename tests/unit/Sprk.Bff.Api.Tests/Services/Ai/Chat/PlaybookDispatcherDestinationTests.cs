using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Tests covering <see cref="PlaybookDispatcher"/>'s population of the new
/// <see cref="DispatchResult.NodeDestination"/> and <see cref="DispatchResult.WidgetType"/>
/// fields from the matched playbook's primary DeliverOutput node
/// (<c>sprk_playbooknode.sprk_configjson</c>).
///
/// <para>
/// Covers spec <b>FR-14c</b> (matched playbook returns populated routing),
/// <b>FR-14f</b> (null/empty/malformed configjson defaults to <c>Chat</c>), and
/// the <b>R6 FR-26 chat-summarize convergence invariant</b> (existing chat-summarize
/// playbook keeps default <c>Chat</c> destination — preserved via
/// <see cref="NodeRoutingConfig.Parse"/> default semantics, NOT a playbook-specific case).
/// </para>
///
/// <para>
/// Co-located with <see cref="PlaybookDispatcherIntegrationTests"/> in the unit-test
/// project (the integration project lacks a dispatcher harness, and the existing
/// "integration" tests run with all mocked dependencies in the unit project — same
/// pattern is followed here per task 047 prescription).
/// </para>
/// </summary>
public class PlaybookDispatcherDestinationTests
{
    // ──────────────────────────────────────────────────────────
    // Test constants
    // ──────────────────────────────────────────────────────────

    private static readonly string WorkspacePlaybookId = Guid.NewGuid().ToString();
    private const string WorkspacePlaybookName = "summarize-document-for-workspace";

    // Anchored to the R6 FR-26 convergence-point playbook GUID
    // (see project CLAUDE.md "Decisions Made": "Chat-summarize migrates first (FR-05)").
    private static readonly string ChatSummarizePlaybookId = "44285d15-0000-0000-0000-000000000001";
    private const string ChatSummarizePlaybookName = "summarize-document-for-chat";

    private const string TestTenantId = "test-tenant-destination-001";

    // ──────────────────────────────────────────────────────────
    // Mocks
    // ──────────────────────────────────────────────────────────

    private readonly Mock<IChatClient> _executionClientMock;
    private readonly Mock<INodeService> _nodeServiceMock;
    private readonly InMemoryTenantCache _cache;
    private readonly Mock<ILogger<PlaybookDispatcher>> _loggerMock;
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<SearchIndexClient> _searchIndexClientMock;
    private readonly Mock<SearchClient> _searchClientMock;
    private readonly Mock<ILogger<PlaybookEmbeddingService>> _embeddingLoggerMock;

    public PlaybookDispatcherDestinationTests()
    {
        _executionClientMock = new Mock<IChatClient>();
        _nodeServiceMock = new Mock<INodeService>();
        _cache = new InMemoryTenantCache();
        _loggerMock = new Mock<ILogger<PlaybookDispatcher>>();
        _openAiClientMock = new Mock<IOpenAiClient>();
        _searchIndexClientMock = new Mock<SearchIndexClient>(MockBehavior.Loose);
        _searchClientMock = new Mock<SearchClient>(MockBehavior.Loose);
        _embeddingLoggerMock = new Mock<ILogger<PlaybookEmbeddingService>>();

        _searchIndexClientMock
            .Setup(c => c.GetSearchClient(It.IsAny<string>()))
            .Returns(_searchClientMock.Object);

        _openAiClientMock
            .Setup(c => c.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReadOnlyMemory<float>(new float[3072]));

        // InMemoryTenantCache returns null/default by default — no cache-miss setup required.
    }

    private PlaybookDispatcher CreateDispatcher()
    {
        var embeddingService = new PlaybookEmbeddingService(
            _searchIndexClientMock.Object,
            _openAiClientMock.Object,
            _embeddingLoggerMock.Object);

        return new PlaybookDispatcher(
            embeddingService,
            _executionClientMock.Object,
            _nodeServiceMock.Object,
            _cache,
            TestTenantId,
            _loggerMock.Object);
    }

    // ──────────────────────────────────────────────────────────
    // FR-14c — workspace playbook populates NodeDestination + WidgetType
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Spec FR-14c acceptance: dispatching the <c>summarize-document-for-workspace</c>
    /// playbook returns <see cref="NodeDestination.Workspace"/> +
    /// <c>WidgetType = "structured-output-stream"</c>, sourced from the matched
    /// DeliverOutput node's <c>sprk_configjson</c>.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_ReturnsWorkspaceWithStructuredOutputStream_ForSummarizeDocumentForWorkspacePlaybook()
    {
        // Arrange — high-confidence vector match (skips Stage 2) on the workspace playbook
        SetupSearchReturnsPlaybook(
            playbookId: WorkspacePlaybookId,
            playbookName: WorkspacePlaybookName,
            score: 0.92);

        SetupNodeServiceReturnsOutputNodeWithConfig(
            playbookId: WorkspacePlaybookId,
            configJson:
                """{"deliveryType":"json","template":"{{summary.output}}","destination":"workspace","widgetType":"structured-output-stream"}""");

        var dispatcher = CreateDispatcher();

        // Act
        var result = await dispatcher.DispatchAsync(
            "summarize this document into the workspace",
            hostContext: null);

        // Assert
        result.Should().NotBeNull();
        result!.Matched.Should().BeTrue();
        result.PlaybookId.Should().Be(WorkspacePlaybookId);
        result.NodeDestination.Should().Be(NodeDestination.Workspace);
        result.WidgetType.Should().Be("structured-output-stream");
    }

    // ──────────────────────────────────────────────────────────
    // FR-14f — null configJson defaults to Chat
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Spec FR-14f acceptance: when a matched playbook's DeliverOutput node has a
    /// <c>null</c> <c>sprk_configjson</c>, the dispatcher returns the default
    /// <see cref="NodeDestination.Chat"/> + <c>WidgetType = null</c>.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_ReturnsChatDefault_WhenConfigJsonIsNull()
    {
        // Arrange — high-confidence match on a generic playbook whose node has null configJson
        var playbookId = Guid.NewGuid().ToString();
        SetupSearchReturnsPlaybook(
            playbookId: playbookId,
            playbookName: "generic-text-playbook",
            score: 0.91);

        SetupNodeServiceReturnsOutputNodeWithConfig(
            playbookId: playbookId,
            configJson: null);

        var dispatcher = CreateDispatcher();

        // Act
        var result = await dispatcher.DispatchAsync(
            "do something generic",
            hostContext: null);

        // Assert
        result.Should().NotBeNull();
        result!.Matched.Should().BeTrue();
        result.NodeDestination.Should().Be(NodeDestination.Chat);
        result.WidgetType.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────
    // R6 FR-26 — chat-summarize convergence invariant
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// R6 FR-26 convergence invariant: dispatching the canonical chat-summarize
    /// playbook (sprk_playbookid <c>44285d15-...</c>) MUST stay
    /// <see cref="NodeDestination.Chat"/>. The mechanism preserving this is
    /// <see cref="NodeRoutingConfig.Parse"/>: an empty/legacy <c>sprk_configjson</c>
    /// (no <c>destination</c> property) parses to the default <c>Chat</c> destination
    /// — NOT a per-playbook special case.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_ChatSummarizePlaybookStaysChatDestination()
    {
        // Arrange — match the canonical chat-summarize playbook, with the legacy
        // configJson shape that has NO destination/widgetType properties.
        // Per FR-26: parse-default = Chat, so no special case is needed in the dispatcher.
        SetupSearchReturnsPlaybook(
            playbookId: ChatSummarizePlaybookId,
            playbookName: ChatSummarizePlaybookName,
            score: 0.93);

        SetupNodeServiceReturnsOutputNodeWithConfig(
            playbookId: ChatSummarizePlaybookId,
            configJson: """{"deliveryType":"markdown","template":"## Summary\n{{summary.output.tldr}}"}""");

        var dispatcher = CreateDispatcher();

        // Act
        var result = await dispatcher.DispatchAsync(
            "summarize this document for chat",
            hostContext: null);

        // Assert — FR-26 convergence: chat-summarize MUST remain Chat
        result.Should().NotBeNull();
        result!.Matched.Should().BeTrue();
        result.PlaybookId.Should().Be(ChatSummarizePlaybookId);
        result.NodeDestination.Should().Be(NodeDestination.Chat);
        result.WidgetType.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────
    // Heuristic lock-in — terminal node selection across multiple nodes
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Locks in the primary-node selection heuristic: when a playbook has multiple
    /// nodes (e.g. an AI analysis node followed by a DeliverOutput terminal node),
    /// <see cref="PlaybookDispatcher"/> selects the <see cref="NodeType.Output"/>
    /// node (returned first-by-execution-order from <see cref="INodeService.GetNodesAsync"/>)
    /// as the source of the routing config — NOT the AI analysis node.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_SelectsTerminalOutputNode_WhenMultipleNodesExist()
    {
        // Arrange — a playbook with two nodes: an AIAnalysis node FIRST (would be wrong
        // pick) and an Output node SECOND (the correct primary/terminal node).
        var playbookId = Guid.NewGuid().ToString();
        var playbookGuid = Guid.Parse(playbookId);

        SetupSearchReturnsPlaybook(
            playbookId: playbookId,
            playbookName: "multi-node-playbook",
            score: 0.93);

        var nodes = new[]
        {
            // AI node — has its own (decoy) configJson; MUST be ignored by routing extraction
            new PlaybookNodeDto
            {
                Id = Guid.NewGuid(),
                PlaybookId = playbookGuid,
                NodeType = NodeType.AIAnalysis,
                Name = "Analyze",
                ExecutionOrder = 0,
                ConfigJson = """{"destination":"side-effect","widgetType":"WRONG"}""",
            },
            // Output node — the terminal node, source of the canonical routing config
            new PlaybookNodeDto
            {
                Id = Guid.NewGuid(),
                PlaybookId = playbookGuid,
                NodeType = NodeType.Output,
                OutputType = OutputType.Text,
                Name = "Deliver Output",
                ExecutionOrder = 1,
                ConfigJson =
                    """{"deliveryType":"json","destination":"workspace","widgetType":"structured-output-stream"}""",
            },
        };

        _nodeServiceMock
            .Setup(s => s.GetNodesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(nodes);

        var dispatcher = CreateDispatcher();

        // Act
        var result = await dispatcher.DispatchAsync(
            "multi-node trigger phrase",
            hostContext: null);

        // Assert — routing comes from the Output node, NOT the AI node
        result.Should().NotBeNull();
        result!.Matched.Should().BeTrue();
        result.NodeDestination.Should().Be(NodeDestination.Workspace);
        result.WidgetType.Should().Be("structured-output-stream");
    }

    // ══════════════════════════════════════════════════════════
    // Mock Setup Helpers
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Configures the mock SearchClient to return a single playbook candidate with the
    /// specified ID/name/score via <see cref="SearchModelFactory"/>.
    /// </summary>
    private void SetupSearchReturnsPlaybook(string playbookId, string playbookName, double score)
    {
        var doc = new PlaybookEmbeddingDocument
        {
            Id = playbookId,
            PlaybookId = playbookId,
            PlaybookName = playbookName,
            Description = $"Test playbook: {playbookName}",
            TriggerPhrases = ["summarize", "analyze"],
            RecordType = "sprk_matter",
            EntityType = "matter",
            Tags = ["test"]
        };

        var searchResult = SearchModelFactory.SearchResult(doc, score, null);
        var searchResults = SearchModelFactory.SearchResults(
            values: new List<SearchResult<PlaybookEmbeddingDocument>> { searchResult },
            totalCount: 1,
            facets: null,
            coverage: null,
            rawResponse: null!);

        _searchClientMock
            .Setup(c => c.SearchAsync<PlaybookEmbeddingDocument>(
                It.IsAny<string?>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(searchResults, null!));
    }

    /// <summary>
    /// Configures the mock <see cref="INodeService"/> to return a single
    /// <see cref="NodeType.Output"/> node with the given <c>configJson</c>. The node
    /// uses <see cref="OutputType.Text"/> + autonomous execution to keep this helper
    /// orthogonal to the OutputType/HITL assertions (covered by sibling test class).
    /// </summary>
    private void SetupNodeServiceReturnsOutputNodeWithConfig(string playbookId, string? configJson)
    {
        var outputNode = new PlaybookNodeDto
        {
            Id = Guid.NewGuid(),
            PlaybookId = Guid.Parse(playbookId),
            NodeType = NodeType.Output,
            OutputType = OutputType.Text,
            RequiresConfirmation = false,
            Name = "Deliver Output",
            ConfigJson = configJson,
        };

        _nodeServiceMock
            .Setup(s => s.GetNodesAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { outputNode });
    }
}
