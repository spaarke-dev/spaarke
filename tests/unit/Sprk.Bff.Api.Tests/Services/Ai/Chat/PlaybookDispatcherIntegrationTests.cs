using System.Diagnostics;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;
using Xunit;

// Alias to resolve ChatMessage ambiguity
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Integration tests for <see cref="PlaybookDispatcher"/>.
///
/// Tests cover:
/// - Semantic matching accuracy (correct playbook matched)
/// - Parameter extraction (correct values extracted from natural language)
/// - HITL vs autonomous execution path selection
/// - Dispatch timing (NFR-04: &lt;2 seconds for vector search + LLM refinement)
/// - No-match graceful handling
///
/// <para>
/// AI Search is mocked via <see cref="SearchModelFactory"/> for deterministic matching.
/// The LLM refinement stage is mocked via <see cref="Mock{IChatClient}"/>.
/// </para>
/// </summary>
public class PlaybookDispatcherIntegrationTests
{
    // ──────────────────────────────────────────────────────────
    // Test constants
    // ──────────────────────────────────────────────────────────

    private static readonly string EmailPlaybookId = Guid.NewGuid().ToString();
    private const string EmailPlaybookName = "Send Email";
    private const string TestTenantId = "test-tenant-001";

    // ──────────────────────────────────────────────────────────
    // Mocks
    // ──────────────────────────────────────────────────────────

    private readonly Mock<IChatClient> _executionClientMock;
    private readonly Mock<INodeService> _nodeServiceMock;
    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly Mock<ILogger<PlaybookDispatcher>> _loggerMock;
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<SearchIndexClient> _searchIndexClientMock;
    private readonly Mock<SearchClient> _searchClientMock;
    private readonly Mock<ILogger<PlaybookEmbeddingService>> _embeddingLoggerMock;

    public PlaybookDispatcherIntegrationTests()
    {
        _executionClientMock = new Mock<IChatClient>();
        _nodeServiceMock = new Mock<INodeService>();
        _cacheMock = new Mock<IDistributedCache>();
        _loggerMock = new Mock<ILogger<PlaybookDispatcher>>();
        _openAiClientMock = new Mock<IOpenAiClient>();
        _searchIndexClientMock = new Mock<SearchIndexClient>(MockBehavior.Loose);
        _searchClientMock = new Mock<SearchClient>(MockBehavior.Loose);
        _embeddingLoggerMock = new Mock<ILogger<PlaybookEmbeddingService>>();

        // Default: SearchIndexClient.GetSearchClient returns mock SearchClient
        _searchIndexClientMock
            .Setup(c => c.GetSearchClient(It.IsAny<string>()))
            .Returns(_searchClientMock.Object);

        // Default: OpenAI embedding returns a dummy vector (3072 dims)
        _openAiClientMock
            .Setup(c => c.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReadOnlyMemory<float>(new float[3072]));

        // Default: cache returns null (no cached result)
        _cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
    }

