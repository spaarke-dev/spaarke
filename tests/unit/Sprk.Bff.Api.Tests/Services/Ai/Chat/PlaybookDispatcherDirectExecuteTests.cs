using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for <see cref="PlaybookDispatcher.BuildDispatchResultForPlaybookAsync"/>
/// (chat-routing-redesign-r1 Track 1 / FR-50 production-smoke unblocker).
///
/// <para>
/// This new public method is the direct-execution entry point for the FR-50
/// <c>/api/ai/playbook-dispatch/execute</c> endpoint. It is invoked AFTER the user
/// has clicked a candidate from a <c>playbook_options</c> SSE event (FR-49), so it
/// MUST bypass Stage 1 vector match + Stage 2 LLM refinement (FR-48 invariant: user
/// click is the authorization) and produce a populated <see cref="DispatchResult"/>
/// directly from the playbook's DeliverOutput node metadata.
/// </para>
///
/// <para>
/// Coverage:
/// <list type="bullet">
///   <item><description>Method returns Matched=true with caller-supplied playbookId / playbookName.</description></item>
///   <item><description>OutputType / NodeDestination / WidgetType / RequiresConfirmation are sourced from the playbook's primary DeliverOutput node (same path used by DispatchAsync — guards regression).</description></item>
///   <item><description>Confidence is set to 1.0 (user-selected — no model confidence applies).</description></item>
///   <item><description>ExtractedParameters defaults to empty dictionary when caller passes null.</description></item>
///   <item><description>Caller-supplied parameters flow through verbatim.</description></item>
///   <item><description>Invalid inputs throw ArgumentException (null/whitespace playbookId or playbookName).</description></item>
///   <item><description>FR-48 invariant: no auto-execute flag on the result type.</description></item>
/// </list>
/// </para>
/// </summary>
[Trait("category", "binding-invariant")]
public class PlaybookDispatcherDirectExecuteTests
{
    private static readonly string TestPlaybookId = Guid.NewGuid().ToString();
    private const string TestPlaybookName = "summarize-document-for-workspace";
    private const string TestTenantId = "test-tenant-fr50-direct";

    private readonly Mock<IChatClient> _executionClientMock = new();
    private readonly Mock<INodeService> _nodeServiceMock = new();
    private readonly InMemoryTenantCache _cache = new();
    private readonly Mock<ILogger<PlaybookDispatcher>> _loggerMock = new();
    private readonly Mock<IOpenAiClient> _openAiClientMock = new();
    private readonly Mock<SearchIndexClient> _searchIndexClientMock = new(MockBehavior.Loose);
    private readonly Mock<ILogger<PlaybookEmbeddingService>> _embeddingLoggerMock = new();

