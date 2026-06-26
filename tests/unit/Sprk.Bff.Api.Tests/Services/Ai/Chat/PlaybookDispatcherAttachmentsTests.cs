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
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Backward-compatibility tests for the FR-15 / task 110 extension of
/// <see cref="PlaybookDispatcher.DispatchAsync"/> with the optional
/// <c>IReadOnlyList&lt;ChatMessageAttachment&gt;? attachments</c> parameter.
///
/// <para>
/// <b>FR-15 invariant covered</b>: callers passing <c>null</c> or an empty attachment list
/// MUST observe behavior identical to the pre-task-110 message-only dispatch (no regression
/// on the no-files path). Callers passing a single attachment MUST still dispatch
/// successfully through the existing message-only fall-through (file-aware Phase A/B/C
/// classification lands in downstream tasks 111R/112/113R/114R — task 110 is signature
/// extension only).
/// </para>
///
/// <para>
/// Mock setup mirrors <see cref="PlaybookDispatcherIntegrationTests"/> and
/// <see cref="PlaybookDispatcherDestinationTests"/> patterns. The single high-confidence
/// vector-match path is exercised; LLM refinement is not invoked because all matches go
/// above the 0.85 confidence threshold.
/// </para>
/// </summary>
public class PlaybookDispatcherAttachmentsTests
{
    // ──────────────────────────────────────────────────────────
    // Test constants
    // ──────────────────────────────────────────────────────────

    private static readonly string SummarizePlaybookId = Guid.NewGuid().ToString();
    private const string SummarizePlaybookName = "summarize-document-for-chat";
    private const string TestTenantId = "test-tenant-attachments-110";

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

