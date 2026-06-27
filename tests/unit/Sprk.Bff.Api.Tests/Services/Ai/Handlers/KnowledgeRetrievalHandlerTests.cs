using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="KnowledgeRetrievalHandler"/> (R6 Wave 7c — replaces the legacy
/// hardcoded <c>KnowledgeRetrievalTools</c> class previously registered on
/// <see cref="Chat.SprkChatAgentFactory"/>).
/// </summary>
/// <remarks>
/// Covers: 4-point contract; 2-method dispatch via sprk_configuration.method discriminator
/// (GetKnowledgeSource / SearchKnowledgeBase); chat + playbook execution paths; Wave 7b
/// metadata plumbing (citations + widget envelopes); ChatKnowledgeScope filter; ADR-015
/// telemetry hygiene (no query content, no excerpt content); tenantId enforcement (ADR-014);
/// IRagService failure → ToolResult.Error.
/// </remarks>
public sealed class KnowledgeRetrievalHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly Assembly BffAssembly = typeof(IToolHandler).Assembly;

    private readonly Mock<IRagService> _ragServiceMock = new();

    private KnowledgeRetrievalHandler CreateHandler() => new(
        _ragServiceMock.Object,
        CreateLogger<KnowledgeRetrievalHandler>());

    private static AnalysisTool BuildKnowledgeTool(string method) =>
        BuildAnalysisTool(
            handlerClass: nameof(KnowledgeRetrievalHandler),
            configuration: $"{{\"method\":\"{method}\"}}",
            toolType: ToolType.Custom);

    private static RagSearchResponse BuildRagResponse(params (string id, string docName, string content)[] hits)
    {
        return new RagSearchResponse
        {
            Query = "fixture",
            Results = hits.Select(h => new RagSearchResult
            {
                Id = h.id,
                DocumentName = h.docName,
                Content = h.content,
                ChunkIndex = 0,
                ChunkCount = 1,
                Score = 0.9
            }).ToList(),
            TotalCount = hits.Length,
            SearchDurationMs = 10,
            EmbeddingDurationMs = 5
        };
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 4-point contract tests
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void HandlerType_IsRegisteredInDi()
    {
        var services = BuildToolFrameworkServiceCollection();

        var registeredImplementations = services
            .Where(d => d.ServiceType == typeof(IToolHandler) && d.ImplementationType is not null)
            .Select(d => d.ImplementationType!)
            .ToList();

        registeredImplementations.Should().Contain(
            typeof(KnowledgeRetrievalHandler),
            because: "the handler must be auto-discovered via assembly scan (R6 Pillar 2: no manual DI lines)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        var handler = CreateHandler();

        handler.HandlerId.Should().Be(
            nameof(KnowledgeRetrievalHandler),
            because: "R6 Pillar 2 binding: HandlerId == nameof(handler class) so sprk_handlerclass routes to this handler at runtime");
    }

    [Fact]
    public void Metadata_IsValid()
    {
        var handler = CreateHandler();
        var metadata = handler.Metadata;

        metadata.Should().NotBeNull();
        metadata.Name.Should().NotBeNullOrWhiteSpace();
        metadata.Description.Should().NotBeNullOrWhiteSpace();
        metadata.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
    }

    [Fact]
    public void SupportedToolTypes_IsNonEmpty()
    {
        var handler = CreateHandler();

        handler.SupportedToolTypes.Should().NotBeNullOrEmpty();
        handler.SupportedToolTypes.Should().Contain(ToolType.Custom);
    }

    [Fact]
    public void SupportedInvocationContexts_IsBoth()
    {
        var handler = CreateHandler();

        handler.SupportedInvocationContexts.Should().Be(
            InvocationContextKind.Both,
            because: "R6 Wave 7c FR-12: KnowledgeRetrievalHandler must be invocable from both playbook and chat function calling");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Validate (playbook) — tenantId enforcement
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_Succeeds_WithValidContext()
    {
        var handler = CreateHandler();
        var ctx = BuildToolExecutionContext();
        var tool = BuildKnowledgeTool("SearchKnowledgeBase");

        var result = handler.Validate(ctx, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenTenantIdIsMissing()
    {
        var handler = CreateHandler();
        var ctx = BuildToolExecutionContext(tenantId: "");
        var tool = BuildKnowledgeTool("SearchKnowledgeBase");

        var result = handler.Validate(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ValidateChat — argument shape per method
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateChat_Succeeds_WithKnowledgeSourceId_GetMethod()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"knowledgeSourceId\":\"src-1\"}");
        var tool = BuildKnowledgeTool("GetKnowledgeSource");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateChat_Succeeds_WithQuery_SearchMethod()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"query\":\"compliance\"}");
        var tool = BuildKnowledgeTool("SearchKnowledgeBase");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateChat_Fails_WhenKnowledgeSourceIdMissing_GetMethod()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{}");
        var tool = BuildKnowledgeTool("GetKnowledgeSource");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("knowledgeSourceId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenQueryMissing_SearchMethod()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{}");
        var tool = BuildKnowledgeTool("SearchKnowledgeBase");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("query", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenTenantIdMissing()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"query\":\"x\"}",
            tenantId: "");
        var tool = BuildKnowledgeTool("SearchKnowledgeBase");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenMethodUnsupported()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"x\"}");
        var tool = BuildKnowledgeTool("DropTheKnowledgeBase");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("method", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenJsonMalformed()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{not json");
        var tool = BuildKnowledgeTool("SearchKnowledgeBase");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("malformed", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteChatAsync — GetKnowledgeSource: citations + widget
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_GetKnowledgeSource_ReturnsCitationsInMetadata()
    {
        var response = BuildRagResponse(
            ("chunk-1", "Policy.pdf", "Excerpt one."),
            ("chunk-2", "Policy.pdf", "Excerpt two."));

        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"knowledgeSourceId\":\"src-abc\"}");
        var tool = BuildKnowledgeTool("GetKnowledgeSource");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Should().ContainKey(ToolResultMetadataKeys.Citations);

        var citations = result.Metadata![ToolResultMetadataKeys.Citations] as IReadOnlyList<ToolResultCitation>
                        ?? (result.Metadata![ToolResultMetadataKeys.Citations] as IEnumerable<ToolResultCitation>)?.ToList();
        citations.Should().NotBeNull();
        citations!.Should().HaveCount(2);
        citations![0].ChunkId.Should().Be("chunk-1");
        citations![0].SourceName.Should().Be("Policy.pdf");
    }

    [Fact]
    public async Task ExecuteChatAsync_GetKnowledgeSource_EmitsSourcePaneWidget()
    {
        var response = BuildRagResponse(("chunk-1", "Brief.pdf", "Content."));

        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"knowledgeSourceId\":\"src-xyz\"}");
        var tool = BuildKnowledgeTool("GetKnowledgeSource");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Metadata.Should().ContainKey(ToolResultMetadataKeys.Widget);
        var widget = result.Metadata![ToolResultMetadataKeys.Widget] as ToolResultWidget;
        widget.Should().NotBeNull();
        widget!.PaneType.Should().Be("source_pane");
        widget.WidgetType.Should().Be("DocumentViewer");
    }

    [Fact]
    public async Task ExecuteChatAsync_GetKnowledgeSource_ReturnsEmptySummary_WhenNoResults()
    {
        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildRagResponse());

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"knowledgeSourceId\":\"missing\"}");
        var tool = BuildKnowledgeTool("GetKnowledgeSource");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<KnowledgeRetrievalHandler.KnowledgeRetrievalPayload>();
        payload!.ResultCount.Should().Be(0);
        payload.Message.Should().Contain("No content found");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteChatAsync — SearchKnowledgeBase: citations, NO widget, scope filter
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_SearchKnowledgeBase_ReturnsCitations_NoWidget()
    {
        var response = BuildRagResponse(
            ("c1", "Doc1.pdf", "Foo"),
            ("c2", "Doc2.pdf", "Bar"));

        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"query\":\"compliance\",\"topK\":3}");
        var tool = BuildKnowledgeTool("SearchKnowledgeBase");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Metadata.Should().ContainKey(ToolResultMetadataKeys.Citations);
        result.Metadata!.Should().NotContainKey(ToolResultMetadataKeys.Widget);
    }

    [Fact]
    public async Task ExecuteChatAsync_SearchKnowledgeBase_ForwardsKnowledgeScopeIds_ToRagOptions()
    {
        var scopeIds = new[] { "ks-1", "ks-2", "ks-3" };
        var capturedOptions = (RagSearchOptions?)null;

        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, RagSearchOptions, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(BuildRagResponse(("c1", "D.pdf", "x")));

        var handler = CreateHandler();
        var baseCtx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"x\"}");
        var ctx = baseCtx with
        {
            KnowledgeScope = new ChatKnowledgeScope(
                RagKnowledgeSourceIds: scopeIds,
                InlineContent: null,
                SkillInstructions: null,
                ActiveDocumentId: null)
        };
        var tool = BuildKnowledgeTool("SearchKnowledgeBase");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedOptions.Should().NotBeNull();
        capturedOptions!.KnowledgeSourceIds.Should().BeEquivalentTo(scopeIds,
            because: "R6 Wave 7c: ChatInvocationContext.KnowledgeScope.RagKnowledgeSourceIds must filter the search");
    }

    [Fact]
    public async Task ExecuteChatAsync_SearchKnowledgeBase_NoKnowledgeScope_DoesNotSetIds()
    {
        var capturedOptions = (RagSearchOptions?)null;
        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, RagSearchOptions, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(BuildRagResponse(("c1", "D.pdf", "x")));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"x\"}");
        var tool = BuildKnowledgeTool("SearchKnowledgeBase");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedOptions!.KnowledgeSourceIds.Should().BeNull(
            because: "standalone chat (no playbook) must search tenant-wide");
    }

    [Fact]
    public async Task ExecuteChatAsync_PassesTenantIdToRagService()
    {
        var capturedOptions = (RagSearchOptions?)null;
        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, RagSearchOptions, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(BuildRagResponse(("c", "D.pdf", "x")));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"query\":\"x\"}",
            tenantId: "tenant-special");
        var tool = BuildKnowledgeTool("SearchKnowledgeBase");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        capturedOptions!.TenantId.Should().Be("tenant-special",
            because: "ADR-014: tenant scoping must be enforced on every IRagService call");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteAsync (playbook) — reads args from Configuration; no widget
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_PlaybookContext_GetKnowledgeSource_FromConfig()
    {
        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildRagResponse(("c1", "D.pdf", "x")));

        var handler = CreateHandler();
        var ctx = BuildToolExecutionContext();
        var tool = BuildAnalysisTool(
            handlerClass: nameof(KnowledgeRetrievalHandler),
            configuration: JsonSerializer.Serialize(new
            {
                method = "GetKnowledgeSource",
                knowledgeSourceId = "src-pb"
            }));

        var result = await handler.ExecuteAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        // Playbook path: citations metadata present, but NO widget (no SSE channel).
        result.Metadata.Should().ContainKey(ToolResultMetadataKeys.Citations);
        result.Metadata!.Should().NotContainKey(ToolResultMetadataKeys.Widget);
    }

    [Fact]
    public async Task ExecuteAsync_PlaybookContext_DefaultsToSearchKnowledgeBase_WhenConfigEmpty()
    {
        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildRagResponse(("c1", "D.pdf", "x")));

        var handler = CreateHandler();
        var ctx = BuildToolExecutionContext();
        var tool = BuildAnalysisTool(
            handlerClass: nameof(KnowledgeRetrievalHandler),
            configuration: JsonSerializer.Serialize(new { query = "policy" }));

        var result = await handler.ExecuteAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<KnowledgeRetrievalHandler.KnowledgeRetrievalPayload>();
        payload!.Method.Should().Be("SearchKnowledgeBase");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // IRagService failures
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ReturnsError_WhenRagServiceThrows()
    {
        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Search index unavailable"));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"x\"}");
        var tool = BuildKnowledgeTool("SearchKnowledgeBase");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsCancelled_WhenTokenCancelled()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"x\"}");
        var tool = BuildKnowledgeTool("SearchKnowledgeBase");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await handler.ExecuteChatAsync(ctx, tool, cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.Cancelled);
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsValidationError_WhenGetKnowledgeSourceArgsMissing()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{}");
        var tool = BuildKnowledgeTool("GetKnowledgeSource");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("knowledgeSourceId");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 telemetry — query content + excerpt content must NEVER leak into logs
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_RespectsAdr015_DoesNotLogQuery_OrExcerptContent()
    {
        const string secretQuery = "QUERY-CONTAINING-CLIENT-NAME-AcmeCorpInc-MatterX2026";
        const string secretExcerpt = "EXCERPT-WITH-PRIVILEGED-CONTENT-Trade-Secret-Formula-XJ7K";

        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildRagResponse(("c1", "Policy.pdf", secretExcerpt)));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"query\":\"{secretQuery}\"}}");
        var tool = BuildKnowledgeTool("SearchKnowledgeBase");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        AssertTelemetryRespectsAdr015(secretQuery, secretExcerpt);
    }

    [Fact]
    public async Task Telemetry_RespectsAdr015_GetKnowledgeSource_DoesNotLogExcerptContent()
    {
        const string secretExcerpt = "EXCERPT-WITH-CONFIDENTIAL-CASE-NAME-SmithVJonesCase-DocketX";

        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildRagResponse(("c1", "Brief.pdf", secretExcerpt)));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"knowledgeSourceId\":\"src-confidential\"}");
        var tool = BuildKnowledgeTool("GetKnowledgeSource");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        AssertTelemetryRespectsAdr015(secretExcerpt);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // DI bootstrap helper (shared with other handler tests)
    // ═════════════════════════════════════════════════════════════════════════════

    private static IServiceCollection BuildToolFrameworkServiceCollection()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolFramework(configuration);
        return services;
    }
}