    public PlaybookDispatcherDirectExecuteTests()
    {
        // InMemoryTenantCache returns default (null) for any uncached key — no setup needed.
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

    private void SetupOutputNode(string playbookId, OutputType outputType, string? configJson,
        bool? requiresConfirmation = null, string? targetPage = null)
    {
        var node = new PlaybookNodeDto
        {
            Id = Guid.NewGuid(),
            PlaybookId = Guid.Parse(playbookId),
            NodeType = NodeType.Output,
            OutputType = outputType,
            RequiresConfirmation = requiresConfirmation,
            TargetPage = targetPage,
            Name = "Deliver Output",
            ConfigJson = configJson,
        };

        _nodeServiceMock
            .Setup(s => s.GetNodesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { node });
    }

    // ───────────────────────────────────────────────────────────────────
    // FR-50 happy path: workspace playbook → populated DispatchResult
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildDispatchResultForPlaybookAsync_ReturnsMatchedResult_WithWorkspaceRoutingFromNode()
    {
        // Arrange — workspace playbook with structured-output-stream widget config.
        SetupOutputNode(
            TestPlaybookId,
            OutputType.Text,
            configJson: """{"deliveryType":"json","destination":"workspace","widgetType":"structured-output-stream"}""");

        var dispatcher = CreateDispatcher();

        // Act
        var result = await dispatcher.BuildDispatchResultForPlaybookAsync(
            TestPlaybookId,
            TestPlaybookName,
            extractedParameters: null);

        // Assert — bypass-the-classifier semantics
        result.Should().NotBeNull();
        result.Matched.Should().BeTrue();
        result.PlaybookId.Should().Be(TestPlaybookId);
        result.PlaybookName.Should().Be(TestPlaybookName);
        result.Confidence.Should().Be(1.0, "user-selected playbook is not a model confidence");
        result.OutputType.Should().Be(OutputType.Text);
        result.NodeDestination.Should().Be(NodeDestination.Workspace);
        result.WidgetType.Should().Be("structured-output-stream");
        result.ExtractedParameters.Should().BeEmpty();
    }

    // ───────────────────────────────────────────────────────────────────
    // FR-50 parameter pass-through
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildDispatchResultForPlaybookAsync_PassesExtractedParametersThrough_Verbatim()
    {
        SetupOutputNode(TestPlaybookId, OutputType.Text, configJson: null);

        var dispatcher = CreateDispatcher();
        var parameters = new Dictionary<string, string>
        {
            ["sessionAttachmentIds"] = "a1,a2",
            ["userIntent"] = "summarize",
        };

        var result = await dispatcher.BuildDispatchResultForPlaybookAsync(
            TestPlaybookId,
            TestPlaybookName,
            parameters);

        result.ExtractedParameters.Should().HaveCount(2);
        result.ExtractedParameters["sessionAttachmentIds"].Should().Be("a1,a2");
        result.ExtractedParameters["userIntent"].Should().Be("summarize");
    }

    // ───────────────────────────────────────────────────────────────────
    // FR-50 defaults when configJson is null/empty (chat destination)
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildDispatchResultForPlaybookAsync_DefaultsToChatDestination_WhenConfigJsonIsNull()
    {
        SetupOutputNode(TestPlaybookId, OutputType.Text, configJson: null);

        var dispatcher = CreateDispatcher();

        var result = await dispatcher.BuildDispatchResultForPlaybookAsync(
            TestPlaybookId,
            TestPlaybookName,
            extractedParameters: null);

        // Per NodeRoutingConfig.Parse(null) — default Chat + null widget (FR-14f / R6 FR-26)
        result.NodeDestination.Should().Be(NodeDestination.Chat);
        result.WidgetType.Should().BeNull();
    }

    // ───────────────────────────────────────────────────────────────────
    // FR-50 input validation
    // ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BuildDispatchResultForPlaybookAsync_ThrowsArgumentException_WhenPlaybookIdInvalid(string? playbookId)
    {
        var dispatcher = CreateDispatcher();
        var act = async () => await dispatcher.BuildDispatchResultForPlaybookAsync(
            playbookId!,
            TestPlaybookName,
            extractedParameters: null);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*playbookId*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BuildDispatchResultForPlaybookAsync_ThrowsArgumentException_WhenPlaybookNameInvalid(string? playbookName)
    {
        var dispatcher = CreateDispatcher();
        var act = async () => await dispatcher.BuildDispatchResultForPlaybookAsync(
            TestPlaybookId,
            playbookName!,
            extractedParameters: null);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*playbookName*");
    }

    // ───────────────────────────────────────────────────────────────────
    // FR-48 invariant: DispatchResult shape has no AutoExecute property
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void DispatchResult_ShapeHasNoAutoExecuteProperty_GuardsFr48Invariant()
    {
        var properties = typeof(DispatchResult)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.GetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .ToArray();

        properties.Should().NotContain(
            p => p.Contains("AutoExecute", StringComparison.OrdinalIgnoreCase),
            "FR-48 binding invariant: no auto-execute flag may exist on DispatchResult");
    }
}
