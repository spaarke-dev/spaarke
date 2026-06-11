using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="DocumentSearchHandler"/> (R6 Wave 8 — replaces the legacy
/// hardcoded <c>DocumentSearchTools</c> class previously registered on
/// <see cref="Chat.SprkChatAgentFactory"/>).
/// </summary>
/// <remarks>
/// Covers: 4-point contract; 2-method dispatch via sprk_configuration.method discriminator
/// (SearchDocuments / SearchDiscovery); chat + playbook execution paths; Wave 7b metadata
/// plumbing (citations + widget envelopes for both methods); ChatKnowledgeScope filter
/// (RagKnowledgeSourceIds for SearchDocuments; ParentEntity scoping for SearchDiscovery);
/// ADR-015 telemetry hygiene (no query content at Information level; query content allowed
/// at Debug level only — sentinel scan still enforces no excerpt content anywhere);
/// tenantId enforcement (ADR-014); IRagService failure → ToolResult.Error; cancellation;
/// chat-context dispatch via the ToolHandlerToAIFunctionAdapter.
/// </remarks>
public sealed class DocumentSearchHandlerTests : TypedToolHandlerTestFixture
{
    private const string ValidJsonSchema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "query": { "type": "string" },
            "topK": { "type": "integer" }
          },
          "required": ["query"]
        }
        """;

    private static readonly Assembly BffAssembly = typeof(IToolHandler).Assembly;

    private readonly Mock<IRagService> _ragServiceMock = new();

    private DocumentSearchHandler CreateHandler() => new(
        _ragServiceMock.Object,
        CreateLogger<DocumentSearchHandler>());

    private static AnalysisTool BuildDocumentSearchTool(string method) =>
        BuildAnalysisTool(
            handlerClass: nameof(DocumentSearchHandler),
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
            typeof(DocumentSearchHandler),
            because: "the handler must be auto-discovered via assembly scan (R6 Pillar 2: no manual DI lines)");
    }

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        var handler = CreateHandler();

        handler.HandlerId.Should().Be(
            nameof(DocumentSearchHandler),
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
            because: "R6 Wave 8 FR-12: DocumentSearchHandler must be invocable from both playbook and chat function calling");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Validate (playbook) — tenantId enforcement
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_Succeeds_WithValidContext()
    {
        var handler = CreateHandler();
        var ctx = BuildToolExecutionContext();
        var tool = BuildDocumentSearchTool("SearchDocuments");

        var result = handler.Validate(ctx, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Fails_WhenTenantIdIsMissing()
    {
        var handler = CreateHandler();
        var ctx = BuildToolExecutionContext(tenantId: "");
        var tool = BuildDocumentSearchTool("SearchDocuments");

        var result = handler.Validate(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ValidateChat — argument shape per method
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateChat_Succeeds_WithQuery_SearchDocuments()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"contracts\"}");
        var tool = BuildDocumentSearchTool("SearchDocuments");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateChat_Succeeds_WithQuery_SearchDiscovery()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"acquisitions\"}");
        var tool = BuildDocumentSearchTool("SearchDiscovery");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateChat_Fails_WhenQueryMissing()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{}");
        var tool = BuildDocumentSearchTool("SearchDocuments");

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
        var tool = BuildDocumentSearchTool("SearchDocuments");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenMethodUnsupported()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"x\"}");
        var tool = BuildDocumentSearchTool("DropTheIndex");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("method", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenJsonMalformed()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{not json");
        var tool = BuildDocumentSearchTool("SearchDocuments");

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("malformed", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteChatAsync — SearchDocuments: citations + widget
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_SearchDocuments_ReturnsCitationsAndWidget()
    {
        var response = BuildRagResponse(
            ("c-1", "Policy.pdf", "First excerpt."),
            ("c-2", "Policy.pdf", "Second excerpt."));

        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"compliance\"}");
        var tool = BuildDocumentSearchTool("SearchDocuments");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Should().ContainKey(ToolResultMetadataKeys.Citations);
        result.Metadata!.Should().ContainKey(ToolResultMetadataKeys.Widget);

        var citations = result.Metadata![ToolResultMetadataKeys.Citations] as IReadOnlyList<ToolResultCitation>
                        ?? (result.Metadata![ToolResultMetadataKeys.Citations] as IEnumerable<ToolResultCitation>)?.ToList();
        citations.Should().NotBeNull();
        citations!.Should().HaveCount(2);
        citations![0].ChunkId.Should().Be("c-1");
        citations![0].SourceName.Should().Be("Policy.pdf");

        var widget = result.Metadata![ToolResultMetadataKeys.Widget] as ToolResultWidget;
        widget.Should().NotBeNull();
        widget!.PaneType.Should().Be("output_pane");
        widget.WidgetType.Should().Be("SearchResults");
    }

    [Fact]
    public async Task ExecuteChatAsync_SearchDocuments_ForwardsKnowledgeScopeIds_ToRagOptions()
    {
        var scopeIds = new[] { "ks-1", "ks-2" };
        RagSearchOptions? capturedOptions = null;

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
        var tool = BuildDocumentSearchTool("SearchDocuments");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedOptions.Should().NotBeNull();
        capturedOptions!.KnowledgeSourceIds.Should().BeEquivalentTo(scopeIds,
            because: "R6 Wave 8: ChatInvocationContext.KnowledgeScope.RagKnowledgeSourceIds must filter SearchDocuments");
    }

    [Fact]
    public async Task ExecuteChatAsync_SearchDocuments_NoKnowledgeScope_DoesNotSetIds()
    {
        RagSearchOptions? capturedOptions = null;
        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, RagSearchOptions, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(BuildRagResponse(("c1", "D.pdf", "x")));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"x\"}");
        var tool = BuildDocumentSearchTool("SearchDocuments");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedOptions!.KnowledgeSourceIds.Should().BeNull(
            because: "standalone chat (no playbook) must search tenant-wide");
    }

    [Fact]
    public async Task ExecuteChatAsync_SearchDocuments_ReturnsEmptySummary_WhenNoResults()
    {
        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildRagResponse());

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"nothing\"}");
        var tool = BuildDocumentSearchTool("SearchDocuments");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<DocumentSearchHandler.DocumentSearchPayload>();
        payload!.ResultCount.Should().Be(0);
        payload.Message.Should().Contain("No relevant documents");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteChatAsync — SearchDiscovery: citations + widget (isDiscovery=true) + scope
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_SearchDiscovery_ReturnsCitationsAndWidget()
    {
        var response = BuildRagResponse(
            ("d-1", "Brief.pdf", "Discovery excerpt one."),
            ("d-2", "Memo.pdf", "Discovery excerpt two."));

        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"merger\"}");
        var tool = BuildDocumentSearchTool("SearchDiscovery");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Metadata.Should().ContainKey(ToolResultMetadataKeys.Citations);
        result.Metadata!.Should().ContainKey(ToolResultMetadataKeys.Widget);

        var widget = result.Metadata![ToolResultMetadataKeys.Widget] as ToolResultWidget;
        widget.Should().NotBeNull();
        widget!.PaneType.Should().Be("output_pane");
        widget.WidgetType.Should().Be("SearchResults");
    }

    [Fact]
    public async Task ExecuteChatAsync_SearchDiscovery_AppliesDiscoveryMinScore()
    {
        // SearchDiscovery uses MinScore = 0.5 (wider net than SearchDocuments' default 0.7).
        RagSearchOptions? capturedOptions = null;
        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, RagSearchOptions, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(BuildRagResponse(("d1", "D.pdf", "x")));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"explore\"}");
        var tool = BuildDocumentSearchTool("SearchDiscovery");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        capturedOptions!.MinScore.Should().BeApproximately(0.5f, 0.001f,
            because: "SearchDiscovery casts a wider net than SearchDocuments via lower MinScore");
    }

    [Fact]
    public async Task ExecuteChatAsync_SearchDiscovery_ForwardsParentEntity_FromKnowledgeScope()
    {
        // When the chat session is host-context-bound, ChatKnowledgeScope carries the
        // parent entity type+id. SearchDiscovery scopes to that entity rather than tenant-wide.
        RagSearchOptions? capturedOptions = null;
        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, RagSearchOptions, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(BuildRagResponse(("d1", "D.pdf", "x")));

        var handler = CreateHandler();
        var baseCtx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"explore\"}");
        var matterEntityId = Guid.NewGuid().ToString();
        var ctx = baseCtx with
        {
            KnowledgeScope = new ChatKnowledgeScope(
                RagKnowledgeSourceIds: Array.Empty<string>(),
                InlineContent: null,
                SkillInstructions: null,
                ActiveDocumentId: null,
                ParentEntityType: "sprk_matter",
                ParentEntityId: matterEntityId)
        };
        var tool = BuildDocumentSearchTool("SearchDiscovery");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        capturedOptions!.ParentEntityType.Should().Be("sprk_matter");
        capturedOptions.ParentEntityId.Should().Be(matterEntityId,
            because: "R6 Wave 8: SearchDiscovery scopes to the playbook's host-context parent entity when set");
    }

    [Fact]
    public async Task ExecuteChatAsync_SearchDiscovery_NoHostContext_StaysTenantWide()
    {
        RagSearchOptions? capturedOptions = null;
        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, RagSearchOptions, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(BuildRagResponse(("d1", "D.pdf", "x")));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"x\"}");
        var tool = BuildDocumentSearchTool("SearchDiscovery");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        capturedOptions!.ParentEntityType.Should().BeNull();
        capturedOptions.ParentEntityId.Should().BeNull(
            because: "standalone chat (no host context) keeps discovery tenant-wide");
    }

    [Fact]
    public async Task ExecuteChatAsync_SearchDiscovery_DefaultTopK_IsTen()
    {
        RagSearchOptions? capturedOptions = null;
        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, RagSearchOptions, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(BuildRagResponse(("d1", "D.pdf", "x")));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"x\"}");
        var tool = BuildDocumentSearchTool("SearchDiscovery");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        capturedOptions!.TopK.Should().Be(10,
            because: "SearchDiscovery default topK is 10 (matches legacy DocumentSearchTools)");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-014 tenant isolation — tenantId always forwarded to IRagService
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_SearchDocuments_PassesTenantIdToRagService()
    {
        RagSearchOptions? capturedOptions = null;
        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, RagSearchOptions, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(BuildRagResponse(("c", "D.pdf", "x")));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"query\":\"x\"}",
            tenantId: "tenant-special-1");
        var tool = BuildDocumentSearchTool("SearchDocuments");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        capturedOptions!.TenantId.Should().Be("tenant-special-1",
            because: "ADR-014: tenant scoping must be enforced on every IRagService call");
    }

    [Fact]
    public async Task ExecuteChatAsync_SearchDiscovery_PassesTenantIdToRagService()
    {
        RagSearchOptions? capturedOptions = null;
        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, RagSearchOptions, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(BuildRagResponse(("c", "D.pdf", "x")));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"query\":\"x\"}",
            tenantId: "tenant-special-2");
        var tool = BuildDocumentSearchTool("SearchDiscovery");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        capturedOptions!.TenantId.Should().Be("tenant-special-2",
            because: "ADR-014: tenant scoping must be enforced on every IRagService call (discovery path too)");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteAsync (playbook) — reads args from Configuration; no widget
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_PlaybookContext_SearchDocuments_FromConfig()
    {
        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildRagResponse(("c1", "D.pdf", "x")));

        var handler = CreateHandler();
        var ctx = BuildToolExecutionContext();
        var tool = BuildAnalysisTool(
            handlerClass: nameof(DocumentSearchHandler),
            configuration: JsonSerializer.Serialize(new
            {
                method = "SearchDocuments",
                query = "policy"
            }));

        var result = await handler.ExecuteAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        // Playbook path: citations metadata present, but NO widget (no SSE channel).
        result.Metadata.Should().ContainKey(ToolResultMetadataKeys.Citations);
        result.Metadata!.Should().NotContainKey(ToolResultMetadataKeys.Widget);
    }

    [Fact]
    public async Task ExecuteAsync_PlaybookContext_DefaultsToSearchDocuments_WhenConfigEmpty()
    {
        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildRagResponse(("c1", "D.pdf", "x")));

        var handler = CreateHandler();
        var ctx = BuildToolExecutionContext();
        var tool = BuildAnalysisTool(
            handlerClass: nameof(DocumentSearchHandler),
            configuration: JsonSerializer.Serialize(new { query = "compliance" }));

        var result = await handler.ExecuteAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<DocumentSearchHandler.DocumentSearchPayload>();
        payload!.Method.Should().Be("SearchDocuments");
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
        var tool = BuildDocumentSearchTool("SearchDocuments");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsCancelled_WhenTokenCancelled()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"x\"}");
        var tool = BuildDocumentSearchTool("SearchDocuments");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await handler.ExecuteChatAsync(ctx, tool, cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.Cancelled);
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsValidationError_WhenQueryMissing()
    {
        var handler = CreateHandler();
        // Bypass ValidateChat by going straight to Execute with empty query in JSON
        // (the dispatcher's own validation surfaces the error).
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{}");
        var tool = BuildDocumentSearchTool("SearchDocuments");

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("query");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 telemetry — query content must NEVER leak above Debug;
    // excerpt content must NEVER appear in logs at any level
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Telemetry_RespectsAdr015_SearchDocuments_DoesNotLogExcerptContent()
    {
        const string secretExcerpt = "EXCERPT-WITH-PRIVILEGED-CONTENT-Trade-Secret-Formula-XJ7K";

        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildRagResponse(("c1", "Policy.pdf", secretExcerpt)));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: "{\"query\":\"benign query\"}");
        var tool = BuildDocumentSearchTool("SearchDocuments");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        // ADR-015 binding: excerpts must NEVER appear in logs at any level
        AssertTelemetryRespectsAdr015(secretExcerpt);
    }

    [Fact]
    public async Task Telemetry_RespectsAdr015_SearchDiscovery_DoesNotLogExcerptContent()
    {
        const string secretExcerpt = "EXCERPT-WITH-CONFIDENTIAL-CASE-NAME-SmithVJonesCase-DocketX";

        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildRagResponse(("c1", "Brief.pdf", secretExcerpt)));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{\"query\":\"benign\"}");
        var tool = BuildDocumentSearchTool("SearchDiscovery");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        AssertTelemetryRespectsAdr015(secretExcerpt);
    }

    [Fact]
    public async Task Telemetry_RespectsAdr015_QueryTextOnlyAllowedAtDebugLevel()
    {
        // The handler emits queryLen at Debug level (allowed); the query text itself must
        // never appear at any level. This test asserts the structural rule by inspecting the
        // handler source-of-truth via the level distribution of captured messages.
        // (The sentinel-substring scan is the canonical assertion — see the two tests above.)
        const string secretQuery = "QUERY-CONTAINING-CLIENT-NAME-AcmeCorpInc-MatterX2026";

        _ragServiceMock
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildRagResponse(("c", "D.pdf", "harmless content")));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: $"{{\"query\":\"{secretQuery}\"}}");
        var tool = BuildDocumentSearchTool("SearchDocuments");

        await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        // The secret query text must not appear at Information level. (At Debug level the
        // handler logs only queryLen — never the query itself — so the sentinel must not
        // appear at ANY captured level.)
        var infoMessages = CapturedLogMessages
            .Where(m => m.LogLevel == Microsoft.Extensions.Logging.LogLevel.Information)
            .Select(m => m.FormattedMessage);

        foreach (var msg in infoMessages)
        {
            msg.Should().NotContain(secretQuery,
                "ADR-015: query text must NEVER appear in Information-level telemetry");
        }

        AssertTelemetryRespectsAdr015(secretQuery);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Chat-context dispatch via the ToolHandlerToAIFunctionAdapter (FR-11 wiring)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Adapter_WrapsDocumentSearchHandler_ExposesRowMetadataToLLM()
    {
        // Proves the adapter accepts DocumentSearchHandler as a chat-available handler.
        var row = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "search_documents",
            Description = "Targeted search over the knowledge index.",
            HandlerClass = nameof(DocumentSearchHandler),
            AvailableInContexts = ToolAvailabilityContext.Both,
            Configuration = "{\"method\":\"SearchDocuments\"}",
            JsonSchema = ValidJsonSchema,
            OwnerType = ScopeOwnerType.System
        };
        var handler = CreateHandler();

        Func<ChatInvocationContext> contextFactory = () => new ChatInvocationContext
        {
            ChatSessionId = Guid.NewGuid(),
            TenantId = DefaultTenantId,
            ToolArgumentsJson = "{\"query\":\"compliance\"}"
        };

        // Act + Assert — must not throw
        var adapter = new ToolHandlerToAIFunctionAdapter(
            row, handler, contextFactory, NullLogger.Instance);

        adapter.Name.Should().Be("search_documents");
        adapter.Description.Should().Be("Targeted search over the knowledge index.");
        adapter.JsonSchema.ValueKind.Should().Be(JsonValueKind.Object,
            because: "FR-10: the LLM consumes JsonSchema from the Dataverse row");
    }

    [Fact]
    public void Adapter_WrapsDocumentSearchHandler_DiscoveryRow_AlsoAccepted()
    {
        // Same handler, second row (SearchDiscovery) — proves the multi-method dispatch
        // pattern works at the adapter boundary (mirrors KnowledgeRetrievalHandler shape).
        var row = new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "search_discovery",
            Description = "Broad discovery search.",
            HandlerClass = nameof(DocumentSearchHandler),
            AvailableInContexts = ToolAvailabilityContext.Both,
            Configuration = "{\"method\":\"SearchDiscovery\"}",
            JsonSchema = ValidJsonSchema,
            OwnerType = ScopeOwnerType.System
        };
        var handler = CreateHandler();

        Func<ChatInvocationContext> contextFactory = () => new ChatInvocationContext
        {
            ChatSessionId = Guid.NewGuid(),
            TenantId = DefaultTenantId,
            ToolArgumentsJson = "{\"query\":\"acquisitions\"}"
        };

        var adapter = new ToolHandlerToAIFunctionAdapter(
            row, handler, contextFactory, NullLogger.Instance);

        adapter.Name.Should().Be("search_discovery");
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