    // ──────────────────────────────────────────────────────────
    // Factory
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="PlaybookDispatcher"/> instance with mock dependencies.
    /// </summary>
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
            _cacheMock.Object,
            TestTenantId,
            _loggerMock.Object);
    }

    // ──────────────────────────────────────────────────────────
    // Test 1: Semantic matching — correct playbook matched
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatcherMatchesEmailPlaybook_WhenUserSaysSendByEmail()
    {
        // Arrange — return a single high-confidence email playbook from vector search
        SetupSearchReturnsEmailPlaybook(score: 0.92);
        SetupNodeServiceReturnsDialogOutput();

        var dispatcher = CreateDispatcher();

        // Act
        var result = await dispatcher.DispatchAsync(
            "send this document by email to john@example.com",
            hostContext: null);

        // Assert
        result.Should().NotBeNull();
        result!.Matched.Should().BeTrue();
        result.PlaybookId.Should().Be(EmailPlaybookId);
        result.PlaybookName.Should().Be(EmailPlaybookName);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.85);
    }

    // ──────────────────────────────────────────────────────────
    // Test 2: Parameter extraction — correct values extracted
    // ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("send this by email to john@example.com", "john@example.com")]
    [InlineData("email the document to jane.doe@company.org", "jane.doe@company.org")]
    [InlineData("forward to support@spaarke.com please", "support@spaarke.com")]
    public async Task DispatcherExtractsRecipient_FromNaturalLanguagePhrasings(
        string userMessage, string expectedRecipient)
    {
        // Arrange — multiple candidates require Stage 2 LLM refinement for parameter extraction
        SetupSearchReturnsEmailPlaybook(score: 0.78); // Below 0.85 threshold → triggers Stage 2
        SetupLlmRefinementReturnsMatch(expectedRecipient);
        SetupNodeServiceReturnsTextOutput();

        var dispatcher = CreateDispatcher();

        // Act
        var result = await dispatcher.DispatchAsync(userMessage, hostContext: null);

        // Assert
        result.Should().NotBeNull();
        result!.Matched.Should().BeTrue();
        result.ExtractedParameters.Should().ContainKey("recipient");
        result.ExtractedParameters["recipient"].Should().Be(expectedRecipient);
    }

    // ──────────────────────────────────────────────────────────
    // Test 3: HITL path — dialog output with requiresConfirmation=true
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task HitlPath_ReturnsDialogOutput_WhenPlaybookRequiresConfirmation()
    {
        // Arrange — high-confidence match with dialog output node
        SetupSearchReturnsEmailPlaybook(score: 0.92);
        SetupNodeServiceReturnsDialogOutput(requiresConfirmation: true);

        var dispatcher = CreateDispatcher();

        // Act
        var result = await dispatcher.DispatchAsync(
            "send this by email",
            hostContext: null);

        // Assert
        result.Should().NotBeNull();
        result!.Matched.Should().BeTrue();
        result.OutputType.Should().Be(OutputType.Dialog);
        result.RequiresConfirmation.Should().BeTrue();
        result.TargetPage.Should().Be("sprk_emailcomposer");
    }

    // ──────────────────────────────────────────────────────────
    // Test 4: Autonomous path — text output with requiresConfirmation=false
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AutonomousPath_ExecutesDirectly_WhenPlaybookDoesNotRequireConfirmation()
    {
        // Arrange — high-confidence match with text output (autonomous)
        SetupSearchReturnsEmailPlaybook(score: 0.92);
        SetupNodeServiceReturnsTextOutput();

        var dispatcher = CreateDispatcher();

        // Act
        var result = await dispatcher.DispatchAsync(
            "summarize this document",
            hostContext: null);

        // Assert
        result.Should().NotBeNull();
        result!.Matched.Should().BeTrue();
        result.OutputType.Should().Be(OutputType.Text);
        result.RequiresConfirmation.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────
    // Test 5: NFR-04 timing — dispatch completes within 2 seconds
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchCompletes_WithinTwoSeconds_WithMockServices()
    {
        // Arrange — both stages with mock services should be near-instant
        SetupSearchReturnsEmailPlaybook(score: 0.78); // Triggers Stage 2
        SetupLlmRefinementReturnsMatch("test@example.com");
        SetupNodeServiceReturnsTextOutput();

        var dispatcher = CreateDispatcher();
        var stopwatch = Stopwatch.StartNew();

        // Act
        var result = await dispatcher.DispatchAsync(
            "send this by email to test@example.com",
            hostContext: null);

        // Assert
        stopwatch.Stop();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(2000,
            "NFR-04 requires dispatch to complete within 2 seconds (vector search + LLM refinement)");
        result.Should().NotBeNull();
        result!.Matched.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────
    // Test 6: No match — returns NoMatch gracefully
    // ──────────────────────────────────────────────────────────

    [Fact]
    public async Task NoMatchFound_ReturnsNoMatchDispatch_WhenNoPlaybookMatches()
    {
        // Arrange — search returns 0 candidates
        SetupSearchReturnsEmptyResults();

        var dispatcher = CreateDispatcher();

        // Act
        var result = await dispatcher.DispatchAsync(
            "what is the weather like today",
            hostContext: null);

        // Assert
        result.Should().NotBeNull();
        result!.Matched.Should().BeFalse();
        result.PlaybookId.Should().BeNull();
        result.PlaybookName.Should().BeNull();
        result.Confidence.Should().Be(0);
        result.ExtractedParameters.Should().BeEmpty();
    }

    // ══════════════════════════════════════════════════════════
    // Mock Setup Helpers
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Configures the mock SearchClient to return a single email playbook candidate
    /// with the specified similarity score via SearchModelFactory.
    /// </summary>
    private void SetupSearchReturnsEmailPlaybook(double score)
    {
        var emailDoc = new PlaybookEmbeddingDocument
        {
            Id = EmailPlaybookId,
            PlaybookId = EmailPlaybookId,
            PlaybookName = EmailPlaybookName,
            Description = "Send a document via email to a recipient",
            TriggerPhrases = ["send by email", "email this", "forward to"],
            RecordType = "sprk_matter",
            EntityType = "matter",
            Tags = ["email", "communication"]
        };

        var searchResult = SearchModelFactory.SearchResult(emailDoc, score, null);
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
    /// Configures the mock SearchClient to return zero candidates.
    /// </summary>
    private void SetupSearchReturnsEmptyResults()
    {
        var emptyResults = SearchModelFactory.SearchResults(
            values: new List<SearchResult<PlaybookEmbeddingDocument>>(),
            totalCount: 0,
            facets: null,
            coverage: null,
            rawResponse: null!);

        _searchClientMock
            .Setup(c => c.SearchAsync<PlaybookEmbeddingDocument>(
                It.IsAny<string?>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(emptyResults, null!));
    }

    /// <summary>
    /// Configures the mock IChatClient (execution client) to return an LLM refinement
    /// JSON response selecting the email playbook with the specified recipient parameter.
    /// Used when Stage 2 is triggered (score below 0.85 threshold).
    /// </summary>
    private void SetupLlmRefinementReturnsMatch(string recipient)
    {
        var llmResponseJson =
            $"{{\"playbookId\":\"{EmailPlaybookId}\",\"confidence\":0.92,\"parameters\":{{\"recipient\":\"{recipient}\"}}}}";

        var response = new ChatResponse(
            new List<AiChatMessage>
            {
                new(ChatRole.Assistant, llmResponseJson)
            });

        _executionClientMock
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IList<AiChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
    }

    /// <summary>
    /// Configures the mock INodeService to return a DeliverOutput node with dialog output type
    /// and the specified confirmation requirement.
    /// </summary>
    private void SetupNodeServiceReturnsDialogOutput(bool requiresConfirmation = true)
    {
        var outputNode = new PlaybookNodeDto
        {
            Id = Guid.NewGuid(),
            PlaybookId = Guid.Parse(EmailPlaybookId),
            NodeType = NodeType.Output,
            OutputType = OutputType.Dialog,
            RequiresConfirmation = requiresConfirmation,
            TargetPage = "sprk_emailcomposer",
            Name = "Deliver Output"
        };

        _nodeServiceMock
            .Setup(s => s.GetNodesAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { outputNode });
    }

    /// <summary>
    /// Configures the mock INodeService to return a DeliverOutput node with text output type
    /// (autonomous execution, no confirmation required).
    /// </summary>
    private void SetupNodeServiceReturnsTextOutput()
    {
        var outputNode = new PlaybookNodeDto
        {
            Id = Guid.NewGuid(),
            PlaybookId = Guid.Parse(EmailPlaybookId),
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