    public PlaybookDispatcherAttachmentsTests()
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
    // FR-15 case (a): null attachments dispatches identically
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Spec FR-15 backward-compat invariant: explicitly passing <c>null</c> for the new
    /// optional <c>attachments</c> parameter must produce a successful match on the
    /// existing high-confidence vector path — identical to the pre-task-110 signature.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_ReturnsMatch_WhenAttachmentsIsNull()
    {
        // Arrange — single high-confidence candidate skips Stage 2 (LLM refinement)
        SetupSearchReturnsSummarizePlaybook(score: 0.92);
        SetupNodeServiceReturnsTextOutput();

        var dispatcher = CreateDispatcher();

        // Act — explicit null attachments (FR-15 backward-compat path)
        var result = await dispatcher.DispatchAsync(
            "summarize this document",
            hostContext: null,
            cancellationToken: CancellationToken.None,
            attachments: null);

        // Assert — message-only path produced a match exactly as today
        result.Should().NotBeNull();
        result!.Matched.Should().BeTrue();
        result.PlaybookId.Should().Be(SummarizePlaybookId);
        result.PlaybookName.Should().Be(SummarizePlaybookName);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.85);
    }

    /// <summary>
    /// Spec FR-15 backward-compat invariant: callers using the pre-task-110 overload style
    /// (omitting the new optional <c>attachments</c> argument entirely) must observe
    /// identical behavior. Verifies the default <c>null</c> is wired correctly at the
    /// signature level.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_ReturnsMatch_WhenAttachmentsArgumentOmitted()
    {
        // Arrange
        SetupSearchReturnsSummarizePlaybook(score: 0.92);
        SetupNodeServiceReturnsTextOutput();

        var dispatcher = CreateDispatcher();

        // Act — call with original (pre-task-110) positional shape; attachments defaults to null
        var result = await dispatcher.DispatchAsync(
            "summarize this document",
            hostContext: null);

        // Assert
        result.Should().NotBeNull();
        result!.Matched.Should().BeTrue();
        result.PlaybookId.Should().Be(SummarizePlaybookId);
    }

    // ──────────────────────────────────────────────────────────
    // FR-15 case (b): empty list dispatches identically
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Spec FR-15 backward-compat invariant: an empty attachment list is semantically
    /// equivalent to <c>null</c> — both fall through to the message-only path. Verifies
    /// the <c>Count == 0</c> branch of the early guard.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_ReturnsMatch_WhenAttachmentsIsEmptyList()
    {
        // Arrange
        SetupSearchReturnsSummarizePlaybook(score: 0.92);
        SetupNodeServiceReturnsTextOutput();

        var dispatcher = CreateDispatcher();

        var emptyAttachments = Array.Empty<ChatMessageAttachment>();

        // Act
        var result = await dispatcher.DispatchAsync(
            "summarize this document",
            hostContext: null,
            cancellationToken: CancellationToken.None,
            attachments: emptyAttachments);

        // Assert — message-only path produced a match exactly as today
        result.Should().NotBeNull();
        result!.Matched.Should().BeTrue();
        result.PlaybookId.Should().Be(SummarizePlaybookId);
        result.PlaybookName.Should().Be(SummarizePlaybookName);
    }

    // ──────────────────────────────────────────────────────────
    // FR-15 case (c): single attachment dispatches successfully
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Spec FR-15 task-110 acceptance: a single attachment dispatches successfully via the
    /// existing message-only path (no regression on the file-present route). Tasks
    /// 111R-114R will extend the dispatcher to apply file-aware Phase A/B/C classification
    /// when attachments are present; this test pins the pre-extension contract — a single
    /// attachment must NOT short-circuit the existing dispatch behavior.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_ReturnsMatch_WhenSingleAttachmentProvided()
    {
        // Arrange
        SetupSearchReturnsSummarizePlaybook(score: 0.92);
        SetupNodeServiceReturnsTextOutput();

        var dispatcher = CreateDispatcher();

        var attachments = new List<ChatMessageAttachment>
        {
            new(
                Filename: "contract-draft.pdf",
                ContentType: "application/pdf",
                TextContent: "This is a sample NDA between parties A and B...")
        };

        // Act
        var result = await dispatcher.DispatchAsync(
            "summarize this document",
            hostContext: null,
            cancellationToken: CancellationToken.None,
            attachments: attachments);

        // Assert — message-only path still produced a match (tasks 111R-114R extend later)
        result.Should().NotBeNull();
        result!.Matched.Should().BeTrue();
        result.PlaybookId.Should().Be(SummarizePlaybookId);
        result.PlaybookName.Should().Be(SummarizePlaybookName);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.85);
    }

    // ──────────────────────────────────────────────────────────
    // FR-15 / Wave 5-A foundation: signature accepts multi-file lists
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Wave 5-A foundation check: the signature accepts a multi-attachment list without
    /// throwing or regressing the message-only dispatch path. Downstream tasks (111R
    /// LLM intent rerank, 112 Phase B vector match, 113R top-N selector, 114R composite
    /// output node) will activate file-aware classification on top of this wiring; task
    /// 110 verifies the parameter is accepted and the no-op-for-now contract holds.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_ReturnsMatch_WhenMultipleAttachmentsProvided()
    {
        // Arrange
        SetupSearchReturnsSummarizePlaybook(score: 0.92);
        SetupNodeServiceReturnsTextOutput();

        var dispatcher = CreateDispatcher();

        var attachments = new List<ChatMessageAttachment>
        {
            new("contract-a.pdf", "application/pdf", "Contract A text..."),
            new("contract-b.pdf", "application/pdf", "Contract B text..."),
            new("notes.txt", "text/plain", "Comparison notes...")
        };

        // Act
        var result = await dispatcher.DispatchAsync(
            "compare these documents",
            hostContext: null,
            cancellationToken: CancellationToken.None,
            attachments: attachments);

        // Assert
        result.Should().NotBeNull();
        result!.Matched.Should().BeTrue();
        result.PlaybookId.Should().Be(SummarizePlaybookId);
    }

    // ══════════════════════════════════════════════════════════
    // Mock Setup Helpers (mirror PlaybookDispatcherIntegrationTests patterns)
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Configures the mock SearchClient to return a single summarize playbook candidate
    /// with the specified similarity score via <see cref="SearchModelFactory"/>.
    /// </summary>
    private void SetupSearchReturnsSummarizePlaybook(double score)
    {
        var doc = new PlaybookEmbeddingDocument
        {
            Id = SummarizePlaybookId,
            PlaybookId = SummarizePlaybookId,
            PlaybookName = SummarizePlaybookName,
            Description = "Summarize a document and emit the summary into chat.",
            TriggerPhrases = ["summarize this", "summarize document", "tldr"],
            RecordType = "sprk_matter",
            EntityType = "matter",
            Tags = ["summarize", "chat"]
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
    /// Configures the mock <see cref="INodeService"/> to return a DeliverOutput node with
    /// text output type (autonomous execution, no confirmation required).
    /// </summary>
    private void SetupNodeServiceReturnsTextOutput()
    {
        var outputNode = new PlaybookNodeDto
        {
            Id = Guid.NewGuid(),
            PlaybookId = Guid.Parse(SummarizePlaybookId),
            NodeType = NodeType.Output,
            OutputType = OutputType.Text,
            RequiresConfirmation = false,
            Name = "Deliver Output"
        };

        _nodeServiceMock
            .Setup(s => s.GetNodesAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { outputNode });
    }
}
